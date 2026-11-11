-- Isolated source-contract fixture, NOT a production baseline or migration replacement.
CREATE TABLE users(tenant_id bigint,user_id int,role_id int,is_active boolean,PRIMARY KEY(tenant_id,user_id));
CREATE TABLE roles(tenant_id bigint,role_id int,role_name text,PRIMARY KEY(tenant_id,role_id));
CREATE TABLE stores(tenant_id bigint,store_id bigint,PRIMARY KEY(tenant_id,store_id));
CREATE TABLE store_locations(tenant_id bigint,store_id bigint,location_id bigint,is_active boolean NOT NULL DEFAULT true,PRIMARY KEY(tenant_id,location_id));
CREATE FUNCTION sf_tenant() RETURNS bigint LANGUAGE sql STABLE AS $$SELECT current_setting('app.tenant_id',true)::bigint$$;
CREATE FUNCTION sf_store() RETURNS bigint LANGUAGE sql STABLE AS $$SELECT current_setting('app.store_id',true)::bigint$$;
CREATE FUNCTION sf_location() RETURNS bigint LANGUAGE sql STABLE AS $$SELECT current_setting('app.location_id',true)::bigint$$;
CREATE FUNCTION sf_actor() RETURNS int LANGUAGE sql STABLE AS $$SELECT current_setting('app.user_id',true)::int$$;
CREATE FUNCTION sf_visible_business_profile() RETURNS text LANGUAGE sql STABLE AS $$SELECT current_setting('app.business_profile_code',true)$$;
CREATE FUNCTION sf_can_location(loc bigint) RETURNS boolean LANGUAGE sql STABLE AS $$SELECT EXISTS(SELECT 1 FROM store_locations WHERE tenant_id=sf_tenant() AND location_id=loc AND store_id=sf_store())$$;
CREATE FUNCTION sf_shop_member(shop bigint) RETURNS boolean LANGUAGE sql STABLE AS $$SELECT EXISTS(SELECT 1 FROM users WHERE tenant_id=sf_tenant() AND user_id=sf_actor() AND is_active) AND shop=sf_store()$$;
CREATE FUNCTION sf_owner() RETURNS boolean LANGUAGE sql STABLE AS $$SELECT sf_actor()=1$$;
CREATE FUNCTION sf_transfer_permission(m text,a text) RETURNS boolean LANGUAGE sql STABLE AS $$SELECT sf_actor()<>3$$;
CREATE FUNCTION sf_quotation_immutable() RETURNS trigger LANGUAGE plpgsql AS $$BEGIN RAISE EXCEPTION 'Immutable'; END$$;
CREATE TABLE sales(tenant_id bigint,location_id bigint,sale_id bigint,invoice_number text);
CREATE TABLE payments(tenant_id bigint,location_id bigint,payment_id bigint,sale_id bigint,payment_mode text,amount numeric,payment_date timestamp);
CREATE TABLE service_payments(tenant_id bigint,location_id bigint,service_payment_id bigint,amount numeric,payment_mode text,payment_date timestamptz);
CREATE TABLE sale_returns(tenant_id bigint,location_id bigint,sale_return_id bigint,return_number text,refund_amount numeric,refund_mode text,created_at timestamptz);
CREATE TABLE supplier_payments(tenant_id bigint,location_id bigint,supplier_payment_id bigint,amount numeric,payment_mode text,notes text,payment_date timestamptz);
CREATE TABLE expense_payments(tenant_id bigint,location_id bigint,expense_id uuid,movement_id bigint UNIQUE,amount numeric,payment_mode text,paid_date date,reference text);
CREATE TABLE expenses(tenant_id bigint,expense_id uuid,description text);
ALTER TABLE expenses ADD COLUMN store_id bigint DEFAULT 1;
ALTER TABLE expense_payments ADD COLUMN business_profile_code varchar(40) DEFAULT 'ELECTRICAL';
ALTER TABLE service_payments ADD COLUMN service_request_id bigint DEFAULT 1;
ALTER TABLE supplier_payments ADD COLUMN purchase_id bigint DEFAULT 1;
ALTER TABLE sale_returns ADD COLUMN sale_id bigint DEFAULT 1;
CREATE FUNCTION sf_expense_cash_movements(loc bigint,s timestamptz,e timestamptz,zone text) RETURNS TABLE(id bigint,amount numeric,occurred timestamptz) LANGUAGE sql STABLE SECURITY DEFINER AS $$
 SELECT movement_id,amount,paid_date::timestamp AT TIME ZONE zone FROM expense_payments WHERE tenant_id=sf_tenant() AND location_id=loc AND loc=sf_location() AND sf_actor() IN(1,2) AND payment_mode='CASH' AND paid_date::timestamp AT TIME ZONE zone>=s AND paid_date::timestamp AT TIME ZONE zone<e
$$;
CREATE TABLE cash_closings(closing_id bigserial,tenant_id bigint DEFAULT sf_tenant(),location_id bigint,business_date date,version int,request_id uuid,request_payload jsonb,snapshot jsonb,closed_by int);
DO $$DECLARE tab text;BEGIN
 FOREACH tab IN ARRAY ARRAY['sales','payments','service_payments','sale_returns','supplier_payments','cash_closings'] LOOP
 EXECUTE format('ALTER TABLE %I ENABLE ROW LEVEL SECURITY',tab);EXECUTE format('ALTER TABLE %I FORCE ROW LEVEL SECURITY',tab);
 EXECUTE format('CREATE POLICY scoped ON %I USING(tenant_id=sf_tenant() AND location_id=sf_location())',tab);
 END LOOP;
END$$;
GRANT USAGE ON SCHEMA public TO stockflow_runtime;
GRANT SELECT,INSERT ON ALL TABLES IN SCHEMA public TO stockflow_runtime;
GRANT USAGE,SELECT ON ALL SEQUENCES IN SCHEMA public TO stockflow_runtime;
INSERT INTO stores VALUES(1,1),(1,2),(2,1);
INSERT INTO store_locations(tenant_id,store_id,location_id) VALUES(1,1,1),(1,2,2),(2,1,1);
INSERT INTO roles VALUES(1,1,'admin'),(1,2,'Cashier'),(2,1,'admin');
INSERT INTO users VALUES(1,1,1,true),(1,2,2,true),(1,3,1,true),(2,1,1,true);
