-- READ ONLY: run after migration 028. This script does not change data.
BEGIN TRANSACTION READ ONLY;

SELECT name,to_regclass('public.'||name) AS installed_table
FROM unnest(ARRAY['sale_returns','sale_return_items','supplier_payments','purchase_returns','purchase_return_items',
 'stock_counts','stock_count_items','service_payments','sale_fulfilments','trade_ins','warranty_rmas']) name
ORDER BY name;

SELECT m.module_code,array_agg(a.action_code ORDER BY a.action_code) AS operation_actions
FROM modules m JOIN module_actions a ON a.module_id=m.module_id AND a.is_active
WHERE (m.module_code,a.action_code) IN (('SALES','COLLECT'),('SALES','RETURN'),('SALES','FULFIL'),
 ('PURCHASES','PAY'),('PURCHASES','RETURN'),('SERVICE','COLLECT'))
GROUP BY m.module_code ORDER BY m.module_code;

-- Every result below should be zero rows.
SELECT 'sale return exceeds sold quantity' issue,i.sale_item_id,sum(i.quantity) returned,s.quantity sold
FROM sale_return_items i JOIN sale_items s USING(sale_item_id) GROUP BY i.sale_item_id,s.quantity HAVING sum(i.quantity)>s.quantity
UNION ALL
SELECT 'purchase return exceeds received quantity',i.purchase_item_id,sum(i.quantity),p.received_quantity
FROM purchase_return_items i JOIN purchase_items p USING(purchase_item_id) GROUP BY i.purchase_item_id,p.received_quantity HAVING sum(i.quantity)>p.received_quantity;

SELECT 'negative customer balance' issue,s.sale_id id,s.total_amount-r.returned-p.paid+r.refunded balance
FROM sales s
CROSS JOIN LATERAL (SELECT coalesce(sum(return_total),0) returned,coalesce(sum(refund_amount),0) refunded FROM sale_returns WHERE sale_id=s.sale_id) r
CROSS JOIN LATERAL (SELECT coalesce(sum(amount),0) paid FROM payments WHERE sale_id=s.sale_id AND payment_mode<>'CREDIT') p
WHERE s.total_amount-r.returned-p.paid+r.refunded<0;

SELECT 'invalid operation movement' issue,inventory_transaction_id id,quantity::numeric balance
FROM inventory_transactions WHERE reference_type IN('SALE_RETURN','PURCHASE_RETURN','STOCK_COUNT') AND quantity<=0;

ROLLBACK;
