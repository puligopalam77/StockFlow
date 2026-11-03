-- Phase 2 baseline from the supplied roadmap. Run against stockflow_db; existing tables and indexes are skipped.

CREATE TABLE IF NOT EXISTS roles
(
    role_id SERIAL PRIMARY KEY,
    role_name VARCHAR(50) NOT NULL UNIQUE,
    description VARCHAR(200),
    is_active BOOLEAN NOT NULL DEFAULT TRUE,
    created_at TIMESTAMP NOT NULL DEFAULT CURRENT_TIMESTAMP
);

CREATE TABLE IF NOT EXISTS users
(
    user_id SERIAL PRIMARY KEY,
    role_id INT NOT NULL,
    full_name VARCHAR(100) NOT NULL,
    username VARCHAR(50) NOT NULL UNIQUE,
    password_hash TEXT NOT NULL,
    mobile_number VARCHAR(20),
    email VARCHAR(150),
    is_active BOOLEAN NOT NULL DEFAULT TRUE,
    created_at TIMESTAMP NOT NULL DEFAULT CURRENT_TIMESTAMP,
    updated_at TIMESTAMP,

    CONSTRAINT fk_users_roles
        FOREIGN KEY (role_id)
        REFERENCES roles(role_id)
);

CREATE TABLE IF NOT EXISTS categories
(
    category_id SERIAL PRIMARY KEY,
    category_name VARCHAR(100) NOT NULL UNIQUE,
    description VARCHAR(250),
    is_active BOOLEAN NOT NULL DEFAULT TRUE,
    created_at TIMESTAMP NOT NULL DEFAULT CURRENT_TIMESTAMP
);

CREATE TABLE IF NOT EXISTS brands
(
    brand_id SERIAL PRIMARY KEY,
    brand_name VARCHAR(100) NOT NULL UNIQUE,
    is_active BOOLEAN NOT NULL DEFAULT TRUE,
    created_at TIMESTAMP NOT NULL DEFAULT CURRENT_TIMESTAMP
);

CREATE TABLE IF NOT EXISTS suppliers
(
    supplier_id SERIAL PRIMARY KEY,
    supplier_name VARCHAR(150) NOT NULL,
    contact_person VARCHAR(100),
    mobile_number VARCHAR(20),
    email VARCHAR(150),
    address TEXT,
    gst_number VARCHAR(30),
    is_active BOOLEAN NOT NULL DEFAULT TRUE,
    created_at TIMESTAMP NOT NULL DEFAULT CURRENT_TIMESTAMP,
    updated_at TIMESTAMP
);

CREATE TABLE IF NOT EXISTS customers
(
    customer_id SERIAL PRIMARY KEY,
    customer_name VARCHAR(150) NOT NULL,
    mobile_number VARCHAR(20),
    email VARCHAR(150),
    address TEXT,
    gst_number VARCHAR(30),
    is_active BOOLEAN NOT NULL DEFAULT TRUE,
    created_at TIMESTAMP NOT NULL DEFAULT CURRENT_TIMESTAMP,
    updated_at TIMESTAMP
);

CREATE TABLE IF NOT EXISTS products
(
    product_id SERIAL PRIMARY KEY,
    category_id INT NOT NULL,
    brand_id INT,
    product_name VARCHAR(150) NOT NULL,
    model VARCHAR(100),
    sku VARCHAR(100),
    barcode VARCHAR(100),
    purchase_price NUMERIC(12,2) NOT NULL DEFAULT 0,
    selling_price NUMERIC(12,2) NOT NULL DEFAULT 0,
    tax_percentage NUMERIC(5,2) NOT NULL DEFAULT 0,
    warranty_months INT NOT NULL DEFAULT 0,
    minimum_stock_level INT NOT NULL DEFAULT 0,
    is_serial_tracked BOOLEAN NOT NULL DEFAULT FALSE,
    is_active BOOLEAN NOT NULL DEFAULT TRUE,
    created_at TIMESTAMP NOT NULL DEFAULT CURRENT_TIMESTAMP,
    updated_at TIMESTAMP,

    CONSTRAINT fk_products_categories
        FOREIGN KEY (category_id)
        REFERENCES categories(category_id),

    CONSTRAINT fk_products_brands
        FOREIGN KEY (brand_id)
        REFERENCES brands(brand_id)
);

CREATE TABLE IF NOT EXISTS product_units
(
    product_unit_id BIGSERIAL PRIMARY KEY,
    product_id INT NOT NULL,
    serial_number VARCHAR(150),
    barcode VARCHAR(150),
    unit_status VARCHAR(30) NOT NULL DEFAULT 'IN_STOCK',
    purchase_id BIGINT,
    sale_id BIGINT,
    created_at TIMESTAMP NOT NULL DEFAULT CURRENT_TIMESTAMP,
    updated_at TIMESTAMP,

    CONSTRAINT fk_product_units_products
        FOREIGN KEY (product_id)
        REFERENCES products(product_id)
);

CREATE TABLE IF NOT EXISTS audit_logs
(
    audit_log_id BIGSERIAL PRIMARY KEY,
    user_id INT,
    module_name VARCHAR(100),
    action_name VARCHAR(50),
    table_name VARCHAR(100),
    record_id BIGINT,
    old_values JSONB,
    new_values JSONB,
    reason TEXT,
    created_at TIMESTAMP NOT NULL DEFAULT CURRENT_TIMESTAMP,

    CONSTRAINT fk_audit_user
        FOREIGN KEY (user_id)
        REFERENCES users(user_id)
);

CREATE INDEX IF NOT EXISTS idx_products_barcode
ON products(barcode);

CREATE INDEX IF NOT EXISTS idx_products_sku
ON products(sku);

CREATE INDEX IF NOT EXISTS idx_product_units_serial_number
ON product_units(serial_number);

CREATE INDEX IF NOT EXISTS idx_product_units_barcode
ON product_units(barcode);

CREATE INDEX IF NOT EXISTS idx_customers_mobile_number
ON customers(mobile_number);

CREATE UNIQUE INDEX IF NOT EXISTS ux_product_units_serial_number
ON product_units(serial_number)
WHERE serial_number IS NOT NULL;

CREATE UNIQUE INDEX IF NOT EXISTS ux_products_sku
ON products(sku)
WHERE sku IS NOT NULL;
