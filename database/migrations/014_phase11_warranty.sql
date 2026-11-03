-- Execute manually AFTER 013 in the StockFlow application database.
-- Existing ambiguous/duplicate warranties fail validation; no history is deleted.
BEGIN;
ALTER TABLE warranties ADD COLUMN IF NOT EXISTS warranty_type VARCHAR(30);
ALTER TABLE warranties ADD COLUMN IF NOT EXISTS warranty_provider VARCHAR(30);
ALTER TABLE warranties ADD COLUMN IF NOT EXISTS claim_count INT NOT NULL DEFAULT 0;
ALTER TABLE warranties ADD COLUMN IF NOT EXISTS replacement_product_unit_id BIGINT REFERENCES product_units(product_unit_id);
ALTER TABLE warranties ADD COLUMN IF NOT EXISTS parent_warranty_id BIGINT REFERENCES warranties(warranty_id);
ALTER TABLE warranties ADD COLUMN IF NOT EXISTS replacement_warranty_rule VARCHAR(30);
ALTER TABLE warranties ADD COLUMN IF NOT EXISTS updated_at TIMESTAMP;
CREATE SEQUENCE IF NOT EXISTS warranty_claim_number_seq AS BIGINT START WITH 1 NO CYCLE;
CREATE TABLE IF NOT EXISTS warranty_claims (
    warranty_claim_id BIGSERIAL PRIMARY KEY,
    warranty_id BIGINT NOT NULL REFERENCES warranties(warranty_id),
    claim_number VARCHAR(50) NOT NULL UNIQUE,
    claim_date TIMESTAMP NOT NULL DEFAULT CURRENT_TIMESTAMP,
    complaint VARCHAR(1000) NOT NULL CHECK (LENGTH(BTRIM(complaint)) > 0),
    claim_status VARCHAR(30) NOT NULL DEFAULT 'OPEN' CHECK (claim_status IN ('OPEN','UNDER_REVIEW','APPROVED','REJECTED','REPLACED','CLOSED')),
    resolution_type VARCHAR(30) CHECK (resolution_type IN ('REPAIR','REPLACEMENT','REJECTED','NO_FAULT_FOUND','MANUFACTURER_REFERRED')),
    resolution_notes VARCHAR(2000),
    replacement_product_unit_id BIGINT REFERENCES product_units(product_unit_id),
    replacement_warranty_id BIGINT REFERENCES warranties(warranty_id),
    created_by INT NOT NULL REFERENCES users(user_id),
    created_at TIMESTAMP NOT NULL DEFAULT CURRENT_TIMESTAMP,
    updated_at TIMESTAMP
);
CREATE TABLE IF NOT EXISTS warranty_events (
    warranty_event_id BIGSERIAL PRIMARY KEY,
    warranty_id BIGINT NOT NULL REFERENCES warranties(warranty_id),
    warranty_claim_id BIGINT REFERENCES warranty_claims(warranty_claim_id),
    action_name VARCHAR(30) NOT NULL,
    old_status VARCHAR(30), new_status VARCHAR(30), remarks TEXT,
    request_id UUID UNIQUE,
    request_payload JSONB,
    created_by INT NOT NULL REFERENCES users(user_id),
    created_at TIMESTAMP NOT NULL DEFAULT CURRENT_TIMESTAMP
);
DO $$ BEGIN
    IF NOT EXISTS(SELECT 1 FROM pg_constraint WHERE conname='ck_warranty_values' AND conrelid='warranties'::regclass) THEN
        ALTER TABLE warranties ADD CONSTRAINT ck_warranty_values CHECK (
            warranty_end_date >= warranty_start_date AND claim_count >= 0
            AND warranty_status IN ('ACTIVE','EXPIRED','CLAIMED','REPLACED','CANCELLED')
            AND (warranty_type IS NULL OR warranty_type IN ('FULL_REPLACEMENT','PRO_RATA','REPAIR','SERVICE_ONLY'))
            AND (warranty_provider IS NULL OR warranty_provider IN ('MANUFACTURER','DEALER','BOTH'))
            AND (replacement_warranty_rule IS NULL OR replacement_warranty_rule IN ('CONTINUE_ORIGINAL','NEW_FULL_WARRANTY','CUSTOM')));
    END IF;
    IF EXISTS(SELECT 1 FROM inventory_transactions WHERE transaction_type NOT IN
        ('PURCHASE','CUSTOMER_RETURN','ADJUSTMENT_IN','SERVICE_IN','SALE','SUPPLIER_RETURN','DAMAGED','ADJUSTMENT_OUT','SERVICE_OUT','WARRANTY_REPLACEMENT')) THEN
        RAISE EXCEPTION 'Review unknown or ambiguous inventory movement types before applying Phase 11.';
    END IF;
END $$;
CREATE UNIQUE INDEX IF NOT EXISTS ux_warranty_original_item ON warranties(sale_item_id) WHERE parent_warranty_id IS NULL;
CREATE UNIQUE INDEX IF NOT EXISTS ux_warranty_current_unit ON warranties(product_unit_id)
    WHERE product_unit_id IS NOT NULL AND warranty_status NOT IN ('REPLACED','CANCELLED');
CREATE UNIQUE INDEX IF NOT EXISTS ux_warranty_open_claim ON warranty_claims(warranty_id) WHERE claim_status IN ('OPEN','UNDER_REVIEW','APPROVED');
CREATE INDEX IF NOT EXISTS idx_warranties_product_unit ON warranties(product_unit_id);
CREATE INDEX IF NOT EXISTS idx_warranties_end_date ON warranties(warranty_end_date);
CREATE INDEX IF NOT EXISTS idx_warranties_status ON warranties(warranty_status);
CREATE INDEX IF NOT EXISTS idx_warranty_claims_warranty ON warranty_claims(warranty_id);
CREATE INDEX IF NOT EXISTS idx_warranty_claims_status ON warranty_claims(claim_status);
CREATE INDEX IF NOT EXISTS idx_warranty_events_warranty ON warranty_events(warranty_id,warranty_event_id);
CREATE OR REPLACE VIEW vw_current_stock AS
SELECT p.product_id,COALESCE(SUM(CASE
    WHEN it.transaction_type IN ('PURCHASE','CUSTOMER_RETURN','ADJUSTMENT_IN','SERVICE_IN') THEN it.quantity::BIGINT
    WHEN it.transaction_type IN ('SALE','SUPPLIER_RETURN','DAMAGED','ADJUSTMENT_OUT','SERVICE_OUT','WARRANTY_REPLACEMENT') THEN -it.quantity::BIGINT
    ELSE 0::BIGINT END),0)::BIGINT AS current_stock
FROM products p LEFT JOIN inventory_transactions it ON it.product_id=p.product_id GROUP BY p.product_id;
COMMIT;
