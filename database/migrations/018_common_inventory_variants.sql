-- Run manually after 017, with the application stopped. Back up the database first.
-- Positive quantities and the existing movement directions are retained.
BEGIN;
CREATE TABLE product_variants (
    variant_id BIGSERIAL PRIMARY KEY,
    product_id INT NOT NULL REFERENCES products(product_id),
    variant_name VARCHAR(150) NOT NULL CHECK (btrim(variant_name) <> ''),
    sku VARCHAR(100) NOT NULL CHECK (btrim(sku) <> ''),
    is_default BOOLEAN NOT NULL DEFAULT FALSE,
    is_serial_tracked BOOLEAN NOT NULL DEFAULT FALSE,
    purchase_price NUMERIC(12,2) NOT NULL DEFAULT 0 CHECK (purchase_price >= 0),
    selling_price NUMERIC(12,2) NOT NULL DEFAULT 0 CHECK (selling_price >= 0),
    tax_percentage NUMERIC(5,2) NOT NULL DEFAULT 0 CHECK (tax_percentage BETWEEN 0 AND 100),
    warranty_months INT NOT NULL DEFAULT 0 CHECK (warranty_months >= 0),
    minimum_stock_level INT NOT NULL DEFAULT 0 CHECK (minimum_stock_level >= 0),
    attributes JSONB NOT NULL DEFAULT '{}' CHECK (jsonb_typeof(attributes) = 'object'),
    is_active BOOLEAN NOT NULL DEFAULT TRUE,
    created_at TIMESTAMP NOT NULL DEFAULT CURRENT_TIMESTAMP,
    UNIQUE (product_id, variant_id)
);
CREATE UNIQUE INDEX ux_variants_sku ON product_variants(lower(btrim(sku)));
CREATE UNIQUE INDEX ux_variants_default ON product_variants(product_id) WHERE is_default;
CREATE TABLE variant_barcodes (
    variant_barcode_id BIGSERIAL PRIMARY KEY,
    variant_id BIGINT NOT NULL REFERENCES product_variants(variant_id),
    barcode VARCHAR(150) NOT NULL CHECK (btrim(barcode) <> ''),
    barcode_source VARCHAR(30) NOT NULL CHECK (barcode_source IN ('INTERNAL','MANUFACTURER')),
    is_primary BOOLEAN NOT NULL DEFAULT FALSE,
    is_active BOOLEAN NOT NULL DEFAULT TRUE
);
CREATE UNIQUE INDEX ux_variant_barcode ON variant_barcodes(lower(btrim(barcode)));
CREATE UNIQUE INDEX ux_variant_primary_barcode ON variant_barcodes(variant_id) WHERE is_primary AND is_active;
INSERT INTO product_variants(product_id,variant_name,sku,is_default,is_serial_tracked,purchase_price,selling_price,tax_percentage,warranty_months,minimum_stock_level,is_active)
SELECT product_id,'DEFAULT',COALESCE(NULLIF(btrim(sku),''),'LEGACY-'||product_id),TRUE,is_serial_tracked,purchase_price,selling_price,tax_percentage,warranty_months,minimum_stock_level,is_active FROM products;
INSERT INTO variant_barcodes(variant_id,barcode,barcode_source,is_primary)
SELECT v.variant_id,btrim(p.barcode),'MANUFACTURER',TRUE FROM products p JOIN product_variants v USING(product_id) WHERE NULLIF(btrim(p.barcode),'') IS NOT NULL;
ALTER TABLE product_units ADD COLUMN manufacturer_barcode VARCHAR(150), ADD COLUMN internal_barcode VARCHAR(150);
CREATE UNIQUE INDEX ux_unit_internal_barcode ON product_units(lower(btrim(internal_barcode))) WHERE internal_barcode IS NOT NULL;
DO $$ DECLARE tab TEXT; BEGIN
    FOREACH tab IN ARRAY ARRAY['purchase_items','sale_items','product_units','inventory_transactions','service_parts','service_requests'] LOOP
        EXECUTE format('ALTER TABLE %I ADD COLUMN variant_id BIGINT',tab);
        EXECUTE format('UPDATE %I t SET variant_id=v.variant_id FROM product_variants v WHERE t.product_id=v.product_id AND v.is_default',tab);
        EXECUTE format('ALTER TABLE %I ALTER COLUMN variant_id SET NOT NULL',tab);
        EXECUTE format('ALTER TABLE %I ADD FOREIGN KEY(product_id,variant_id) REFERENCES product_variants(product_id,variant_id)',tab);
        EXECUTE format('CREATE INDEX ON %I(variant_id)',tab);
    END LOOP;
END $$;
ALTER TABLE sale_items ADD COLUMN variant_name_snapshot VARCHAR(150);
ALTER TABLE purchase_items ADD COLUMN variant_name_snapshot VARCHAR(150), ADD COLUMN discount_amount NUMERIC(12,2) NOT NULL DEFAULT 0 CHECK(discount_amount >= 0);
UPDATE sale_items SET variant_name_snapshot='DEFAULT';
UPDATE purchase_items SET variant_name_snapshot='DEFAULT';
-- Compatibility for existing integrations: missing variant means DEFAULT, or the exact unit's variant.
CREATE FUNCTION stockflow_resolve_variant() RETURNS trigger LANGUAGE plpgsql AS $$
DECLARE unit_variant BIGINT;
BEGIN
    IF TG_TABLE_NAME IN ('sale_items','inventory_transactions','service_parts','service_requests') THEN
        IF NEW.product_unit_id IS NOT NULL THEN
            SELECT variant_id INTO unit_variant FROM product_units WHERE product_unit_id=NEW.product_unit_id AND product_id=NEW.product_id;
            IF unit_variant IS NULL THEN RAISE EXCEPTION 'Physical unit does not belong to product' USING ERRCODE='23514'; END IF;
            IF NEW.variant_id IS NOT NULL AND NEW.variant_id<>unit_variant THEN RAISE EXCEPTION 'Physical unit does not belong to variant' USING ERRCODE='23514'; END IF;
            NEW.variant_id:=unit_variant;
        END IF;
    END IF;
    IF NEW.variant_id IS NULL THEN SELECT variant_id INTO NEW.variant_id FROM product_variants WHERE product_id=NEW.product_id AND is_default; END IF;
    RETURN NEW;
END $$;
DO $$ DECLARE tab TEXT; BEGIN
    FOREACH tab IN ARRAY ARRAY['purchase_items','sale_items','product_units','inventory_transactions','service_parts','service_requests'] LOOP
        EXECUTE format('CREATE TRIGGER resolve_variant BEFORE INSERT OR UPDATE ON %I FOR EACH ROW EXECUTE FUNCTION stockflow_resolve_variant()',tab);
    END LOOP;
END $$;
CREATE FUNCTION stockflow_default_variant() RETURNS trigger LANGUAGE plpgsql AS $$
DECLARE vid BIGINT;
BEGIN
    INSERT INTO product_variants(product_id,variant_name,sku,is_default,is_serial_tracked,purchase_price,selling_price,tax_percentage,warranty_months,minimum_stock_level,is_active)
    VALUES(NEW.product_id,'DEFAULT',COALESCE(NULLIF(btrim(NEW.sku),''),'LEGACY-'||NEW.product_id),TRUE,NEW.is_serial_tracked,NEW.purchase_price,NEW.selling_price,NEW.tax_percentage,NEW.warranty_months,NEW.minimum_stock_level,NEW.is_active)
    RETURNING variant_id INTO vid;
    IF NULLIF(btrim(NEW.barcode),'') IS NOT NULL THEN
        INSERT INTO variant_barcodes(variant_id,barcode,barcode_source,is_primary) VALUES(vid,btrim(NEW.barcode),'MANUFACTURER',TRUE);
    END IF;
    RETURN NEW;
END $$;
CREATE TRIGGER create_default_variant AFTER INSERT ON products FOR EACH ROW EXECUTE FUNCTION stockflow_default_variant();
CREATE FUNCTION stockflow_protect_variant_tracking() RETURNS trigger LANGUAGE plpgsql AS $$
BEGIN
    IF NEW.is_serial_tracked<>OLD.is_serial_tracked AND
       (EXISTS(SELECT 1 FROM inventory_transactions WHERE variant_id=OLD.variant_id)
        OR EXISTS(SELECT 1 FROM product_units WHERE variant_id=OLD.variant_id)
        OR EXISTS(SELECT 1 FROM purchase_items WHERE variant_id=OLD.variant_id)) THEN
        RAISE EXCEPTION 'Tracking cannot change after the variant has been used' USING ERRCODE='23514';
    END IF;
    RETURN NEW;
END $$;
CREATE TRIGGER protect_variant_tracking BEFORE UPDATE ON product_variants FOR EACH ROW EXECUTE FUNCTION stockflow_protect_variant_tracking();
-- Legacy product editing remains an editor for the default variant's commercial fields.
CREATE FUNCTION stockflow_sync_default_variant() RETURNS trigger LANGUAGE plpgsql AS $$
DECLARE vid BIGINT;
BEGIN
    UPDATE product_variants SET sku=COALESCE(NULLIF(btrim(NEW.sku),''),'LEGACY-'||NEW.product_id),
        is_serial_tracked=NEW.is_serial_tracked,purchase_price=NEW.purchase_price,selling_price=NEW.selling_price,
        tax_percentage=NEW.tax_percentage,warranty_months=NEW.warranty_months,minimum_stock_level=NEW.minimum_stock_level
    WHERE product_id=NEW.product_id AND is_default RETURNING variant_id INTO vid;
    IF NEW.barcode IS DISTINCT FROM OLD.barcode AND NULLIF(btrim(NEW.barcode),'') IS NOT NULL THEN
        IF EXISTS(SELECT 1 FROM variant_barcodes WHERE lower(btrim(barcode))=lower(btrim(NEW.barcode)) AND variant_id<>vid) THEN
            RAISE EXCEPTION 'Barcode belongs to another variant' USING ERRCODE='23505';
        END IF;
        INSERT INTO variant_barcodes(variant_id,barcode,barcode_source) VALUES(vid,btrim(NEW.barcode),'MANUFACTURER') ON CONFLICT DO NOTHING;
    END IF;
    RETURN NEW;
END $$;
CREATE TRIGGER sync_default_variant AFTER UPDATE ON products FOR EACH ROW EXECUTE FUNCTION stockflow_sync_default_variant();
CREATE VIEW vw_variant_current_stock AS
SELECT v.variant_id,v.product_id,COALESCE(SUM(CASE
    WHEN it.transaction_type IN ('PURCHASE','CUSTOMER_RETURN','ADJUSTMENT_IN','SERVICE_IN') THEN it.quantity::BIGINT
    WHEN it.transaction_type IN ('SALE','SUPPLIER_RETURN','DAMAGED','ADJUSTMENT_OUT','SERVICE_OUT','WARRANTY_REPLACEMENT') THEN -it.quantity::BIGINT
    ELSE 0::BIGINT END),0)::BIGINT AS current_stock
FROM product_variants v LEFT JOIN inventory_transactions it ON it.variant_id=v.variant_id GROUP BY v.variant_id,v.product_id;
-- Fail without changing history if existing balances cannot be reproduced.
DO $$ BEGIN
    IF EXISTS(SELECT 1 FROM vw_current_stock p JOIN (SELECT product_id,SUM(current_stock) AS stock FROM vw_variant_current_stock GROUP BY product_id) v USING(product_id) WHERE p.current_stock<>v.stock) THEN
        RAISE EXCEPTION 'Stock reconciliation failed. Review legacy movement directions; migration rolled back.';
    END IF;
END $$;
CREATE OR REPLACE VIEW vw_current_stock AS SELECT product_id,SUM(current_stock)::BIGINT AS current_stock FROM vw_variant_current_stock GROUP BY product_id;
COMMIT;
