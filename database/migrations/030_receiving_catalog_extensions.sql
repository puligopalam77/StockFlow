-- MANUAL: stop StockFlow, back up, execute after 029 as PostgreSQL superuser, then restart.
-- Adds production receiving fields and a tenant-scoped supplier/manufacturer catalogue.
BEGIN;
DO $$ BEGIN
 IF NOT EXISTS(SELECT 1 FROM pg_roles WHERE rolname=current_user AND rolsuper) THEN RAISE EXCEPTION 'Apply as PostgreSQL superuser'; END IF;
 IF to_regclass('public.module_navigation_permissions') IS NULL OR to_regclass('public.supplier_payments') IS NULL THEN RAISE EXCEPTION 'Apply existing migrations through 029 first'; END IF;
END $$;

ALTER TABLE products ADD COLUMN hsn_code varchar(20),
 ADD COLUMN unit_of_measure varchar(20) NOT NULL DEFAULT 'PIECE',
 ADD COLUMN product_image_url varchar(500),
 ADD COLUMN product_image_name varchar(255),
 ADD COLUMN product_image_content_type varchar(100),
 ADD COLUMN product_image_data bytea,
 ADD CONSTRAINT ck_product_image CHECK(
  (product_image_data IS NULL AND product_image_name IS NULL AND product_image_content_type IS NULL)
  OR (product_image_data IS NOT NULL AND product_image_name IS NOT NULL AND product_image_content_type IS NOT NULL AND octet_length(product_image_data)<=3000000)),
 ADD CONSTRAINT ck_product_uom CHECK(unit_of_measure IN('PIECE','BOX','PAIR','SET'));

ALTER TABLE product_variants ADD COLUMN mrp numeric(12,2) NOT NULL DEFAULT 0,
 ADD COLUMN pack_quantity integer NOT NULL DEFAULT 1,
 ADD COLUMN supplier_product_code varchar(100),
 ADD CONSTRAINT ck_variant_mrp_pack CHECK(mrp>=0 AND pack_quantity BETWEEN 1 AND 10000 AND (mrp=0 OR selling_price<=mrp));

ALTER TABLE purchases ADD COLUMN supplier_invoice_date date,
 ADD COLUMN freight_amount numeric(12,2) NOT NULL DEFAULT 0,
 ADD COLUMN other_landed_cost numeric(12,2) NOT NULL DEFAULT 0,
 ADD COLUMN invoice_attachment_name varchar(255),
 ADD COLUMN invoice_attachment_content_type varchar(100),
 ADD COLUMN invoice_attachment_data bytea,
 ADD CONSTRAINT ck_purchase_landed_cost CHECK(freight_amount>=0 AND other_landed_cost>=0),
 ADD CONSTRAINT ck_purchase_attachment CHECK(
  (invoice_attachment_data IS NULL AND invoice_attachment_name IS NULL AND invoice_attachment_content_type IS NULL)
  OR (invoice_attachment_data IS NOT NULL AND invoice_attachment_name IS NOT NULL AND invoice_attachment_content_type IS NOT NULL
      AND octet_length(invoice_attachment_data)<=5000000));
UPDATE purchases SET supplier_invoice_date=purchase_date WHERE supplier_invoice_number IS NOT NULL AND supplier_invoice_date IS NULL;

ALTER TABLE purchase_items ADD COLUMN damaged_quantity integer NOT NULL DEFAULT 0,
 ADD COLUMN damaged_serial_numbers text[] NOT NULL DEFAULT '{}',
 ADD COLUMN pending_received_quantity integer NOT NULL DEFAULT 0,
 ADD COLUMN pending_damaged_quantity integer NOT NULL DEFAULT 0,
 ADD COLUMN pending_serial_numbers text[] NOT NULL DEFAULT '{}',
 ADD COLUMN pending_damaged_serial_numbers text[] NOT NULL DEFAULT '{}',
 ADD COLUMN unit_of_measure varchar(20) NOT NULL DEFAULT 'PIECE',
 ADD COLUMN pack_quantity integer NOT NULL DEFAULT 1,
 ADD COLUMN mrp numeric(12,2) NOT NULL DEFAULT 0,
 ADD COLUMN supplier_product_code varchar(100),
 ADD COLUMN batch_number varchar(100),
 ADD COLUMN manufacturing_date date,
 ADD COLUMN expiry_date date;
-- Preserve the old Complete action for drafts created before this migration.
UPDATE purchase_items i SET pending_received_quantity=i.quantity-i.received_quantity,pending_serial_numbers=i.serial_numbers
FROM purchases p WHERE p.purchase_id=i.purchase_id AND p.status='DRAFT';
ALTER TABLE purchase_items
 ADD CONSTRAINT ck_purchase_item_receipt_quantities CHECK(
  damaged_quantity>=0 AND pending_received_quantity>=0 AND pending_damaged_quantity>=0
  AND received_quantity+damaged_quantity<=quantity
  AND pending_received_quantity+pending_damaged_quantity<=quantity-received_quantity-damaged_quantity),
 ADD CONSTRAINT ck_purchase_item_pack CHECK(unit_of_measure IN('PIECE','BOX','PAIR','SET') AND pack_quantity BETWEEN 1 AND 10000 AND mrp>=0),
 ADD CONSTRAINT ck_purchase_item_dates CHECK(expiry_date IS NULL OR manufacturing_date IS NULL OR expiry_date>=manufacturing_date);

DO $$ DECLARE c record; BEGIN
 FOR c IN SELECT conname FROM pg_constraint WHERE conrelid='public.purchase_receipt_items'::regclass AND contype='c'
  AND pg_get_constraintdef(oid) ILIKE '%quantity > 0%' LOOP EXECUTE format('ALTER TABLE purchase_receipt_items DROP CONSTRAINT %I',c.conname); END LOOP;
END $$;
ALTER TABLE purchase_receipt_items ADD COLUMN damaged_quantity integer NOT NULL DEFAULT 0,
 ADD COLUMN damaged_serial_numbers text[] NOT NULL DEFAULT '{}',
 ADD COLUMN unit_of_measure varchar(20) NOT NULL DEFAULT 'PIECE',
 ADD COLUMN pack_quantity integer NOT NULL DEFAULT 1,
 ADD COLUMN mrp numeric(12,2) NOT NULL DEFAULT 0,
 ADD COLUMN supplier_product_code varchar(100),
 ADD COLUMN batch_number varchar(100),
 ADD COLUMN manufacturing_date date,
 ADD COLUMN expiry_date date,
 ADD CONSTRAINT ck_receipt_resolved_quantity CHECK(quantity>=0 AND damaged_quantity>=0 AND quantity+damaged_quantity>0),
 ADD CONSTRAINT ck_receipt_item_pack_dates CHECK(unit_of_measure IN('PIECE','BOX','PAIR','SET') AND pack_quantity BETWEEN 1 AND 10000 AND mrp>=0 AND (expiry_date IS NULL OR manufacturing_date IS NULL OR expiry_date>=manufacturing_date));
ALTER TABLE purchase_receipts ADD COLUMN supplier_invoice_date date;

CREATE TABLE supplier_product_catalog(
 catalog_product_id bigserial PRIMARY KEY,
 tenant_id bigint NOT NULL DEFAULT sf_tenant(),
 supplier_id integer,
 source_name varchar(150) NOT NULL,
 barcode varchar(150) NOT NULL,
 product_name varchar(150) NOT NULL,
 category_id integer,
 brand_id integer,
 model varchar(100),
 hsn_code varchar(20),
 unit_of_measure varchar(20) NOT NULL DEFAULT 'PIECE' CHECK(unit_of_measure IN('PIECE','BOX','PAIR','SET')),
 tracking_type varchar(20) NOT NULL DEFAULT 'SERIAL' CHECK(tracking_type IN('QUANTITY','SERIAL')),
 purchase_price numeric(12,2) NOT NULL DEFAULT 0 CHECK(purchase_price>=0),
 selling_price numeric(12,2) NOT NULL DEFAULT 0 CHECK(selling_price>=0),
 mrp numeric(12,2) NOT NULL DEFAULT 0 CHECK(mrp>=0),
 tax_percentage numeric(5,2) NOT NULL DEFAULT 0 CHECK(tax_percentage BETWEEN 0 AND 100),
 warranty_months integer NOT NULL DEFAULT 0 CHECK(warranty_months BETWEEN 0 AND 240),
 warranty_provider varchar(20) CHECK(warranty_provider IN('MANUFACTURER','DEALER','BOTH')),
 supplier_product_code varchar(100),
 attributes jsonb NOT NULL DEFAULT '{}',
 is_active boolean NOT NULL DEFAULT true,
 created_at timestamptz NOT NULL DEFAULT now(),updated_at timestamptz,
 UNIQUE(tenant_id,catalog_product_id),
 FOREIGN KEY(tenant_id,supplier_id) REFERENCES suppliers(tenant_id,supplier_id),
 FOREIGN KEY(tenant_id,category_id) REFERENCES categories(tenant_id,category_id),
 FOREIGN KEY(tenant_id,brand_id) REFERENCES brands(tenant_id,brand_id)
);
CREATE UNIQUE INDEX ux_supplier_catalog_barcode ON supplier_product_catalog(tenant_id,coalesce(supplier_id,0),lower(btrim(barcode)));
CREATE INDEX ix_supplier_catalog_lookup ON supplier_product_catalog(tenant_id,lower(btrim(barcode))) WHERE is_active;
ALTER TABLE supplier_product_catalog ENABLE ROW LEVEL SECURITY;
ALTER TABLE supplier_product_catalog FORCE ROW LEVEL SECURITY;
CREATE POLICY tenant_isolation ON supplier_product_catalog USING(tenant_id=sf_tenant()) WITH CHECK(tenant_id=sf_tenant());
GRANT SELECT,INSERT,UPDATE,DELETE ON supplier_product_catalog TO stockflow_runtime;
GRANT USAGE,SELECT ON SEQUENCE supplier_product_catalog_catalog_product_id_seq TO stockflow_runtime;
COMMIT;
