-- MANUAL: back up, stop StockFlow, apply as superuser after 086, restart.
-- No backfill: an authorized user must establish opening cash explicitly.
BEGIN;
DO $$ BEGIN
 IF NOT EXISTS(SELECT 1 FROM pg_roles WHERE rolname=current_user AND rolsuper) THEN RAISE EXCEPTION 'Apply as PostgreSQL superuser'; END IF;
 IF to_regclass('public.expense_payments') IS NULL OR to_regclass('public.cash_closings') IS NULL THEN RAISE EXCEPTION 'Apply migrations through 086 first'; END IF;
END $$;
CREATE FUNCTION public.sf_cash_allowed() RETURNS boolean LANGUAGE sql STABLE SECURITY DEFINER SET search_path=pg_catalog,public AS $$
 SELECT public.sf_tenant()>0 AND public.sf_location()>0 AND public.sf_can_location(public.sf_location())
 AND public.sf_shop_member(public.sf_store()) AND EXISTS(SELECT 1 FROM public.store_locations l WHERE l.tenant_id=public.sf_tenant() AND l.location_id=public.sf_location() AND l.store_id=public.sf_store() AND l.is_active)
 AND public.sf_transfer_permission('REPORTS','VIEW')
 AND (public.sf_owner() OR EXISTS(SELECT 1 FROM public.users u JOIN public.roles r USING(tenant_id,role_id) WHERE u.tenant_id=public.sf_tenant() AND u.user_id=public.sf_actor() AND u.is_active AND lower(btrim(r.role_name))='admin'))
$$;
CREATE TABLE public.cash_adjustments(
 tenant_id bigint NOT NULL DEFAULT public.sf_tenant(), store_id bigint NOT NULL DEFAULT public.sf_store(),
 location_id bigint NOT NULL DEFAULT public.sf_location(), business_profile_code varchar(40) NOT NULL DEFAULT public.sf_visible_business_profile(),
 adjustment_id uuid NOT NULL, movement_number bigint GENERATED ALWAYS AS IDENTITY UNIQUE,
 kind text NOT NULL CHECK(kind IN('OPENING','OWNER_IN','OWNER_OUT','BANK_DEPOSIT','BANK_WITHDRAWAL','REVERSAL')),
 amount numeric(14,2) NOT NULL, reason varchar(1000) NOT NULL CHECK(length(btrim(reason)) BETWEEN 3 AND 1000),
 start_date date, reverses_id uuid, request_payload jsonb NOT NULL, created_by integer NOT NULL DEFAULT public.sf_actor(), created_at timestamptz NOT NULL DEFAULT now(),
 PRIMARY KEY(tenant_id,adjustment_id),
 FOREIGN KEY(tenant_id,location_id) REFERENCES public.store_locations(tenant_id,location_id),
 FOREIGN KEY(tenant_id,store_id) REFERENCES public.stores(tenant_id,store_id),
 FOREIGN KEY(tenant_id,created_by) REFERENCES public.users(tenant_id,user_id),
 FOREIGN KEY(tenant_id,reverses_id) REFERENCES public.cash_adjustments(tenant_id,adjustment_id), UNIQUE(tenant_id,reverses_id),
 CHECK(abs(amount)<=9999999999.99), CHECK((kind='OPENING')=(start_date IS NOT NULL)), CHECK((kind='REVERSAL')=(reverses_id IS NOT NULL)),
 CHECK(CASE WHEN kind='OPENING' THEN amount>=0 WHEN kind IN('OWNER_IN','BANK_WITHDRAWAL') THEN amount>0 WHEN kind IN('OWNER_OUT','BANK_DEPOSIT') THEN amount<0 ELSE amount<>0 END)
);
CREATE UNIQUE INDEX cash_opening_once ON public.cash_adjustments(tenant_id,location_id,business_profile_code) WHERE kind='OPENING';
CREATE INDEX cash_adjustment_day ON public.cash_adjustments(tenant_id,location_id,business_profile_code,created_at);
ALTER TABLE public.cash_adjustments ENABLE ROW LEVEL SECURITY;
ALTER TABLE public.cash_adjustments FORCE ROW LEVEL SECURITY;
CREATE POLICY cash_adjustment_scope ON public.cash_adjustments USING(tenant_id=public.sf_tenant() AND store_id=public.sf_store() AND location_id=public.sf_location() AND business_profile_code=public.sf_visible_business_profile() AND public.sf_cash_allowed())
 WITH CHECK(tenant_id=public.sf_tenant() AND store_id=public.sf_store() AND location_id=public.sf_location() AND business_profile_code=public.sf_visible_business_profile() AND created_by=public.sf_actor() AND public.sf_cash_allowed());
CREATE TRIGGER cash_adjustment_immutable BEFORE UPDATE OR DELETE ON public.cash_adjustments FOR EACH ROW EXECUTE FUNCTION public.sf_quotation_immutable();
-- Writes are only through the validated function, including callers using runtime SQL directly.
CREATE FUNCTION public.sf_cash_adjust(id uuid,body jsonb,today date) RETURNS uuid LANGUAGE plpgsql SECURITY DEFINER SET search_path=pg_catalog,public AS $$
DECLARE prior public.cash_adjustments; original public.cash_adjustments; k text:=body->>'kind'; value numeric:=(body->>'amount')::numeric; first_day date:=(body->>'startDate')::date; why text:=btrim(body->>'reason'); reversal uuid:=(body->>'reversesId')::uuid;
BEGIN
 IF public.sf_cash_allowed() IS DISTINCT FROM true THEN RAISE EXCEPTION 'Cash operation denied' USING ERRCODE='42501'; END IF;
 PERFORM pg_advisory_xact_lock(hashtextextended('cash:'||public.sf_tenant()::text||':'||public.sf_location()::text,0));
 SELECT * INTO prior FROM public.cash_adjustments WHERE tenant_id=public.sf_tenant() AND adjustment_id=id;
 IF FOUND THEN
  IF prior.store_id<>public.sf_store() OR prior.location_id<>public.sf_location() OR prior.business_profile_code<>public.sf_visible_business_profile() OR prior.created_by<>public.sf_actor() OR prior.request_payload IS DISTINCT FROM body THEN RAISE EXCEPTION 'Cash request identity conflicts' USING ERRCODE='23514'; END IF;
  RETURN id;
 END IF;
 IF id IS NULL OR id='00000000-0000-0000-0000-000000000000'::uuid OR k IS NULL OR k NOT IN('OPENING','OWNER_IN','OWNER_OUT','BANK_DEPOSIT','BANK_WITHDRAWAL','REVERSAL') OR value IS NULL OR value<0 OR (k<>'OPENING' AND value=0) OR value>9999999999.99 OR round(value,2)<>value OR why IS NULL OR length(why) NOT BETWEEN 3 AND 1000 OR (k='OPENING')<>(first_day IS NOT NULL) OR (k='REVERSAL')<>(reversal IS NOT NULL) THEN RAISE EXCEPTION 'Invalid cash adjustment' USING ERRCODE='23514'; END IF;
 IF k='OPENING' THEN
  -- Date cannot be future even for direct runtime callers; shop timezone is fixed by deployment.
  IF first_day<'2000-01-01' OR first_day>(now() AT TIME ZONE 'Asia/Kolkata')::date OR first_day>today OR EXISTS(SELECT 1 FROM public.cash_adjustments WHERE tenant_id=public.sf_tenant() AND location_id=public.sf_location() AND business_profile_code=public.sf_visible_business_profile() AND kind='OPENING') THEN RAISE EXCEPTION 'Opening already exists or date is invalid' USING ERRCODE='23514'; END IF;
  IF EXISTS(SELECT 1 FROM public.cash_closings WHERE tenant_id=public.sf_tenant() AND location_id=public.sf_location() AND business_date>=first_day AND (coalesce((snapshot->>'cashAdded')::numeric,0)<>0 OR coalesce((snapshot->>'cashRemoved')::numeric,0)<>0)) THEN RAISE EXCEPTION 'Choose a tracking date after legacy closings with extra cash and reconcile that opening first' USING ERRCODE='23514'; END IF;
 ELSE
  IF NOT EXISTS(SELECT 1 FROM public.cash_adjustments WHERE tenant_id=public.sf_tenant() AND location_id=public.sf_location() AND business_profile_code=public.sf_visible_business_profile() AND kind='OPENING') THEN RAISE EXCEPTION 'Initialize opening cash first' USING ERRCODE='23514'; END IF;
 END IF;
 IF k IN('OWNER_OUT','BANK_DEPOSIT') THEN value:=-value; END IF;
 IF k='REVERSAL' THEN
  SELECT * INTO original FROM public.cash_adjustments WHERE tenant_id=public.sf_tenant() AND adjustment_id=reversal AND store_id=public.sf_store() AND location_id=public.sf_location() AND business_profile_code=public.sf_visible_business_profile();
  IF NOT FOUND OR original.kind IN('OPENING','REVERSAL') OR abs(original.amount)<>value OR EXISTS(SELECT 1 FROM public.cash_adjustments WHERE tenant_id=public.sf_tenant() AND reverses_id=reversal) THEN RAISE EXCEPTION 'Adjustment unavailable or already reversed' USING ERRCODE='23514'; END IF;
  value:=-original.amount;
 END IF;
 INSERT INTO public.cash_adjustments(adjustment_id,kind,amount,reason,start_date,reverses_id,request_payload) VALUES(id,k,value,why,first_day,reversal,body);
 RETURN id;
END $$;
-- Narrow expense detail projection: same authority as the existing closing aggregate.
CREATE FUNCTION public.sf_cash_expenses(loc bigint,start_at timestamptz,end_at timestamptz,zone text)
 RETURNS TABLE(id bigint,amount numeric,occurred timestamptz,reference text,description text)
 LANGUAGE sql STABLE SECURITY DEFINER SET search_path=pg_catalog,public AS $$
 SELECT m.id,m.amount,m.occurred,coalesce(nullif(p.reference,''),'EXP-'||m.id::text),e.description
 FROM public.sf_expense_cash_movements(loc,start_at,end_at,zone) m
 JOIN public.expense_payments p ON p.tenant_id=public.sf_tenant() AND p.movement_id=m.id
 JOIN public.expenses e ON e.tenant_id=p.tenant_id AND e.expense_id=p.expense_id
$$;
REVOKE ALL ON FUNCTION public.sf_cash_allowed(),public.sf_cash_adjust(uuid,jsonb,date),public.sf_cash_expenses(bigint,timestamptz,timestamptz,text) FROM PUBLIC;
GRANT EXECUTE ON FUNCTION public.sf_cash_allowed(),public.sf_cash_adjust(uuid,jsonb,date),public.sf_cash_expenses(bigint,timestamptz,timestamptz,text) TO stockflow_runtime;
GRANT SELECT ON public.cash_adjustments TO stockflow_runtime;
COMMIT;
