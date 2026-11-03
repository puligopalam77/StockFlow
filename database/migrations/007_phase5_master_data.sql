-- Run manually in StockFlow_DB. This script does not remove or merge any records.
-- If a duplicate or invalid value is reported, correct it explicitly and rerun.
BEGIN;

DO $$
BEGIN
    IF EXISTS (SELECT 1 FROM categories GROUP BY LOWER(BTRIM(category_name)) HAVING COUNT(*) > 1) THEN
        RAISE EXCEPTION 'Duplicate category names exist (ignoring case and surrounding spaces). Resolve them first.';
    END IF;
    IF EXISTS (SELECT 1 FROM brands GROUP BY LOWER(BTRIM(brand_name)) HAVING COUNT(*) > 1) THEN
        RAISE EXCEPTION 'Duplicate brand names exist (ignoring case and surrounding spaces). Resolve them first.';
    END IF;
    IF EXISTS (SELECT 1 FROM products WHERE sku IS NOT NULL GROUP BY sku HAVING COUNT(*) > 1) THEN
        RAISE EXCEPTION 'Duplicate product SKUs exist. Resolve them first.';
    END IF;
    IF EXISTS (SELECT 1 FROM products WHERE barcode IS NOT NULL GROUP BY barcode HAVING COUNT(*) > 1) THEN
        RAISE EXCEPTION 'Duplicate product barcodes exist. Resolve them first.';
    END IF;
END $$;

CREATE UNIQUE INDEX IF NOT EXISTS ux_categories_name ON categories (LOWER(BTRIM(category_name)));
CREATE UNIQUE INDEX IF NOT EXISTS ux_brands_name ON brands (LOWER(BTRIM(brand_name)));
CREATE UNIQUE INDEX IF NOT EXISTS ux_products_sku ON products(sku) WHERE sku IS NOT NULL;
CREATE UNIQUE INDEX IF NOT EXISTS ux_products_barcode ON products(barcode) WHERE barcode IS NOT NULL;

DO $$
BEGIN
    IF NOT EXISTS (SELECT 1 FROM pg_constraint WHERE conname = 'ck_categories_name' AND conrelid = 'categories'::regclass) THEN
        ALTER TABLE categories ADD CONSTRAINT ck_categories_name CHECK (LENGTH(BTRIM(category_name)) > 0);
    END IF;
    IF NOT EXISTS (SELECT 1 FROM pg_constraint WHERE conname = 'ck_brands_name' AND conrelid = 'brands'::regclass) THEN
        ALTER TABLE brands ADD CONSTRAINT ck_brands_name CHECK (LENGTH(BTRIM(brand_name)) > 0);
    END IF;
    IF NOT EXISTS (SELECT 1 FROM pg_constraint WHERE conname = 'ck_suppliers_name' AND conrelid = 'suppliers'::regclass) THEN
        ALTER TABLE suppliers ADD CONSTRAINT ck_suppliers_name CHECK (LENGTH(BTRIM(supplier_name)) > 0);
    END IF;
    IF NOT EXISTS (SELECT 1 FROM pg_constraint WHERE conname = 'ck_customers_name' AND conrelid = 'customers'::regclass) THEN
        ALTER TABLE customers ADD CONSTRAINT ck_customers_name CHECK (LENGTH(BTRIM(customer_name)) > 0);
    END IF;
    IF NOT EXISTS (SELECT 1 FROM pg_constraint WHERE conname = 'ck_products_master_values' AND conrelid = 'products'::regclass) THEN
        ALTER TABLE products ADD CONSTRAINT ck_products_master_values CHECK (
            LENGTH(BTRIM(product_name)) > 0
            AND purchase_price >= 0 AND selling_price >= 0
            AND tax_percentage BETWEEN 0 AND 100
            AND warranty_months BETWEEN 0 AND 240 AND minimum_stock_level >= 0
        );
    END IF;
END $$;
COMMIT;
