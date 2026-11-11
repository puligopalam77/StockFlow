-- REVIEW TOOL ONLY. Does not create, update, seed or migrate anything.
-- Use ONLY with an explicitly selected local PostgreSQL connection and working
-- credentials. Do not use an application/remote connection string by default.
BEGIN READ ONLY;
DO $$ BEGIN
 IF inet_server_addr() IS NULL OR inet_server_addr() NOT IN('127.0.0.1'::inet,'::1'::inet)
 THEN RAISE EXCEPTION 'Local loopback PostgreSQL connection required'; END IF;
END $$;
SELECT current_database() AS database_name,inet_server_addr() AS server_address,
 inet_server_port() AS server_port,current_user AS connected_role,
 current_setting('cluster_name') AS cluster_marker,current_setting('server_version') AS server_version;
SELECT datname AS database_name FROM pg_database WHERE NOT datistemplate ORDER BY datname;
SELECT count(*) AS public_table_count FROM information_schema.tables
 WHERE table_schema='public' AND table_type='BASE TABLE';
SELECT name,to_regclass('public.'||name) IS NOT NULL AS present
 FROM unnest(ARRAY['tenants','users','stores','store_locations','module_actions',
 'auth_protection_keys','auth_sessions','sales','payments','supplier_payments',
 'expenses','expense_payments','cash_adjustments','cash_opening_expense_reviews',
 'bank_accounts','bank_movements','bank_movement_allocations']) AS name;
SELECT signature,to_regprocedure(signature) IS NOT NULL AS present
 FROM unnest(ARRAY['public.sf_snapshot_retail_variant()',
 'public.sf_read_protection_keys()','public.sf_write_protection_key(text,text)',
 'public.sf_cash_entry_reason_guard()']) AS signature;
SELECT table_name,column_name,data_type FROM information_schema.columns
 WHERE table_schema='public' AND table_name IN('sale_items','purchase_items')
 AND column_name IN('variant_attributes_snapshot','sku_snapshot',
 'unit_of_measure_snapshot','product_name_snapshot') ORDER BY table_name,column_name;
SELECT tgrelid::regclass::text AS table_name,tgname,tgenabled
 FROM pg_trigger WHERE NOT tgisinternal AND tgname='zz_retail_variant_snapshot';
COMMIT;
