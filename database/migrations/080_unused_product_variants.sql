-- MANUAL ONLY, after 079. Back up, stop all app instances/writers; run ENTIRE file as postgres.
-- Allows unused simple products to enable variants. No identifiers, stock or documents are rewritten.
-- The existing default variant keeps its Product Code/SKU; additional variants use reserved suffixes.
BEGIN;
SET LOCAL lock_timeout='15s';
DO $$ BEGIN
 IF NOT EXISTS(SELECT 1 FROM pg_roles WHERE rolname=current_user AND rolsuper) THEN RAISE EXCEPTION 'Run as PostgreSQL superuser'; END IF;
 IF to_regprocedure('public.sf_product_edit_history(integer,bigint)') IS NULL
  OR to_regprocedure('public.sf_protect_product_edit()') IS NULL THEN RAISE EXCEPTION 'Apply migration 069 and subsequent migrations first'; END IF;
END $$;
LOCK TABLE public.products,public.product_variants IN SHARE ROW EXCLUSIVE MODE;
DO $migration$
DECLARE definition text; old_guard text; new_guard text;
BEGIN
 definition:=pg_get_functiondef('public.sf_catalog_identifier_guard()'::regprocedure);
 old_guard:='IF NEW.has_variants AND NOT OLD.has_variants AND EXISTS(SELECT 1 FROM public.product_variants v WHERE v.tenant_id=NEW.tenant_id AND v.product_id=NEW.product_id AND upper(v.sku)=upper(NEW.parent_product_code)) THEN RAISE EXCEPTION ''This simple product already uses its product code as its SKU. Register a separate variant product to preserve its stock identity''; END IF;';
 new_guard:='IF NEW.has_variants AND NOT OLD.has_variants AND public.sf_product_edit_history(OLD.product_id,NULL::bigint) THEN RAISE EXCEPTION ''Variant structure cannot change after stock or transaction history; use an authorized inventory correction'' USING ERRCODE=''23514''; END IF;';
 IF position(old_guard IN definition)>0 THEN
  EXECUTE replace(definition,old_guard,new_guard);
 ELSIF position(new_guard IN definition)=0 THEN
  RAISE EXCEPTION 'Unexpected catalog identifier guard; no changes applied';
 END IF;
END $migration$;
-- Capability marker for application preflight; no access to records or allocation state.
CREATE OR REPLACE FUNCTION public.sf_simple_variant_edit_available() RETURNS boolean
 LANGUAGE sql IMMUTABLE SET search_path=pg_catalog AS $$ SELECT true $$;
REVOKE ALL ON FUNCTION public.sf_simple_variant_edit_available() FROM PUBLIC;
COMMIT;
