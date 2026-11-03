-- Phase 7 database setup only. Execute manually in StockFlow_DB after Phase 6.
-- Legacy ADJUSTMENT/SERVICE movements have ambiguous directions and must be reviewed first.
BEGIN;
DO $$
BEGIN
    IF EXISTS (SELECT 1 FROM inventory_transactions WHERE transaction_type NOT IN
        ('PURCHASE','CUSTOMER_RETURN','ADJUSTMENT_IN','SERVICE_IN','SALE','SUPPLIER_RETURN','DAMAGED','ADJUSTMENT_OUT','SERVICE_OUT')) THEN
        RAISE EXCEPTION 'Review legacy/unknown movement types before enabling the Phase 7 stock view. No history was changed.';
    END IF;
    IF EXISTS (SELECT 1 FROM product_units WHERE serial_number IS NOT NULL GROUP BY LOWER(BTRIM(serial_number)) HAVING COUNT(*) > 1) THEN
        RAISE EXCEPTION 'Resolve duplicate serial numbers (ignoring case/spaces) before creating the unique index.';
    END IF;
END $$;

CREATE INDEX IF NOT EXISTS idx_inventory_transactions_product ON inventory_transactions(product_id);
CREATE INDEX IF NOT EXISTS idx_inventory_transactions_type ON inventory_transactions(transaction_type);
CREATE INDEX IF NOT EXISTS idx_inventory_transactions_created_at ON inventory_transactions(created_at);
CREATE INDEX IF NOT EXISTS idx_product_units_product_status ON product_units(product_id, unit_status);
CREATE UNIQUE INDEX IF NOT EXISTS ux_product_units_serial_ci ON product_units(LOWER(BTRIM(serial_number))) WHERE serial_number IS NOT NULL;

CREATE OR REPLACE VIEW vw_current_stock AS
SELECT p.product_id,
    COALESCE(SUM(CASE
        WHEN it.transaction_type IN ('PURCHASE','CUSTOMER_RETURN','ADJUSTMENT_IN','SERVICE_IN') THEN it.quantity::BIGINT
        WHEN it.transaction_type IN ('SALE','SUPPLIER_RETURN','DAMAGED','ADJUSTMENT_OUT','SERVICE_OUT') THEN -it.quantity::BIGINT
        ELSE 0::BIGINT
    END), 0)::BIGINT AS current_stock
FROM products p
LEFT JOIN inventory_transactions it ON it.product_id = p.product_id
GROUP BY p.product_id;
COMMIT;
