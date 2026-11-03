-- OPTIONAL READ ONLY, after baseline through 027. No new migration is required for Phase 7.
-- Run as PostgreSQL administrator; this script reviews ALL tenants/stores.
-- Edit the date literals in the first query to match the report you are checking.
BEGIN TRANSACTION READ ONLY;
WITH period AS (SELECT DATE '2026-03-01' AS first_day,DATE '2026-03-31'+1 AS after_last_day),
movements AS (
 SELECT it.*,CASE WHEN transaction_type IN('PURCHASE','CUSTOMER_RETURN','ADJUSTMENT_IN','SERVICE_IN','TRANSFER_IN') THEN quantity::bigint
  WHEN transaction_type IN('SALE','SUPPLIER_RETURN','DAMAGED','ADJUSTMENT_OUT','SERVICE_OUT','WARRANTY_REPLACEMENT','TRANSFER_OUT','TRANSFER_LOSS') THEN -quantity::bigint ELSE 0 END signed
 FROM inventory_transactions it,period WHERE created_at<after_last_day
)
SELECT tenant_id,location_id,variant_id,
 coalesce(sum(signed) FILTER(WHERE created_at<first_day),0) AS opening_balance,
 coalesce(sum(signed) FILTER(WHERE created_at>=first_day),0) AS net_period_movement,
 sum(signed) AS closing_balance
FROM movements,period GROUP BY tenant_id,location_id,variant_id ORDER BY tenant_id,location_id,variant_id;

-- Integrity results below should return zero rows.
SELECT s.tenant_id,s.location_id,s.sale_id,s.invoice_number,s.total_amount,coalesce(sum(i.line_total),0) AS item_value
FROM sales s LEFT JOIN sale_items i USING(tenant_id,sale_id)
WHERE s.status='COMPLETED'
GROUP BY s.tenant_id,s.location_id,s.sale_id,s.invoice_number,s.total_amount
HAVING s.total_amount<>coalesce(sum(i.line_total),0);

WITH invoices AS (
 SELECT s.tenant_id,s.location_id,s.sale_id,sum(i.quantity::bigint) AS invoiced
 FROM sales s JOIN sale_items i USING(tenant_id,sale_id) WHERE s.status='COMPLETED'
 GROUP BY s.tenant_id,s.location_id,s.sale_id
), ledger AS (
 SELECT tenant_id,location_id,reference_id AS sale_id,sum(quantity::bigint) AS removed
 FROM inventory_transactions WHERE transaction_type='SALE' AND reference_type='SALE'
 GROUP BY tenant_id,location_id,reference_id
)
SELECT i.*,coalesce(l.removed,0) AS ledger_quantity FROM invoices i LEFT JOIN ledger l USING(tenant_id,location_id,sale_id)
WHERE i.invoiced<>coalesce(l.removed,0);

SELECT s.tenant_id,s.sale_id,s.total_amount,sum(p.amount) AS collected
FROM sales s JOIN payments p USING(tenant_id,sale_id)
WHERE s.status='COMPLETED' AND p.payment_mode<>'CREDIT'
GROUP BY s.tenant_id,s.sale_id,s.total_amount HAVING sum(p.amount)>s.total_amount;

SELECT tenant_id,inventory_transaction_id,quantity,transaction_type FROM inventory_transactions
WHERE quantity<=0 OR transaction_type NOT IN('PURCHASE','CUSTOMER_RETURN','ADJUSTMENT_IN','SERVICE_IN','TRANSFER_IN','SALE','SUPPLIER_RETURN','DAMAGED','ADJUSTMENT_OUT','SERVICE_OUT','WARRANTY_REPLACEMENT','TRANSFER_OUT','TRANSFER_LOSS');
ROLLBACK;
