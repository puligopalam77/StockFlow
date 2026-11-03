-- MANUAL ONLY, after 073. Back up, stop writers, run the ENTIRE file as postgres.
-- No historical invoice, stock, payment, SKU or discount values are rewritten.
BEGIN;
SET LOCAL lock_timeout='15s';
DO $$ BEGIN
 IF NOT EXISTS(SELECT 1 FROM pg_roles WHERE rolname=current_user AND rolsuper) THEN RAISE EXCEPTION 'Run as PostgreSQL superuser'; END IF;
 IF to_regclass('public.gst_tax_rules') IS NULL THEN RAISE EXCEPTION 'Apply 073 first'; END IF;
END $$;
INSERT INTO public.module_actions(module_id,action_code,action_name,is_sensitive)
 SELECT module_id,'MANUAL_ITEM','Bill non-catalog items without stock posting',true FROM public.modules WHERE module_code='SALES';
INSERT INTO public.user_action_permissions(tenant_id,user_id,module_action_id,is_allowed)
 SELECT u.tenant_id,u.user_id,a.module_action_id,true FROM public.users u CROSS JOIN public.module_actions a JOIN public.modules m USING(module_id)
 WHERE u.is_tenant_owner AND m.module_code='SALES' AND a.action_code='MANUAL_ITEM';
INSERT INTO public.role_action_permissions(tenant_id,role_id,module_action_id,is_allowed)
 SELECT DISTINCT u.tenant_id,u.role_id,a.module_action_id,false FROM public.users u CROSS JOIN public.module_actions a JOIN public.modules m USING(module_id)
 WHERE m.module_code='SALES' AND a.action_code='MANUAL_ITEM' ON CONFLICT DO NOTHING;

ALTER TABLE public.sale_items ALTER COLUMN product_id DROP NOT NULL, ALTER COLUMN variant_id DROP NOT NULL,
 ADD COLUMN is_manual boolean NOT NULL DEFAULT false, ADD COLUMN description_snapshot varchar(1000),
 ADD COLUMN mrp_snapshot numeric(12,2), ADD COLUMN discount_type varchar(12) NOT NULL DEFAULT 'FIXED',
 ADD COLUMN discount_percent numeric(5,2), ADD COLUMN manual_tax_snapshot jsonb,
 ADD CONSTRAINT sale_line_identity CHECK(
  (NOT is_manual AND product_id IS NOT NULL AND variant_id IS NOT NULL AND manual_tax_snapshot IS NULL)
  OR (is_manual AND product_id IS NULL AND variant_id IS NULL AND product_unit_id IS NULL AND product_name_snapshot IS NOT NULL AND length(btrim(product_name_snapshot))>0 AND manual_tax_snapshot IS NOT NULL)),
 ADD CONSTRAINT sale_line_discount CHECK((discount_type='FIXED' AND discount_percent IS NULL) OR (discount_type='PERCENTAGE' AND discount_percent IS NOT NULL AND discount_percent BETWEEN 0 AND 100)),
 ADD CONSTRAINT sale_line_mrp CHECK(mrp_snapshot IS NULL OR mrp_snapshot>0);

-- Cashiers can read reviewed classifications for this shop without tax administration access.
CREATE FUNCTION public.sf_pos_manual_tax(id bigint DEFAULT NULL) RETURNS SETOF jsonb
LANGUAGE plpgsql STABLE SECURITY DEFINER SET search_path=pg_catalog,public AS $$
DECLARE context jsonb; BEGIN
 IF NOT public.sf_gst_allowed('CREATE','SALES') OR NOT public.sf_gst_allowed('MANUAL_ITEM','SALES') OR NOT public.sf_can_location(public.sf_location()) THEN
  RAISE EXCEPTION 'Manual item billing permission required' USING ERRCODE='42501'; END IF;
 context:=public.sf_gst_sale_context();
 RETURN QUERY SELECT r.tax || jsonb_build_object('ruleId',r.rule_id,'ruleName',r.rule_name,
  'taxRate',CASE WHEN context->>'registrationType' IN('COMPOSITION','UNREGISTERED') THEN 0 ELSE (r.tax->>'taxRate')::numeric END)
 FROM public.gst_tax_rules r WHERE r.tenant_id=public.sf_tenant() AND r.store_id=public.sf_store()
 AND r.business_profile_code=public.sf_visible_business_profile() AND r.review_status='REVIEWED' AND r.status='ACTIVE'
 AND CURRENT_DATE BETWEEN r.effective_from AND coalesce(r.effective_to,'infinity') AND (id IS NULL OR r.rule_id=id)
 -- Retain the existing POS calculator's supported issuance boundary.
 AND r.tax->>'classification'='GOODS' AND r.tax->>'treatment' IN('TAXABLE','NIL','EXEMPT','NON_GST')
 AND coalesce((r.tax->>'reverseCharge')::boolean,false)=false AND coalesce((r.tax->>'cessRate')::numeric,0)=0
 ORDER BY r.rule_name,r.rule_id;
END $$;
REVOKE ALL ON FUNCTION public.sf_pos_manual_tax(bigint) FROM PUBLIC;
GRANT EXECUTE ON FUNCTION public.sf_pos_manual_tax(bigint) TO stockflow_runtime;

-- Catalog guards continue to apply to every registered line. Manual lines have their own guard.
DROP TRIGGER z_active_catalog_write ON public.sale_items;
CREATE TRIGGER z_active_catalog_write BEFORE INSERT ON public.sale_items FOR EACH ROW WHEN(NOT NEW.is_manual) EXECUTE FUNCTION public.sf_active_catalog_write_guard();
DROP TRIGGER zz_retail_variant_snapshot ON public.sale_items;
CREATE TRIGGER zz_retail_variant_snapshot BEFORE INSERT OR UPDATE ON public.sale_items FOR EACH ROW WHEN(NOT NEW.is_manual) EXECUTE FUNCTION public.sf_snapshot_retail_variant();
CREATE FUNCTION public.sf_pos_line_guard() RETURNS trigger LANGUAGE plpgsql SET search_path=pg_catalog,public AS $$
DECLARE tax jsonb; BEGIN
 IF TG_OP='UPDATE' THEN
  IF (NEW.is_manual,NEW.description_snapshot,NEW.mrp_snapshot,NEW.discount_type,NEW.discount_percent,NEW.manual_tax_snapshot)
   IS DISTINCT FROM (OLD.is_manual,OLD.description_snapshot,OLD.mrp_snapshot,OLD.discount_type,OLD.discount_percent,OLD.manual_tax_snapshot)
   OR OLD.is_manual AND to_jsonb(NEW) IS DISTINCT FROM to_jsonb(OLD) THEN
   RAISE EXCEPTION 'Posted line snapshots cannot be rewritten' USING ERRCODE='23514'; END IF;
  RETURN NEW;
 END IF;
 IF NEW.is_manual THEN
  IF NOT EXISTS(SELECT 1 FROM public.sales s WHERE s.tenant_id=NEW.tenant_id AND s.sale_id=NEW.sale_id
   AND s.created_by=public.sf_actor() AND s.location_id=public.sf_location() AND s.status='COMPLETED' AND s.xmin=pg_current_xact_id()::xid) THEN
   RAISE EXCEPTION 'Manual items can only be posted in the original checkout transaction' USING ERRCODE='23514'; END IF;
  SELECT value INTO tax FROM public.sf_pos_manual_tax((NEW.manual_tax_snapshot->>'ruleId')::bigint) value;
  IF tax IS NULL OR tax IS DISTINCT FROM NEW.manual_tax_snapshot OR NEW.tax_percentage<>(tax->>'taxRate')::numeric THEN
   RAISE EXCEPTION 'Select a current reviewed tax rule for this shop' USING ERRCODE='23514'; END IF;
  NEW.unit_of_measure_snapshot:=tax->>'uqc';NEW.sku_snapshot:=NULL;NEW.variant_name_snapshot:=NULL;NEW.variant_attributes_snapshot:='{}';
 ELSE
  SELECT nullif(mrp,0) INTO NEW.mrp_snapshot FROM public.product_variants WHERE tenant_id=NEW.tenant_id AND variant_id=NEW.variant_id;
 END IF;
 IF NEW.mrp_snapshot IS NOT NULL AND NEW.line_total>NEW.quantity*NEW.mrp_snapshot THEN
  RAISE EXCEPTION 'Customer payable line amount including tax exceeds MRP' USING ERRCODE='23514'; END IF;
 RETURN NEW;
END $$;
CREATE TRIGGER zy_pos_line_guard BEFORE INSERT OR UPDATE ON public.sale_items FOR EACH ROW EXECUTE FUNCTION public.sf_pos_line_guard();
REVOKE ALL ON FUNCTION public.sf_pos_line_guard() FROM PUBLIC;

CREATE FUNCTION public.sf_pos_payment_reference_used(mode text,reference text) RETURNS boolean
LANGUAGE sql STABLE SECURITY DEFINER SET search_path=pg_catalog,public AS $$
 SELECT public.sf_gst_allowed('CREATE','SALES') AND public.sf_can_location(public.sf_location()) AND EXISTS(
  SELECT 1 FROM public.payments p JOIN public.sales s USING(tenant_id,sale_id)
  WHERE s.tenant_id=public.sf_tenant() AND s.store_id=public.sf_store() AND s.business_profile_code=public.sf_visible_business_profile()
  AND s.status='COMPLETED' AND p.payment_mode=mode AND upper(btrim(p.reference_number))=upper(btrim(reference)))
$$;
REVOKE ALL ON FUNCTION public.sf_pos_payment_reference_used(text,text) FROM PUBLIC;
GRANT EXECUTE ON FUNCTION public.sf_pos_payment_reference_used(text,text) TO stockflow_runtime;

-- Reuse the original transaction-bound GST capture and component calculations.
DO $migration$ DECLARE definition text; old_select text; BEGIN
 definition:=pg_get_functiondef('public.sf_gst_capture_sale(bigint,text)'::regprocedure);
 old_select:='SELECT * INTO tax FROM public.sf_gst_resolve_tax(item.product_id,item.variant_id,s.sale_date::date);';
 IF position(old_select IN definition)=0 OR position('JOIN public.products p USING(tenant_id,product_id)' IN definition)=0 THEN RAISE EXCEPTION 'Unexpected GST capture definition'; END IF;
 definition:=replace(definition,'JOIN public.products p USING(tenant_id,product_id)','LEFT JOIN public.products p USING(tenant_id,product_id)');
 definition:=replace(definition,old_select,$branch$IF item.is_manual THEN
   SELECT * INTO tax FROM jsonb_populate_record(NULL::public.gst_tax_configurations,jsonb_build_object(
    'rule_id',item.manual_tax_snapshot->'ruleId','classification',item.manual_tax_snapshot->'classification',
    'hsn_sac',item.manual_tax_snapshot->'hsnSac','uqc',item.manual_tax_snapshot->'uqc','treatment',item.manual_tax_snapshot->'treatment',
    'tax_rate',item.manual_tax_snapshot->'taxRate','cess_rate',item.manual_tax_snapshot->'cessRate',
    'reverse_charge',item.manual_tax_snapshot->'reverseCharge','prices_include_tax',item.manual_tax_snapshot->'pricesIncludeTax'));
  ELSE SELECT * INTO tax FROM public.sf_gst_resolve_tax(item.product_id,item.variant_id,s.sale_date::date); END IF;$branch$);
 definition:=replace(definition,'''sourceLineId'',item.sale_item_id','''isManual'',item.is_manual,''sourceLineId'',item.sale_item_id');
 EXECUTE definition;
END $migration$;

ALTER TABLE public.sale_return_items ALTER COLUMN product_id DROP NOT NULL, ALTER COLUMN variant_id DROP NOT NULL;
ALTER TABLE public.sale_return_items DROP CONSTRAINT sale_return_items_disposition_check;
ALTER TABLE public.sale_return_items ADD CONSTRAINT sale_return_items_disposition_check CHECK(disposition IN('RESTOCK','DAMAGED','SUPPLIER_RETURN','SERVICE_HOLD','NON_STOCK'));
ALTER TABLE public.sale_return_items ADD CONSTRAINT return_stock_identity CHECK(
 (product_id IS NOT NULL AND variant_id IS NOT NULL AND disposition<>'NON_STOCK') OR (product_id IS NULL AND variant_id IS NULL AND product_unit_id IS NULL AND disposition='NON_STOCK'));
CREATE FUNCTION public.sf_pos_return_guard() RETURNS trigger LANGUAGE plpgsql AS $$ BEGIN
 IF NOT EXISTS(SELECT 1 FROM public.sale_items i WHERE i.tenant_id=NEW.tenant_id AND i.sale_item_id=NEW.sale_item_id
  AND i.product_id IS NOT DISTINCT FROM NEW.product_id AND i.variant_id IS NOT DISTINCT FROM NEW.variant_id
  AND i.product_unit_id IS NOT DISTINCT FROM NEW.product_unit_id) THEN
  RAISE EXCEPTION 'Return must preserve original line identity' USING ERRCODE='23514'; END IF;
 RETURN NEW; END $$;
CREATE TRIGGER pos_return_guard BEFORE INSERT ON public.sale_return_items FOR EACH ROW EXECUTE FUNCTION public.sf_pos_return_guard();
REVOKE ALL ON FUNCTION public.sf_pos_return_guard() FROM PUBLIC;
COMMIT;
