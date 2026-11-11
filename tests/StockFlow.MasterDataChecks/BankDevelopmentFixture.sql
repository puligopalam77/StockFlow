-- Synthetic source contracts missing from the narrow migration-review fixture.
-- Not a production migration. The authentic full chain still needs migration 049.
ALTER TABLE expenses ADD COLUMN status text NOT NULL DEFAULT 'POSTED';
ALTER TABLE expense_payments ADD COLUMN payment_id uuid NOT NULL DEFAULT gen_random_uuid(),ADD COLUMN kind text NOT NULL DEFAULT 'PAYMENT',ADD COLUMN reverses_id uuid;
CREATE TABLE purchases(tenant_id bigint,purchase_id bigint,store_id bigint,business_profile_code text DEFAULT 'ELECTRICAL',status text DEFAULT 'COMPLETED');
CREATE TABLE service_requests(tenant_id bigint,service_request_id bigint,store_id bigint,business_profile_code text DEFAULT 'ELECTRICAL');
INSERT INTO purchases VALUES(1,1,1,'ELECTRICAL','COMPLETED');
INSERT INTO service_requests VALUES(1,1,1,'ELECTRICAL');
INSERT INTO modules(module_code,module_name,is_active,show_in_navigation) VALUES('SALES','Synthetic Sales',true,false),('PURCHASES','Synthetic Purchases',true,false),('EXPENSES','Synthetic Expenses',true,false),('SERVICE','Synthetic Service',true,false);
INSERT INTO module_actions(module_id,action_code,action_name,is_sensitive) SELECT module_id,'VIEW','View synthetic payment source',false FROM modules WHERE module_code IN('SALES','PURCHASES','EXPENSES','SERVICE');
-- Two additional synthetic source payments, no real source data or bank credentials.
INSERT INTO supplier_payments(tenant_id,location_id,supplier_payment_id,amount,payment_mode,notes,payment_date,purchase_id) VALUES(1,1,1,20000,'BANK','Synthetic supplier settlement','2026-11-10 10:00+05:30',1);
INSERT INTO expenses(tenant_id,expense_id,description,store_id,status) VALUES(1,md5('bank-expense')::uuid,'Synthetic bank expense',1,'POSTED');
INSERT INTO expense_payments(tenant_id,location_id,expense_id,movement_id,amount,payment_mode,paid_date,reference,payment_id) VALUES(1,1,md5('bank-expense')::uuid,1,3000,'BANK_TRANSFER','2026-11-10','SYNTHETIC-EXPENSE',md5('bank-expense-payment')::uuid);
