-- MANUAL, read-only business data snapshot. Run as superuser with all writers stopped:
-- (1) BEFORE 051/052, save/export results; (2) AFTER migration, compare every count and hash.
-- No profile/stock/history is changed. Only session-local temporary results are created.
-- Run once more after a UI switch with no business postings; hashes must still match.
BEGIN;
DO $$ BEGIN
 IF NOT (SELECT rolsuper FROM pg_roles WHERE rolname=current_user) THEN
  RAISE EXCEPTION 'Use PostgreSQL superuser so hidden rows are included';
 END IF;
END $$;
CREATE TEMP TABLE sf_profile_preservation(table_name text,row_count bigint,original_data_hash text) ON COMMIT DROP;
DO $$ DECLARE tab text; expression text; BEGIN
 FOREACH tab IN ARRAY ARRAY['categories','products','product_variants','variant_barcodes','product_units','inventory_transactions',
  'sales','sale_items','payments','purchases','purchase_items','purchase_receipts','purchase_receipt_items',
  'stock_intakes','stock_intake_items','stock_transfers','stock_transfer_items','stock_transfer_units','stock_transfer_operations',
  'stock_counts','stock_count_items','sale_returns','sale_return_items','purchase_returns','purchase_return_items','supplier_payments',
  'service_requests','service_parts','service_history','service_operations','service_payments','warranties','warranty_claims','warranty_events','warranty_rmas',
  'sale_fulfilments','trade_ins','cash_closings','billing_drafts','billing_approvals','customers','suppliers','brands','brand_categories',
  'supplier_product_catalog','notification_outbox','notifications'] LOOP
  expression:='to_jsonb(r)';
  IF tab='brands' THEN expression:=expression||'-''aliases'''; END IF;
  IF tab='products' THEN expression:=expression||'-''product_status''-''product_images'''; END IF;
  IF tab=ANY(ARRAY['sales','purchases','stock_intakes','stock_transfers','stock_counts','cash_closings','billing_drafts',
   'billing_approvals','customers','suppliers','brands','supplier_product_catalog','notification_outbox']) THEN
   expression:=expression||'-''business_profile_code''';
  END IF;
  EXECUTE format($query$INSERT INTO sf_profile_preservation
   SELECT %L,count(*),md5(COALESCE(string_agg(hash,'' ORDER BY hash),''))
   FROM (SELECT md5((%s)::text) hash FROM public.%I r) source$query$,tab,expression,tab);
 END LOOP;
END $$;
SELECT * FROM sf_profile_preservation ORDER BY table_name;
ROLLBACK;
