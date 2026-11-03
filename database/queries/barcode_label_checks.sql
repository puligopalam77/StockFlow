-- OPTIONAL, READ ONLY: run as database administrator after existing migration 026.
-- Phase 4 adds no schema migration and does not change configuration data.
BEGIN TRANSACTION READ ONLY;
SELECT current_database(), to_regclass('public.variant_barcodes') AS variant_codes,
       to_regclass('public.product_units') AS units,
       to_regclass('public.stock_intakes') AS guided_receipts;

-- Identifier ambiguity check: should return zero rows. Aliases of the SAME target are allowed.
WITH identifiers AS (
 SELECT tenant_id, lower(btrim(barcode)) code, 'VARIANT:' || variant_id target
 FROM variant_barcodes WHERE is_active
 UNION ALL
 SELECT tenant_id, lower(btrim(code)), 'UNIT:' || product_unit_id
 FROM product_units CROSS JOIN LATERAL unnest(ARRAY[serial_number,barcode,manufacturer_barcode,internal_barcode]) code
 WHERE code IS NOT NULL AND btrim(code)<>''
)
SELECT tenant_id,code,count(DISTINCT target) AS matching_targets
FROM identifiers GROUP BY tenant_id,code HAVING count(DISTINCT target)>1;

-- Stored internal identifiers: reprinting must reuse these values, not regenerate them.
SELECT tenant_id,variant_id,barcode FROM variant_barcodes
WHERE barcode_source='INTERNAL' AND is_active ORDER BY variant_id DESC LIMIT 100;
SELECT tenant_id,location_id,product_unit_id,serial_number,manufacturer_barcode,internal_barcode
FROM product_units ORDER BY product_unit_id DESC LIMIT 100;

-- Receipt line identities and quantities available for optional label preparation.
SELECT h.tenant_id,h.location_id,h.intake_id,h.kind,i.variant_id,
       i.product_name_snapshot,i.variant_name_snapshot,i.quantity,cardinality(i.serial_numbers) AS individual_units
FROM stock_intakes h JOIN stock_intake_items i USING(tenant_id,location_id,intake_id)
ORDER BY h.intake_id DESC,i.intake_item_id LIMIT 100;
ROLLBACK;
