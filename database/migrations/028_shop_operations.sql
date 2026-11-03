-- MANUAL: stop StockFlow, back up, execute after 027 as PostgreSQL superuser, then restart.
-- Adds customer collections/returns, supplier settlements/returns, stock counts,
-- service collections, delivery/installation, trade-ins and manufacturer RMA tracking.
BEGIN;
DO $$ BEGIN
 IF NOT EXISTS(SELECT 1 FROM pg_roles WHERE rolname=current_user AND rolsuper) THEN RAISE EXCEPTION 'Apply as PostgreSQL superuser'; END IF;
 IF to_regclass('public.stock_transfer_operations') IS NULL OR to_regclass('public.user_action_permissions') IS NULL THEN RAISE EXCEPTION 'Apply existing migrations through 027 first'; END IF;
END $$;

ALTER TABLE payments ADD COLUMN IF NOT EXISTS request_id UUID;
ALTER TABLE payments ADD COLUMN IF NOT EXISTS request_payload JSONB;
CREATE UNIQUE INDEX IF NOT EXISTS ux_payments_request ON payments(tenant_id,request_id) WHERE request_id IS NOT NULL;
CREATE UNIQUE INDEX IF NOT EXISTS ux_purchase_items_operation_location ON purchase_items(tenant_id,location_id,purchase_item_id);
CREATE UNIQUE INDEX IF NOT EXISTS ux_warranty_claims_operation_location ON warranty_claims(tenant_id,location_id,warranty_claim_id);

-- New operations appear as actions under their existing modules in Roles & permissions.
INSERT INTO module_actions(module_id,action_code,action_name,is_sensitive)
SELECT m.module_id,x.code,x.name,x.sensitive FROM modules m CROSS JOIN (VALUES
 ('SALES','COLLECT','Collect customer payment',false),('SALES','RETURN','Return / refund / exchange',true),('SALES','FULFIL','Delivery, installation & trade-in',false),
 ('PURCHASES','PAY','Pay supplier',false),('PURCHASES','RETURN','Return to supplier',true),('SERVICE','COLLECT','Collect service payment',false)
) x(module,code,name,sensitive) WHERE m.module_code=x.module ON CONFLICT(module_id,action_code) DO NOTHING;
INSERT INTO user_action_permissions(tenant_id,user_id,module_action_id,is_allowed)
SELECT u.tenant_id,u.user_id,a.module_action_id,true FROM users u JOIN modules m ON m.module_code IN('SALES','PURCHASES','SERVICE')
JOIN module_actions a ON a.module_id=m.module_id AND a.action_code='VIEW' WHERE u.is_tenant_owner
ON CONFLICT(user_id,module_action_id) DO UPDATE SET is_allowed=true;
INSERT INTO user_action_permissions(tenant_id,user_id,module_action_id,is_allowed)
SELECT u.tenant_id,u.user_id,a.module_action_id,true FROM users u JOIN module_actions a ON true JOIN modules m ON m.module_id=a.module_id
WHERE u.is_tenant_owner AND (m.module_code,a.action_code) IN (('SALES','COLLECT'),('SALES','RETURN'),('SALES','FULFIL'),('PURCHASES','PAY'),('PURCHASES','RETURN'),('SERVICE','COLLECT'))
ON CONFLICT(user_id,module_action_id) DO UPDATE SET is_allowed=true;
-- Preserve the intent of existing grants while keeping the new actions independently assignable.
INSERT INTO user_action_permissions(tenant_id,user_id,module_action_id,is_allowed)
SELECT p.tenant_id,p.user_id,n.module_action_id,true FROM user_action_permissions p
JOIN module_actions old ON old.module_action_id=p.module_action_id AND p.is_allowed JOIN modules m ON m.module_id=old.module_id
JOIN module_actions n ON n.module_id=m.module_id AND n.action_code=CASE
 WHEN m.module_code='PURCHASES' AND old.action_code='EDIT' THEN 'PAY'
 WHEN m.module_code='PURCHASES' AND old.action_code='APPROVE' THEN 'RETURN'
 WHEN m.module_code='SERVICE' AND old.action_code='DELIVER' THEN 'COLLECT' END
WHERE (m.module_code,old.action_code) IN (('PURCHASES','EDIT'),('PURCHASES','APPROVE'),('SERVICE','DELIVER')) ON CONFLICT DO NOTHING;

CREATE TABLE sale_returns(
 sale_return_id BIGSERIAL PRIMARY KEY,tenant_id BIGINT NOT NULL DEFAULT sf_tenant(),location_id BIGINT NOT NULL DEFAULT NULLIF(sf_location(),0),
 sale_id BIGINT NOT NULL,return_number VARCHAR(50) NOT NULL,return_date TIMESTAMPTZ NOT NULL DEFAULT now(),
 return_total NUMERIC(12,2) NOT NULL CHECK(return_total>=0),refund_mode VARCHAR(30) NOT NULL CHECK(refund_mode IN('CASH','CARD','UPI','BANK','CREDIT_ADJUSTMENT','EXCHANGE','NONE')),
 refund_amount NUMERIC(12,2) NOT NULL DEFAULT 0 CHECK(refund_amount>=0),notes VARCHAR(2000) NOT NULL,
 request_id UUID NOT NULL,request_payload JSONB NOT NULL,created_by INT NOT NULL,created_at TIMESTAMPTZ NOT NULL DEFAULT now(),
 UNIQUE(tenant_id,request_id),UNIQUE(tenant_id,return_number),UNIQUE(tenant_id,location_id,sale_return_id),
 FOREIGN KEY(tenant_id,location_id,sale_id) REFERENCES sales(tenant_id,location_id,sale_id),
 FOREIGN KEY(tenant_id,created_by) REFERENCES users(tenant_id,user_id),FOREIGN KEY(tenant_id,location_id) REFERENCES store_locations(tenant_id,location_id),
 CHECK(refund_amount<=return_total)
);
CREATE TABLE sale_return_items(
 sale_return_item_id BIGSERIAL PRIMARY KEY,tenant_id BIGINT NOT NULL DEFAULT sf_tenant(),location_id BIGINT NOT NULL DEFAULT NULLIF(sf_location(),0),
 sale_return_id BIGINT NOT NULL,sale_item_id BIGINT NOT NULL,product_id INT NOT NULL,variant_id BIGINT NOT NULL,product_unit_id BIGINT,
 quantity INT NOT NULL CHECK(quantity BETWEEN 1 AND 10000),amount NUMERIC(12,2) NOT NULL CHECK(amount>=0),
 disposition VARCHAR(30) NOT NULL CHECK(disposition IN('RESTOCK','DAMAGED','SUPPLIER_RETURN','SERVICE_HOLD')),
 FOREIGN KEY(tenant_id,location_id,sale_return_id) REFERENCES sale_returns(tenant_id,location_id,sale_return_id),
 FOREIGN KEY(tenant_id,location_id,sale_item_id) REFERENCES sale_items(tenant_id,location_id,sale_item_id),
 FOREIGN KEY(tenant_id,product_id) REFERENCES products(tenant_id,product_id),FOREIGN KEY(tenant_id,variant_id) REFERENCES product_variants(tenant_id,variant_id),
 FOREIGN KEY(tenant_id,product_unit_id) REFERENCES product_units(tenant_id,product_unit_id)
);
CREATE UNIQUE INDEX ux_sale_return_unit ON sale_return_items(tenant_id,product_unit_id) WHERE product_unit_id IS NOT NULL;

CREATE TABLE supplier_payments(
 supplier_payment_id BIGSERIAL PRIMARY KEY,tenant_id BIGINT NOT NULL DEFAULT sf_tenant(),location_id BIGINT NOT NULL DEFAULT NULLIF(sf_location(),0),
 purchase_id BIGINT NOT NULL,payment_date TIMESTAMPTZ NOT NULL DEFAULT now(),payment_mode VARCHAR(30) NOT NULL CHECK(payment_mode IN('CASH','CARD','UPI','BANK','CHEQUE')),
 amount NUMERIC(12,2) NOT NULL CHECK(amount>0),reference_number VARCHAR(100),notes VARCHAR(1000),request_id UUID NOT NULL,request_payload JSONB NOT NULL,created_by INT NOT NULL,created_at TIMESTAMPTZ NOT NULL DEFAULT now(),
 UNIQUE(tenant_id,request_id),UNIQUE(tenant_id,location_id,supplier_payment_id),
 FOREIGN KEY(tenant_id,location_id,purchase_id) REFERENCES purchases(tenant_id,location_id,purchase_id),FOREIGN KEY(tenant_id,created_by) REFERENCES users(tenant_id,user_id),
 FOREIGN KEY(tenant_id,location_id) REFERENCES store_locations(tenant_id,location_id)
);
CREATE TABLE purchase_returns(
 purchase_return_id BIGSERIAL PRIMARY KEY,tenant_id BIGINT NOT NULL DEFAULT sf_tenant(),location_id BIGINT NOT NULL DEFAULT NULLIF(sf_location(),0),
 purchase_id BIGINT NOT NULL,return_number VARCHAR(50) NOT NULL,return_date TIMESTAMPTZ NOT NULL DEFAULT now(),return_total NUMERIC(12,2) NOT NULL CHECK(return_total>=0),
 supplier_reference VARCHAR(100),notes VARCHAR(2000) NOT NULL,request_id UUID NOT NULL,request_payload JSONB NOT NULL,created_by INT NOT NULL,created_at TIMESTAMPTZ NOT NULL DEFAULT now(),
 UNIQUE(tenant_id,request_id),UNIQUE(tenant_id,return_number),UNIQUE(tenant_id,location_id,purchase_return_id),
 FOREIGN KEY(tenant_id,location_id,purchase_id) REFERENCES purchases(tenant_id,location_id,purchase_id),FOREIGN KEY(tenant_id,created_by) REFERENCES users(tenant_id,user_id),
 FOREIGN KEY(tenant_id,location_id) REFERENCES store_locations(tenant_id,location_id)
);
CREATE TABLE purchase_return_items(
 purchase_return_item_id BIGSERIAL PRIMARY KEY,tenant_id BIGINT NOT NULL DEFAULT sf_tenant(),location_id BIGINT NOT NULL DEFAULT NULLIF(sf_location(),0),
 purchase_return_id BIGINT NOT NULL,purchase_item_id BIGINT NOT NULL,product_id INT NOT NULL,variant_id BIGINT NOT NULL,product_unit_id BIGINT,
 quantity INT NOT NULL CHECK(quantity BETWEEN 1 AND 10000),amount NUMERIC(12,2) NOT NULL CHECK(amount>=0),reason VARCHAR(1000) NOT NULL,
 FOREIGN KEY(tenant_id,location_id,purchase_return_id) REFERENCES purchase_returns(tenant_id,location_id,purchase_return_id),
 FOREIGN KEY(tenant_id,location_id,purchase_item_id) REFERENCES purchase_items(tenant_id,location_id,purchase_item_id),
 FOREIGN KEY(tenant_id,product_id) REFERENCES products(tenant_id,product_id),FOREIGN KEY(tenant_id,variant_id) REFERENCES product_variants(tenant_id,variant_id),
 FOREIGN KEY(tenant_id,product_unit_id) REFERENCES product_units(tenant_id,product_unit_id)
);
CREATE UNIQUE INDEX ux_purchase_return_unit ON purchase_return_items(tenant_id,product_unit_id) WHERE product_unit_id IS NOT NULL;

CREATE TABLE stock_counts(
 stock_count_id BIGSERIAL PRIMARY KEY,tenant_id BIGINT NOT NULL DEFAULT sf_tenant(),location_id BIGINT NOT NULL DEFAULT NULLIF(sf_location(),0),
 count_number VARCHAR(50) NOT NULL,counted_at TIMESTAMPTZ NOT NULL DEFAULT now(),status VARCHAR(20) NOT NULL DEFAULT 'POSTED' CHECK(status IN('POSTED')),
 notes VARCHAR(2000) NOT NULL,request_id UUID NOT NULL,request_payload JSONB NOT NULL,created_by INT NOT NULL,created_at TIMESTAMPTZ NOT NULL DEFAULT now(),
 UNIQUE(tenant_id,request_id),UNIQUE(tenant_id,count_number),UNIQUE(tenant_id,location_id,stock_count_id),
 FOREIGN KEY(tenant_id,created_by) REFERENCES users(tenant_id,user_id),FOREIGN KEY(tenant_id,location_id) REFERENCES store_locations(tenant_id,location_id)
);
CREATE TABLE stock_count_items(
 stock_count_item_id BIGSERIAL PRIMARY KEY,tenant_id BIGINT NOT NULL DEFAULT sf_tenant(),location_id BIGINT NOT NULL DEFAULT NULLIF(sf_location(),0),
 stock_count_id BIGINT NOT NULL,product_id INT NOT NULL,variant_id BIGINT NOT NULL,expected_quantity INT NOT NULL,counted_quantity INT NOT NULL CHECK(counted_quantity BETWEEN 0 AND 100000),
 variance INT NOT NULL,remarks VARCHAR(1000),FOREIGN KEY(tenant_id,location_id,stock_count_id) REFERENCES stock_counts(tenant_id,location_id,stock_count_id),
 FOREIGN KEY(tenant_id,product_id) REFERENCES products(tenant_id,product_id),FOREIGN KEY(tenant_id,variant_id) REFERENCES product_variants(tenant_id,variant_id),UNIQUE(stock_count_id,variant_id)
);

CREATE TABLE service_payments(
 service_payment_id BIGSERIAL PRIMARY KEY,tenant_id BIGINT NOT NULL DEFAULT sf_tenant(),location_id BIGINT NOT NULL DEFAULT NULLIF(sf_location(),0),
 service_request_id BIGINT NOT NULL,payment_date TIMESTAMPTZ NOT NULL DEFAULT now(),payment_mode VARCHAR(30) NOT NULL CHECK(payment_mode IN('CASH','CARD','UPI','BANK','CREDIT')),
 amount NUMERIC(12,2) NOT NULL CHECK(amount>0),reference_number VARCHAR(100),request_id UUID NOT NULL,request_payload JSONB NOT NULL,created_by INT NOT NULL,created_at TIMESTAMPTZ NOT NULL DEFAULT now(),
 UNIQUE(tenant_id,request_id),FOREIGN KEY(tenant_id,location_id,service_request_id) REFERENCES service_requests(tenant_id,location_id,service_request_id),
 FOREIGN KEY(tenant_id,created_by) REFERENCES users(tenant_id,user_id),FOREIGN KEY(tenant_id,location_id) REFERENCES store_locations(tenant_id,location_id)
);
CREATE TABLE sale_fulfilments(
 fulfilment_id BIGSERIAL PRIMARY KEY,tenant_id BIGINT NOT NULL DEFAULT sf_tenant(),location_id BIGINT NOT NULL DEFAULT NULLIF(sf_location(),0),sale_id BIGINT NOT NULL,
 fulfilment_type VARCHAR(20) NOT NULL CHECK(fulfilment_type IN('DELIVERY','INSTALLATION','DELIVERY_INSTALLATION')),status VARCHAR(20) NOT NULL CHECK(status IN('SCHEDULED','IN_PROGRESS','COMPLETED','CANCELLED')),
 scheduled_at TIMESTAMPTZ NOT NULL,address VARCHAR(1000) NOT NULL,contact_number VARCHAR(30),technician_name VARCHAR(150),notes VARCHAR(2000),completed_at TIMESTAMPTZ,
 request_id UUID NOT NULL,request_payload JSONB NOT NULL,created_by INT NOT NULL,created_at TIMESTAMPTZ NOT NULL DEFAULT now(),UNIQUE(tenant_id,request_id),
 FOREIGN KEY(tenant_id,location_id,sale_id) REFERENCES sales(tenant_id,location_id,sale_id),FOREIGN KEY(tenant_id,created_by) REFERENCES users(tenant_id,user_id),FOREIGN KEY(tenant_id,location_id) REFERENCES store_locations(tenant_id,location_id)
);
CREATE TABLE trade_ins(
 trade_in_id BIGSERIAL PRIMARY KEY,tenant_id BIGINT NOT NULL DEFAULT sf_tenant(),location_id BIGINT NOT NULL DEFAULT NULLIF(sf_location(),0),sale_id BIGINT NOT NULL,
 item_type VARCHAR(50) NOT NULL,brand_model VARCHAR(200),serial_number VARCHAR(150),allowance_amount NUMERIC(12,2) NOT NULL CHECK(allowance_amount>=0),
 status VARCHAR(20) NOT NULL CHECK(status IN('RECEIVED','VALUED','RESOLD','RECYCLED','DISPOSED')),notes VARCHAR(1000),request_id UUID NOT NULL,request_payload JSONB NOT NULL,created_by INT NOT NULL,created_at TIMESTAMPTZ NOT NULL DEFAULT now(),
 UNIQUE(tenant_id,request_id),FOREIGN KEY(tenant_id,location_id,sale_id) REFERENCES sales(tenant_id,location_id,sale_id),FOREIGN KEY(tenant_id,created_by) REFERENCES users(tenant_id,user_id),FOREIGN KEY(tenant_id,location_id) REFERENCES store_locations(tenant_id,location_id)
);
CREATE TABLE warranty_rmas(
 warranty_rma_id BIGSERIAL PRIMARY KEY,tenant_id BIGINT NOT NULL DEFAULT sf_tenant(),location_id BIGINT NOT NULL DEFAULT NULLIF(sf_location(),0),warranty_claim_id BIGINT NOT NULL,
 provider_reference VARCHAR(100),status VARCHAR(30) NOT NULL CHECK(status IN('READY_TO_SEND','SENT_TO_PROVIDER','PROVIDER_RECEIVED','REPAIRED','REPLACED','REJECTED','RETURNED_TO_CUSTOMER')),
 dispatched_at TIMESTAMPTZ,returned_at TIMESTAMPTZ,charge_amount NUMERIC(12,2) NOT NULL DEFAULT 0 CHECK(charge_amount>=0),notes VARCHAR(2000),request_id UUID NOT NULL,request_payload JSONB NOT NULL,created_by INT NOT NULL,created_at TIMESTAMPTZ NOT NULL DEFAULT now(),
 UNIQUE(tenant_id,request_id),FOREIGN KEY(tenant_id,location_id,warranty_claim_id) REFERENCES warranty_claims(tenant_id,location_id,warranty_claim_id),
 FOREIGN KEY(tenant_id,created_by) REFERENCES users(tenant_id,user_id),FOREIGN KEY(tenant_id,location_id) REFERENCES store_locations(tenant_id,location_id)
);
CREATE INDEX ON sale_returns(sale_id,return_date);
CREATE INDEX ON sale_return_items(sale_return_id,sale_item_id);
CREATE INDEX ON supplier_payments(purchase_id,payment_date);
CREATE INDEX ON purchase_returns(purchase_id,return_date);
CREATE INDEX ON purchase_return_items(purchase_return_id,purchase_item_id);
CREATE INDEX ON stock_count_items(stock_count_id,variant_id);
CREATE INDEX ON service_payments(service_request_id,payment_date);
CREATE INDEX ON sale_fulfilments(sale_id,scheduled_at);
CREATE INDEX ON trade_ins(sale_id,created_at);
CREATE INDEX ON warranty_rmas(warranty_claim_id,created_at);

DO $$ DECLARE tab text; BEGIN
 FOREACH tab IN ARRAY ARRAY['sale_returns','sale_return_items','supplier_payments','purchase_returns','purchase_return_items','stock_counts','stock_count_items','service_payments','sale_fulfilments','trade_ins','warranty_rmas'] LOOP
  EXECUTE format('CREATE INDEX ON %I(tenant_id,location_id)',tab);
  EXECUTE format('ALTER TABLE %I ENABLE ROW LEVEL SECURITY',tab);EXECUTE format('ALTER TABLE %I FORCE ROW LEVEL SECURITY',tab);
  EXECUTE format('CREATE POLICY tenant_isolation ON %I USING(tenant_id=sf_tenant()) WITH CHECK(tenant_id=sf_tenant())',tab);
  EXECUTE format('CREATE POLICY location_isolation ON %I AS RESTRICTIVE USING(sf_visible_location(location_id)) WITH CHECK(sf_location()>0 AND sf_visible_location(location_id))',tab);
 END LOOP;
END $$;
GRANT SELECT,INSERT,UPDATE ON sale_returns,sale_return_items,supplier_payments,purchase_returns,purchase_return_items,stock_counts,stock_count_items,service_payments,sale_fulfilments,trade_ins,warranty_rmas TO stockflow_runtime;
GRANT USAGE,SELECT ON ALL SEQUENCES IN SCHEMA public TO stockflow_runtime;
COMMIT;
