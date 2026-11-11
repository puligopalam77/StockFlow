-- MANUAL: back up, stop StockFlow, apply as superuser after 087, restart.
-- Cash in Hand only. Existing bank-labelled history is not rewritten.
BEGIN;
DO $$ BEGIN
 IF NOT EXISTS(SELECT 1 FROM pg_roles WHERE rolname=current_user AND rolsuper) THEN RAISE EXCEPTION 'Apply as PostgreSQL superuser'; END IF;
 IF to_regclass('public.cash_adjustments') IS NULL THEN RAISE EXCEPTION 'Apply migration 087 first'; END IF;
END $$;
CREATE FUNCTION public.sf_cash_entry_release_guard() RETURNS trigger
 LANGUAGE plpgsql SET search_path=pg_catalog,public AS $$
BEGIN
 IF NEW.kind IN('BANK_DEPOSIT','BANK_WITHDRAWAL') THEN
  RAISE EXCEPTION 'Bank transactions and transfers are deferred. Use Cash Entry only for owner contributions or withdrawals.' USING ERRCODE='23514';
 END IF;
 RETURN NEW;
END $$;
REVOKE ALL ON FUNCTION public.sf_cash_entry_release_guard() FROM PUBLIC;
CREATE TRIGGER cash_entry_release_scope BEFORE INSERT ON public.cash_adjustments
 FOR EACH ROW EXECUTE FUNCTION public.sf_cash_entry_release_guard();
-- Existing openings keep their day-start meaning. Only new CURRENT openings use
-- the exact cutoff and source watermarks; no historical amounts are changed.
ALTER TABLE public.cash_adjustments ADD COLUMN tracking_started_at timestamptz;
ALTER TABLE public.cash_adjustments ADD COLUMN source_cutoffs jsonb;
ALTER TABLE public.cash_adjustments ADD CONSTRAINT cash_opening_cutoff CHECK(
 (tracking_started_at IS NULL AND source_cutoffs IS NULL) OR
 (kind='OPENING' AND tracking_started_at IS NOT NULL AND source_cutoffs IS NOT NULL AND jsonb_typeof(source_cutoffs)='object'
  AND start_date=(tracking_started_at AT TIME ZONE 'Asia/Kolkata')::date));
ALTER TABLE public.cash_adjustments ALTER COLUMN created_at SET DEFAULT clock_timestamp();
CREATE TABLE public.cash_opening_expense_reviews(
 tenant_id bigint NOT NULL DEFAULT public.sf_tenant(),location_id bigint NOT NULL DEFAULT public.sf_location(),
 business_profile_code varchar(40) NOT NULL DEFAULT public.sf_visible_business_profile(),
 request_id uuid NOT NULL,opening_id uuid NOT NULL,movement_id bigint NOT NULL,version bigint NOT NULL CHECK(version>0),
 timing text NOT NULL CHECK(timing IN('BEFORE','AFTER')),reason varchar(1000) NOT NULL CHECK(length(btrim(reason)) BETWEEN 3 AND 1000),
 created_by integer NOT NULL DEFAULT public.sf_actor(),created_at timestamptz NOT NULL DEFAULT clock_timestamp(),request_payload jsonb NOT NULL,
 PRIMARY KEY(tenant_id,request_id),UNIQUE(tenant_id,opening_id,movement_id,version),
 FOREIGN KEY(tenant_id,opening_id) REFERENCES public.cash_adjustments(tenant_id,adjustment_id),
 FOREIGN KEY(tenant_id,location_id) REFERENCES public.store_locations(tenant_id,location_id),
 FOREIGN KEY(movement_id) REFERENCES public.expense_payments(movement_id),
 FOREIGN KEY(tenant_id,created_by) REFERENCES public.users(tenant_id,user_id)
);
ALTER TABLE public.cash_opening_expense_reviews ENABLE ROW LEVEL SECURITY;
ALTER TABLE public.cash_opening_expense_reviews FORCE ROW LEVEL SECURITY;
CREATE POLICY cash_opening_review_scope ON public.cash_opening_expense_reviews USING(tenant_id=public.sf_tenant() AND location_id=public.sf_location() AND business_profile_code=public.sf_visible_business_profile() AND public.sf_cash_allowed());
CREATE TRIGGER cash_opening_review_immutable BEFORE UPDATE OR DELETE ON public.cash_opening_expense_reviews FOR EACH ROW EXECUTE FUNCTION public.sf_quotation_immutable();
GRANT SELECT ON public.cash_opening_expense_reviews TO stockflow_runtime;

-- Reuse the existing posting/setup lock. This also covers direct runtime inserts
-- and prevents a transaction that started before opening from committing across
-- the baseline without being assigned to one side of the source cutoff.
CREATE FUNCTION public.sf_cash_posting_boundary() RETURNS trigger LANGUAGE plpgsql SET search_path=pg_catalog,public AS $$
BEGIN
 IF public.sf_tenant()>0 THEN
  PERFORM pg_advisory_xact_lock(hashtextextended('catalog-registration:'||public.sf_tenant()::text,0));
 END IF;
 RETURN NULL;
END $$;
REVOKE ALL ON FUNCTION public.sf_cash_posting_boundary() FROM PUBLIC;
DO $$ DECLARE tab text; BEGIN
 FOREACH tab IN ARRAY ARRAY['payments','service_payments','sale_returns','supplier_payments','expense_payments'] LOOP
  EXECUTE format('CREATE TRIGGER cash_posting_boundary BEFORE INSERT ON public.%I FOR EACH STATEMENT EXECUTE FUNCTION public.sf_cash_posting_boundary()',tab);
 END LOOP;
END $$;

ALTER FUNCTION public.sf_cash_adjust(uuid,jsonb,date) RENAME TO sf_cash_adjust_day_start;
REVOKE ALL ON FUNCTION public.sf_cash_adjust_day_start(uuid,jsonb,date) FROM PUBLIC,stockflow_runtime;
CREATE FUNCTION public.sf_cash_adjust(id uuid,body jsonb,today date) RETURNS uuid
 LANGUAGE plpgsql SECURITY DEFINER SET search_path=pg_catalog,public AS $$
DECLARE prior public.cash_adjustments; value numeric; cutoffs jsonb; stamp timestamptz;
BEGIN
 IF public.sf_cash_allowed() IS DISTINCT FROM true THEN RAISE EXCEPTION 'Cash operation denied' USING ERRCODE='42501'; END IF;
 PERFORM pg_advisory_xact_lock(hashtextextended('catalog-registration:'||public.sf_tenant()::text,0));
 IF body->>'openingMode' IS NOT NULL AND body->>'openingMode'<>'CURRENT' THEN RAISE EXCEPTION 'Invalid opening mode' USING ERRCODE='23514'; END IF;
 IF body->>'openingMode' IS DISTINCT FROM 'CURRENT' THEN RETURN public.sf_cash_adjust_day_start(id,body,today); END IF;
 PERFORM pg_advisory_xact_lock(hashtextextended('cash:'||public.sf_tenant()::text||':'||public.sf_location()::text,0));
 SELECT * INTO prior FROM public.cash_adjustments WHERE tenant_id=public.sf_tenant() AND adjustment_id=id;
 IF FOUND THEN
  IF prior.location_id<>public.sf_location() OR prior.store_id<>public.sf_store() OR prior.business_profile_code<>public.sf_visible_business_profile() OR prior.created_by<>public.sf_actor() OR prior.request_payload IS DISTINCT FROM body THEN RAISE EXCEPTION 'Cash request identity conflicts' USING ERRCODE='23514'; END IF;
  RETURN id;
 END IF;
 value:=(body->>'amount')::numeric;
 IF id IS NULL OR id='00000000-0000-0000-0000-000000000000'::uuid OR body->>'kind' IS DISTINCT FROM 'OPENING' OR body->>'startDate' IS NOT NULL OR body->>'reversesId' IS NOT NULL OR value IS NULL OR value<0 OR value>9999999999.99 OR round(value,2)<>value OR length(btrim(coalesce(body->>'reason',''))) NOT BETWEEN 3 AND 1000 THEN RAISE EXCEPTION 'Invalid current cash opening' USING ERRCODE='23514'; END IF;
 stamp:=clock_timestamp();
 SELECT jsonb_build_object(
  'SALE',(SELECT coalesce(max(payment_id),0) FROM public.payments WHERE tenant_id=public.sf_tenant() AND location_id=public.sf_location()),
  'SERVICE',(SELECT coalesce(max(service_payment_id),0) FROM public.service_payments WHERE tenant_id=public.sf_tenant() AND location_id=public.sf_location()),
  'REFUND',(SELECT coalesce(max(sale_return_id),0) FROM public.sale_returns WHERE tenant_id=public.sf_tenant() AND location_id=public.sf_location()),
  'SUPPLIER',(SELECT coalesce(max(supplier_payment_id),0) FROM public.supplier_payments WHERE tenant_id=public.sf_tenant() AND location_id=public.sf_location()),
  'EXPENSE',(SELECT coalesce(max(movement_id),0) FROM public.expense_payments WHERE tenant_id=public.sf_tenant() AND location_id=public.sf_location())) INTO cutoffs;
 INSERT INTO public.cash_adjustments(adjustment_id,kind,amount,reason,start_date,request_payload,tracking_started_at,source_cutoffs)
 VALUES(id,'OPENING',value,btrim(body->>'reason'),(stamp AT TIME ZONE 'Asia/Kolkata')::date,body,stamp,cutoffs);
 RETURN id;
END $$;
REVOKE ALL ON FUNCTION public.sf_cash_adjust(uuid,jsonb,date) FROM PUBLIC;
GRANT EXECUTE ON FUNCTION public.sf_cash_adjust(uuid,jsonb,date) TO stockflow_runtime;

CREATE FUNCTION public.sf_cash_review_expense(id uuid,movement bigint,expected bigint,decision text,why text) RETURNS void
 LANGUAGE plpgsql SECURITY DEFINER SET search_path=pg_catalog,public AS $$
DECLARE opening public.cash_adjustments; prior public.cash_opening_expense_reviews; latest bigint; payload jsonb;
BEGIN
 IF public.sf_cash_allowed() IS DISTINCT FROM true THEN RAISE EXCEPTION 'Cash review denied' USING ERRCODE='42501'; END IF;
 PERFORM pg_advisory_xact_lock(hashtextextended('catalog-registration:'||public.sf_tenant()::text,0));
 PERFORM pg_advisory_xact_lock(hashtextextended('cash:'||public.sf_tenant()::text||':'||public.sf_location()::text,0));
 IF id IS NULL OR id='00000000-0000-0000-0000-000000000000'::uuid OR expected IS NULL OR expected<0 OR decision IS NULL OR decision NOT IN('BEFORE','AFTER') OR length(btrim(coalesce(why,''))) NOT BETWEEN 3 AND 1000 THEN RAISE EXCEPTION 'Invalid cash payment timing review' USING ERRCODE='23514'; END IF;
 payload:=jsonb_build_object('movement',movement,'expected',expected,'timing',decision,'reason',btrim(why));
 SELECT * INTO prior FROM public.cash_opening_expense_reviews WHERE tenant_id=public.sf_tenant() AND request_id=id;
 IF FOUND THEN
  IF prior.location_id<>public.sf_location() OR prior.business_profile_code<>public.sf_visible_business_profile() OR prior.created_by<>public.sf_actor() OR prior.request_payload IS DISTINCT FROM payload THEN RAISE EXCEPTION 'Cash review request identity conflicts' USING ERRCODE='23514'; END IF;
  RETURN;
 END IF;
 SELECT * INTO opening FROM public.cash_adjustments WHERE tenant_id=public.sf_tenant() AND location_id=public.sf_location() AND business_profile_code=public.sf_visible_business_profile() AND kind='OPENING';
 IF opening.tracking_started_at IS NULL OR NOT EXISTS(
  SELECT 1 FROM public.expense_payments p JOIN public.expenses e USING(tenant_id,expense_id)
  WHERE p.tenant_id=public.sf_tenant() AND p.location_id=public.sf_location() AND e.store_id=public.sf_store() AND p.business_profile_code=public.sf_visible_business_profile()
  AND p.movement_id=movement AND p.payment_mode='CASH' AND p.paid_date=opening.start_date AND p.movement_id>(opening.source_cutoffs->>'EXPENSE')::bigint
 ) THEN RAISE EXCEPTION 'Payment does not require opening-day timing review' USING ERRCODE='23514'; END IF;
 SELECT coalesce(max(version),0) INTO latest FROM public.cash_opening_expense_reviews WHERE tenant_id=public.sf_tenant() AND opening_id=opening.adjustment_id AND movement_id=movement;
 IF latest<>expected THEN RAISE EXCEPTION 'Cash payment timing changed. Refresh before reviewing.' USING ERRCODE='23514'; END IF;
 INSERT INTO public.cash_opening_expense_reviews(request_id,opening_id,movement_id,version,timing,reason,request_payload)
 VALUES(id,opening.adjustment_id,movement,latest+1,decision,btrim(why),payload);
END $$;
REVOKE ALL ON FUNCTION public.sf_cash_review_expense(uuid,bigint,bigint,text,text) FROM PUBLIC;
GRANT EXECUTE ON FUNCTION public.sf_cash_review_expense(uuid,bigint,bigint,text,text) TO stockflow_runtime;
COMMIT;
