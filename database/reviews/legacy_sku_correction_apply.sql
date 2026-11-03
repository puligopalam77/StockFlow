-- MANUAL, ONE-TIME correction for the supplied SKU.CSV (tenant 1, products 1..5).
-- Back up StockFlow_DB, stop ALL app/worker writers, and deploy the accompanying
-- ScanRepository/ProductRepository changes BEFORE reopening the app.
-- Run the ENTIRE file as postgres against StockFlow_DB, after migration 069.
-- This file COMMITS. On ANY error, run ROLLBACK; do not execute later statements alone.
-- Old SKUs remain reserved and searchable/scannable as aliases. Existing physical
-- barcodes, serials, IDs, prices, stock and historical documents are not rewritten.
-- Numbers are allocated above LIVE high-water marks, including abandoned reservations.
BEGIN;
SET LOCAL lock_timeout='15s';
SET LOCAL session_replication_role=origin;
DO $$ BEGIN
 IF current_database()<>'StockFlow_DB' THEN RAISE EXCEPTION 'Connect to StockFlow_DB before running this correction'; END IF;
 IF NOT EXISTS(SELECT 1 FROM pg_roles WHERE rolname=current_user AND rolsuper) THEN RAISE EXCEPTION 'Run as PostgreSQL superuser'; END IF;
 IF to_regprocedure('public.sf_protect_product_edit()') IS NULL THEN RAISE EXCEPTION 'Apply migration 069 first'; END IF;
 IF to_regclass('public.catalog_sku_corrections') IS NOT NULL THEN RAISE EXCEPTION 'Correction table already exists; do not apply this file twice'; END IF;
END $$;
SELECT pg_advisory_xact_lock(hashtextextended('catalog-registration:1',0));
LOCK TABLE public.products,public.product_variants,public.categories,
 public.catalog_identifier_ledger,public.catalog_prefix_registry IN ACCESS EXCLUSIVE MODE;

CREATE TEMP TABLE sku_correction_source(
 tenant_id bigint,profile text,product_id integer,variant_id bigint,old_sku text,
 prefix text,product_name text,category_name text,variant_name text,has_variants boolean
) ON COMMIT DROP;
INSERT INTO sku_correction_source VALUES
 (1,'ELECTRICAL',1,1,'LEGACY-1','CTC','TEST Cable','Create Test categor','DEFAULT',false),
 (1,'ELECTRICAL',2,2,'LEGACY-2','CTC','TEST Device','Create Test categor','DEFAULT',false),
 (1,'ELECTRICAL',3,3,'ITEM-9E75E95966AD4955','BAT','Luminous 150ah battery','Battery','DEFAULT',false),
 (1,'BIKE_ACCESSORIES',4,4,'SF-273B51B805D91943','HEL','Helmet','Helmets','DEFAULT',false),
 (1,'BIKE_ACCESSORIES',5,5,'SF-87CE9957065EE43C-01','HLM','Helmet','Full Face Helmets','XS / Black',true),
 (1,'BIKE_ACCESSORIES',5,6,'SF-87CE9957065EE43C-02','HLM','Helmet','Full Face Helmets','S / Black',true),
 (1,'BIKE_ACCESSORIES',5,7,'SF-87CE9957065EE43C-03','HLM','Helmet','Full Face Helmets','M / Black',true),
 (1,'BIKE_ACCESSORIES',5,8,'SF-87CE9957065EE43C-04','HLM','Helmet','Full Face Helmets','L / Black',true);
DO $$ BEGIN
 IF (SELECT count(*) FROM sku_correction_source s JOIN public.products p USING(tenant_id,product_id)
   JOIN public.categories c USING(tenant_id,category_id)
   JOIN public.product_variants v ON v.tenant_id=s.tenant_id AND v.product_id=s.product_id AND v.variant_id=s.variant_id
   WHERE v.sku=s.old_sku AND v.variant_name=s.variant_name AND p.product_name=s.product_name
   AND p.parent_product_code IS NULL AND p.has_variants=s.has_variants
   AND c.business_profile_code=s.profile AND p.identifier_profile=s.profile AND v.identifier_profile=s.profile
   AND c.sku_prefix=s.prefix AND c.category_name=s.category_name)<>8
 THEN RAISE EXCEPTION 'Data differs from supplied SKU.CSV; correction cancelled'; END IF;
 IF EXISTS(SELECT 1 FROM public.product_variants v WHERE (v.tenant_id,v.product_id) IN(SELECT tenant_id,product_id FROM sku_correction_source)
   AND NOT EXISTS(SELECT 1 FROM sku_correction_source s WHERE s.tenant_id=v.tenant_id AND s.variant_id=v.variant_id))
 THEN RAISE EXCEPTION 'Additional variants now exist; obtain a new audit'; END IF;
 IF EXISTS(SELECT 1 FROM public.products p WHERE (p.tenant_id,p.product_id) IN(SELECT tenant_id,product_id FROM sku_correction_source)
   AND NULLIF(btrim(p.sku),'') IS NOT NULL AND NOT EXISTS(SELECT 1 FROM sku_correction_source s WHERE s.tenant_id=p.tenant_id AND s.product_id=p.product_id AND s.old_sku=p.sku))
 THEN RAISE EXCEPTION 'Unexpected legacy product SKU alias; correction cancelled'; END IF;
 IF EXISTS(SELECT 1 FROM sku_correction_source s JOIN public.catalog_identifier_ledger l
   ON l.tenant_id=s.tenant_id AND l.profile=s.profile AND l.code=upper(btrim(s.old_sku))
   WHERE l.product_id IS DISTINCT FROM s.product_id OR l.variant_id IS DISTINCT FROM s.variant_id)
 THEN RAISE EXCEPTION 'Old identifier ledger ownership differs; correction cancelled'; END IF;
END $$;

-- Capture every non-catalog table that this correction must leave byte-for-byte unchanged.
CREATE TEMP TABLE sku_correction_protected(table_name text PRIMARY KEY,fingerprint text) ON COMMIT DROP;
DO $$ DECLARE tab text; digest text; BEGIN
 FOREACH tab IN ARRAY ARRAY['inventory_transactions','purchases','purchase_items','purchase_receipts','purchase_receipt_items',
  'sales','sale_items','product_units','stock_counts','stock_count_items','stock_transfers','stock_transfer_items',
  'stock_item_labels','purchase_item_labels','barcode_print_requests','variant_barcodes','variant_supplier_identifiers',
  'catalog_variant_reservations'] LOOP
  EXECUTE format('LOCK TABLE public.%I IN SHARE MODE',tab);
  EXECUTE format('SELECT md5(COALESCE(string_agg(j::text,%L ORDER BY j::text),%L)) FROM (SELECT to_jsonb(t) j FROM public.%I t) q','', '',tab) INTO digest;
  INSERT INTO sku_correction_protected VALUES(tab,digest);
 END LOOP;
END $$;
CREATE TEMP TABLE sku_correction_before_products ON COMMIT DROP AS SELECT tenant_id,product_id,to_jsonb(p) value FROM public.products p;
CREATE TEMP TABLE sku_correction_before_variants ON COMMIT DROP AS SELECT tenant_id,variant_id,to_jsonb(v) value FROM public.product_variants v;
CREATE TEMP TABLE sku_correction_before_ledger ON COMMIT DROP AS SELECT tenant_id,profile,code,to_jsonb(l) value FROM public.catalog_identifier_ledger l;

-- Durable aliases are separate from manufacturer barcodes and manufacturer serials.
CREATE TABLE public.catalog_sku_corrections(
 tenant_id bigint NOT NULL,profile text NOT NULL,product_id integer NOT NULL,variant_id bigint NOT NULL,
 old_sku text NOT NULL,new_sku text NOT NULL,old_product_code text,new_product_code text NOT NULL,
 correction_reason text NOT NULL DEFAULT 'Reviewed legacy SKU format correction from supplied SKU.CSV',
 corrected_at timestamptz NOT NULL DEFAULT now(),corrected_by text NOT NULL DEFAULT current_user,
 PRIMARY KEY(tenant_id,profile,old_sku),
 FOREIGN KEY(tenant_id,product_id) REFERENCES public.products(tenant_id,product_id),
 FOREIGN KEY(tenant_id,variant_id) REFERENCES public.product_variants(tenant_id,variant_id),
 FOREIGN KEY(product_id,variant_id) REFERENCES public.product_variants(product_id,variant_id)
);
CREATE UNIQUE INDEX ux_catalog_old_sku ON public.catalog_sku_corrections(tenant_id,profile,lower(btrim(old_sku)));
ALTER TABLE public.catalog_sku_corrections ENABLE ROW LEVEL SECURITY;
ALTER TABLE public.catalog_sku_corrections FORCE ROW LEVEL SECURITY;
CREATE POLICY corrected_sku_shop ON public.catalog_sku_corrections FOR SELECT
 USING(tenant_id=public.sf_tenant() AND profile=public.sf_visible_business_profile()
 AND EXISTS(SELECT 1 FROM public.products p JOIN public.categories c USING(tenant_id,category_id)
 WHERE p.tenant_id=catalog_sku_corrections.tenant_id AND p.product_id=catalog_sku_corrections.product_id
 AND c.business_profile_code=catalog_sku_corrections.profile));
REVOKE ALL ON public.catalog_sku_corrections FROM PUBLIC,stockflow_runtime;
GRANT SELECT ON public.catalog_sku_corrections TO stockflow_runtime;

-- Preserve category metadata and exact guard states before the maintenance bypass.
CREATE TEMP TABLE sku_correction_before_categories ON COMMIT DROP AS SELECT tenant_id,category_id,to_jsonb(c) value FROM public.categories c;
CREATE TEMP TABLE sku_correction_triggers ON COMMIT DROP AS
 SELECT tgrelid::regclass relation,tgname,tgenabled FROM pg_trigger
 WHERE tgrelid IN('public.products'::regclass,'public.product_variants'::regclass,'public.categories'::regclass) AND NOT tgisinternal;
ALTER TABLE public.products DISABLE TRIGGER USER;
ALTER TABLE public.product_variants DISABLE TRIGGER USER;
ALTER TABLE public.categories DISABLE TRIGGER USER;
-- Allocate directly under the maintenance lock; no profile-switching of either shop.
DO $$ DECLARE f record; s record; high bigint; n bigint; family text; newsku text; suffix integer; BEGIN
 FOR f IN SELECT DISTINCT src.tenant_id,src.profile,src.product_id,src.prefix,src.has_variants,p.category_id
   FROM sku_correction_source src JOIN public.products p USING(tenant_id,product_id) ORDER BY src.profile,src.prefix,src.product_id LOOP
  SELECT last_number INTO high FROM public.catalog_prefix_registry WHERE tenant_id=f.tenant_id AND profile=f.profile AND prefix=f.prefix;
  IF high IS NULL THEN RAISE EXCEPTION 'Missing prefix registry for %',f.prefix; END IF;
  SELECT greatest(high,COALESCE(max(split_part(code,'-',2)::bigint),0)) INTO high FROM (
   SELECT upper(btrim(l.code)) code FROM public.catalog_identifier_ledger l WHERE l.tenant_id=f.tenant_id AND l.profile=f.profile
   UNION ALL SELECT upper(btrim(p.parent_product_code)) FROM public.products p WHERE p.tenant_id=f.tenant_id AND p.identifier_profile=f.profile
   UNION ALL SELECT upper(btrim(p.sku)) FROM public.products p WHERE p.tenant_id=f.tenant_id AND p.identifier_profile=f.profile
   UNION ALL SELECT upper(btrim(v.sku)) FROM public.product_variants v WHERE v.tenant_id=f.tenant_id AND v.identifier_profile=f.profile
  ) ids WHERE code ~ ('^'||f.prefix||'-[0-9]{1,18}(-.*)?$');
  n:=high;
  LOOP
   n:=n+1;family:=f.prefix||'-'||lpad(n::text,greatest(4,length(n::text)),'0');
   EXIT WHEN NOT EXISTS(SELECT 1 FROM public.catalog_identifier_ledger l WHERE l.tenant_id=f.tenant_id AND l.profile=f.profile AND (l.code=family OR starts_with(l.code,family||'-')));
  END LOOP;
  UPDATE public.catalog_prefix_registry SET last_number=n,issued=true WHERE tenant_id=f.tenant_id AND profile=f.profile AND prefix=f.prefix;
  UPDATE public.categories SET sku_prefix_used=true WHERE tenant_id=f.tenant_id AND category_id=f.category_id;
  INSERT INTO public.catalog_identifier_ledger(tenant_id,profile,code,product_id,category_id,variant_counter)
   VALUES(f.tenant_id,f.profile,family,f.product_id,f.category_id,CASE WHEN f.has_variants THEN (SELECT count(*) FROM sku_correction_source WHERE tenant_id=f.tenant_id AND product_id=f.product_id) ELSE 0 END);
  suffix:=0;
  FOR s IN SELECT * FROM sku_correction_source WHERE tenant_id=f.tenant_id AND product_id=f.product_id ORDER BY variant_id LOOP
   suffix:=suffix+1;newsku:=CASE WHEN f.has_variants THEN family||'-'||lpad(suffix::text,2,'0') ELSE family END;
   -- Old reservations remain permanent tombstones. Never relabel or delete ledger rows.
   INSERT INTO public.catalog_identifier_ledger(tenant_id,profile,code,product_id,variant_id)
    VALUES(s.tenant_id,s.profile,upper(btrim(s.old_sku)),s.product_id,s.variant_id) ON CONFLICT DO NOTHING;
   IF f.has_variants THEN
    INSERT INTO public.catalog_identifier_ledger(tenant_id,profile,code,product_id,variant_id) VALUES(s.tenant_id,s.profile,newsku,s.product_id,s.variant_id);
   ELSE
    UPDATE public.catalog_identifier_ledger SET variant_id=s.variant_id WHERE tenant_id=s.tenant_id AND profile=s.profile AND code=family;
   END IF;
   INSERT INTO public.catalog_sku_corrections(tenant_id,profile,product_id,variant_id,old_sku,new_sku,old_product_code,new_product_code)
    VALUES(s.tenant_id,s.profile,s.product_id,s.variant_id,s.old_sku,newsku,NULL,family);
  END LOOP;
 END LOOP;
END $$;
-- New SKU strings must not conflict with any barcode or unit identifier.
DO $$ BEGIN
 IF EXISTS(SELECT 1 FROM public.catalog_sku_corrections a WHERE
   EXISTS(SELECT 1 FROM public.variant_barcodes b WHERE upper(btrim(b.barcode))=a.new_sku)
   OR EXISTS(SELECT 1 FROM public.product_units u WHERE a.new_sku=ANY(ARRAY[upper(btrim(u.serial_number)),upper(btrim(u.barcode)),upper(btrim(u.internal_barcode)),upper(btrim(u.manufacturer_barcode))])))
 THEN RAISE EXCEPTION 'Proposed SKU conflicts with a barcode/serial; correction cancelled'; END IF;
END $$;

-- Maintenance-only bypass, under exclusive locks. Restore the EXACT trigger states
-- in this transaction. Foreign keys/unique/check constraints remain active throughout.
UPDATE public.product_variants v SET sku=a.new_sku
 FROM public.catalog_sku_corrections a WHERE v.tenant_id=a.tenant_id AND v.variant_id=a.variant_id AND v.sku=a.old_sku;
UPDATE public.products p SET parent_product_code=q.new_product_code,
 sku=CASE WHEN NOT p.has_variants THEN q.new_product_code ELSE COALESCE((SELECT a.new_sku FROM public.catalog_sku_corrections a WHERE a.tenant_id=p.tenant_id AND a.product_id=p.product_id AND a.old_sku=p.sku),p.sku) END,
 updated_at=now()
 FROM (SELECT DISTINCT tenant_id,product_id,new_product_code FROM public.catalog_sku_corrections) q
 WHERE p.tenant_id=q.tenant_id AND p.product_id=q.product_id;
DO $$ DECLARE t record; BEGIN
 FOR t IN SELECT * FROM sku_correction_triggers LOOP
  IF t.tgenabled<>'D' THEN EXECUTE format('ALTER TABLE %s ENABLE %s TRIGGER %I',t.relation,CASE t.tgenabled WHEN 'A' THEN 'ALWAYS' WHEN 'R' THEN 'REPLICA' ELSE '' END,t.tgname); END IF;
 END LOOP;
END $$;

-- Verify the complete correction, including all unrelated products (such as HLM-0001).
DO $$ DECLARE t record; digest text; BEGIN
 IF (SELECT count(*) FROM public.catalog_sku_corrections)<>8 OR
 EXISTS(SELECT 1 FROM public.catalog_sku_corrections a JOIN public.product_variants v USING(tenant_id,variant_id)
 JOIN public.products p ON p.tenant_id=a.tenant_id AND p.product_id=a.product_id
 WHERE v.sku<>a.new_sku OR p.parent_product_code IS DISTINCT FROM a.new_product_code)
 THEN RAISE EXCEPTION 'Correction verification failed'; END IF;
 IF EXISTS(SELECT 1 FROM public.products p JOIN sku_correction_before_products b USING(tenant_id,product_id)
 WHERE CASE WHEN EXISTS(SELECT 1 FROM public.catalog_sku_corrections a WHERE a.tenant_id=p.tenant_id AND a.product_id=p.product_id)
 THEN to_jsonb(p)-ARRAY['sku','parent_product_code','updated_at'] IS DISTINCT FROM b.value-ARRAY['sku','parent_product_code','updated_at'] ELSE to_jsonb(p) IS DISTINCT FROM b.value END)
 OR EXISTS(SELECT 1 FROM public.product_variants v JOIN sku_correction_before_variants b USING(tenant_id,variant_id)
 WHERE CASE WHEN EXISTS(SELECT 1 FROM public.catalog_sku_corrections a WHERE a.tenant_id=v.tenant_id AND a.variant_id=v.variant_id)
 THEN to_jsonb(v)-ARRAY['sku','updated_at'] IS DISTINCT FROM b.value-ARRAY['sku','updated_at'] ELSE to_jsonb(v) IS DISTINCT FROM b.value END)
 THEN RAISE EXCEPTION 'Non-identifier product/variant data changed'; END IF;
 IF EXISTS(SELECT 1 FROM public.categories c JOIN sku_correction_before_categories b USING(tenant_id,category_id)
 WHERE to_jsonb(c)-'sku_prefix_used' IS DISTINCT FROM b.value-'sku_prefix_used')
 THEN RAISE EXCEPTION 'Category metadata changed'; END IF;
 IF EXISTS(SELECT 1 FROM sku_correction_before_ledger b LEFT JOIN public.catalog_identifier_ledger l USING(tenant_id,profile,code) WHERE to_jsonb(l) IS DISTINCT FROM b.value)
 THEN RAISE EXCEPTION 'An old ledger reservation changed'; END IF;
 IF EXISTS(SELECT 1 FROM sku_correction_triggers b JOIN pg_trigger tr ON tr.tgrelid=b.relation::oid AND tr.tgname=b.tgname WHERE tr.tgenabled<>b.tgenabled)
 THEN RAISE EXCEPTION 'Trigger state was not restored'; END IF;
 FOR t IN SELECT * FROM sku_correction_protected LOOP
  EXECUTE format('SELECT md5(COALESCE(string_agg(j::text,%L ORDER BY j::text),%L)) FROM (SELECT to_jsonb(t) j FROM public.%I t) q','', '',t.table_name) INTO digest;
  IF digest IS DISTINCT FROM t.fingerprint THEN RAISE EXCEPTION 'Protected table changed: %',t.table_name; END IF;
 END LOOP;
END $$;
COMMIT;
-- Last statement intentionally returns results in pgAdmin after COMMIT.
SELECT profile,product_id,variant_id,old_sku,new_sku,new_product_code,corrected_at
FROM public.catalog_sku_corrections ORDER BY profile,product_id,variant_id;
