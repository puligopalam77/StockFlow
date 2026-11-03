-- Run manually after 018. Existing completed purchases are treated as fully received.
BEGIN;
ALTER TABLE purchases DROP CONSTRAINT ck_purchases_values;
ALTER TABLE purchases ADD CONSTRAINT ck_purchases_values CHECK (
    subtotal>=0 AND tax_amount>=0 AND discount_amount>=0 AND total_amount>=0
    AND status IN ('DRAFT','PARTIALLY_RECEIVED','COMPLETED','CANCELLED'));
ALTER TABLE purchase_items ADD COLUMN received_quantity INT NOT NULL DEFAULT 0;
UPDATE purchase_items i SET received_quantity=i.quantity FROM purchases p WHERE p.purchase_id=i.purchase_id AND p.status='COMPLETED';
ALTER TABLE purchase_items ADD CONSTRAINT ck_received_quantity CHECK(received_quantity BETWEEN 0 AND quantity);
CREATE TABLE purchase_receipts (
    receipt_id BIGSERIAL PRIMARY KEY,
    purchase_id BIGINT NOT NULL REFERENCES purchases(purchase_id),
    received_date DATE NOT NULL,
    supplier_invoice_number VARCHAR(100),
    notes VARCHAR(2000),
    request_id UUID UNIQUE,
    request_payload JSONB,
    created_by INT NOT NULL REFERENCES users(user_id),
    created_at TIMESTAMP NOT NULL DEFAULT CURRENT_TIMESTAMP
);
CREATE TABLE purchase_receipt_items (
    receipt_item_id BIGSERIAL PRIMARY KEY,
    receipt_id BIGINT NOT NULL REFERENCES purchase_receipts(receipt_id),
    purchase_item_id BIGINT NOT NULL REFERENCES purchase_items(purchase_item_id),
    quantity INT NOT NULL CHECK(quantity>0),
    serial_numbers TEXT[] NOT NULL DEFAULT '{}',
    purchase_price NUMERIC(12,2) NOT NULL,
    discount_amount NUMERIC(12,2) NOT NULL DEFAULT 0,
    tax_percentage NUMERIC(5,2) NOT NULL,
    UNIQUE(receipt_id,purchase_item_id)
);
ALTER TABLE inventory_transactions ADD COLUMN receipt_id BIGINT REFERENCES purchase_receipts(receipt_id);
ALTER TABLE product_units ADD COLUMN receipt_id BIGINT REFERENCES purchase_receipts(receipt_id);
CREATE INDEX ON purchase_receipts(purchase_id);
COMMIT;
