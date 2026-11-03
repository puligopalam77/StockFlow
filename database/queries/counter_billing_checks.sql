-- OPTIONAL, READ ONLY: run as database administrator on the existing baseline through 026.
-- Phase 5 adds no schema migration. These queries do not post or alter sales.
BEGIN TRANSACTION READ ONLY;
SELECT current_database(),to_regclass('public.sales'),to_regclass('public.sale_items'),to_regclass('public.payments');

-- Each query below should return zero rows.
SELECT s.tenant_id,s.location_id,s.sale_id,s.invoice_number,s.total_amount,
       COALESCE(sum(p.amount),0) AS recorded_payment_including_credit
FROM sales s LEFT JOIN payments p ON p.tenant_id=s.tenant_id AND p.sale_id=s.sale_id
WHERE s.status='COMPLETED'
GROUP BY s.tenant_id,s.location_id,s.sale_id,s.invoice_number,s.total_amount
HAVING s.total_amount<>COALESCE(sum(p.amount),0);

SELECT i.tenant_id,i.product_unit_id,count(DISTINCT i.sale_id) AS completed_sales
FROM sale_items i JOIN sales s ON s.tenant_id=i.tenant_id AND s.sale_id=i.sale_id
WHERE i.product_unit_id IS NOT NULL AND s.status='COMPLETED'
GROUP BY i.tenant_id,i.product_unit_id HAVING count(DISTINCT i.sale_id)>1;

SELECT s.tenant_id,s.location_id,s.sale_id,s.invoice_number
FROM sales s JOIN payments p ON p.tenant_id=s.tenant_id AND p.sale_id=s.sale_id
WHERE s.status='COMPLETED' AND s.customer_id IS NULL AND p.payment_mode='CREDIT';

SELECT tenant_id,location_id,variant_id,current_stock FROM vw_location_current_stock WHERE current_stock<0;
SELECT request_id,count(*) FROM sales WHERE request_id IS NOT NULL GROUP BY request_id HAVING count(*)>1;
ROLLBACK;
