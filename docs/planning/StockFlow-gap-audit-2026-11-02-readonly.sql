-- Audit evidence only. Run with psql -X -v ON_ERROR_STOP=1 against the intended database.
-- No application migrations or business data are changed. No contact details or secrets are selected.
BEGIN ISOLATION LEVEL REPEATABLE READ READ ONLY;
SELECT current_database() AS inspected_database, current_setting('server_version') AS postgres_version;
SELECT count(*) AS public_tables FROM pg_tables WHERE schemaname='public';
SELECT table_name FROM information_schema.tables WHERE table_schema='public' ORDER BY table_name;
SELECT table_name,column_name,data_type FROM information_schema.columns
 WHERE table_schema='public' AND table_name IN ('inventory_transactions','purchase_items','purchase_receipt_items','sale_items','billing_policy')
 ORDER BY table_name,ordinal_position;
SELECT tablename,policyname,roles,cmd,qual,with_check FROM pg_policies
 WHERE schemaname='public' AND tablename IN ('customers','suppliers','categories','products','sales','purchases','inventory_transactions','billing_policy')
 ORDER BY tablename,policyname;
SELECT c.relname,c.relrowsecurity,c.relforcerowsecurity,
 has_table_privilege('stockflow_runtime',c.oid,'SELECT') AS runtime_select,
 has_table_privilege('stockflow_runtime',c.oid,'INSERT') AS runtime_insert,
 has_table_privilege('stockflow_runtime',c.oid,'UPDATE') AS runtime_update,
 has_table_privilege('stockflow_runtime',c.oid,'DELETE') AS runtime_delete
 FROM pg_class c JOIN pg_namespace n ON n.oid=c.relnamespace
 WHERE n.nspname='public' AND c.relkind='r' AND NOT c.relrowsecurity ORDER BY c.relname;
SELECT store_id,tenant_id,is_active FROM stores ORDER BY tenant_id,store_id;
SELECT tenant_id,business_profile_code FROM tenants ORDER BY tenant_id;
SELECT to_regprocedure('public.sf_billing_policy()') IS NOT NULL AS policy_082_present,
 EXISTS(SELECT 1 FROM module_actions a JOIN modules m USING(module_id) WHERE m.module_code='SALES' AND a.action_code='TAX_OVERRIDE') AS permission_081_present;
-- Deterministic counterexamples using the expressions in OperationsRepository.ProductSalesAsync.
-- These are synthetic VALUES only, not edits or recalculations of posted records.
WITH purchase_items(variant_id,purchase_price,received_quantity,received_day) AS
 (VALUES (1,50::numeric,10,DATE '2026-01-01'),(1,150::numeric,10,DATE '2026-03-01'))
SELECT sum(purchase_price*received_quantity)/sum(received_quantity) AS current_report_unit_cost,
 sum(purchase_price*received_quantity) FILTER(WHERE received_day<=DATE '2026-01-31')/
 sum(received_quantity) FILTER(WHERE received_day<=DATE '2026-01-31') AS january_available_unit_cost
 FROM purchase_items;
SELECT 118::numeric AS invoice_line_including_18_gst,80::numeric AS purchase_cost,
 118-80 AS report_estimated_profit,100-80 AS tax_exclusive_gross_margin,
 118-coalesce(NULL::numeric,0) AS report_margin_with_unknown_catalog_cost;
WITH sold(variant_id,qty) AS (VALUES(1,1)),ret(variant_id,qty) AS (VALUES(2,1))
SELECT (SELECT count(*) FROM sold s LEFT JOIN ret r USING(variant_id)) AS report_row_count,
 (SELECT count(*) FROM ret r WHERE NOT EXISTS(SELECT 1 FROM sold s WHERE s.variant_id=r.variant_id)) AS return_only_variants_omitted;
COMMIT;
