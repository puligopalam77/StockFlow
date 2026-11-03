-- Run manually in the StockFlow application database AFTER migration 014.
-- No inventory movements are posted by this migration.
BEGIN;
ALTER TABLE service_requests ADD COLUMN IF NOT EXISTS version BIGINT NOT NULL DEFAULT 1;
ALTER TABLE service_requests ADD COLUMN IF NOT EXISTS updated_at TIMESTAMP;
ALTER TABLE service_requests ADD COLUMN IF NOT EXISTS unit_status_before_service VARCHAR(30) NOT NULL DEFAULT 'SOLD';
ALTER TABLE service_requests ADD COLUMN IF NOT EXISTS under_warranty_at_receipt BOOLEAN;
ALTER TABLE service_history ADD COLUMN IF NOT EXISTS action_name VARCHAR(30) NOT NULL DEFAULT 'STATUS';
ALTER TABLE service_history ADD COLUMN IF NOT EXISTS details JSONB;

CREATE SEQUENCE IF NOT EXISTS service_request_number_seq AS BIGINT START WITH 1 NO CYCLE;
-- Preserve an existing sequence and skip numbers used by legacy tickets.
SELECT setval('service_request_number_seq', GREATEST(
    (SELECT CASE WHEN is_called THEN last_value+1 ELSE last_value END FROM service_request_number_seq),
    COALESCE((SELECT MAX(substring(service_number FROM '^SRV-[0-9]{4}-([0-9]+)$')::bigint) FROM service_requests),0)+1), false);

CREATE TABLE IF NOT EXISTS service_parts (
    service_part_id BIGSERIAL PRIMARY KEY,
    service_request_id BIGINT NOT NULL REFERENCES service_requests(service_request_id),
    product_id INT NOT NULL REFERENCES products(product_id),
    product_unit_id BIGINT REFERENCES product_units(product_unit_id),
    product_name_snapshot VARCHAR(150) NOT NULL,
    quantity INT NOT NULL CHECK (quantity > 0),
    unit_price NUMERIC(12,2) NOT NULL DEFAULT 0 CHECK (unit_price >= 0),
    total_amount NUMERIC(12,2) NOT NULL DEFAULT 0 CHECK (total_amount = quantity::numeric * unit_price),
    created_by INT NOT NULL REFERENCES users(user_id),
    created_at TIMESTAMP NOT NULL DEFAULT CURRENT_TIMESTAMP,
    CHECK (product_unit_id IS NULL OR quantity = 1)
);
CREATE TABLE IF NOT EXISTS service_operations (
    request_id UUID PRIMARY KEY,
    service_request_id BIGINT NOT NULL REFERENCES service_requests(service_request_id),
    operation VARCHAR(30) NOT NULL,
    payload JSONB NOT NULL,
    result_id BIGINT NOT NULL,
    created_by INT NOT NULL REFERENCES users(user_id),
    created_at TIMESTAMP NOT NULL DEFAULT CURRENT_TIMESTAMP
);
DO $$ BEGIN
    IF NOT EXISTS (SELECT 1 FROM pg_constraint WHERE conname='ck_service_request_values' AND conrelid='service_requests'::regclass) THEN
        ALTER TABLE service_requests ADD CONSTRAINT ck_service_request_values CHECK (
            status IN ('RECEIVED','INSPECTION','REPAIRING','WAITING_FOR_PART','READY','DELIVERED','CANCELLED')
            AND service_charge >= 0 AND version > 0
            AND (expected_delivery_date IS NULL OR expected_delivery_date >= received_date::date)
            AND (status <> 'DELIVERED' OR delivered_date IS NOT NULL));
    END IF;
END $$;
-- Existing duplicate active tickets must be reviewed manually; no history is deleted.
CREATE UNIQUE INDEX IF NOT EXISTS ux_service_open_unit ON service_requests(product_unit_id)
    WHERE product_unit_id IS NOT NULL AND status NOT IN ('DELIVERED','CANCELLED');
CREATE UNIQUE INDEX IF NOT EXISTS ux_service_part_unit ON service_parts(product_unit_id) WHERE product_unit_id IS NOT NULL;
CREATE INDEX IF NOT EXISTS idx_service_requests_status ON service_requests(status);
CREATE INDEX IF NOT EXISTS idx_service_requests_customer ON service_requests(customer_id);
CREATE INDEX IF NOT EXISTS idx_service_requests_product_unit ON service_requests(product_unit_id);
CREATE INDEX IF NOT EXISTS idx_service_requests_received_date ON service_requests(received_date DESC,service_request_id DESC);
CREATE INDEX IF NOT EXISTS idx_service_history_request ON service_history(service_request_id,service_history_id);
CREATE INDEX IF NOT EXISTS idx_service_parts_request ON service_parts(service_request_id,service_part_id);
COMMIT;
