-- Phase 2 baseline from the supplied roadmap. Run against stockflow_db; existing tables and indexes are skipped.

CREATE TABLE IF NOT EXISTS warranties
(
    warranty_id BIGSERIAL PRIMARY KEY,
    sale_item_id BIGINT NOT NULL,
    product_unit_id BIGINT,
    warranty_start_date DATE NOT NULL,
    warranty_end_date DATE NOT NULL,
    warranty_status VARCHAR(30) NOT NULL DEFAULT 'ACTIVE',
    notes TEXT,
    created_at TIMESTAMP NOT NULL DEFAULT CURRENT_TIMESTAMP,

    CONSTRAINT fk_warranties_sale_item
        FOREIGN KEY (sale_item_id)
        REFERENCES sale_items(sale_item_id),

    CONSTRAINT fk_warranties_product_unit
        FOREIGN KEY (product_unit_id)
        REFERENCES product_units(product_unit_id)
);

CREATE TABLE IF NOT EXISTS service_requests
(
    service_request_id BIGSERIAL PRIMARY KEY,
    service_number VARCHAR(50) NOT NULL UNIQUE,
    customer_id INT NOT NULL,
    product_id INT NOT NULL,
    product_unit_id BIGINT,
    warranty_id BIGINT,
    complaint TEXT NOT NULL,
    diagnosis TEXT,
    repair_details TEXT,
    service_charge NUMERIC(12,2) NOT NULL DEFAULT 0,
    status VARCHAR(30) NOT NULL DEFAULT 'RECEIVED',
    received_date TIMESTAMP NOT NULL DEFAULT CURRENT_TIMESTAMP,
    expected_delivery_date DATE,
    delivered_date TIMESTAMP,
    assigned_to INT,
    notes TEXT,
    created_by INT NOT NULL,
    created_at TIMESTAMP NOT NULL DEFAULT CURRENT_TIMESTAMP,

    CONSTRAINT fk_service_customer
        FOREIGN KEY (customer_id)
        REFERENCES customers(customer_id),

    CONSTRAINT fk_service_product
        FOREIGN KEY (product_id)
        REFERENCES products(product_id),

    CONSTRAINT fk_service_unit
        FOREIGN KEY (product_unit_id)
        REFERENCES product_units(product_unit_id),

    CONSTRAINT fk_service_warranty
        FOREIGN KEY (warranty_id)
        REFERENCES warranties(warranty_id),

    CONSTRAINT fk_service_assigned_user
        FOREIGN KEY (assigned_to)
        REFERENCES users(user_id),

    CONSTRAINT fk_service_created_user
        FOREIGN KEY (created_by)
        REFERENCES users(user_id)
);

CREATE TABLE IF NOT EXISTS service_history
(
    service_history_id BIGSERIAL PRIMARY KEY,
    service_request_id BIGINT NOT NULL,
    old_status VARCHAR(30),
    new_status VARCHAR(30) NOT NULL,
    remarks TEXT,
    changed_by INT NOT NULL,
    changed_at TIMESTAMP NOT NULL DEFAULT CURRENT_TIMESTAMP,

    CONSTRAINT fk_service_history_request
        FOREIGN KEY (service_request_id)
        REFERENCES service_requests(service_request_id),

    CONSTRAINT fk_service_history_user
        FOREIGN KEY (changed_by)
        REFERENCES users(user_id)
);

CREATE INDEX IF NOT EXISTS idx_service_requests_status
ON service_requests(status);
