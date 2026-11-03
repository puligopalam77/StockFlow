-- MANUAL ONLY. Stop StockFlow, back up, and apply once after migration 023.
-- Existing tenant ownership/RLS stays in place. No historical relationships are deleted.
BEGIN;
ALTER TABLE categories
 ADD COLUMN category_code varchar(30) GENERATED ALWAYS AS
 ('CAT' || lpad(category_id::text,greatest(4,length(category_id::text)),'0')) STORED,
 ADD COLUMN parent_category_id integer,
 ADD COLUMN updated_at timestamptz,
 ADD COLUMN created_by integer,
 ADD COLUMN updated_by integer,
 ALTER COLUMN description TYPE varchar(500);
ALTER TABLE brands
 ADD COLUMN brand_code varchar(30) GENERATED ALWAYS AS
 ('BRD' || lpad(brand_id::text,greatest(4,length(brand_id::text)),'0')) STORED,
 ADD COLUMN description varchar(500),
 ADD COLUMN updated_at timestamptz,
 ADD COLUMN created_by integer,
 ADD COLUMN updated_by integer;
ALTER TABLE categories ALTER COLUMN category_code SET NOT NULL;
ALTER TABLE brands ALTER COLUMN brand_code SET NOT NULL;
ALTER TABLE categories ADD CONSTRAINT uq_category_tenant_id UNIQUE(tenant_id,category_id),
 ADD CONSTRAINT fk_category_parent_tenant FOREIGN KEY(tenant_id,parent_category_id) REFERENCES categories(tenant_id,category_id),
 ADD CONSTRAINT ck_category_not_self_parent CHECK(parent_category_id IS NULL OR parent_category_id<>category_id),
 ADD CONSTRAINT ck_category_registration_name CHECK(length(btrim(category_name)) BETWEEN 2 AND 100 AND category_name ~ '[[:alnum:]]') NOT VALID,
 ADD FOREIGN KEY(tenant_id,created_by) REFERENCES users(tenant_id,user_id),
 ADD FOREIGN KEY(tenant_id,updated_by) REFERENCES users(tenant_id,user_id);
ALTER TABLE brands
 ADD CONSTRAINT ck_brand_registration_name CHECK(length(btrim(brand_name)) BETWEEN 2 AND 100 AND brand_name ~ '[[:alnum:]]') NOT VALID,
 ADD FOREIGN KEY(tenant_id,created_by) REFERENCES users(tenant_id,user_id),
 ADD FOREIGN KEY(tenant_id,updated_by) REFERENCES users(tenant_id,user_id);
CREATE UNIQUE INDEX ux_category_tenant_code ON categories(tenant_id,lower(category_code));
CREATE UNIQUE INDEX ux_brand_tenant_code ON brands(tenant_id,lower(brand_code));
DROP INDEX ux_categories_name;
CREATE UNIQUE INDEX ux_category_root_name ON categories(tenant_id,lower(btrim(category_name))) WHERE parent_category_id IS NULL;
CREATE UNIQUE INDEX ux_category_child_name ON categories(tenant_id,parent_category_id,lower(btrim(category_name))) WHERE parent_category_id IS NOT NULL;
-- ux_brands_name already enforces tenant-scoped case-insensitive brand names.

CREATE FUNCTION sf_validate_catalog_registration() RETURNS trigger LANGUAGE plpgsql AS $$
DECLARE current_parent integer; seen integer[]; depth integer; subtree_depth integer;
BEGIN
 -- Share this lock with product assignment checks, including concurrent edits.
 PERFORM pg_advisory_xact_lock(hashtextextended('catalog-registration:' || NEW.tenant_id::text,0));
 IF TG_OP='UPDATE' THEN
   IF NEW.tenant_id<>OLD.tenant_id OR
      (to_jsonb(NEW)->>(CASE WHEN TG_TABLE_NAME='categories' THEN 'category_id' ELSE 'brand_id' END)) <>
      (to_jsonb(OLD)->>(CASE WHEN TG_TABLE_NAME='categories' THEN 'category_id' ELSE 'brand_id' END)) THEN
     RAISE EXCEPTION 'Catalog identity cannot change' USING ERRCODE='23514';
   END IF;
   NEW.created_by:=OLD.created_by; NEW.created_at:=OLD.created_at;
   NEW.updated_by:=NULLIF(sf_actor(),0); NEW.updated_at:=CURRENT_TIMESTAMP;
 ELSE NEW.created_by:=NULLIF(sf_actor(),0); NEW.updated_by:=NULL; NEW.updated_at:=NULL;
 END IF;
 IF TG_TABLE_NAME='categories' THEN
   NEW.category_name:=btrim(NEW.category_name); NEW.description:=NULLIF(btrim(NEW.description),'');
   seen:=ARRAY[NEW.category_id]; depth:=1; current_parent:=NEW.parent_category_id;
   WHILE current_parent IS NOT NULL LOOP
     IF current_parent=ANY(seen) THEN RAISE EXCEPTION 'Circular category hierarchy' USING ERRCODE='23514'; END IF;
     seen:=array_append(seen,current_parent); depth:=depth+1;
     IF NOT EXISTS(SELECT 1 FROM categories WHERE tenant_id=NEW.tenant_id AND category_id=current_parent AND (is_active OR NOT NEW.is_active)) THEN
       RAISE EXCEPTION 'Choose an active parent category in this tenant' USING ERRCODE='23514';
     END IF;
     SELECT parent_category_id INTO current_parent FROM categories WHERE tenant_id=NEW.tenant_id AND category_id=current_parent;
   END LOOP;
   WITH RECURSIVE descendants AS (
     SELECT category_id,1 AS levels FROM categories WHERE tenant_id=NEW.tenant_id AND parent_category_id=NEW.category_id
     UNION ALL SELECT c.category_id,d.levels+1 FROM categories c JOIN descendants d ON c.parent_category_id=d.category_id
       WHERE c.tenant_id=NEW.tenant_id AND d.levels<4
   ) SELECT COALESCE(max(levels),0) INTO subtree_depth FROM descendants;
   IF depth+subtree_depth>3 THEN RAISE EXCEPTION 'Category hierarchy is limited to three levels' USING ERRCODE='23514'; END IF;
   IF NOT NEW.is_active AND (
     EXISTS(SELECT 1 FROM products WHERE tenant_id=NEW.tenant_id AND category_id=NEW.category_id AND is_active) OR
     EXISTS(SELECT 1 FROM categories WHERE tenant_id=NEW.tenant_id AND parent_category_id=NEW.category_id AND is_active)
   ) THEN RAISE EXCEPTION 'Reassign or deactivate active products and child categories first' USING ERRCODE='23514'; END IF;
 ELSE
   NEW.brand_name:=btrim(NEW.brand_name); NEW.description:=NULLIF(btrim(NEW.description),'');
   IF NOT NEW.is_active AND EXISTS(SELECT 1 FROM products WHERE tenant_id=NEW.tenant_id AND brand_id=NEW.brand_id AND is_active) THEN
     RAISE EXCEPTION 'Reassign or deactivate active products first' USING ERRCODE='23514';
   END IF;
 END IF;
 RETURN NEW;
END $$;
CREATE TRIGGER validate_category_registration BEFORE INSERT OR UPDATE ON categories FOR EACH ROW EXECUTE FUNCTION sf_validate_catalog_registration();
CREATE TRIGGER validate_brand_registration BEFORE INSERT OR UPDATE ON brands FOR EACH ROW EXECUTE FUNCTION sf_validate_catalog_registration();

CREATE FUNCTION sf_audit_catalog_registration() RETURNS trigger LANGUAGE plpgsql AS $$ BEGIN
 INSERT INTO audit_logs(tenant_id,user_id,module_name,action_name,table_name,record_id,old_values,new_values)
 VALUES(NEW.tenant_id,NULLIF(sf_actor(),0),CASE WHEN TG_TABLE_NAME='categories' THEN 'Categories' ELSE 'Brands' END,
 TG_OP,TG_TABLE_NAME,(to_jsonb(NEW)->>(CASE WHEN TG_TABLE_NAME='categories' THEN 'category_id' ELSE 'brand_id' END))::integer,
 CASE WHEN TG_OP='UPDATE' THEN to_jsonb(OLD) ELSE NULL END,to_jsonb(NEW));
 RETURN NEW;
END $$;
CREATE TRIGGER audit_category_registration AFTER INSERT OR UPDATE ON categories FOR EACH ROW EXECUTE FUNCTION sf_audit_catalog_registration();
CREATE TRIGGER audit_brand_registration AFTER INSERT OR UPDATE ON brands FOR EACH ROW EXECUTE FUNCTION sf_audit_catalog_registration();

CREATE FUNCTION sf_validate_product_catalog_assignment() RETURNS trigger LANGUAGE plpgsql AS $$ BEGIN
 PERFORM pg_advisory_xact_lock(hashtextextended('catalog-registration:' || NEW.tenant_id::text,0));
 -- Inactive historical products retain their existing relationships.
 IF NEW.is_active THEN
   IF NOT EXISTS(SELECT 1 FROM categories WHERE tenant_id=NEW.tenant_id AND category_id=NEW.category_id AND is_active) THEN
     RAISE EXCEPTION 'Select an active category' USING ERRCODE='23514'; END IF;
   IF NEW.brand_id IS NOT NULL AND NOT EXISTS(SELECT 1 FROM brands WHERE tenant_id=NEW.tenant_id AND brand_id=NEW.brand_id AND is_active) THEN
     RAISE EXCEPTION 'Select an active brand' USING ERRCODE='23514'; END IF;
 END IF;
 RETURN NEW;
END $$;
CREATE TRIGGER validate_product_catalog_assignment BEFORE INSERT OR UPDATE OF category_id,brand_id,is_active ON products
 FOR EACH ROW EXECUTE FUNCTION sf_validate_product_catalog_assignment();
COMMIT;
