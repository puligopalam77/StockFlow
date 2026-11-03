-- MANUAL ONLY, after 080. Back up; run ENTIRE file as postgres.
-- Adds an explicit ordinary-billing permission. Does not alter catalog tax rates or posted invoices.
BEGIN;
DO $$ BEGIN
 IF NOT EXISTS(SELECT 1 FROM pg_roles WHERE rolname=current_user AND rolsuper) THEN RAISE EXCEPTION 'Run as PostgreSQL superuser'; END IF;
 IF to_regprocedure('public.sf_simple_variant_edit_available()') IS NULL THEN RAISE EXCEPTION 'Apply 080 first'; END IF;
END $$;
INSERT INTO public.module_actions(module_id,action_code,action_name,is_sensitive)
 SELECT module_id,'TAX_OVERRIDE','Override tax on ordinary bills with a recorded reason',true FROM public.modules WHERE module_code='SALES'
 ON CONFLICT DO NOTHING;
INSERT INTO public.user_action_permissions(tenant_id,user_id,module_action_id,is_allowed)
 SELECT u.tenant_id,u.user_id,a.module_action_id,true FROM public.users u CROSS JOIN public.module_actions a JOIN public.modules m USING(module_id)
 WHERE u.is_tenant_owner AND m.module_code='SALES' AND a.action_code='TAX_OVERRIDE' ON CONFLICT DO NOTHING;
INSERT INTO public.role_action_permissions(tenant_id,role_id,module_action_id,is_allowed)
 SELECT DISTINCT u.tenant_id,u.role_id,a.module_action_id,false FROM public.users u CROSS JOIN public.module_actions a JOIN public.modules m USING(module_id)
 WHERE m.module_code='SALES' AND a.action_code='TAX_OVERRIDE' ON CONFLICT DO NOTHING;
COMMIT;
