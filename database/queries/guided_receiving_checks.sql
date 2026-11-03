-- READ ONLY: run manually as database administrator after migration 026.
BEGIN TRANSACTION READ ONLY;
SELECT current_database(),to_regclass('public.stock_intakes') AS intake_table,
 to_regclass('public.stock_intake_items') AS intake_items_table;

SELECT h.tenant_id,h.location_id,h.intake_id,h.kind,h.received_date,h.purchase_id,h.created_by,
 sum(i.quantity) AS recorded_quantity,sum(i.quantity*i.purchase_price) AS cost_excluding_tax
FROM stock_intakes h JOIN stock_intake_items i USING(tenant_id,location_id,intake_id)
GROUP BY h.tenant_id,h.location_id,h.intake_id,h.kind,h.received_date,h.purchase_id,h.created_by
ORDER BY h.intake_id DESC LIMIT 100;

-- Both queries should return zero rows.
SELECT request_id,count(*) FROM stock_intakes GROUP BY request_id HAVING count(*)>1;
SELECT h.intake_id,h.kind,h.purchase_id FROM stock_intakes h
LEFT JOIN purchases p ON p.tenant_id=h.tenant_id AND p.location_id=h.location_id AND p.purchase_id=h.purchase_id
WHERE (h.kind='OPENING_STOCK' AND h.purchase_id IS NOT NULL)
 OR (h.kind='RECEIPT' AND (p.purchase_id IS NULL OR p.status<>'COMPLETED'));

-- Opening quantities should match their ledger movements.
SELECT h.intake_id,h.tenant_id,h.location_id,
 (SELECT sum(quantity) FROM stock_intake_items i WHERE i.intake_id=h.intake_id) AS recorded_quantity,
 (SELECT sum(quantity) FROM inventory_transactions m WHERE m.tenant_id=h.tenant_id
 AND m.location_id=h.location_id AND m.reference_type='OPENING_STOCK' AND m.reference_id=h.intake_id) AS posted_quantity
FROM stock_intakes h WHERE h.kind='OPENING_STOCK' ORDER BY h.intake_id;
ROLLBACK;
