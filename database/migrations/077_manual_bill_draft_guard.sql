-- MANUAL ONLY, after 076. Back up, stop writers; run the ENTIRE file as postgres.
-- Allow authorized non-catalog lines in held drafts. No invoice, stock or payment changes.
BEGIN;
SET LOCAL lock_timeout='15s';
DO $$ BEGIN
 IF NOT EXISTS(SELECT 1 FROM pg_roles WHERE rolname=current_user AND rolsuper) THEN RAISE EXCEPTION 'Run as PostgreSQL superuser'; END IF;
 IF to_regclass('public.notification_whatsapp_connections') IS NULL OR to_regprocedure('public.sf_pos_manual_no_gst()') IS NULL THEN RAISE EXCEPTION 'Apply migrations through 076 first'; END IF;
END $$;
DO $migration$ DECLARE definition text; target text; BEGIN
 definition:=replace(pg_get_functiondef('public.sf_profile_posting_guard()'::regprocedure),E'\r\n',E'\n');
 target:='   SELECT to_jsonb(p) INTO item FROM public.products p WHERE p.tenant_id=NEW.tenant_id
    AND p.product_id=COALESCE(draft_item->>''productId'',draft_item->>''ProductId'')::integer;';
 target:=replace(target,E'\r\n',E'\n');
 IF position(target IN definition)=0 OR position('Manual draft line identity must not reference catalog stock' IN definition)>0 THEN RAISE EXCEPTION 'Unexpected draft profile guard; migration not applied'; END IF;
 definition:=replace(definition,target,$branch$   IF TG_TABLE_NAME='billing_drafts' AND jsonb_typeof(draft_item->'manualItem')='object' THEN
    IF NOT public.sf_gst_allowed('CREATE','SALES') OR NOT public.sf_gst_allowed('MANUAL_ITEM','SALES') THEN
     RAISE EXCEPTION 'Manual item billing permission required' USING ERRCODE='42501';
    END IF;
    IF coalesce(draft_item->>'productId',draft_item->>'ProductId','0')<>'0' OR coalesce(draft_item->>'variantId',draft_item->>'VariantId',draft_item->>'productUnitId',draft_item->>'ProductUnitId') IS NOT NULL THEN
     RAISE EXCEPTION 'Manual draft line identity must not reference catalog stock' USING ERRCODE='23514';
    END IF;
    CONTINUE;
   END IF;
$branch$ || target);
 EXECUTE definition;
END $migration$;
COMMIT;
