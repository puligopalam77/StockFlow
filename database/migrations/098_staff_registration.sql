-- MANUAL ONLY, once after 097. Back up and stop writers; run the ENTIRE file as postgres.
-- No users, passwords, ownership, role grants or shop assignments are rewritten.
BEGIN;
SET LOCAL lock_timeout='15s';
DO $$ BEGIN
 IF NOT (SELECT rolsuper FROM pg_roles WHERE rolname=current_user) THEN RAISE EXCEPTION 'Run as PostgreSQL superuser'; END IF;
 IF to_regclass('public.role_templates') IS NULL THEN RAISE EXCEPTION 'Apply migrations through 097 first'; END IF;
END $$;

-- Classification is explicit metadata, never inferred from a display name.
-- Existing roles remain Employee-compatible until deliberately reviewed by the owner.
ALTER TABLE public.roles ADD COLUMN staff_account_type varchar(20) NOT NULL DEFAULT 'EMPLOYEE'
 CONSTRAINT roles_staff_account_type_check CHECK(staff_account_type IN('EMPLOYEE','SHOP_ADMIN'));

CREATE FUNCTION public.sf_staff_manager() RETURNS boolean
LANGUAGE sql STABLE SECURITY DEFINER SET search_path=pg_catalog,public AS $$
 SELECT public.sf_owner() OR EXISTS(SELECT 1 FROM public.users u JOIN public.roles r USING(tenant_id,role_id)
 WHERE u.tenant_id=public.sf_tenant() AND u.user_id=public.sf_actor() AND u.is_active AND r.is_active
 AND (r.staff_account_type='SHOP_ADMIN' OR r.role_name='Admin'))
$$;

CREATE FUNCTION public.sf_staff_shop(id bigint) RETURNS boolean
LANGUAGE sql STABLE SECURITY DEFINER SET search_path=pg_catalog,public AS $$
 SELECT EXISTS(SELECT 1 FROM public.stores s WHERE s.tenant_id=public.sf_tenant() AND s.store_id=id AND s.is_active
 AND (public.sf_owner() OR (public.sf_staff_manager() AND EXISTS(SELECT 1 FROM public.user_store_access a
 WHERE a.tenant_id=s.tenant_id AND a.user_id=public.sf_actor() AND a.store_id=id AND a.is_active AND a.is_default))))
$$;

CREATE FUNCTION public.sf_staff_target(id integer) RETURNS boolean
LANGUAGE sql STABLE SECURITY DEFINER SET search_path=pg_catalog,public AS $$
 SELECT EXISTS(SELECT 1 FROM public.users u JOIN public.roles r USING(tenant_id,role_id)
 WHERE u.tenant_id=public.sf_tenant() AND u.user_id=id AND
 (public.sf_owner() OR u.user_id=public.sf_actor() OR (public.sf_staff_manager()
 AND public.sf_transfer_permission('USER_MANAGEMENT','VIEW') AND NOT u.is_tenant_owner
 AND r.staff_account_type='EMPLOYEE' AND lower(btrim(r.role_name)) NOT IN('admin','owner','main owner')
 AND NOT EXISTS(SELECT 1 FROM public.user_action_permissions p JOIN public.module_actions a USING(module_action_id)
 JOIN public.modules m USING(module_id) WHERE p.tenant_id=u.tenant_id AND p.user_id=u.user_id AND p.is_allowed
 AND a.is_active AND m.is_active AND NOT public.sf_transfer_permission(m.module_code,a.action_code))
 AND (SELECT count(*) FROM public.user_store_access a WHERE a.tenant_id=u.tenant_id AND a.user_id=id AND a.is_active)=1
 AND EXISTS(SELECT 1 FROM public.user_store_access a WHERE a.tenant_id=u.tenant_id AND a.user_id=id AND a.is_active AND public.sf_staff_shop(a.store_id))
 AND NOT EXISTS(SELECT 1 FROM public.user_location_access a WHERE a.tenant_id=u.tenant_id AND a.user_id=id))))
$$;

CREATE FUNCTION public.sf_staff_role_classification_guard() RETURNS trigger
LANGUAGE plpgsql SECURITY DEFINER SET search_path=pg_catalog,public AS $$
BEGIN
 IF current_setting('role',true) IS DISTINCT FROM 'stockflow_runtime' THEN RETURN NEW; END IF;
 IF TG_OP='UPDATE' AND OLD.staff_account_type IS DISTINCT FROM NEW.staff_account_type THEN
  IF NOT public.sf_owner() THEN RAISE EXCEPTION 'Only the owner can classify staff roles' USING ERRCODE='42501'; END IF;
  IF EXISTS(SELECT 1 FROM public.users WHERE tenant_id=OLD.tenant_id AND role_id=OLD.role_id) THEN
   RAISE EXCEPTION 'Create a new classified role instead of changing a role assigned to users' USING ERRCODE='23514'; END IF;
 ELSIF TG_OP='INSERT' AND NEW.staff_account_type='SHOP_ADMIN' AND NOT public.sf_owner() THEN
  RAISE EXCEPTION 'Only the owner can create Shop Admin roles' USING ERRCODE='42501';
 END IF;
 RETURN NEW;
END $$;
CREATE TRIGGER staff_role_classification_guard BEFORE INSERT OR UPDATE ON public.roles
 FOR EACH ROW EXECUTE FUNCTION public.sf_staff_role_classification_guard();

CREATE FUNCTION public.sf_staff_assignment_guard() RETURNS trigger
LANGUAGE plpgsql SECURITY DEFINER SET search_path=pg_catalog,public AS $$
DECLARE target public.users; role_type text;
BEGIN
 IF current_setting('role',true) IS DISTINCT FROM 'stockflow_runtime' THEN
  IF TG_OP='DELETE' THEN RETURN OLD; ELSE RETURN NEW; END IF;
 END IF;
 IF public.sf_owner() THEN IF TG_OP='DELETE' THEN RETURN OLD; ELSE RETURN NEW; END IF; END IF;
 SELECT u.* INTO target FROM public.users u WHERE u.tenant_id=public.sf_tenant() AND u.user_id=CASE WHEN TG_OP='DELETE' THEN OLD.user_id ELSE NEW.user_id END;
 SELECT staff_account_type INTO role_type FROM public.roles WHERE tenant_id=target.tenant_id AND role_id=target.role_id;
 IF NOT public.sf_staff_manager() OR NOT public.sf_transfer_permission('STORES','MANAGE_ACCESS')
 OR target.is_tenant_owner OR role_type<>'EMPLOYEE' OR TG_TABLE_NAME='user_location_access' THEN
  RAISE EXCEPTION 'Shop assignment is outside your delegation' USING ERRCODE='42501'; END IF;
 IF TG_OP='DELETE' THEN
  IF NOT public.sf_staff_shop(OLD.store_id) THEN RAISE EXCEPTION 'Shop is outside your delegation' USING ERRCODE='42501'; END IF;
  RETURN OLD;
 END IF;
 IF NEW.tenant_id<>public.sf_tenant() OR NOT public.sf_staff_shop(NEW.store_id) OR NOT NEW.is_default THEN
  RAISE EXCEPTION 'Delegated staff must use the assigned shop only' USING ERRCODE='42501'; END IF;
 RETURN NEW;
END $$;
CREATE TRIGGER staff_store_assignment_guard BEFORE INSERT OR UPDATE OR DELETE ON public.user_store_access
 FOR EACH ROW EXECUTE FUNCTION public.sf_staff_assignment_guard();
CREATE TRIGGER staff_warehouse_assignment_guard BEFORE INSERT OR UPDATE OR DELETE ON public.user_location_access
 FOR EACH ROW EXECUTE FUNCTION public.sf_staff_assignment_guard();

CREATE FUNCTION public.sf_staff_user_guard() RETURNS trigger
LANGUAGE plpgsql SECURITY DEFINER SET search_path=pg_catalog,public AS $$
DECLARE chosen public.roles;
BEGIN
 IF current_setting('role',true) IS DISTINCT FROM 'stockflow_runtime' THEN RETURN NEW; END IF;
 IF TG_OP='INSERT' OR NEW.role_id IS DISTINCT FROM OLD.role_id THEN
  IF NOT public.sf_staff_manager() OR NOT public.sf_transfer_permission('USER_MANAGEMENT',CASE WHEN TG_OP='INSERT' THEN 'CREATE' ELSE 'EDIT' END) THEN
   RAISE EXCEPTION 'Staff management is not delegated' USING ERRCODE='42501'; END IF;
  SELECT * INTO chosen FROM public.roles WHERE tenant_id=public.sf_tenant() AND role_id=NEW.role_id AND is_active;
  IF NOT FOUND OR lower(btrim(chosen.role_name)) IN('admin','owner','main owner') OR EXISTS(
   SELECT 1 FROM public.users WHERE tenant_id=chosen.tenant_id AND role_id=chosen.role_id AND is_tenant_owner)
   OR (chosen.staff_account_type='SHOP_ADMIN' AND NOT public.sf_owner()) THEN
   RAISE EXCEPTION 'Role is not assignable' USING ERRCODE='42501'; END IF;
 END IF;
 IF TG_OP='INSERT' AND NEW.is_tenant_owner OR TG_OP='UPDATE' AND NEW.is_tenant_owner IS DISTINCT FROM OLD.is_tenant_owner THEN
  RAISE EXCEPTION 'Ordinary staff registration cannot change ownership' USING ERRCODE='42501'; END IF;
 IF TG_OP='UPDATE' AND NEW.user_id<>public.sf_actor() AND
 ROW(NEW.role_id,NEW.is_active,NEW.password_hash,NEW.full_name,NEW.username,NEW.email,NEW.mobile_number,NEW.require_two_factor,NEW.must_change_password)
 IS DISTINCT FROM ROW(OLD.role_id,OLD.is_active,OLD.password_hash,OLD.full_name,OLD.username,OLD.email,OLD.mobile_number,OLD.require_two_factor,OLD.must_change_password)
 AND NOT public.sf_staff_target(OLD.user_id) THEN
  RAISE EXCEPTION 'Staff account is outside your assigned shop' USING ERRCODE='42501'; END IF;
 RETURN NEW;
END $$;
CREATE TRIGGER staff_user_guard BEFORE INSERT OR UPDATE ON public.users
 FOR EACH ROW EXECUTE FUNCTION public.sf_staff_user_guard();

REVOKE ALL ON FUNCTION public.sf_staff_manager(),public.sf_staff_shop(bigint),public.sf_staff_target(integer),
 public.sf_staff_role_classification_guard(),public.sf_staff_assignment_guard(),public.sf_staff_user_guard() FROM PUBLIC;
GRANT EXECUTE ON FUNCTION public.sf_staff_manager(),public.sf_staff_shop(bigint),public.sf_staff_target(integer) TO stockflow_runtime;
COMMIT;
