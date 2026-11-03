-- OPTIONAL performance indexes for Phase 13. Run manually after migration 015.
-- The dashboard works with the Phase 12 schema; no data backfill or new tables are required.
BEGIN;
CREATE INDEX IF NOT EXISTS idx_sales_completed_dashboard ON sales(sale_date DESC,sale_id DESC)
    WHERE status='COMPLETED';
CREATE INDEX IF NOT EXISTS idx_warranties_dashboard_expiry ON warranties(warranty_end_date,sale_item_id)
    WHERE warranty_status IN ('ACTIVE','CLAIMED');
CREATE INDEX IF NOT EXISTS idx_inventory_dashboard_trend ON inventory_transactions(created_at,transaction_type);
COMMIT;
