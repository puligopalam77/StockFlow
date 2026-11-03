-- Run ONLY against the isolated restored category_two_level_test database.
BEGIN;
DO $$ BEGIN IF current_database()<>'category_two_level_test' THEN RAISE EXCEPTION 'Isolated category_two_level_test required'; END IF; END $$;
SELECT set_config('app.tenant_id','1',true),set_config('app.user_id','0',true),set_config('app.business_profile_code','BIKE_ACCESSORIES',true);
DO $$ BEGIN
 IF (SELECT count(*) FROM categories)<>34 THEN RAISE EXCEPTION 'Snapshot category count changed'; END IF;
 IF EXISTS(SELECT 1 FROM categories WHERE category_id IN(8,11,43,47,50) AND parent_category_id IS NOT NULL) THEN RAISE EXCEPTION 'Existing IDs not promoted'; END IF;
 IF NOT EXISTS(SELECT 1 FROM categories WHERE category_id=37 AND is_retired_grouping AND NOT is_active AND category_code='CAT0037') THEN RAISE EXCEPTION 'Old root identity not retained'; END IF;
 IF NOT EXISTS(SELECT 1 FROM categories WHERE category_id=12 AND parent_category_id=8 AND category_code='CAT0012') THEN RAISE EXCEPTION 'Child ID/code/parent changed'; END IF;
 PERFORM public.sf_organize_bike_hierarchy(1);
 IF (SELECT count(*) FROM categories)<>34 THEN RAISE EXCEPTION 'Profile initialization recreated/duplicated category identities'; END IF;
 BEGIN INSERT INTO categories(tenant_id,business_profile_code,category_name,parent_category_id) VALUES(1,'BIKE_ACCESSORIES','Forbidden third',12);RAISE EXCEPTION 'Third level accepted'; EXCEPTION WHEN check_violation THEN NULL; END;
 BEGIN INSERT INTO categories(tenant_id,business_profile_code,category_name) VALUES(1,'BIKE_ACCESSORIES',' Bike   Spare Parts ');RAISE EXCEPTION 'Normalized duplicate root accepted'; EXCEPTION WHEN unique_violation THEN NULL; END;
 BEGIN INSERT INTO categories(tenant_id,business_profile_code,category_name,parent_category_id) VALUES(1,'BIKE_ACCESSORIES',' Brake  Parts ',8);RAISE EXCEPTION 'Duplicate sibling accepted'; EXCEPTION WHEN unique_violation THEN NULL; END;
 BEGIN UPDATE categories SET parent_category_id=7 WHERE category_id=8;RAISE EXCEPTION 'Direct reparent accepted'; EXCEPTION WHEN check_violation THEN NULL; END;
 BEGIN UPDATE categories SET is_active=true WHERE category_id=37;RAISE EXCEPTION 'Retired grouping reactivated'; EXCEPTION WHEN check_violation THEN NULL; END;
END $$;
UPDATE categories SET is_active=false WHERE category_id=7;
DO $$ BEGIN
 IF NOT (SELECT is_active FROM categories WHERE category_id=38) THEN RAISE EXCEPTION 'Child status overwritten'; END IF;
 BEGIN INSERT INTO categories(tenant_id,business_profile_code,category_name,parent_category_id) VALUES(1,'BIKE_ACCESSORIES','New under inactive',7);RAISE EXCEPTION 'Inactive parent accepted'; EXCEPTION WHEN check_violation THEN NULL; END;
 BEGIN UPDATE products SET category_id=38 WHERE category_id=7;RAISE EXCEPTION 'New assignment to child of inactive parent accepted'; EXCEPTION WHEN check_violation THEN NULL; END;
END $$;
-- Existing product metadata/status updates are not blocked solely by category deactivation.
UPDATE products SET is_active=is_active WHERE category_id=38;
UPDATE categories SET is_active=false WHERE category_id=39;
UPDATE categories SET is_active=true WHERE category_id=7;
DO $$ BEGIN IF (SELECT is_active FROM categories WHERE category_id=39) THEN RAISE EXCEPTION 'Independently inactive child changed'; END IF; END $$;
INSERT INTO tenants(tenant_id,tenant_code,business_name,business_profile_code) VALUES(999065,'TEST065','Isolated tenant test','BIKE_ACCESSORIES');
DO $$ BEGIN
 BEGIN INSERT INTO categories(tenant_id,business_profile_code,category_name,parent_category_id) VALUES(999065,'BIKE_ACCESSORIES','Cross tenant child',8);RAISE EXCEPTION 'Cross-shop parent accepted'; EXCEPTION WHEN check_violation THEN NULL; END;
END $$;
INSERT INTO categories(tenant_id,business_profile_code,category_name) VALUES(999065,'BIKE_ACCESSORIES','Bike Spare Parts');
SET LOCAL ROLE stockflow_runtime;
DO $$ BEGIN IF EXISTS(SELECT 1 FROM categories WHERE tenant_id=999065) THEN RAISE EXCEPTION 'Other tenant visible through category RLS'; END IF; END $$;
RESET ROLE;
ROLLBACK;
SELECT 'PASS: IDs, codes, children, third-level writes, normalized uniqueness, status semantics, existing product updates and tenant RLS verified; test mutations rolled back.' result;
