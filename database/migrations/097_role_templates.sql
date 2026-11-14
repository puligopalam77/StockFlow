-- MANUAL ONLY after 096. Back up, stop writers, run this ENTIRE file as postgres.
-- Reusable tenant template overrides only. No existing roles, users, permissions,
-- shop assignments, invoices, stock or financial records are changed.
-- Built-in recommendations live in DefaultRoleTemplates.cs; editing one creates
-- a tenant override here. New tenants automatically see those same recommendations.
BEGIN;
SET LOCAL lock_timeout='15s';
DO $$ BEGIN
 IF NOT EXISTS(SELECT 1 FROM pg_roles WHERE rolname=current_user AND rolsuper) THEN RAISE EXCEPTION 'Run as PostgreSQL superuser'; END IF;
 IF to_regprocedure('public.sf_shop_setup_status(bigint,text,date)') IS NULL THEN RAISE EXCEPTION 'Apply 096 first'; END IF;
END $$;

CREATE TABLE public.role_templates(
 template_id serial PRIMARY KEY,
 tenant_id bigint NOT NULL DEFAULT public.sf_tenant() REFERENCES public.tenants(tenant_id),
 template_key varchar(80) NOT NULL CHECK(template_key ~ '^[a-z0-9-]{1,80}$'),
 template_name varchar(50) NOT NULL CHECK(length(btrim(template_name)) BETWEEN 2 AND 50 AND lower(btrim(template_name)) NOT IN('admin','owner','main owner')),
 description varchar(200), is_active boolean NOT NULL DEFAULT true,
 version bigint NOT NULL DEFAULT 1,
 updated_at timestamptz NOT NULL DEFAULT now(),
 updated_by integer NOT NULL DEFAULT public.sf_actor(),
 UNIQUE(tenant_id,template_id), UNIQUE(tenant_id,template_key),
 FOREIGN KEY(tenant_id,updated_by) REFERENCES public.users(tenant_id,user_id)
);
CREATE UNIQUE INDEX role_template_name_ci ON public.role_templates(tenant_id,lower(btrim(template_name)));
CREATE TABLE public.role_template_action_permissions(
 tenant_id bigint NOT NULL DEFAULT public.sf_tenant(), template_id integer NOT NULL,
 module_action_id bigint NOT NULL REFERENCES public.module_actions(module_action_id),
 is_allowed boolean NOT NULL DEFAULT true CHECK(is_allowed),
 PRIMARY KEY(tenant_id,template_id,module_action_id),
 FOREIGN KEY(tenant_id,template_id) REFERENCES public.role_templates(tenant_id,template_id)
);
ALTER TABLE public.role_templates ENABLE ROW LEVEL SECURITY;
ALTER TABLE public.role_templates FORCE ROW LEVEL SECURITY;
ALTER TABLE public.role_template_action_permissions ENABLE ROW LEVEL SECURITY;
ALTER TABLE public.role_template_action_permissions FORCE ROW LEVEL SECURITY;
CREATE POLICY template_read ON public.role_templates FOR SELECT TO stockflow_runtime
 USING(tenant_id=public.sf_tenant() AND public.sf_transfer_permission('ROLE_MANAGEMENT','VIEW'));
CREATE POLICY template_insert ON public.role_templates FOR INSERT TO stockflow_runtime
 WITH CHECK(tenant_id=public.sf_tenant() AND public.sf_transfer_permission('ROLE_MANAGEMENT','MANAGE_PERMISSIONS'));
CREATE POLICY template_update ON public.role_templates FOR UPDATE TO stockflow_runtime
 USING(tenant_id=public.sf_tenant() AND public.sf_transfer_permission('ROLE_MANAGEMENT','MANAGE_PERMISSIONS'))
 WITH CHECK(tenant_id=public.sf_tenant() AND public.sf_transfer_permission('ROLE_MANAGEMENT','MANAGE_PERMISSIONS'));
CREATE POLICY template_action_read ON public.role_template_action_permissions FOR SELECT TO stockflow_runtime
 USING(tenant_id=public.sf_tenant() AND public.sf_transfer_permission('ROLE_MANAGEMENT','VIEW'));
CREATE POLICY template_action_write ON public.role_template_action_permissions FOR ALL TO stockflow_runtime
 USING(tenant_id=public.sf_tenant() AND public.sf_transfer_permission('ROLE_MANAGEMENT','MANAGE_PERMISSIONS'))
 WITH CHECK(tenant_id=public.sf_tenant() AND public.sf_transfer_permission('ROLE_MANAGEMENT','MANAGE_PERMISSIONS')
 AND EXISTS(SELECT 1 FROM public.module_actions a JOIN public.modules m USING(module_id)
 WHERE a.module_action_id=role_template_action_permissions.module_action_id AND a.is_active AND m.is_active
 AND public.sf_transfer_permission(m.module_code,a.action_code)));
CREATE FUNCTION public.sf_role_template_revision() RETURNS trigger LANGUAGE plpgsql SET search_path=pg_catalog,public AS $$
BEGIN
 IF TG_OP='UPDATE' THEN
  IF (NEW.template_id,NEW.tenant_id,NEW.template_key) IS DISTINCT FROM (OLD.template_id,OLD.tenant_id,OLD.template_key) THEN RAISE EXCEPTION 'Template identity cannot change' USING ERRCODE='23514'; END IF;
  NEW.version:=OLD.version+1;
 ELSE NEW.version:=1; END IF;
 NEW.updated_by:=public.sf_actor(); NEW.updated_at:=now(); RETURN NEW;
END $$;
CREATE TRIGGER role_template_revision BEFORE INSERT OR UPDATE ON public.role_templates FOR EACH ROW EXECUTE FUNCTION public.sf_role_template_revision();
CREATE FUNCTION public.sf_role_template_view_guard() RETURNS trigger LANGUAGE plpgsql SET search_path=pg_catalog,public AS $$
DECLARE tid bigint; id integer;
BEGIN
 IF TG_OP='DELETE' THEN tid:=OLD.tenant_id;id:=OLD.template_id; ELSE tid:=NEW.tenant_id;id:=NEW.template_id; END IF;
 IF EXISTS(SELECT 1 FROM public.role_template_action_permissions p JOIN public.module_actions a USING(module_action_id)
 WHERE p.tenant_id=tid AND p.template_id=id AND a.action_code<>'VIEW' AND NOT EXISTS(
 SELECT 1 FROM public.role_template_action_permissions v JOIN public.module_actions va USING(module_action_id)
 WHERE v.tenant_id=tid AND v.template_id=id AND va.module_id=a.module_id AND va.action_code='VIEW')) THEN
 RAISE EXCEPTION 'View access is required for template actions' USING ERRCODE='23514'; END IF;
 RETURN NULL;
END $$;
CREATE CONSTRAINT TRIGGER role_template_view_guard AFTER INSERT OR UPDATE OR DELETE ON public.role_template_action_permissions
 DEFERRABLE INITIALLY DEFERRED FOR EACH ROW EXECUTE FUNCTION public.sf_role_template_view_guard();
REVOKE ALL ON public.role_templates,public.role_template_action_permissions FROM PUBLIC,stockflow_runtime;
GRANT SELECT,INSERT,UPDATE ON public.role_templates TO stockflow_runtime;
GRANT SELECT,INSERT,DELETE ON public.role_template_action_permissions TO stockflow_runtime;
GRANT USAGE ON SEQUENCE public.role_templates_template_id_seq TO stockflow_runtime;
REVOKE ALL ON FUNCTION public.sf_role_template_revision(),public.sf_role_template_view_guard() FROM PUBLIC;
COMMIT;
