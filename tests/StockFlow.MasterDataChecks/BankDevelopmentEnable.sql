-- Explicit LOCAL SYNTHETIC development activation only. Never a migration.
CREATE TABLE user_location_access(tenant_id bigint,user_id integer,location_id bigint,is_active boolean NOT NULL DEFAULT true);
CREATE OR REPLACE FUNCTION sf_owner() RETURNS boolean LANGUAGE sql STABLE SECURITY DEFINER SET search_path=pg_catalog,public AS $$
 SELECT EXISTS(SELECT 1 FROM users u JOIN tenants t USING(tenant_id) WHERE u.tenant_id=sf_tenant() AND u.user_id=sf_actor() AND u.is_active AND u.is_tenant_owner AND t.is_active)
$$;
CREATE OR REPLACE FUNCTION sf_shop_member(shop bigint) RETURNS boolean LANGUAGE sql STABLE SECURITY DEFINER SET search_path=pg_catalog,public AS $$
 SELECT EXISTS(SELECT 1 FROM stores s JOIN users u ON u.tenant_id=s.tenant_id WHERE s.tenant_id=sf_tenant() AND s.store_id=shop AND s.is_active AND u.user_id=sf_actor() AND u.is_active
 AND (u.is_tenant_owner OR EXISTS(SELECT 1 FROM user_store_access a WHERE a.tenant_id=u.tenant_id AND a.user_id=u.user_id AND a.store_id=shop AND a.is_active)
 OR EXISTS(SELECT 1 FROM user_location_access a JOIN store_locations l USING(tenant_id,location_id) WHERE a.tenant_id=u.tenant_id AND a.user_id=u.user_id AND a.is_active AND l.store_id=shop AND l.is_active)))
$$;
CREATE OR REPLACE FUNCTION sf_can_location(loc bigint) RETURNS boolean LANGUAGE sql STABLE SECURITY DEFINER SET search_path=pg_catalog,public AS $$
 SELECT EXISTS(SELECT 1 FROM store_locations l JOIN users u USING(tenant_id) WHERE l.tenant_id=sf_tenant() AND l.location_id=loc AND l.is_active AND u.user_id=sf_actor() AND u.is_active
 AND (u.is_tenant_owner OR EXISTS(SELECT 1 FROM user_store_access a WHERE a.tenant_id=u.tenant_id AND a.user_id=u.user_id AND a.store_id=l.store_id AND a.is_active)
 OR EXISTS(SELECT 1 FROM user_location_access a WHERE a.tenant_id=u.tenant_id AND a.user_id=u.user_id AND a.location_id=loc AND a.is_active)))
$$;
UPDATE bank_release_control SET configuration_enabled=true,posting_enabled=true;
UPDATE modules SET is_active=true WHERE module_code='BANK_ACCOUNTS';
INSERT INTO user_action_permissions SELECT 1,u.user_id,a.module_action_id,true FROM users u CROSS JOIN module_actions a JOIN modules m ON m.module_id=a.module_id
WHERE u.tenant_id=1 AND u.user_id IN(1,2) AND m.module_code IN('BANK_ACCOUNTS','SALES','PURCHASES','EXPENSES','SERVICE');
