-- MANUAL: stop StockFlow, back up, execute after 028 as PostgreSQL superuser, then restart the updated application.
-- Keeps all existing module IDs and permissions. Adds database-owned module/submodule navigation metadata.
BEGIN;
DO $$ BEGIN
 IF NOT EXISTS(SELECT 1 FROM pg_roles WHERE rolname=current_user AND rolsuper) THEN RAISE EXCEPTION 'Apply as PostgreSQL superuser'; END IF;
 IF to_regclass('public.module_actions') IS NULL OR NOT EXISTS(SELECT 1 FROM modules WHERE module_code='ROLE_MANAGEMENT') THEN
  RAISE EXCEPTION 'Apply existing migrations through 028 first';
 END IF;
END $$;

ALTER TABLE modules
 ADD COLUMN IF NOT EXISTS parent_module_id integer,
 ADD COLUMN IF NOT EXISTS description varchar(300),
 ADD COLUMN IF NOT EXISTS navigation_icon varchar(50),
 ADD COLUMN IF NOT EXISTS show_in_navigation boolean NOT NULL DEFAULT false;

DO $$ BEGIN
 IF NOT EXISTS(SELECT 1 FROM pg_constraint WHERE conname='fk_modules_parent') THEN
  ALTER TABLE modules ADD CONSTRAINT fk_modules_parent FOREIGN KEY(parent_module_id) REFERENCES modules(module_id);
 END IF;
END $$;
CREATE INDEX IF NOT EXISTS ix_modules_parent_sort ON modules(parent_module_id,sort_order,module_id) WHERE is_active;

INSERT INTO modules(module_code,module_name,sort_order,is_active,navigation_icon,show_in_navigation) VALUES
 ('RECEIVE_STOCK','Receive Stock',10,true,'in',true),
 ('SELL_BILLING','Sell / Billing',20,true,'document',true),
 ('INVENTORY_CONTROL','Inventory',30,true,'box',true),
 ('SERVICE_WARRANTY','Service & Warranty',50,true,'tools',true),
 ('SETUP','Setup',60,true,'settings',true)
ON CONFLICT(module_code) DO UPDATE SET module_name=excluded.module_name,sort_order=excluded.sort_order,
 is_active=true,navigation_icon=excluded.navigation_icon,show_in_navigation=true,parent_module_id=NULL,route=NULL;

-- Existing permission modules become submodules without changing their IDs.
WITH hierarchy(child_code,parent_code,module_name,route,sort_order,description,visible) AS (VALUES
 ('PURCHASES','RECEIVE_STOCK','Purchase orders & deliveries','/Purchases',10,'Advanced purchase orders, supplier deliveries and purchase records',true),
 ('STOCK_INFLOW','RECEIVE_STOCK','Inflow history','/StockInflow',30,'Review received stock movements',true),
 ('SALES','SELL_BILLING','Sales & invoices','/Sales',10,'Counter billing, collections, returns and sales records',true),
 ('INVOICES','SELL_BILLING','Invoices',NULL,30,'Print and export completed invoices',false),
 ('INVENTORY','INVENTORY_CONTROL','Current stock','/Inventory',10,'Current quantities, serials and movement history',true),
 ('STOCK_ADJUSTMENT','INVENTORY_CONTROL','Stock adjustments / opening stock',NULL,15,'Opening stock and authorized stock corrections',false),
 ('STOCK_TRANSFER','INVENTORY_CONTROL','Stock transfers','/Transfers',30,'Request, dispatch and receive inter-store transfers',true),
 ('STOCK_OUTFLOW','INVENTORY_CONTROL','Outflow history','/StockOutflow',40,'Review stock leaving the location',true),
 ('SCAN','INVENTORY_CONTROL','Scan product / serial','/Scan',50,'Find registered items using a product, unit or serial label',true),
 ('SEARCH','INVENTORY_CONTROL','Search records','/Search',60,'Search permitted operational records',true),
 ('SERVICE','SERVICE_WARRANTY','Service history','/Service',10,'Repairs, assignment, delivery and collection',true),
 ('PENDING_SERVICES','SERVICE_WARRANTY','Pending services','/Service/Pending',30,'Review pending repair work',true),
 ('WARRANTY_LOOKUP','SERVICE_WARRANTY','Warranty lookup','/Warranty/Lookup',40,'Check warranty coverage',true),
 ('WARRANTY_CLAIMS','SERVICE_WARRANTY','Warranty claims','/Warranty/Claims',50,'Record and resolve warranty claims',true),
 ('EXPIRING_WARRANTIES','SERVICE_WARRANTY','Expiring warranties','/Warranty/Expiring',60,'Review warranties nearing expiry',true),
 ('PRODUCTS','SETUP','Products','/Products',10,'Product registration',false),
 ('PRODUCT_VARIANTS','SETUP','Product configurations / variants',NULL,11,'Pricing, identifiers and product specifications',false),
 ('BARCODES','SETUP','Barcodes & labels',NULL,12,'Existing and internal identifiers, printing and reprinting',false),
 ('CATEGORIES','SETUP','Categories','/Categories',13,'Product category setup',false),
 ('BRANDS','SETUP','Brands','/Brands',14,'Brand setup',false),
 ('SUPPLIERS','SETUP','Suppliers','/Suppliers',15,'Supplier registration',false),
 ('CUSTOMERS','SETUP','Customers','/Customers',16,'Customer records and billing lookup',false),
 ('USER_MANAGEMENT','SETUP','Users','/Admin/Users',30,'Create accounts and assign module actions',true),
 ('ROLE_MANAGEMENT','SETUP','Roles & permissions','/Admin/Roles',40,'Role templates for new user permissions',true),
 ('STORES','SETUP','Stores','/Admin/Stores',50,'Store setup and employee assignments',true),
 ('SECURITY','SETUP','Security settings','/Admin/Security',60,'Password and authenticator policies',true),
 ('AUDIT_LOGS','SETUP','Audit logs','/Admin/AuditLogs',70,'Review security and operational changes',true),
 ('COMPANY_SETTINGS','SETUP','Company settings','/Settings/Company',80,'Business and invoice settings',true)
)
UPDATE modules m SET parent_module_id=p.module_id,module_name=h.module_name,route=h.route,sort_order=h.sort_order,
 description=h.description,show_in_navigation=h.visible
FROM hierarchy h JOIN modules p ON p.module_code=h.parent_code WHERE m.module_code=h.child_code;

UPDATE modules SET module_name='Dashboard',route='/',sort_order=1,description='Shop overview',navigation_icon='home',show_in_navigation=true,parent_module_id=NULL WHERE module_code='DASHBOARD';
UPDATE modules SET module_name='Reports',route='/Reports',sort_order=40,description='Daily, weekly, monthly and custom period reports',navigation_icon='chart',show_in_navigation=true,parent_module_id=NULL WHERE module_code='REPORTS';

-- Navigation-only submodules delegate authorization to an existing permission module below.
INSERT INTO modules(module_code,module_name,route,sort_order,is_active,parent_module_id,description,show_in_navigation)
SELECT x.code,x.name,x.route,x.sort_order,true,p.module_id,x.description,true
FROM (VALUES
 ('NEW_STOCK_RECEIPT','New stock receipt','/Purchases/Create',20,'RECEIVE_STOCK','Create a supplier purchase or incoming stock draft'),
 ('NEW_SALE','New sale','/Sales/Create',20,'SELL_BILLING','Start scanner-based counter billing'),
 ('LOW_STOCK','Low stock','/Inventory/LowStock',20,'INVENTORY_CONTROL','Review items at or below their reorder level'),
 ('NEW_SERVICE_REQUEST','New service request','/Service/Create',20,'SERVICE_WARRANTY','Register a repair or service request'),
 ('CONFIGURATION','Configuration','/Configuration',20,'SETUP','Products, categories, brands, suppliers and customers')
) x(code,name,route,sort_order,parent_code,description)
JOIN modules p ON p.module_code=x.parent_code
ON CONFLICT(module_code) DO UPDATE SET module_name=excluded.module_name,route=excluded.route,sort_order=excluded.sort_order,
 is_active=true,parent_module_id=excluded.parent_module_id,description=excluded.description,show_in_navigation=true;

CREATE TABLE IF NOT EXISTS module_navigation_permissions(
 navigation_module_id integer NOT NULL REFERENCES modules(module_id) ON DELETE CASCADE,
 permission_module_id integer NOT NULL REFERENCES modules(module_id),
 action_code varchar(50) NOT NULL DEFAULT 'VIEW',
 PRIMARY KEY(navigation_module_id,permission_module_id,action_code)
);
TRUNCATE module_navigation_permissions;

WITH requirements(navigation_code,permission_code,action_code) AS (VALUES
 ('DASHBOARD','DASHBOARD','VIEW'),('REPORTS','REPORTS','VIEW'),
 ('PURCHASES','PURCHASES','VIEW'),('NEW_STOCK_RECEIPT','PURCHASES','CREATE'),('STOCK_INFLOW','STOCK_INFLOW','VIEW'),
 ('SALES','SALES','VIEW'),('NEW_SALE','SALES','CREATE'),
 ('INVENTORY','INVENTORY','VIEW'),('LOW_STOCK','INVENTORY','VIEW'),('STOCK_TRANSFER','STOCK_TRANSFER','VIEW'),
 ('STOCK_OUTFLOW','STOCK_OUTFLOW','VIEW'),('SCAN','SCAN','VIEW'),('SEARCH','SEARCH','VIEW'),
 ('SERVICE','SERVICE','VIEW'),('NEW_SERVICE_REQUEST','SERVICE','CREATE'),('PENDING_SERVICES','PENDING_SERVICES','VIEW'),
 ('WARRANTY_LOOKUP','WARRANTY_LOOKUP','VIEW'),('WARRANTY_CLAIMS','WARRANTY_CLAIMS','VIEW'),('EXPIRING_WARRANTIES','EXPIRING_WARRANTIES','VIEW'),
 ('CONFIGURATION','PRODUCTS','VIEW'),('CONFIGURATION','CATEGORIES','VIEW'),('CONFIGURATION','BRANDS','VIEW'),
 ('CONFIGURATION','SUPPLIERS','VIEW'),('CONFIGURATION','CUSTOMERS','VIEW'),
 ('USER_MANAGEMENT','USER_MANAGEMENT','VIEW'),('ROLE_MANAGEMENT','ROLE_MANAGEMENT','VIEW'),('STORES','STORES','VIEW'),
 ('SECURITY','SECURITY','VIEW'),('AUDIT_LOGS','AUDIT_LOGS','VIEW'),('COMPANY_SETTINGS','COMPANY_SETTINGS','VIEW')
)
INSERT INTO module_navigation_permissions(navigation_module_id,permission_module_id,action_code)
SELECT n.module_id,p.module_id,r.action_code FROM requirements r
JOIN modules n ON n.module_code=r.navigation_code JOIN modules p ON p.module_code=r.permission_code;

REVOKE ALL ON module_navigation_permissions FROM PUBLIC;
GRANT SELECT ON modules,module_actions,module_navigation_permissions TO stockflow_runtime;
COMMIT;
