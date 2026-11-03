-- MANUAL ONLY. Stop StockFlow and back up first. Apply once after migration 022.
-- Existing tenant association and row-security policies from 020 are retained.
BEGIN;
ALTER TABLE suppliers
 ADD COLUMN supplier_code varchar(30) GENERATED ALWAYS AS
 ('SUP' || lpad(supplier_id::text,greatest(6,length(supplier_id::text)),'0')) STORED,
 ADD COLUMN supplier_type varchar(30),
 ADD COLUMN alternate_mobile varchar(20),
 ADD COLUMN country_code varchar(2) NOT NULL DEFAULT 'ZZ',
 ADD COLUMN state varchar(100),
 ADD COLUMN city varchar(100),
 ADD COLUMN postal_code varchar(20),
 ADD COLUMN payment_terms_days integer CHECK(payment_terms_days BETWEEN 0 AND 365),
 ADD COLUMN notes varchar(1000);
-- ZZ flags unknown legacy country; do not invent country/address information.
ALTER TABLE suppliers ALTER COLUMN country_code SET DEFAULT 'IN';
ALTER TABLE suppliers ALTER COLUMN supplier_code SET NOT NULL;
CREATE UNIQUE INDEX ux_supplier_tenant_code ON suppliers(tenant_id,lower(supplier_code));
ALTER TABLE suppliers ADD CONSTRAINT ck_supplier_type
 CHECK(supplier_type IS NULL OR supplier_type IN ('MANUFACTURER','DISTRIBUTOR','WHOLESALER','DEALER','IMPORTER','SERVICE_PROVIDER','OTHER'));
ALTER TABLE suppliers ADD CONSTRAINT ck_supplier_country CHECK(country_code ~ '^[A-Z]{2}$');
ALTER TABLE suppliers ADD CONSTRAINT ck_supplier_registration_text
 CHECK(length(btrim(supplier_name)) BETWEEN 2 AND 150 AND (address IS NULL OR length(address)<=500)) NOT VALID;
-- Codes follow the immutable existing supplier ID. No store identifier is used.
CREATE FUNCTION sf_protect_supplier_identity() RETURNS trigger LANGUAGE plpgsql AS $$ BEGIN
 IF NEW.supplier_id<>OLD.supplier_id OR NEW.tenant_id<>OLD.tenant_id THEN
   RAISE EXCEPTION 'Supplier identity cannot be changed' USING ERRCODE='23514';
 END IF;
 RETURN NEW;
END $$;
CREATE TRIGGER protect_supplier_identity BEFORE UPDATE ON suppliers
 FOR EACH ROW EXECUTE FUNCTION sf_protect_supplier_identity();
CREATE FUNCTION sf_audit_supplier_registration() RETURNS trigger LANGUAGE plpgsql AS $$ BEGIN
 INSERT INTO audit_logs(tenant_id,user_id,module_name,action_name,table_name,record_id,old_values,new_values)
 VALUES(NEW.tenant_id,NULLIF(sf_actor(),0),'Suppliers',TG_OP,'suppliers',NEW.supplier_id,
 CASE WHEN TG_OP='UPDATE' THEN to_jsonb(OLD) ELSE NULL END,to_jsonb(NEW));
 RETURN NEW;
END $$;
CREATE TRIGGER audit_supplier_registration AFTER INSERT OR UPDATE ON suppliers
 FOR EACH ROW EXECUTE FUNCTION sf_audit_supplier_registration();

ALTER TABLE purchases ADD COLUMN supplier_name_snapshot varchar(150),
 ADD COLUMN supplier_code_snapshot varchar(30),
 ADD COLUMN supplier_gst_snapshot varchar(30),
 ADD COLUMN supplier_address_snapshot text,
 ADD COLUMN supplier_snapshot_at timestamptz,
 ADD COLUMN payment_terms_days integer CHECK(payment_terms_days BETWEEN 0 AND 365);
-- Do not backfill completed historical documents using today's supplier details.
CREATE FUNCTION sf_purchase_supplier_snapshot() RETURNS trigger LANGUAGE plpgsql AS $$
DECLARE s public.suppliers%ROWTYPE; capture boolean;
BEGIN
 IF TG_OP='UPDATE' THEN
   IF OLD.status<>'DRAFT' THEN
     NEW.supplier_name_snapshot:=OLD.supplier_name_snapshot;
     NEW.supplier_code_snapshot:=OLD.supplier_code_snapshot;
     NEW.supplier_gst_snapshot:=OLD.supplier_gst_snapshot;
     NEW.supplier_address_snapshot:=OLD.supplier_address_snapshot;
     NEW.supplier_snapshot_at:=OLD.supplier_snapshot_at;
     NEW.payment_terms_days:=OLD.payment_terms_days;
     RETURN NEW;
   END IF;
 END IF;
 capture := TG_OP='INSERT';
 IF TG_OP='UPDATE' THEN capture:=NEW.supplier_id<>OLD.supplier_id OR NEW.status<>'DRAFT' OR OLD.supplier_snapshot_at IS NULL; END IF;
 IF capture OR NEW.payment_terms_days IS NULL THEN
   SELECT * INTO STRICT s FROM public.suppliers WHERE tenant_id=NEW.tenant_id AND supplier_id=NEW.supplier_id;
   IF capture THEN
     NEW.supplier_name_snapshot:=s.supplier_name;NEW.supplier_code_snapshot:=s.supplier_code;
     NEW.supplier_gst_snapshot:=s.gst_number;
     NEW.supplier_address_snapshot:=concat_ws(', ',s.address,s.city,s.state,s.postal_code,NULLIF(s.country_code,'ZZ'));
     NEW.supplier_snapshot_at:=CURRENT_TIMESTAMP;
   END IF;
   NEW.payment_terms_days:=COALESCE(NEW.payment_terms_days,s.payment_terms_days);
 END IF;
 RETURN NEW;
END $$;
CREATE TRIGGER purchase_supplier_snapshot BEFORE INSERT OR UPDATE ON purchases
 FOR EACH ROW EXECUTE FUNCTION sf_purchase_supplier_snapshot();
COMMIT;
