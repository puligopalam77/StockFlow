-- Synthetic seed ONLY, after actual baseline migrations in a disposable cluster.
SELECT set_config('app.tenant_id','1',false),set_config('app.user_id','1',false),
 set_config('app.store_id','1',false),set_config('app.location_id','1',false),
 set_config('app.business_profile_code','ELECTRICAL',false);
UPDATE users SET username=CASE user_id WHEN 1 THEN 'synthetic-bank-owner' WHEN 2 THEN 'synthetic-bank-admin' ELSE 'synthetic-bank-cashier' END,
 full_name='Synthetic actor',password_hash='NOT-A-LOGIN-HASH',is_tenant_owner=(user_id=1),role_id=CASE WHEN user_id=3 THEN 2 ELSE 1 END WHERE tenant_id=1;
INSERT INTO users(tenant_id,user_id,role_id,is_active,username,full_name,password_hash,is_tenant_owner)
 VALUES(2,4,1,true,'synthetic-bank-other','Synthetic other owner','NOT-A-LOGIN-HASH',true);
UPDATE stores SET store_code='SYNTHETIC-'||tenant_id||'-'||store_id,store_name='Synthetic shop';
INSERT INTO stores(tenant_id,store_id,store_code,store_name) VALUES(2,3,'BANK-TEST-OTHER','Synthetic other shop');
UPDATE store_locations SET location_name='Synthetic bank counter';
INSERT INTO store_locations(tenant_id,store_id,location_id,location_name) VALUES(2,3,3,'Synthetic other counter');
INSERT INTO user_store_access(tenant_id,user_id,store_id,is_active)
 SELECT 1,user_id,1,true FROM users WHERE username IN('synthetic-bank-admin','synthetic-bank-cashier');
INSERT INTO sales(tenant_id,store_id,location_id,sale_id,invoice_number,subtotal,total_amount,status,created_by)
 VALUES(1,1,1,1,'SYNTHETIC-BANK-BASELINE',5000,5000,'COMPLETED',1);
INSERT INTO payments(tenant_id,location_id,payment_id,sale_id,payment_mode,amount,reference_number,created_by,payment_date)
 SELECT 1,1,1,sale_id,'UPI',5000,'SYNTHETIC-BASELINE-UPI',1,'2026-11-09'::timestamp FROM sales WHERE invoice_number='SYNTHETIC-BANK-BASELINE';
CREATE TABLE bank_test_source_snapshot AS
 SELECT 'sales' source,md5(coalesce(jsonb_agg(to_jsonb(s) ORDER BY sale_id)::text,'[]')) digest FROM sales s
 UNION ALL SELECT 'payments',md5(coalesce(jsonb_agg(to_jsonb(p) ORDER BY payment_id)::text,'[]')) FROM payments p
 UNION ALL SELECT 'cash_adjustments',md5(coalesce(jsonb_agg(to_jsonb(c) ORDER BY movement_number)::text,'[]')) FROM cash_adjustments c
 UNION ALL SELECT 'user_action_permissions',md5(coalesce(jsonb_agg(to_jsonb(p) ORDER BY user_id,module_action_id)::text,'[]')) FROM user_action_permissions p;
