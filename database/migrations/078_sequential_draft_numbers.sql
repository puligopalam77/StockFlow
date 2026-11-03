-- MANUAL ONLY, after 077. Back up, stop ALL writers; run ENTIRE file as postgres.
-- UUIDs, payloads, ownership and updated_at remain unchanged. No invoices or stock touched.
-- Legacy schema has NO original creation timestamp. Do NOT infer it from savedAt/updated_at.
-- If verified creation timestamps have been recovered, before this migration add/populate
-- billing_drafts.created_at timestamptz using the full existing primary key. Otherwise
-- created_at stays NULL and creation_time_source is UNKNOWN. Unknown drafts sort after
-- dated drafts, by UUID (then cashier/location to break duplicate-UUID ties), not update time.
-- Transactional and rerunnable: existing numbers and ledger allocations are retained.
-- Recovery: any error rolls back this file in full. Correct the reported issue and
-- rerun the entire file. Never reset counters or delete the permanent allocation ledger.
-- Store the final per-shop SELECT result alongside the verified pre-deployment backup.
BEGIN;
SET LOCAL lock_timeout='15s';
DO $$ BEGIN
 IF NOT EXISTS(SELECT 1 FROM pg_roles WHERE rolname=current_user AND rolsuper) THEN RAISE EXCEPTION 'Run as PostgreSQL superuser'; END IF;
 IF position('Manual draft line identity must not reference catalog stock' IN pg_get_functiondef('public.sf_profile_posting_guard()'::regprocedure))=0 THEN RAISE EXCEPTION 'Apply 077 first'; END IF;
END $$;
LOCK TABLE public.billing_drafts IN ACCESS EXCLUSIVE MODE;
CREATE TEMP TABLE draft_numbering_before ON COMMIT DROP AS SELECT tenant_id,user_id,location_id,draft_id,label,payload,updated_at,business_profile_code FROM public.billing_drafts;
ALTER TABLE public.billing_drafts ADD COLUMN IF NOT EXISTS store_id bigint,
 ADD COLUMN IF NOT EXISTS created_at timestamptz,
 ADD COLUMN IF NOT EXISTS creation_time_source text NOT NULL DEFAULT 'UNKNOWN',
 ADD COLUMN IF NOT EXISTS draft_sequence bigint;
CREATE TABLE IF NOT EXISTS public.billing_draft_counters(
 tenant_id bigint NOT NULL,store_id bigint NOT NULL,last_number bigint NOT NULL CHECK(last_number>0),
 PRIMARY KEY(tenant_id,store_id),FOREIGN KEY(tenant_id,store_id) REFERENCES public.stores(tenant_id,store_id));
-- Permanent allocations survive draft deletion and completion, so references cannot be recycled.
CREATE TABLE IF NOT EXISTS public.billing_draft_numbers(
 tenant_id bigint NOT NULL,user_id integer NOT NULL,location_id bigint NOT NULL,draft_id uuid NOT NULL,
 store_id bigint NOT NULL,business_profile_code varchar(40) NOT NULL,draft_sequence bigint NOT NULL CHECK(draft_sequence>0),
 created_at timestamptz,creation_time_source text NOT NULL CHECK(creation_time_source IN('SERVER','RECOVERED','UNKNOWN')),
 PRIMARY KEY(tenant_id,user_id,location_id,draft_id),UNIQUE(tenant_id,store_id,draft_sequence),
 FOREIGN KEY(tenant_id,store_id) REFERENCES public.stores(tenant_id,store_id),
 CHECK((creation_time_source='UNKNOWN')=(created_at IS NULL)));
-- Only disable the two metadata-sensitive triggers during the locked metadata backfill.
ALTER TABLE public.billing_drafts DISABLE TRIGGER a_profile_posting_guard;
DROP TRIGGER IF EXISTS aa_billing_draft_number ON public.billing_drafts;
UPDATE public.billing_drafts d SET store_id=l.store_id,
 creation_time_source=CASE WHEN d.created_at IS NULL THEN 'UNKNOWN' WHEN d.creation_time_source='UNKNOWN' THEN 'RECOVERED' ELSE d.creation_time_source END
 FROM public.store_locations l WHERE l.tenant_id=d.tenant_id AND l.location_id=d.location_id;
DO $$ BEGIN
 IF EXISTS(SELECT 1 FROM public.billing_drafts WHERE store_id IS NULL) THEN RAISE EXCEPTION 'A draft has no valid shop relationship; rolled back'; END IF;
END $$;
INSERT INTO public.billing_draft_numbers SELECT tenant_id,user_id,location_id,draft_id,store_id,business_profile_code,draft_sequence,created_at,creation_time_source FROM public.billing_drafts WHERE draft_sequence IS NOT NULL ON CONFLICT DO NOTHING;
INSERT INTO public.billing_draft_counters(tenant_id,store_id,last_number)
 SELECT tenant_id,store_id,max(draft_sequence) FROM public.billing_draft_numbers GROUP BY tenant_id,store_id
 ON CONFLICT(tenant_id,store_id) DO UPDATE SET last_number=greatest(billing_draft_counters.last_number,excluded.last_number);
INSERT INTO public.billing_draft_numbers(tenant_id,user_id,location_id,draft_id,store_id,business_profile_code,draft_sequence,created_at,creation_time_source)
 SELECT d.tenant_id,d.user_id,d.location_id,d.draft_id,d.store_id,d.business_profile_code,
 coalesce(c.last_number,0)+row_number() OVER(PARTITION BY d.tenant_id,d.store_id ORDER BY d.created_at ASC NULLS LAST,d.draft_id,d.user_id,d.location_id),d.created_at,d.creation_time_source
 FROM public.billing_drafts d LEFT JOIN public.billing_draft_counters c USING(tenant_id,store_id)
 WHERE NOT EXISTS(SELECT 1 FROM public.billing_draft_numbers n WHERE (n.tenant_id,n.user_id,n.location_id,n.draft_id)=(d.tenant_id,d.user_id,d.location_id,d.draft_id));
UPDATE public.billing_drafts d SET draft_sequence=n.draft_sequence FROM public.billing_draft_numbers n
 WHERE (n.tenant_id,n.user_id,n.location_id,n.draft_id)=(d.tenant_id,d.user_id,d.location_id,d.draft_id) AND d.draft_sequence IS NULL;
INSERT INTO public.billing_draft_counters SELECT tenant_id,store_id,max(draft_sequence) FROM public.billing_draft_numbers GROUP BY tenant_id,store_id
 ON CONFLICT(tenant_id,store_id) DO UPDATE SET last_number=greatest(billing_draft_counters.last_number,excluded.last_number);
ALTER TABLE public.billing_drafts ENABLE TRIGGER a_profile_posting_guard;
ALTER TABLE public.billing_drafts ALTER COLUMN store_id SET NOT NULL,ALTER COLUMN draft_sequence SET NOT NULL;
CREATE UNIQUE INDEX IF NOT EXISTS ux_billing_draft_reference ON public.billing_drafts(tenant_id,store_id,draft_sequence);
CREATE OR REPLACE FUNCTION public.sf_draft_number(n bigint) RETURNS text LANGUAGE sql IMMUTABLE SET search_path=pg_catalog,public AS $$ SELECT 'DRF-'||lpad(n::text,greatest(4,length(n::text)),'0') $$;
CREATE OR REPLACE FUNCTION public.sf_billing_draft_number() RETURNS trigger LANGUAGE plpgsql SECURITY DEFINER SET search_path=pg_catalog,public AS $$
DECLARE allocation public.billing_draft_numbers; shop bigint; next_number bigint; BEGIN
 IF TG_OP='UPDATE' THEN
  IF (NEW.tenant_id,NEW.user_id,NEW.location_id,NEW.draft_id,NEW.store_id,NEW.business_profile_code,NEW.draft_sequence,NEW.created_at,NEW.creation_time_source)
   IS DISTINCT FROM (OLD.tenant_id,OLD.user_id,OLD.location_id,OLD.draft_id,OLD.store_id,OLD.business_profile_code,OLD.draft_sequence,OLD.created_at,OLD.creation_time_source) THEN
   RAISE EXCEPTION 'Draft identity, number and creation metadata are permanent' USING ERRCODE='23514'; END IF;
  RETURN NEW;
 END IF;
 SELECT l.store_id INTO shop FROM public.store_locations l WHERE l.tenant_id=NEW.tenant_id AND l.location_id=NEW.location_id;
 IF NEW.tenant_id IS DISTINCT FROM public.sf_tenant() OR NEW.user_id IS DISTINCT FROM public.sf_actor()
  OR NEW.location_id IS DISTINCT FROM public.sf_location() OR shop IS DISTINCT FROM public.sf_store()
  OR NEW.business_profile_code IS DISTINCT FROM public.sf_visible_business_profile() OR NOT public.sf_can_location(NEW.location_id)
  OR NOT public.sf_gst_allowed('CREATE','SALES') THEN RAISE EXCEPTION 'Authorized billing shop required' USING ERRCODE='42501'; END IF;
 -- Serialize duplicate UUID retries before taking the shared shop counter row lock.
 PERFORM pg_advisory_xact_lock(hashtextextended('draft-number:'||NEW.tenant_id||':'||NEW.user_id||':'||NEW.location_id||':'||NEW.draft_id,0));
 SELECT * INTO allocation FROM public.billing_draft_numbers WHERE tenant_id=NEW.tenant_id AND user_id=NEW.user_id AND location_id=NEW.location_id AND draft_id=NEW.draft_id;
 IF NOT FOUND THEN
  INSERT INTO public.billing_draft_counters VALUES(NEW.tenant_id,shop,1) ON CONFLICT(tenant_id,store_id) DO UPDATE SET last_number=billing_draft_counters.last_number+1 RETURNING last_number INTO next_number;
  INSERT INTO public.billing_draft_numbers VALUES(NEW.tenant_id,NEW.user_id,NEW.location_id,NEW.draft_id,shop,NEW.business_profile_code,next_number,clock_timestamp(),'SERVER') RETURNING * INTO allocation;
 END IF;
 IF allocation.store_id<>shop OR allocation.business_profile_code<>NEW.business_profile_code THEN RAISE EXCEPTION 'Draft allocation belongs to another shop setup' USING ERRCODE='23514'; END IF;
 NEW.store_id:=allocation.store_id;NEW.draft_sequence:=allocation.draft_sequence;NEW.created_at:=allocation.created_at;NEW.creation_time_source:=allocation.creation_time_source;RETURN NEW;
END $$;
CREATE TRIGGER aa_billing_draft_number BEFORE INSERT OR UPDATE ON public.billing_drafts FOR EACH ROW EXECUTE FUNCTION public.sf_billing_draft_number();
REVOKE ALL ON FUNCTION public.sf_billing_draft_number() FROM PUBLIC;
REVOKE ALL ON FUNCTION public.sf_draft_number(bigint) FROM PUBLIC;
GRANT EXECUTE ON FUNCTION public.sf_draft_number(bigint) TO stockflow_runtime;
ALTER TABLE public.billing_draft_numbers ENABLE ROW LEVEL SECURITY;
ALTER TABLE public.billing_draft_numbers FORCE ROW LEVEL SECURITY;
DROP POLICY IF EXISTS draft_number_owner ON public.billing_draft_numbers;
CREATE POLICY draft_number_owner ON public.billing_draft_numbers FOR SELECT USING(tenant_id=public.sf_tenant() AND user_id=public.sf_actor() AND location_id=public.sf_location() AND store_id=public.sf_store() AND business_profile_code=public.sf_visible_business_profile());
ALTER TABLE public.billing_draft_counters ENABLE ROW LEVEL SECURITY;
ALTER TABLE public.billing_draft_counters FORCE ROW LEVEL SECURITY;
GRANT SELECT ON public.billing_draft_numbers TO stockflow_runtime;
DO $$ BEGIN
 IF EXISTS((SELECT * FROM draft_numbering_before EXCEPT SELECT tenant_id,user_id,location_id,draft_id,label,payload,updated_at,business_profile_code FROM public.billing_drafts)
 UNION ALL (SELECT tenant_id,user_id,location_id,draft_id,label,payload,updated_at,business_profile_code FROM public.billing_drafts EXCEPT SELECT * FROM draft_numbering_before)) THEN RAISE EXCEPTION 'Draft contents changed; migration rolled back'; END IF;
 IF EXISTS(SELECT 1 FROM public.billing_drafts d JOIN public.billing_draft_numbers n USING(tenant_id,user_id,location_id,draft_id) WHERE (d.store_id,d.draft_sequence,d.created_at,d.creation_time_source) IS DISTINCT FROM (n.store_id,n.draft_sequence,n.created_at,n.creation_time_source)) THEN RAISE EXCEPTION 'Existing allocation mismatch; rolled back'; END IF;
END $$;
-- Keep this result for the deployment record. UNKNOWN counts are explicitly unrecovered.
SELECT tenant_id,store_id,count(*) AS numbered_drafts,count(*) FILTER(WHERE creation_time_source='UNKNOWN') AS unknown_creation_times,min(draft_sequence) AS first_number,max(draft_sequence) AS last_number FROM public.billing_drafts GROUP BY tenant_id,store_id ORDER BY tenant_id,store_id;
COMMIT;
