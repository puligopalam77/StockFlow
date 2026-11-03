-- MANUAL ONLY, after 074. Back up, stop writers; run the ENTIRE file as postgres.
-- Optional GST applies to ordinary/unregistered billing, never to GST invoices.
-- No posted financial values or existing tax snapshots are rewritten.
BEGIN;
SET LOCAL lock_timeout='15s';
DO $$ BEGIN
 IF NOT EXISTS(SELECT 1 FROM pg_roles WHERE rolname=current_user AND rolsuper) THEN RAISE EXCEPTION 'Run as PostgreSQL superuser'; END IF;
 IF to_regprocedure('public.sf_pos_manual_tax(bigint)') IS NULL THEN RAISE EXCEPTION 'Apply 074 first'; END IF;
END $$;

CREATE FUNCTION public.sf_pos_manual_no_gst() RETURNS jsonb
LANGUAGE plpgsql STABLE SECURITY DEFINER SET search_path=pg_catalog,public AS $$
DECLARE context jsonb; BEGIN
 IF NOT public.sf_gst_allowed('CREATE','SALES') OR NOT public.sf_gst_allowed('MANUAL_ITEM','SALES') OR NOT public.sf_can_location(public.sf_location()) THEN
  RAISE EXCEPTION 'Manual item billing permission required' USING ERRCODE='42501'; END IF;
 context:=public.sf_gst_sale_context();
 IF coalesce((context->>'mapped')::boolean,false) AND context->>'registrationType'<>'UNREGISTERED' THEN
  RAISE EXCEPTION 'This shop issues GST documents. Select a reviewed GST classification for this item.' USING ERRCODE='23514'; END IF;
 -- Missing classification is explicit, not an invented exemption or HSN code.
 RETURN jsonb_build_object('ruleId',NULL,'ruleName','No GST','classification','GOODS','hsnSac',NULL,'uqc','NOS',
  'treatment','UNCLASSIFIED','taxRate',0,'cessRate',0,'reverseCharge',false,'pricesIncludeTax',NULL,
  'gstOptional',true,'registrationType',context->>'registrationType');
END $$;
REVOKE ALL ON FUNCTION public.sf_pos_manual_no_gst() FROM PUBLIC;
GRANT EXECUTE ON FUNCTION public.sf_pos_manual_no_gst() TO stockflow_runtime;

-- Keep the existing posting and immutable-snapshot checks; select the correct authority.
DO $migration$ DECLARE definition text; target text; BEGIN
 definition:=pg_get_functiondef('public.sf_pos_line_guard()'::regprocedure);
 target:='SELECT value INTO tax FROM public.sf_pos_manual_tax((NEW.manual_tax_snapshot->>''ruleId'')::bigint) value;';
 IF position(target IN definition)=0 THEN RAISE EXCEPTION 'Unexpected manual line guard definition'; END IF;
 definition:=replace(definition,target,$branch$IF NEW.manual_tax_snapshot->>'ruleId' IS NULL THEN
   tax:=public.sf_pos_manual_no_gst();
  ELSE SELECT value INTO tax FROM public.sf_pos_manual_tax((NEW.manual_tax_snapshot->>'ruleId')::bigint) value; END IF;$branch$);
 EXECUTE definition;
END $migration$;

-- An ordinary invoice for an explicitly unregistered shop is not a GST source document.
-- Regular/composition registrations still require the original transaction-bound snapshot.
DO $migration$ DECLARE definition text; target text; BEGIN
 definition:=pg_get_functiondef('public.sf_gst_capture_sale(bigint,text)'::regprocedure);
 target:='IF s.status<>''COMPLETED'' THEN';
 IF position(target IN definition)=0 THEN RAISE EXCEPTION 'Unexpected GST capture definition'; END IF;
 definition:=replace(definition,target,$branch$IF r.registration_type='UNREGISTERED' AND EXISTS(
   SELECT 1 FROM public.sale_items i WHERE i.tenant_id=s.tenant_id AND i.sale_id=id AND i.is_manual
    AND i.manual_tax_snapshot->>'gstOptional'='true' AND i.manual_tax_snapshot->>'registrationType'='UNREGISTERED') THEN RETURN NULL; END IF;
 IF s.status<>'COMPLETED' THEN$branch$);
 EXECUTE definition;
END $migration$;

CREATE OR REPLACE FUNCTION public.sf_gst_sale_commit_guard() RETURNS trigger LANGUAGE plpgsql SECURITY DEFINER SET search_path=pg_catalog,public AS $$ BEGIN
 IF NEW.status='COMPLETED' AND EXISTS(SELECT 1 FROM public.gst_shop_mappings m
  WHERE m.tenant_id=NEW.tenant_id AND m.business_profile_code=NEW.business_profile_code AND m.store_id=NEW.store_id)
  AND NOT EXISTS(SELECT 1 FROM public.gst_documents d WHERE d.tenant_id=NEW.tenant_id AND d.sale_id=NEW.sale_id)
  AND NOT EXISTS(SELECT 1 FROM public.sale_items i WHERE i.tenant_id=NEW.tenant_id AND i.sale_id=NEW.sale_id AND i.is_manual
   AND i.manual_tax_snapshot->>'gstOptional'='true' AND i.manual_tax_snapshot->>'registrationType'='UNREGISTERED') THEN
  RAISE EXCEPTION 'Mapped invoice needs a GST snapshot in the same transaction' USING ERRCODE='23514';
 END IF; RETURN NEW;
END $$;
REVOKE ALL ON FUNCTION public.sf_gst_sale_commit_guard() FROM PUBLIC;
COMMIT;
