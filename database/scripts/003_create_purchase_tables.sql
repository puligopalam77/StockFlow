-- Phase 2 baseline from the supplied roadmap. Run against stockflow_db; existing tables and indexes are skipped.

CREATE TABLE IF NOT EXISTS purchases
(
    purchase_id BIGSERIAL PRIMARY KEY,
    supplier_id INT NOT NULL,
    purchase_number VARCHAR(50) NOT NULL UNIQUE,
    supplier_invoice_number VARCHAR(100),
    purchase_date DATE NOT NULL,
    received_date DATE,
    subtotal NUMERIC(12,2) NOT NULL DEFAULT 0,
    tax_amount NUMERIC(12,2) NOT NULL DEFAULT 0,
    discount_amount NUMERIC(12,2) NOT NULL DEFAULT 0,
    total_amount NUMERIC(12,2) NOT NULL DEFAULT 0,
    status VARCHAR(30) NOT NULL DEFAULT 'COMPLETED',
    notes TEXT,
    created_by INT NOT NULL,
    created_at TIMESTAMP NOT NULL DEFAULT CURRENT_TIMESTAMP,

    CONSTRAINT fk_purchases_supplier
        FOREIGN KEY (supplier_id)
        REFERENCES suppliers(supplier_id),

    CONSTRAINT fk_purchases_user
        FOREIGN KEY (created_by)
        REFERENCES users(user_id)
);

CREATE TABLE IF NOT EXISTS purchase_items
(
    purchase_item_id BIGSERIAL PRIMARY KEY,
    purchase_id BIGINT NOT NULL,
    product_id INT NOT NULL,
    quantity INT NOT NULL,
    purchase_price NUMERIC(12,2) NOT NULL,
    tax_percentage NUMERIC(5,2) NOT NULL DEFAULT 0,
    line_total NUMERIC(12,2) NOT NULL,

    CONSTRAINT fk_purchase_items_purchase
        FOREIGN KEY (purchase_id)
        REFERENCES purchases(purchase_id),

    CONSTRAINT fk_purchase_items_product
        FOREIGN KEY (product_id)
        REFERENCES products(product_id)
);

CREATE TABLE IF NOT EXISTS inventory_transactions
(
    inventory_transaction_id BIGSERIAL PRIMARY KEY,
    product_id INT NOT NULL,
    product_unit_id BIGINT,
    transaction_type VARCHAR(30) NOT NULL,
    quantity INT NOT NULL,
    reference_type VARCHAR(30),
    reference_id BIGINT,
    remarks TEXT,
    created_by INT NOT NULL,
    created_at TIMESTAMP NOT NULL DEFAULT CURRENT_TIMESTAMP,

    CONSTRAINT fk_inventory_product
        FOREIGN KEY (product_id)
        REFERENCES products(product_id),

    CONSTRAINT fk_inventory_product_unit
        FOREIGN KEY (product_unit_id)
        REFERENCES product_units(product_unit_id),

    CONSTRAINT fk_inventory_user
        FOREIGN KEY (created_by)
        REFERENCES users(user_id)
);

CREATE INDEX IF NOT EXISTS idx_purchases_purchase_number
ON purchases(purchase_number);

CREATE INDEX IF NOT EXISTS idx_inventory_transactions_product
ON inventory_transactions(product_id);

CREATE INDEX IF NOT EXISTS idx_inventory_transactions_created_at
ON inventory_transactions(created_at);
