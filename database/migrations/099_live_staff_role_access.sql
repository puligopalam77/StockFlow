-- MANUAL ONLY, once after 098. Back up, stop writers, run the ENTIRE file as postgres.
-- Existing custom user grants and protected/owner accounts are preserved.
BEGIN;
SET LOCAL lock_timeout='15s';
DO $$ BEGIN
 IF NOT (SELECT rolsuper FROM pg_roles WHERE rolname=current_user) THEN RAISE EXCEPTION 'Run as PostgreSQL superuser'; END IF;
 IF to_regprocedure('public.sf_staff_target(integer)') IS NULL THEN RAISE EXCEPTION 'Apply migrations through 098 first'; END IF;
END $$;

ALTER TABLE public.users ADD COLUMN uses_role_permissions boolean NOT NULL DEFAULT false;
-- Link existing ordinary staff only when their allowed actions already exactly
-- match the saved role. Keep custom grants and ownership unchanged.
UPDATE public.users u SET uses_role_permissions=true,security_version=security_version+1
FROM public.roles r WHERE r.tenant_id=u.tenant_id AND r.role_id=u.role_id
AND NOT u.is_tenant_owner AND lower(btrim(r.role_name)) NOT IN('admin','owner','main owner')
AND NOT EXISTS(
 (SELECT module_action_id FROM public.user_action_permissions WHERE tenant_id=u.tenant_id AND user_id=u.user_id AND is_allowed
 EXCEPT SELECT module_action_id FROM public.role_action_permissions WHERE tenant_id=u.tenant_id AND role_id=u.role_id AND is_allowed)
 UNION ALL
 (SELECT module_action_id FROM public.role_action_permissions WHERE tenant_id=u.tenant_id AND role_id=u.role_id AND is_allowed
 EXCEPT SELECT module_action_id FROM public.user_action_permissions WHERE tenant_id=u.tenant_id AND user_id=u.user_id AND is_allowed));

CREATE VIEW public.effective_user_action_permissions WITH(security_invoker=true) AS
 SELECT u.tenant_id,u.user_id,p.module_action_id,p.is_allowed,p.assigned_by,p.assigned_at
 FROM public.users u JOIN public.role_action_permissions p USING(tenant_id,role_id) WHERE u.uses_role_permissions
 UNION ALL
 SELECT p.tenant_id,p.user_id,p.module_action_id,p.is_allowed,p.assigned_by,p.assigned_at
 FROM public.user_action_permissions p JOIN public.users u USING(tenant_id,user_id) WHERE NOT u.uses_role_permissions;
GRANT SELECT ON public.effective_user_action_permissions TO stockflow_runtime;

-- Existing database permission helpers (billing, GST, CRM, catalog, notifications,
-- staff boundaries) must read the same effective access as the .NET application.
-- Rewrite only read references; leave the original grant tables and write guards.
DO $$ DECLARE f record; definition text; BEGIN
 FOR f IN SELECT p.oid FROM pg_proc p JOIN pg_namespace n ON n.oid=p.pronamespace
 WHERE n.nspname='public' AND p.prokind='f' AND p.prosrc LIKE '%user_action_permissions%'
 AND p.proname<>'sf_action_view_dependency' LOOP
  definition:=pg_get_functiondef(f.oid);
  IF definition ~* '(INSERT\s+INTO|UPDATE|DELETE\s+FROM)\s+(public\.)?user_action_permissions' THEN
   RAISE EXCEPTION 'Review permission-writing function % before migrating',f.oid::regprocedure;
  END IF;
  EXECUTE replace(definition,'user_action_permissions','effective_user_action_permissions');
 END LOOP;
END $$;

CREATE FUNCTION public.sf_linked_staff_guard() RETURNS trigger
LANGUAGE plpgsql SECURITY DEFINER SET search_path=pg_catalog,public AS $$
BEGIN
 IF current_setting('role',true)='stockflow_runtime' AND
 ((TG_OP='INSERT' AND NEW.uses_role_permissions) OR (TG_OP='UPDATE' AND
 (NEW.uses_role_permissions IS DISTINCT FROM OLD.uses_role_permissions OR NEW.uses_role_permissions AND NEW.role_id IS DISTINCT FROM OLD.role_id))) THEN
  IF NOT public.sf_transfer_permission('USER_MANAGEMENT','MANAGE_PERMISSIONS') OR NOT public.sf_staff_manager()
   OR (TG_OP='UPDATE' AND NOT public.sf_staff_target(OLD.user_id)) THEN
   RAISE EXCEPTION 'Role access changes require authorized staff permission management' USING ERRCODE='42501'; END IF;
  IF NEW.is_tenant_owner OR EXISTS(SELECT 1 FROM public.roles WHERE tenant_id=NEW.tenant_id AND role_id=NEW.role_id AND lower(btrim(role_name)) IN('admin','owner','main owner')) THEN
   RAISE EXCEPTION 'Protected accounts retain their existing access policy' USING ERRCODE='42501'; END IF;
  IF NEW.uses_role_permissions AND EXISTS(SELECT 1 FROM public.role_action_permissions p
   JOIN public.module_actions a USING(module_action_id) JOIN public.modules m USING(module_id)
   WHERE p.tenant_id=NEW.tenant_id AND p.role_id=NEW.role_id AND p.is_allowed AND a.is_active AND m.is_active
   AND NOT public.sf_transfer_permission(m.module_code,a.action_code)) THEN
   RAISE EXCEPTION 'Role permissions exceed your grant authority' USING ERRCODE='42501'; END IF;
  IF TG_OP='UPDATE' THEN NEW.security_version:=GREATEST(NEW.security_version,OLD.security_version+1); END IF;
 END IF;
 RETURN NEW;
END $$;
CREATE TRIGGER linked_staff_guard BEFORE INSERT OR UPDATE ON public.users FOR EACH ROW EXECUTE FUNCTION public.sf_linked_staff_guard();

CREATE FUNCTION public.sf_role_access_sessions() RETURNS trigger
LANGUAGE plpgsql SECURITY DEFINER SET search_path=pg_catalog,public AS $$
BEGIN
 IF TG_OP<>'INSERT' THEN UPDATE public.users SET security_version=security_version+1 WHERE tenant_id=OLD.tenant_id AND role_id=OLD.role_id AND uses_role_permissions; END IF;
 IF TG_OP<>'DELETE' AND (TG_OP='INSERT' OR ROW(NEW.tenant_id,NEW.role_id) IS DISTINCT FROM ROW(OLD.tenant_id,OLD.role_id)) THEN
  UPDATE public.users SET security_version=security_version+1 WHERE tenant_id=NEW.tenant_id AND role_id=NEW.role_id AND uses_role_permissions;
 END IF;
 RETURN NULL;
END $$;
CREATE TRIGGER role_access_sessions AFTER INSERT OR UPDATE OR DELETE ON public.role_action_permissions
 FOR EACH ROW EXECUTE FUNCTION public.sf_role_access_sessions();
REVOKE ALL ON FUNCTION public.sf_linked_staff_guard(),public.sf_role_access_sessions() FROM PUBLIC;
COMMIT;
