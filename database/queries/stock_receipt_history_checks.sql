-- Run after migration 031. Every query should return zero rows except the final summary.

SELECT receipt_id,receipt_number,status FROM purchase_receipts
WHERE receipt_number IS NULL OR btrim(receipt_number)='' OR status NOT IN('COMPLETED','REVERSED');

SELECT tenant_id,lower(receipt_number),count(*) FROM purchase_receipts
GROUP BY tenant_id,lower(receipt_number) HAVING count(*)>1;

SELECT inventory_transaction_id,reference_id AS purchase_id FROM inventory_transactions
WHERE transaction_type='PURCHASE' AND reference_type='PURCHASE' AND receipt_id IS NULL;

SELECT intake_id,intake_number,status FROM stock_intakes
WHERE intake_number IS NULL OR btrim(intake_number)='' OR status<>'COMPLETED';

SELECT tenant_id,lower(intake_number),count(*) FROM stock_intakes
GROUP BY tenant_id,lower(intake_number) HAVING count(*)>1;

SELECT module_id,module_name,module_code,route,is_active
FROM modules WHERE module_code='STOCK_INFLOW' AND
 (module_name<>'Stock receipt history' OR route<>'/StockInflow' OR NOT is_active);

SELECT
 (SELECT count(*) FROM purchase_receipts) AS purchase_receipts,
 (SELECT count(*) FROM stock_intakes WHERE kind='OPENING_STOCK') AS opening_stock_receipts,
 (SELECT count(*) FROM sale_returns) AS customer_returns,
 (SELECT count(DISTINCT reference_id) FROM inventory_transactions
   WHERE transaction_type='ADJUSTMENT_IN' AND reference_type='ADJUSTMENT') AS manual_positive_adjustments,
 (SELECT count(*) FROM stock_counts sc WHERE EXISTS(
   SELECT 1 FROM stock_count_items i WHERE i.stock_count_id=sc.stock_count_id AND i.variance>0)) AS positive_stock_counts;
