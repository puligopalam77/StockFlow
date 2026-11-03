-- Phase 2 baseline from the supplied roadmap. Run against stockflow_db; existing tables and indexes are skipped.

CREATE TABLE IF NOT EXISTS sales
(
    sale_id BIGSERIAL PRIMARY KEY,
    customer_id INT,
    invoice_number VARCHAR(50) NOT NULL UNIQUE,
    sale_date TIMESTAMP NOT NULL DEFAULT CURRENT_TIMESTAMP,
    subtotal NUMERIC(12,2) NOT NULL DEFAULT 0,
    discount_amount NUMERIC(12,2) NOT NULL DEFAULT 0,
    tax_amount NUMERIC(12,2) NOT NULL DEFAULT 0,
    total_amount NUMERIC(12,2) NOT NULL DEFAULT 0,
    status VARCHAR(30) NOT NULL DEFAULT 'COMPLETED',
    notes TEXT,
    created_by INT NOT NULL,
    created_at TIMESTAMP NOT NULL DEFAULT CURRENT_TIMESTAMP,

    CONSTRAINT fk_sales_customer
        FOREIGN KEY (customer_id)
        REFERENCES customers(customer_id),

    CONSTRAINT fk_sales_user
        FOREIGN KEY (created_by)
        REFERENCES users(user_id)
);

CREATE TABLE IF NOT EXISTS sale_items
(
    sale_item_id BIGSERIAL PRIMARY KEY,
    sale_id BIGINT NOT NULL,
    product_id INT NOT NULL,
    product_unit_id BIGINT,
    quantity INT NOT NULL,
    selling_price NUMERIC(12,2) NOT NULL,
    discount_amount NUMERIC(12,2) NOT NULL DEFAULT 0,
    tax_percentage NUMERIC(5,2) NOT NULL DEFAULT 0,
    line_total NUMERIC(12,2) NOT NULL,

    CONSTRAINT fk_sale_items_sale
        FOREIGN KEY (sale_id)
        REFERENCES sales(sale_id),

    CONSTRAINT fk_sale_items_product
        FOREIGN KEY (product_id)
        REFERENCES products(product_id),

    CONSTRAINT fk_sale_items_product_unit
        FOREIGN KEY (product_unit_id)
        REFERENCES product_units(product_unit_id)
);

CREATE TABLE IF NOT EXISTS payments
(
    payment_id BIGSERIAL PRIMARY KEY,
    sale_id BIGINT,
    payment_mode VARCHAR(30) NOT NULL,
    amount NUMERIC(12,2) NOT NULL,
    reference_number VARCHAR(100),
    payment_date TIMESTAMP NOT NULL DEFAULT CURRENT_TIMESTAMP,
    created_by INT NOT NULL,

    CONSTRAINT fk_payments_sale
        FOREIGN KEY (sale_id)
        REFERENCES sales(sale_id),

    CONSTRAINT fk_payments_user
        FOREIGN KEY (created_by)
        REFERENCES users(user_id)
);

CREATE INDEX IF NOT EXISTS idx_sales_invoice_number
ON sales(invoice_number);
