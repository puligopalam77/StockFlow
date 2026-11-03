-- MANUAL: stop all instances, back up, apply once after 033 as PostgreSQL superuser, restart.
BEGIN;
DO $$ BEGIN
 IF NOT EXISTS(SELECT 1 FROM pg_roles WHERE rolname=current_user AND rolsuper) THEN RAISE EXCEPTION 'Apply as PostgreSQL superuser'; END IF;
 IF to_regclass('public.auth_sessions') IS NULL THEN RAISE EXCEPTION 'Apply through 033 first'; END IF;
END $$;
CREATE TABLE billing_policy(tenant_id bigint PRIMARY KEY DEFAULT sf_tenant(),discount_limit_percent numeric(5,2) NOT NULL DEFAULT 10 CHECK(discount_limit_percent BETWEEN 0 AND 100));
CREATE TABLE customer_credit_limits(tenant_id bigint NOT NULL DEFAULT sf_tenant(),customer_id integer NOT NULL,credit_limit numeric(12,2) CHECK(credit_limit>=0),PRIMARY KEY(tenant_id,customer_id),FOREIGN KEY(tenant_id,customer_id) REFERENCES customers(tenant_id,customer_id));
CREATE TABLE billing_drafts(tenant_id bigint NOT NULL DEFAULT sf_tenant(),user_id integer NOT NULL DEFAULT NULLIF(current_setting('app.user_id',true),'')::integer,location_id bigint NOT NULL DEFAULT NULLIF(sf_location(),0),draft_id uuid NOT NULL,label varchar(150) NOT NULL,payload jsonb NOT NULL,updated_at timestamptz NOT NULL DEFAULT now(),PRIMARY KEY(tenant_id,user_id,location_id,draft_id),FOREIGN KEY(tenant_id,user_id) REFERENCES users(tenant_id,user_id),FOREIGN KEY(tenant_id,location_id) REFERENCES store_locations(tenant_id,location_id));
CREATE TABLE billing_approvals(tenant_id bigint NOT NULL DEFAULT sf_tenant(),location_id bigint NOT NULL DEFAULT NULLIF(sf_location(),0),draft_id uuid NOT NULL,requested_by integer NOT NULL,financial_hash varchar(64) NOT NULL,summary jsonb NOT NULL,approved_by integer,requested_at timestamptz NOT NULL DEFAULT now(),approved_at timestamptz,PRIMARY KEY(tenant_id,location_id,draft_id),FOREIGN KEY(tenant_id,requested_by) REFERENCES users(tenant_id,user_id),FOREIGN KEY(tenant_id,approved_by) REFERENCES users(tenant_id,user_id));
ALTER TABLE billing_approvals ENABLE ROW LEVEL SECURITY;
ALTER TABLE billing_approvals FORCE ROW LEVEL SECURITY;
CREATE POLICY approval_location ON billing_approvals USING(tenant_id=sf_tenant() AND location_id=sf_location()) WITH CHECK(tenant_id=sf_tenant() AND location_id=sf_location());
GRANT SELECT,INSERT,UPDATE,DELETE ON billing_approvals TO stockflow_runtime;
DO $$ DECLARE tab text; BEGIN
 FOREACH tab IN ARRAY ARRAY['billing_policy','customer_credit_limits','billing_drafts'] LOOP
 EXECUTE format('ALTER TABLE %I ENABLE ROW LEVEL SECURITY',tab);
 EXECUTE format('ALTER TABLE %I FORCE ROW LEVEL SECURITY',tab);
 EXECUTE format('CREATE POLICY tenant_isolation ON %I USING(tenant_id=sf_tenant()) WITH CHECK(tenant_id=sf_tenant())',tab);
 END LOOP;
END $$;
CREATE POLICY draft_owner ON billing_drafts AS RESTRICTIVE USING(user_id=COALESCE(NULLIF(current_setting('app.user_id',true),''),'0')::integer AND location_id=sf_location()) WITH CHECK(user_id=COALESCE(NULLIF(current_setting('app.user_id',true),''),'0')::integer AND location_id=sf_location());
GRANT SELECT,INSERT,UPDATE ON billing_policy,customer_credit_limits TO stockflow_runtime;
GRANT SELECT,INSERT,UPDATE,DELETE ON billing_drafts TO stockflow_runtime;
-- Balance spans all locations of the authenticated tenant; no other tenant can be queried.
CREATE FUNCTION sf_customer_credit_balance(p_customer integer) RETURNS numeric LANGUAGE sql SECURITY DEFINER SET search_path=public,pg_temp AS $$
 SELECT coalesce(sum(greatest(s.total_amount
  -coalesce((SELECT sum(return_total) FROM sale_returns r WHERE r.tenant_id=s.tenant_id AND r.sale_id=s.sale_id),0)
  -coalesce((SELECT sum(amount) FROM payments p WHERE p.tenant_id=s.tenant_id AND p.sale_id=s.sale_id AND payment_mode<>'CREDIT'),0)
  +coalesce((SELECT sum(refund_amount) FROM sale_returns r WHERE r.tenant_id=s.tenant_id AND r.sale_id=s.sale_id),0),0)),0)
 FROM sales s WHERE s.tenant_id=sf_tenant() AND s.customer_id=p_customer AND s.status='COMPLETED'
$$;
REVOKE ALL ON FUNCTION sf_customer_credit_balance(integer) FROM PUBLIC;
GRANT EXECUTE ON FUNCTION sf_customer_credit_balance(integer) TO stockflow_runtime;
INSERT INTO module_actions(module_id,action_code,action_name,is_sensitive)
 SELECT m.module_id,x.code,x.name,true FROM modules m CROSS JOIN (VALUES
 ('PRICE_OVERRIDE','Change selling price'),('DISCOUNT','Apply discounts'),('DISCOUNT_APPROVE','Approve discounts above limit')) x(code,name)
 WHERE m.module_code='SALES';
-- New sensitive operations are granted to tenant owners only; assign staff explicitly.
INSERT INTO user_action_permissions(tenant_id,user_id,module_action_id,is_allowed)
 SELECT u.tenant_id,u.user_id,a.module_action_id,true FROM users u JOIN modules m ON m.module_code='SALES'
 JOIN module_actions a ON a.module_id=m.module_id AND a.action_code IN('PRICE_OVERRIDE','DISCOUNT','DISCOUNT_APPROVE') WHERE u.is_tenant_owner;
COMMIT;
