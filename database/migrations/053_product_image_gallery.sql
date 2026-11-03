-- MANUAL ONLY. Back up and stop application/workers. Run after 052 as PostgreSQL superuser.
-- Existing product images, stock and transaction history are retained unchanged.
BEGIN;
DO $$ BEGIN
 IF NOT (SELECT rolsuper FROM pg_roles WHERE rolname=current_user) THEN RAISE EXCEPTION 'Apply 053 as PostgreSQL superuser'; END IF;
 IF NOT EXISTS(SELECT 1 FROM information_schema.columns WHERE table_schema='public' AND table_name='tenants' AND column_name='catalog_registration_enhancements_enabled') THEN RAISE EXCEPTION 'Apply 052 first'; END IF;
END $$;
CREATE OR REPLACE FUNCTION public.sf_product_gallery_valid(images jsonb) RETURNS boolean
LANGUAGE plpgsql IMMUTABLE SET search_path=pg_catalog,public,pg_temp AS $$
DECLARE image jsonb; identifiers text[]:=ARRAY[]::text[];
BEGIN
 IF jsonb_typeof(images) IS DISTINCT FROM 'array' OR jsonb_array_length(images)>5 THEN RETURN false; END IF;
 FOR image IN SELECT * FROM jsonb_array_elements(images) LOOP
  IF jsonb_typeof(image) IS DISTINCT FROM 'object'
   OR COALESCE(image->>'imageId','')!~'^[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12}$'
   OR image->>'imageId'=ANY(identifiers)
   OR length(COALESCE(image->>'fileName','')) NOT BETWEEN 1 AND 255
   OR COALESCE(image->>'contentType','') NOT IN('image/jpeg','image/png','image/webp')
   OR octet_length(decode(COALESCE(image->>'data',''),'base64')) NOT BETWEEN 1 AND 3000000 THEN RETURN false; END IF;
  identifiers:=array_append(identifiers,image->>'imageId');
 END LOOP;
 RETURN true;
EXCEPTION WHEN OTHERS THEN RETURN false;
END $$;
REVOKE ALL ON FUNCTION public.sf_product_gallery_valid(jsonb) FROM PUBLIC;
GRANT EXECUTE ON FUNCTION public.sf_product_gallery_valid(jsonb) TO stockflow_runtime;
ALTER TABLE public.products ADD COLUMN IF NOT EXISTS product_images jsonb NOT NULL DEFAULT '[]'::jsonb;
ALTER TABLE public.products DROP CONSTRAINT IF EXISTS ck_product_image_gallery;
ALTER TABLE public.products ADD CONSTRAINT ck_product_image_gallery CHECK(public.sf_product_gallery_valid(product_images));
-- The existing products RLS policies also protect this column and image retrieval.
-- Gallery order defines the main image. Existing single images are upgraded only
-- when their owner explicitly edits the gallery; no image data is rewritten here.
ALTER TABLE public.tenants ADD COLUMN IF NOT EXISTS product_image_gallery_enabled boolean NOT NULL DEFAULT true;
COMMENT ON COLUMN public.products.product_images IS 'Ordered gallery, at most five validated image uploads; first is the main image. Existing single-image columns remain the main-image compatibility endpoint.';
COMMIT;
       