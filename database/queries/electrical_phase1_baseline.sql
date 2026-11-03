-- READ ONLY. Run as the database administrator immediately before and after
-- manual migration 046, while business writes are paused. Export both results.
-- All counts, values and ledger quantities below should remain identical.
-- No schema changes, inserts, updates, deletes or automatic stock corrections.
BEGIN TRANSACTION READ ONLY;

-- Review the tenant list before assigning the electrical baseline.
SELECT tenant_id, tenant_code, business_name, is_active
FROM public.tenants
ORDER BY tenant_id;

SELECT tenant_id, 'products' AS record_type, count(*) AS record_count FROM public.products GROUP BY tenant_id
UNION ALL SELECT tenant_id, 'product_variants', count(*) FROM public.product_variants GROUP BY tenant_id
UNION ALL SELECT tenant_id, 'variant_barcodes', count(*) FROM public.variant_barcodes GROUP BY tenant_id
UNION ALL SELECT tenant_id, 'product_units', count(*) FROM public.product_units GROUP BY tenant_id
UNION ALL SELECT tenant_id, 'purchases', count(*) FROM public.purchases GROUP BY tenant_id
UNION ALL SELECT tenant_id, 'purchase_items', count(*) FROM public.purchase_items GROUP BY tenant_id
UNION ALL SELECT tenant_id, 'purchase_receipts', count(*) FROM public.purchase_receipts GROUP BY tenant_id
UNION ALL SELECT tenant_id, 'purchase_receipt_items', count(*) FROM public.purchase_receipt_items GROUP BY tenant_id
UNION ALL SELECT tenant_id, 'sales', count(*) FROM public.sales GROUP BY tenant_id
UNION ALL SELECT tenant_id, 'sale_items', count(*) FROM public.sale_items GROUP BY tenant_id
UNION ALL SELECT tenant_id, 'warranties', count(*) FROM public.warranties GROUP BY tenant_id
UNION ALL SELECT tenant_id, 'warranty_claims', count(*) FROM public.warranty_claims GROUP BY tenant_id
UNION ALL SELECT tenant_id, 'service_requests', count(*) FROM public.service_requests GROUP BY tenant_id
UNION ALL SELECT tenant_id, 'inventory_transactions', count(*) FROM public.inventory_transactions GROUP BY tenant_id
ORDER BY tenant_id, record_type;

SELECT tenant_id, location_id, variant_id, transaction_type,
       count(*) AS movement_count, sum(quantity) AS movement_quantity
FROM public.inventory_transactions
GROUP BY tenant_id, location_id, variant_id, transaction_type
ORDER BY tenant_id, location_id, variant_id, transaction_type;

SELECT tenant_id, 'purchases' AS document_type, count(*) AS document_count,
       sum(total_amount) AS total_amount
FROM public.purchases GROUP BY tenant_id
UNION ALL
SELECT tenant_id, 'sales', count(*), sum(total_amount)
FROM public.sales GROUP BY tenant_id
ORDER BY tenant_id, document_type;

SELECT tenant_id, unit_status, count(*) AS physical_unit_count
FROM public.product_units
GROUP BY tenant_id, unit_status
ORDER BY tenant_id, unit_status;

COMMIT;
