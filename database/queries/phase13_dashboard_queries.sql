-- Optional read-only dashboard verification. Apply migrations through 015 first.
-- All date windows use the database session's calendar.

-- 1. Business KPI values (Admin view).
SELECT
 (SELECT COALESCE(SUM(total_amount),0) FROM sales WHERE status='COMPLETED'
  AND sale_date>=CURRENT_DATE AND sale_date<CURRENT_DATE+1) AS today_sales_amount,
 (SELECT COALESCE(SUM(total_amount),0) FROM sales WHERE status='COMPLETED'
  AND sale_date>=date_trunc('month',CURRENT_DATE)
  AND sale_date<date_trunc('month',CURRENT_DATE)+INTERVAL '1 month') AS month_sales_amount,
 (SELECT COUNT(*) FROM sales WHERE status='COMPLETED'
  AND sale_date>=CURRENT_DATE AND sale_date<CURRENT_DATE+1) AS today_invoice_count,
 (SELECT COUNT(*) FROM products p JOIN vw_current_stock s USING(product_id)
  WHERE p.is_active AND s.current_stock<=p.minimum_stock_level) AS low_stock_count,
 (SELECT COUNT(*) FROM service_requests WHERE status NOT IN ('DELIVERED','CANCELLED')) AS pending_repairs,
 (SELECT COUNT(*) FROM service_requests WHERE status='READY') AS ready_for_delivery,
 (SELECT COUNT(*) FROM warranties w JOIN sale_items si USING(sale_item_id) JOIN sales s USING(sale_id)
  WHERE s.status='COMPLETED' AND w.warranty_status IN ('ACTIVE','CLAIMED')
  AND w.warranty_end_date BETWEEN CURRENT_DATE AND CURRENT_DATE+30) AS expiring_warranties;

-- 2. Inventory summary. SERVICE_IN is inbound; service parts and warranty replacements are outbound.
SELECT
 (SELECT COUNT(*) FROM products WHERE is_active) AS total_products,
 (SELECT COUNT(*) FROM suppliers WHERE is_active) AS total_suppliers,
 COALESCE(SUM(quantity::bigint) FILTER(WHERE transaction_type IN
   ('PURCHASE','CUSTOMER_RETURN','ADJUSTMENT_IN','SERVICE_IN')),0) AS stock_inflow,
 COALESCE(SUM(quantity::bigint) FILTER(WHERE transaction_type IN
   ('SALE','SUPPLIER_RETURN','DAMAGED','ADJUSTMENT_OUT','SERVICE_OUT','WARRANTY_REPLACEMENT')),0) AS stock_outflow,
 (SELECT COALESCE(SUM(current_stock),0) FROM vw_current_stock) AS current_stock
FROM inventory_transactions;

-- 3. Current physical stock for active products, by category.
SELECT c.category_name,SUM(s.current_stock) AS quantity
FROM products p JOIN categories c USING(category_id) JOIN vw_current_stock s USING(product_id)
WHERE p.is_active GROUP BY c.category_id,c.category_name ORDER BY quantity DESC,c.category_name;

-- 4. Low-stock report. Required is zero when stock equals the minimum.
SELECT p.product_name,c.category_name,s.current_stock,p.minimum_stock_level,
       GREATEST(p.minimum_stock_level::bigint-s.current_stock,0) AS required
FROM products p JOIN categories c USING(category_id) JOIN vw_current_stock s USING(product_id)
WHERE p.is_active AND s.current_stock<=p.minimum_stock_level
ORDER BY required DESC,p.product_name;

-- 5. Last 7 days, with zero-filled dates.
WITH days AS (SELECT generate_series(CURRENT_DATE-6,CURRENT_DATE,INTERVAL '1 day') AS day)
SELECT d.day,
 COALESCE(SUM(it.quantity::bigint) FILTER(WHERE it.transaction_type IN
   ('PURCHASE','CUSTOMER_RETURN','ADJUSTMENT_IN','SERVICE_IN')),0) AS inflow,
 COALESCE(SUM(it.quantity::bigint) FILTER(WHERE it.transaction_type IN
   ('SALE','SUPPLIER_RETURN','DAMAGED','ADJUSTMENT_OUT','SERVICE_OUT','WARRANTY_REPLACEMENT')),0) AS outflow
FROM days d LEFT JOIN inventory_transactions it ON it.created_at>=d.day AND it.created_at<d.day+INTERVAL '1 day'
GROUP BY d.day ORDER BY d.day;

-- 6. Unknown/legacy movements need reconciliation. Dashboard follows the Phase 11 stock view.
SELECT transaction_type,COUNT(*) FROM inventory_transactions
WHERE transaction_type NOT IN ('PURCHASE','CUSTOMER_RETURN','ADJUSTMENT_IN','SERVICE_IN',
 'SALE','SUPPLIER_RETURN','DAMAGED','ADJUSTMENT_OUT','SERVICE_OUT','WARRANTY_REPLACEMENT')
GROUP BY transaction_type;
