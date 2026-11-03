-- MANUAL: stop StockFlow, back up, execute after 030 as PostgreSQL superuser, then restart.
-- Adds permanent receipt numbers/status and renames the database-driven inflow navigation label.
BEGIN;
DO $$ BEGIN
 IF NOT EXISTS(SELECT 1 FROM pg_roles WHERE rolname=current_user AND rolsuper) THEN RAISE EXCEPTION 'Apply as PostgreSQL superuser'; END IF;
 IF to_regclass('public.supplier_product_catalog') IS NULL OR to_regclass('public.stock_intakes') IS NULL THEN
  RAISE EXCEPTION 'Apply existing migrations through 030 first';
 END IF;
END $$;

ALTER TABLE purchase_receipts
 ADD COLUMN receipt_number varchar(50),
 ADD COLUMN status varchar(20) NOT NULL DEFAULT 'COMPLETED',
 ADD COLUMN reversed_at timestamptz,
 ADD COLUMN reversed_by integer,
 ADD COLUMN reversal_reason varchar(1000);

-- Migration 019 marked older purchases received but did not create receipt documents.
-- Create one documentary receipt for those ledger movements; stock quantities are not changed.
INSERT INTO purchase_receipts(tenant_id,location_id,purchase_id,received_date,supplier_invoice_number,supplier_invoice_date,notes,created_by,created_at)
SELECT p.tenant_id,p.location_id,p.purchase_id,COALESCE(p.received_date,p.purchase_date),p.supplier_invoice_number,p.supplier_invoice_date,
 'Backfilled from legacy completed purchase',p.created_by,p.created_at
FROM purchases p
WHERE p.status='COMPLETED' AND NOT EXISTS(SELECT 1 FROM purchase_receipts r WHERE r.purchase_id=p.purchase_id)
 AND EXISTS(SELECT 1 FROM inventory_transactions m WHERE m.reference_type='PURCHASE' AND m.reference_id=p.purchase_id AND m.transaction_type='PURCHASE');

INSERT INTO purchase_receipt_items(tenant_id,location_id,receipt_id,purchase_item_id,quantity,damaged_quantity,serial_numbers,damaged_serial_numbers,
 purchase_price,discount_amount,tax_percentage,unit_of_measure,pack_quantity,mrp,supplier_product_code,batch_number,manufacturing_date,expiry_date)
SELECT i.tenant_id,i.location_id,r.receipt_id,i.purchase_item_id,i.received_quantity,i.damaged_quantity,i.serial_numbers,i.damaged_serial_numbers,
 i.purchase_price,i.discount_amount,i.tax_percentage,i.unit_of_measure,i.pack_quantity,i.mrp,i.supplier_product_code,i.batch_number,i.manufacturing_date,i.expiry_date
FROM purchase_items i JOIN purchase_receipts r ON r.purchase_id=i.purchase_id
WHERE r.notes='Backfilled from legacy completed purchase'
 AND (SELECT count(*) FROM purchase_receipts x WHERE x.purchase_id=r.purchase_id)=1
 AND i.received_quantity+i.damaged_quantity>0 AND NOT EXISTS(
 SELECT 1 FROM purchase_receipt_items ri WHERE ri.receipt_id=r.receipt_id AND ri.purchase_item_id=i.purchase_item_id);

UPDATE inventory_transactions m SET receipt_id=r.receipt_id FROM purchase_receipts r
WHERE r.notes='Backfilled from legacy completed purchase'
 AND (SELECT count(*) FROM purchase_receipts x WHERE x.purchase_id=r.purchase_id)=1
 AND m.receipt_id IS NULL AND m.reference_type='PURCHASE' AND m.reference_id=r.purchase_id;
UPDATE product_units u SET receipt_id=r.receipt_id FROM purchase_receipts r
WHERE r.notes='Backfilled from legacy completed purchase'
 AND (SELECT count(*) FROM purchase_receipts x WHERE x.purchase_id=r.purchase_id)=1
 AND u.receipt_id IS NULL AND u.purchase_id=r.purchase_id;

UPDATE purchase_receipts
SET receipt_number='GRN-'||to_char(received_date,'YYYY')||'-'||lpad(receipt_id::text,6,'0')
WHERE receipt_number IS NULL;
ALTER TABLE purchase_receipts ALTER COLUMN receipt_number SET NOT NULL;
CREATE UNIQUE INDEX ux_purchase_receipts_number ON purchase_receipts(tenant_id,lower(receipt_number));

DO $$ BEGIN
 IF NOT EXISTS(SELECT 1 FROM pg_constraint WHERE conname='ck_purchase_receipt_status') THEN
  ALTER TABLE purchase_receipts ADD CONSTRAINT ck_purchase_receipt_status CHECK(
   status IN('COMPLETED','REVERSED') AND
   (status='COMPLETED' AND reversed_at IS NULL AND reversed_by IS NULL AND reversal_reason IS NULL
    OR status='REVERSED' AND reversed_at IS NOT NULL AND reversed_by IS NOT NULL AND length(btrim(reversal_reason))>0));
 END IF;
 IF NOT EXISTS(SELECT 1 FROM pg_constraint WHERE conname='fk_purchase_receipt_reversed_by') THEN
  ALTER TABLE purchase_receipts ADD CONSTRAINT fk_purchase_receipt_reversed_by FOREIGN KEY(tenant_id,reversed_by) REFERENCES users(tenant_id,user_id);
 END IF;
END $$;

CREATE OR REPLACE FUNCTION sf_purchase_receipt_number() RETURNS trigger
LANGUAGE plpgsql SET search_path=pg_catalog,public,pg_temp AS $$
BEGIN
 IF NEW.receipt_number IS NULL OR btrim(NEW.receipt_number)='' THEN
  NEW.receipt_number:='GRN-'||to_char(NEW.received_date,'YYYY')||'-'||lpad(NEW.receipt_id::text,6,'0');
 END IF;
 RETURN NEW;
END $$;
DROP TRIGGER IF EXISTS purchase_receipt_number ON purchase_receipts;
CREATE TRIGGER purchase_receipt_number BEFORE INSERT ON purchase_receipts FOR EACH ROW EXECUTE FUNCTION sf_purchase_receipt_number();

ALTER TABLE stock_intakes
 ADD COLUMN intake_number varchar(50),
 ADD COLUMN status varchar(20) NOT NULL DEFAULT 'COMPLETED';
UPDATE stock_intakes SET intake_number=CASE kind WHEN 'OPENING_STOCK' THEN 'OPEN-' ELSE 'INT-' END
 ||to_char(received_date,'YYYY')||'-'||lpad(intake_id::text,6,'0') WHERE intake_number IS NULL;
ALTER TABLE stock_intakes ALTER COLUMN intake_number SET NOT NULL;
CREATE UNIQUE INDEX ux_stock_intakes_number ON stock_intakes(tenant_id,lower(intake_number));
ALTER TABLE stock_intakes ADD CONSTRAINT ck_stock_intake_status CHECK(status IN('COMPLETED'));

CREATE OR REPLACE FUNCTION sf_stock_intake_number() RETURNS trigger
LANGUAGE plpgsql SET search_path=pg_catalog,public,pg_temp AS $$
BEGIN
 IF NEW.intake_number IS NULL OR btrim(NEW.intake_number)='' THEN
  NEW.intake_number:=CASE NEW.kind WHEN 'OPENING_STOCK' THEN 'OPEN-' ELSE 'INT-' END
   ||to_char(NEW.received_date,'YYYY')||'-'||lpad(NEW.intake_id::text,6,'0');
 END IF;
 RETURN NEW;
END $$;
DROP TRIGGER IF EXISTS stock_intake_number ON stock_intakes;
CREATE TRIGGER stock_intake_number BEFORE INSERT ON stock_intakes FOR EACH ROW EXECUTE FUNCTION sf_stock_intake_number();

UPDATE modules SET module_name='Stock receipt history',description='Review purchase receipts, customer returns, opening stock and approved positive adjustments'
WHERE module_code='STOCK_INFLOW';

CREATE INDEX ix_purchase_receipts_history ON purchase_receipts(tenant_id,location_id,received_date DESC,receipt_id DESC);
CREATE INDEX ix_inventory_receipt_history ON inventory_transactions(tenant_id,location_id,transaction_type,created_at DESC)
 WHERE transaction_type IN('PURCHASE','CUSTOMER_RETURN','ADJUSTMENT_IN');
COMMIT;
