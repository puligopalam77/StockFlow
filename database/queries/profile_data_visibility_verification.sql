-- MANUAL after 051. Run as superuser in pgAdmin. Edit ONLY the three IDs below.
-- Pick an active TENANT OWNER and one of that company's stock locations.
-- Run with Bike selected, then switch through the UI and repeat with Electrical.
-- No persistent records are written; settings/results are session-local and rolled back.
BEGIN;
SELECT set_config('app.tenant_id','0',true);   -- replace 0 with tenant_id
SELECT set_config('app.user_id','0',true);     -- replace 0 with owner user_id
SELECT set_config('app.location_id','0',true); -- replace 0 with location_id
SELECT set_config('app.store_id','0',true);
DO $$ BEGIN
 IF NOT (SELECT rolsuper FROM pg_roles WHERE rolname=current_user) THEN RAISE EXCEPTION 'Start this verification as superuser'; END IF;
 IF public.sf_tenant()<=0 OR public.sf_actor()<=0 OR public.sf_location()<=0 THEN RAISE EXCEPTION 'Edit the tenant, owner and location IDs first'; END IF;
 IF NOT public.sf_owner() OR NOT public.sf_can_location(public.sf_location()) THEN RAISE EXCEPTION 'Use an active tenant owner and an authorized location'; END IF;
 IF NOT EXISTS(SELECT 1 FROM public.tenants WHERE tenant_id=public.sf_tenant() AND profile_data_visibility_enabled) THEN RAISE EXCEPTION '051 is not enabled'; END IF;
END $$;
SELECT set_config('app.business_profile_code',business_profile_code,true) FROM public.tenants WHERE tenant_id=public.sf_tenant();

-- Owner-side expected IDs are calculated without RLS. Runtime-side IDs below
-- exercise the actual restrictive policies and must match exactly (not just counts).
CREATE TEMP TABLE sf_profile_expected(table_name text PRIMARY KEY,ids jsonb NOT NULL) ON COMMIT DROP;
CREATE TEMP TABLE sf_profile_actual(table_name text PRIMARY KEY,ids jsonb NOT NULL) ON COMMIT DROP;
DO $$ DECLARE spec text[]; BEGIN
 FOREACH spec SLICE 1 IN ARRAY ARRAY[
  ['categories','category_id'],['products','product_id'],['product_variants','variant_id'],['product_units','product_unit_id'],
  ['inventory_transactions','inventory_transaction_id'],['sales','sale_id'],['sale_items','sale_item_id'],['payments','payment_id'],
  ['purchases','purchase_id'],['purchase_items','purchase_item_id'],['purchase_receipts','receipt_id'],['purchase_receipt_items','receipt_item_id'],
  ['stock_intakes','intake_id'],['stock_intake_items','intake_item_id'],['stock_transfers','transfer_id'],['stock_transfer_items','transfer_item_id'],
  ['stock_counts','stock_count_id'],['stock_count_items','stock_count_item_id'],['cash_closings','closing_id'],
  ['sale_returns','sale_return_id'],['purchase_returns','purchase_return_id'],['supplier_payments','supplier_payment_id']] LOOP
  EXECUTE format($q$INSERT INTO sf_profile_expected
   SELECT %L,COALESCE(jsonb_agg(r.%I ORDER BY r.%I),'[]'::jsonb) FROM public.%I r
   WHERE r.tenant_id=public.sf_tenant()
   AND public.sf_record_business_profile(%L,to_jsonb(r))=public.sf_visible_business_profile()
   AND (NOT (to_jsonb(r) ? 'location_id') OR (to_jsonb(r)->>'location_id')::bigint=public.sf_location())$q$,
   spec[1],spec[2],spec[2],spec[1],spec[1]);
 END LOOP;
END $$;
GRANT SELECT ON sf_profile_expected TO stockflow_runtime;
GRANT SELECT,INSERT ON sf_profile_actual TO stockflow_runtime;
SET LOCAL ROLE stockflow_runtime;
DO $$ DECLARE spec text[]; BEGIN
 FOREACH spec SLICE 1 IN ARRAY ARRAY[
  ['categories','category_id'],['products','product_id'],['product_variants','variant_id'],['product_units','product_unit_id'],
  ['inventory_transactions','inventory_transaction_id'],['sales','sale_id'],['sale_items','sale_item_id'],['payments','payment_id'],
  ['purchases','purchase_id'],['purchase_items','purchase_item_id'],['purchase_receipts','receipt_id'],['purchase_receipt_items','receipt_item_id'],
  ['stock_intakes','intake_id'],['stock_intake_items','intake_item_id'],['stock_transfers','transfer_id'],['stock_transfer_items','transfer_item_id'],
  ['stock_counts','stock_count_id'],['stock_count_items','stock_count_item_id'],['cash_closings','closing_id'],
  ['sale_returns','sale_return_id'],['purchase_returns','purchase_return_id'],['supplier_payments','supplier_payment_id']] LOOP
  EXECUTE format('INSERT INTO sf_profile_actual SELECT %L,COALESCE(jsonb_agg(r.%I ORDER BY r.%I),''[]''::jsonb) FROM public.%I r',spec[1],spec[2],spec[2],spec[1]);
 END LOOP;
 IF EXISTS(SELECT 1 FROM sf_profile_expected e FULL JOIN sf_profile_actual a USING(table_name) WHERE e.ids IS DISTINCT FROM a.ids) THEN
  RAISE EXCEPTION 'Profile isolation mismatch; compare expected and actual IDs before rollout';
 END IF;
END $$;
SELECT current_setting('app.business_profile_code') profile,e.table_name,
 jsonb_array_length(e.ids) expected_count,jsonb_array_length(a.ids) visible_count,e.ids=a.ids passed
 FROM sf_profile_expected e JOIN sf_profile_actual a USING(table_name) ORDER BY e.table_name;

-- Stale connections must fail closed rather than show a different setup's records.
SELECT set_config('app.business_profile_code',CASE current_setting('app.business_profile_code') WHEN 'ELECTRICAL' THEN 'BIKE_ACCESSORIES' ELSE 'ELECTRICAL' END,true);
DO $$ BEGIN
 IF public.sf_visible_business_profile() IS NOT NULL OR EXISTS(SELECT 1 FROM public.sales)
  OR EXISTS(SELECT 1 FROM public.purchases) OR EXISTS(SELECT 1 FROM public.inventory_transactions)
  OR EXISTS(SELECT 1 FROM public.products) THEN RAISE EXCEPTION 'Stale profile connection exposed business data'; END IF;
END $$;
RESET ROLE;
ROLLBACK;
