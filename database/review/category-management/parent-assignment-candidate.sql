-- REVIEW ONLY; not an applied/numbered migration. Do not run independently.
-- Requires separately reviewed ProductService and Product Registration policy changes.
-- Changes only future product-code reservation eligibility; never renames issued codes.
BEGIN;
DO $$ BEGIN
 IF current_setting('stockflow.category_parent_assignment_approved',true) IS DISTINCT FROM 'yes'
 THEN RAISE EXCEPTION 'Explicit target and coordinated release approval required'; END IF;
 IF to_regprocedure('public.sf_reserve_product_code(integer,uuid,text)') IS NULL
 THEN RAISE EXCEPTION 'Migration 067 allocator prerequisite unavailable'; END IF;
END $$;
CREATE OR REPLACE FUNCTION public.sf_reserve_product_code(cat integer,request uuid,desired text DEFAULT NULL) RETURNS text LANGUAGE plpgsql SECURITY DEFINER SET search_path=pg_catalog,public,pg_temp AS $$
DECLARE tid bigint:=public.sf_tenant(); prof text:=public.sf_visible_business_profile(); pref text; number_value bigint; code_value text;
BEGIN
 IF request IS NULL OR request='00000000-0000-0000-0000-000000000000'::uuid OR prof IS NULL THEN RAISE EXCEPTION 'Select the original shop and use a valid registration request' USING ERRCODE='23514'; END IF;
 PERFORM pg_advisory_xact_lock(hashtextextended('catalog-registration:'||tid::text,0));
 SELECT sku_prefix INTO pref FROM public.categories c WHERE c.tenant_id=tid AND c.business_profile_code=prof AND c.category_id=cat AND c.is_active AND NOT c.is_retired_grouping
 AND (c.parent_category_id IS NULL OR EXISTS(SELECT 1 FROM public.categories p WHERE p.tenant_id=tid AND p.category_id=c.parent_category_id AND p.is_active));
 IF pref IS NULL THEN RAISE EXCEPTION 'Choose an active category with a SKU prefix' USING ERRCODE='23514'; END IF;
 IF NULLIF(btrim(desired),'') IS NOT NULL THEN
  code_value:=upper(btrim(desired));
  IF code_value !~ '^[A-Z0-9][A-Z0-9_-]{2,39}$' THEN RAISE EXCEPTION 'Use a product code of 3 to 40 safe characters'; END IF;
  IF EXISTS(SELECT 1 FROM public.catalog_identifier_ledger WHERE tenant_id=tid AND profile=prof AND code=code_value AND (request_id IS DISTINCT FROM request OR owner_id IS DISTINCT FROM NULLIF(current_setting('app.user_id',true),'')::integer OR category_id IS DISTINCT FROM cat OR product_id IS NOT NULL)) THEN RAISE EXCEPTION 'Product code already issued or reserved' USING ERRCODE='23514'; END IF;
  INSERT INTO public.catalog_identifier_ledger(tenant_id,profile,code,request_id,owner_id,category_id) VALUES(tid,prof,code_value,request,NULLIF(current_setting('app.user_id',true),'')::integer,cat) ON CONFLICT DO NOTHING;
  PERFORM public.sf_note_catalog_number(tid,prof,cat,code_value);
  UPDATE public.catalog_prefix_registry SET issued=true,last_number=greatest(last_number,CASE WHEN code_value ~ ('^'||prefix||'-[0-9]{1,18}(-.*)?$') THEN split_part(code_value,'-',2)::bigint ELSE 0 END) WHERE tenant_id=tid AND profile=prof AND prefix=pref;
  UPDATE public.categories SET sku_prefix_used=true WHERE tenant_id=tid AND category_id=cat;
  RETURN code_value;
 END IF;
 SELECT code INTO code_value FROM public.catalog_identifier_ledger WHERE tenant_id=tid AND profile=prof AND request_id=request AND category_id=cat AND owner_id=NULLIF(current_setting('app.user_id',true),'')::integer ORDER BY created_at LIMIT 1;
 IF FOUND THEN RETURN code_value; END IF;
 LOOP
  UPDATE public.catalog_prefix_registry SET last_number=last_number+1,issued=true WHERE tenant_id=tid AND profile=prof AND prefix=pref RETURNING last_number INTO number_value;
  IF NOT FOUND THEN RAISE EXCEPTION 'SKU prefix is not configured'; END IF;
  code_value:=pref||'-'||lpad(number_value::text,greatest(4,length(number_value::text)),'0');
  EXIT WHEN NOT EXISTS(SELECT 1 FROM public.catalog_identifier_ledger WHERE tenant_id=tid AND profile=prof AND (code=code_value OR starts_with(code,code_value||'-')));
 END LOOP;
 INSERT INTO public.catalog_identifier_ledger(tenant_id,profile,code,request_id,owner_id,category_id) VALUES(tid,prof,code_value,request,NULLIF(current_setting('app.user_id',true),'')::integer,cat);
 UPDATE public.categories SET sku_prefix_used=true WHERE tenant_id=tid AND category_id=cat;
 RETURN code_value;
END $$;
COMMIT;
