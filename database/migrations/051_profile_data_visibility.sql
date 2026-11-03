-- MANUAL ONLY: back up, stop ALL application/worker writers, apply after 050
-- as PostgreSQL superuser, then restart. No SQL is run by application startup.
-- Adds ownership metadata and restrictive policies; never deletes/reprices stock/history.
BEGIN;
DO $$ BEGIN
 IF NOT (SELECT rolsuper FROM pg_roles WHERE rolname=current_user) THEN RAISE EXCEPTION 'Apply 051 as PostgreSQL superuser'; END IF;
 IF NOT EXISTS(SELECT 1 FROM information_schema.columns WHERE table_schema='public' AND table_name='tenants' AND column_name='profile_catalog_switching_enabled') THEN RAISE EXCEPTION 'Apply 050 first'; END IF;
 IF EXISTS(SELECT 1 FROM information_schema.columns WHERE table_schema='public' AND table_name='tenants' AND column_name='profile_data_visibility_enabled') THEN RAISE EXCEPTION '051 is already installed; do not backfill again'; END IF;
END $$;

-- A stale request must not read/write the newly selected profile. Security/login
-- connections without a profile GUC still use the persisted tenant selection.
CREATE FUNCTION public.sf_visible_business_profile() RETURNS text
LANGUAGE sql STABLE SET search_path=pg_catalog,public,pg_temp AS $$
 SELECT t.business_profile_code FROM public.tenants t WHERE t.tenant_id=public.sf_tenant()
 AND (NULLIF(current_setting('app.business_profile_code',true),'') IS NULL
      OR current_setting('app.business_profile_code',true)=t.business_profile_code)
$$;
REVOKE ALL ON FUNCTION public.sf_visible_business_profile() FROM PUBLIC;
GRANT EXECUTE ON FUNCTION public.sf_visible_business_profile() TO stockflow_runtime;

-- Only independent headers need a stored profile. Children retain their existing
-- identities and inherit visibility from their parent. Legacy unlinked records
-- belong to Electrical; known Bike documents are inferred from their actual items.
DO $$ DECLARE tab text; BEGIN
 FOREACH tab IN ARRAY ARRAY['sales','purchases','stock_intakes','stock_transfers','stock_counts',
  'cash_closings','billing_drafts','billing_approvals','customers','suppliers','brands','supplier_product_catalog','notification_outbox'] LOOP
  EXECUTE format('ALTER TABLE public.%I ADD COLUMN business_profile_code varchar(40)',tab);
 END LOOP;
END $$;

-- Metadata-only updates must not invoke catalog audit/normalization or business
-- transition triggers. Preserve each existing trigger's original enable mode.
CREATE TEMP TABLE sf051_trigger_state ON COMMIT DROP AS
 SELECT t.tgrelid::regclass AS relation,t.tgname,t.tgenabled FROM pg_trigger t
 WHERE NOT t.tgisinternal AND t.tgrelid IN (
  'public.sales'::regclass,'public.purchases'::regclass,'public.stock_intakes'::regclass,
  'public.stock_transfers'::regclass,'public.stock_counts'::regclass,'public.cash_closings'::regclass,
  'public.billing_drafts'::regclass,'public.billing_approvals'::regclass,'public.customers'::regclass,
  'public.suppliers'::regclass,'public.brands'::regclass,'public.supplier_product_catalog'::regclass,'public.notification_outbox'::regclass);
DO $$ DECLARE r record; BEGIN
 FOR r IN SELECT * FROM sf051_trigger_state WHERE tgenabled<>'D' LOOP
  EXECUTE format('ALTER TABLE %s DISABLE TRIGGER %I',r.relation,r.tgname);
 END LOOP;
END $$;

-- Refuse mixed historical documents rather than hiding some lines and presenting
-- a misleading partial financial total. The entire migration rolls back on error.
DO $$ DECLARE spec text[]; mixed boolean; BEGIN
 FOREACH spec SLICE 1 IN ARRAY ARRAY[
  ['sales','sale_items','sale_id'],['purchases','purchase_items','purchase_id'],
  ['stock_intakes','stock_intake_items','intake_id'],['stock_transfers','stock_transfer_items','transfer_id'],
  ['stock_counts','stock_count_items','stock_count_id']] LOOP
  EXECUTE format('SELECT EXISTS(SELECT 1 FROM public.%I i JOIN public.product_variants v USING(tenant_id,variant_id)
   JOIN public.products p ON p.tenant_id=v.tenant_id AND p.product_id=v.product_id
   JOIN public.categories c ON c.tenant_id=p.tenant_id AND c.category_id=p.category_id
   GROUP BY i.tenant_id,i.%I HAVING count(DISTINCT c.business_profile_code)>1)',spec[2],spec[3]) INTO mixed;
  IF mixed THEN RAISE EXCEPTION 'Mixed-profile documents in %. Review manually; 051 has made no changes.',spec[1]; END IF;
  EXECUTE format('UPDATE public.%I h SET business_profile_code=q.profile FROM (
   SELECT i.tenant_id,i.%I,min(c.business_profile_code) profile FROM public.%I i
   JOIN public.product_variants v USING(tenant_id,variant_id)
   JOIN public.products p ON p.tenant_id=v.tenant_id AND p.product_id=v.product_id
   JOIN public.categories c ON c.tenant_id=p.tenant_id AND c.category_id=p.category_id
   GROUP BY i.tenant_id,i.%I) q WHERE h.tenant_id=q.tenant_id AND h.%I=q.%I',
   spec[1],spec[3],spec[2],spec[3],spec[3],spec[3]);
 END LOOP;
END $$;

-- Held bills have no posted items yet. Infer their profile from the payload when
-- possible; unknown/empty legacy drafts remain Electrical and are never discarded.
DO $$ DECLARE mixed boolean; BEGIN
 SELECT EXISTS(SELECT 1 FROM public.billing_drafts d
  CROSS JOIN LATERAL jsonb_array_elements(COALESCE(d.payload->'cart',d.payload->'items',d.payload->'Items','[]'::jsonb)) i
  JOIN public.products p ON p.product_id=COALESCE(i->>'productId',i->>'ProductId')::integer AND p.tenant_id=d.tenant_id
  JOIN public.categories c ON c.category_id=p.category_id AND c.tenant_id=p.tenant_id
  GROUP BY d.tenant_id,d.user_id,d.location_id,d.draft_id HAVING count(DISTINCT c.business_profile_code)>1) INTO mixed;
 IF mixed THEN RAISE EXCEPTION 'Mixed-profile held bills. Resolve before installing 051.'; END IF;
 UPDATE public.billing_drafts d SET business_profile_code=q.profile FROM (
  SELECT b.tenant_id,b.user_id,b.location_id,b.draft_id,min(c.business_profile_code) profile
  FROM public.billing_drafts b
  CROSS JOIN LATERAL jsonb_array_elements(COALESCE(b.payload->'cart',b.payload->'items',b.payload->'Items','[]'::jsonb)) i
  JOIN public.products p ON p.product_id=COALESCE(i->>'productId',i->>'ProductId')::integer AND p.tenant_id=b.tenant_id
  JOIN public.categories c ON c.category_id=p.category_id AND c.tenant_id=p.tenant_id
  GROUP BY b.tenant_id,b.user_id,b.location_id,b.draft_id) q
  WHERE d.tenant_id=q.tenant_id AND d.user_id=q.user_id AND d.location_id=q.location_id AND d.draft_id=q.draft_id;
 UPDATE public.billing_approvals a SET business_profile_code=d.business_profile_code FROM public.billing_drafts d
  WHERE d.tenant_id=a.tenant_id AND d.location_id=a.location_id AND d.draft_id=a.draft_id AND d.user_id=a.requested_by;
END $$;

DO $$ DECLARE mixed boolean; BEGIN
 SELECT EXISTS(SELECT 1 FROM public.billing_approvals b
  CROSS JOIN LATERAL jsonb_array_elements(COALESCE(b.summary->'items',b.summary->'Items','[]'::jsonb)) i
  JOIN public.products p ON p.product_id=COALESCE(i->>'productId',i->>'ProductId')::integer AND p.tenant_id=b.tenant_id
  JOIN public.categories c ON c.category_id=p.category_id AND c.tenant_id=p.tenant_id
  GROUP BY b.tenant_id,b.location_id,b.draft_id HAVING count(DISTINCT c.business_profile_code)>1) INTO mixed;
 IF mixed THEN RAISE EXCEPTION 'Mixed-profile discount approvals. Resolve before installing 051.'; END IF;
 UPDATE public.billing_approvals a SET business_profile_code=q.profile FROM (
  SELECT b.tenant_id,b.location_id,b.draft_id,min(c.business_profile_code) profile FROM public.billing_approvals b
  CROSS JOIN LATERAL jsonb_array_elements(COALESCE(b.summary->'items',b.summary->'Items','[]'::jsonb)) i
  JOIN public.products p ON p.product_id=COALESCE(i->>'productId',i->>'ProductId')::integer AND p.tenant_id=b.tenant_id
  JOIN public.categories c ON c.category_id=p.category_id AND c.tenant_id=p.tenant_id
  GROUP BY b.tenant_id,b.location_id,b.draft_id) q
  WHERE q.tenant_id=a.tenant_id AND q.location_id=a.location_id AND q.draft_id=a.draft_id;
END $$;

UPDATE public.supplier_product_catalog s SET business_profile_code=c.business_profile_code FROM public.categories c
 WHERE s.tenant_id=c.tenant_id AND s.category_id=c.category_id;
-- Assign contacts/brands to the single profile that already uses them. A contact
-- or brand genuinely used by both profiles remains shared through the policies below.
UPDATE public.customers c SET business_profile_code=q.profile FROM (
 SELECT tenant_id,customer_id,min(business_profile_code) profile FROM public.sales
 WHERE business_profile_code IS NOT NULL GROUP BY tenant_id,customer_id HAVING count(DISTINCT business_profile_code)=1
) q WHERE q.tenant_id=c.tenant_id AND q.customer_id=c.customer_id;
UPDATE public.suppliers s SET business_profile_code=q.profile FROM (
 SELECT tenant_id,supplier_id,min(business_profile_code) profile FROM public.purchases
 WHERE business_profile_code IS NOT NULL GROUP BY tenant_id,supplier_id HAVING count(DISTINCT business_profile_code)=1
) q WHERE q.tenant_id=s.tenant_id AND q.supplier_id=s.supplier_id;
UPDATE public.brands b SET business_profile_code=q.profile FROM (
 SELECT bc.tenant_id,bc.brand_id,min(c.business_profile_code) profile FROM public.brand_categories bc
 JOIN public.categories c USING(tenant_id,category_id) GROUP BY bc.tenant_id,bc.brand_id HAVING count(DISTINCT c.business_profile_code)=1
) q WHERE q.tenant_id=b.tenant_id AND q.brand_id=b.brand_id;

-- Notification URLs are generated by the server. Keep queued historical messages
-- with the document that generated them, not whichever profile a worker runs under.
CREATE FUNCTION public.sf_notification_record_profile(tid bigint,link text,event_key text DEFAULT NULL) RETURNS text
LANGUAGE plpgsql STABLE SECURITY DEFINER SET search_path=pg_catalog,public,pg_temp AS $$
DECLARE result text; id bigint;
BEGIN
 id:=COALESCE(substring(link from '[?&]id=([0-9]+)'),substring(link from '/([0-9]+)$'),substring(event_key from '^[^:]+:([0-9]+)'))::bigint;
 IF link LIKE '/Sales/%' THEN SELECT business_profile_code INTO result FROM public.sales WHERE tenant_id=tid AND sale_id=id;
 ELSIF link LIKE '/Purchases/%' THEN SELECT business_profile_code INTO result FROM public.purchases WHERE tenant_id=tid AND purchase_id=id;
 ELSIF link LIKE '/StockInflow/Details/PURCHASE/%' THEN SELECT p.business_profile_code INTO result FROM public.purchase_receipts r
  JOIN public.purchases p USING(tenant_id,purchase_id) WHERE r.tenant_id=tid AND r.receipt_id=id;
 ELSIF link LIKE '/Service/%' THEN SELECT c.business_profile_code INTO result FROM public.service_requests s
  JOIN public.products p USING(tenant_id,product_id) JOIN public.categories c USING(tenant_id,category_id) WHERE s.tenant_id=tid AND s.service_request_id=id;
 ELSIF link LIKE '/Warranty/Claims%' THEN SELECT s.business_profile_code INTO result FROM public.warranty_claims wc
  JOIN public.warranties w USING(tenant_id,warranty_id) JOIN public.sale_items i USING(tenant_id,sale_item_id)
  JOIN public.sales s USING(tenant_id,sale_id) WHERE wc.tenant_id=tid AND wc.warranty_claim_id=id;
 ELSIF link LIKE '/Warranty/%' THEN SELECT s.business_profile_code INTO result FROM public.warranties w
  JOIN public.sale_items i USING(tenant_id,sale_item_id) JOIN public.sales s USING(tenant_id,sale_id) WHERE w.tenant_id=tid AND w.warranty_id=id;
 END IF;
 RETURN result;
END $$;
REVOKE ALL ON FUNCTION public.sf_notification_record_profile(bigint,text,text) FROM PUBLIC;
UPDATE public.notification_outbox o SET business_profile_code=public.sf_notification_record_profile(o.tenant_id,o.url,o.event_key);

DO $$ DECLARE tab text; BEGIN
 FOREACH tab IN ARRAY ARRAY['sales','purchases','stock_intakes','stock_transfers','stock_counts',
  'cash_closings','billing_drafts','billing_approvals','customers','suppliers','brands','supplier_product_catalog','notification_outbox'] LOOP
  EXECUTE format('UPDATE public.%I SET business_profile_code=''ELECTRICAL'' WHERE business_profile_code IS NULL',tab);
  EXECUTE format('ALTER TABLE public.%I ALTER COLUMN business_profile_code SET NOT NULL',tab);
  EXECUTE format('ALTER TABLE public.%I ALTER COLUMN business_profile_code SET DEFAULT public.sf_current_business_profile()',tab);
  EXECUTE format('ALTER TABLE public.%I ADD CONSTRAINT ck_record_business_profile CHECK(business_profile_code IN(''ELECTRICAL'',''BIKE_ACCESSORIES'',''CLOTHING'',''FOOTWEAR''))',tab);
  EXECUTE format('CREATE INDEX ON public.%I(tenant_id,business_profile_code)',tab);
 END LOOP;
END $$;

DO $$ DECLARE r record; BEGIN
 FOR r IN SELECT * FROM sf051_trigger_state WHERE tgenabled<>'D' LOOP
  EXECUTE format('ALTER TABLE %s %s TRIGGER %I',r.relation,
   CASE r.tgenabled WHEN 'A' THEN 'ENABLE ALWAYS' WHEN 'R' THEN 'ENABLE REPLICA' ELSE 'ENABLE' END,r.tgname);
 END LOOP;
END $$;

-- The same manufacturer name may be registered independently in each setup.
-- Existing brand IDs, product assignments and cross-profile shared brands survive.
DROP INDEX public.ux_brands_name;
CREATE UNIQUE INDEX ux_brands_name ON public.brands(tenant_id,business_profile_code,lower(btrim(brand_name)));

-- The relationship map is shared by policies and the posting guard. No unrestricted
-- function accepts caller-supplied table names. This map is an immutable whitelist.
CREATE FUNCTION public.sf_profile_parent(tab text) RETURNS text[] LANGUAGE sql IMMUTABLE
SET search_path=pg_catalog,public,pg_temp AS $$ SELECT CASE tab
 WHEN 'products' THEN ARRAY['categories','category_id']
 WHEN 'product_variants' THEN ARRAY['products','product_id']
 WHEN 'variant_barcodes' THEN ARRAY['product_variants','variant_id']
 WHEN 'product_units' THEN ARRAY['products','product_id']
 WHEN 'variant_location_settings' THEN ARRAY['product_variants','variant_id']
 WHEN 'inventory_transactions' THEN ARRAY['products','product_id']
 WHEN 'purchase_items' THEN ARRAY['purchases','purchase_id']
 WHEN 'purchase_receipts' THEN ARRAY['purchases','purchase_id']
 WHEN 'purchase_receipt_items' THEN ARRAY['purchase_receipts','receipt_id']
 WHEN 'sale_items' THEN ARRAY['sales','sale_id']
 WHEN 'payments' THEN ARRAY['sales','sale_id']
 WHEN 'sale_returns' THEN ARRAY['sales','sale_id']
 WHEN 'sale_return_items' THEN ARRAY['sale_returns','sale_return_id']
 WHEN 'purchase_returns' THEN ARRAY['purchases','purchase_id']
 WHEN 'purchase_return_items' THEN ARRAY['purchase_returns','purchase_return_id']
 WHEN 'supplier_payments' THEN ARRAY['purchases','purchase_id']
 WHEN 'stock_intake_items' THEN ARRAY['stock_intakes','intake_id']
 WHEN 'stock_transfer_items' THEN ARRAY['stock_transfers','transfer_id']
 WHEN 'stock_transfer_units' THEN ARRAY['stock_transfer_items','transfer_item_id']
 WHEN 'stock_transfer_operations' THEN ARRAY['stock_transfers','transfer_id']
 WHEN 'stock_count_items' THEN ARRAY['stock_counts','stock_count_id']
 WHEN 'service_requests' THEN ARRAY['products','product_id']
 WHEN 'service_parts' THEN ARRAY['service_requests','service_request_id']
 WHEN 'service_history' THEN ARRAY['service_requests','service_request_id']
 WHEN 'service_operations' THEN ARRAY['service_requests','service_request_id']
 WHEN 'service_payments' THEN ARRAY['service_requests','service_request_id']
 WHEN 'warranties' THEN ARRAY['sale_items','sale_item_id']
 WHEN 'warranty_claims' THEN ARRAY['warranties','warranty_id']
 WHEN 'warranty_events' THEN ARRAY['warranties','warranty_id']
 WHEN 'warranty_rmas' THEN ARRAY['warranty_claims','warranty_claim_id']
 WHEN 'sale_fulfilments' THEN ARRAY['sales','sale_id']
 WHEN 'trade_ins' THEN ARRAY['sales','sale_id']
 WHEN 'brand_categories' THEN ARRAY['categories','category_id']
 WHEN 'customer_credit_limits' THEN ARRAY['customers','customer_id']
 WHEN 'notification_customer_consents' THEN ARRAY['customers','customer_id']
 WHEN 'notification_consent_history' THEN ARRAY['customers','customer_id']
 WHEN 'notifications' THEN ARRAY['notification_outbox','outbox_id','id'] END $$;
REVOKE ALL ON FUNCTION public.sf_profile_parent(text) FROM PUBLIC;

-- Private raw lookup is used only by trigger owners; never exposed to runtime.
CREATE FUNCTION public.sf_record_business_profile(tab text,doc jsonb) RETURNS text
LANGUAGE plpgsql STABLE SET search_path=pg_catalog,public,pg_temp AS $$
DECLARE parent text[]:=public.sf_profile_parent(tab); parent_doc jsonb;
BEGIN
 IF parent IS NULL THEN RETURN doc->>'business_profile_code'; END IF;
 EXECUTE format('SELECT to_jsonb(p) FROM public.%I p WHERE p.tenant_id=$1 AND p.%I=$2',parent[1],COALESCE(parent[3],parent[2]))
  INTO parent_doc USING (doc->>'tenant_id')::bigint,(doc->>parent[2])::bigint;
 RETURN public.sf_record_business_profile(parent[1],parent_doc);
END $$;
REVOKE ALL ON FUNCTION public.sf_record_business_profile(text,jsonb) FROM PUBLIC;

-- Staff may read their own notifications without Company Settings access to the
-- underlying outbox. This helper exposes only a tenant-bound visibility boolean.
CREATE FUNCTION public.sf_notification_profile_visible(event_id bigint) RETURNS boolean
LANGUAGE sql STABLE SECURITY DEFINER SET search_path=pg_catalog,public,pg_temp AS $$
 SELECT EXISTS(SELECT 1 FROM public.notification_outbox o WHERE o.tenant_id=public.sf_tenant()
  AND o.id=event_id AND o.business_profile_code=public.sf_visible_business_profile())
$$;
REVOKE ALL ON FUNCTION public.sf_notification_profile_visible(bigint) FROM PUBLIC;
GRANT EXECUTE ON FUNCTION public.sf_notification_profile_visible(bigint) TO stockflow_runtime;

-- Contact membership is company-wide even when its qualifying invoice is in a
-- different branch. Only the membership boolean bypasses location RLS; balances
-- and document contents still pass their normal branch policies.
CREATE FUNCTION public.sf_shared_contact_profile(kind text,contact_id integer) RETURNS boolean
LANGUAGE plpgsql STABLE SECURITY DEFINER SET search_path=pg_catalog,public,pg_temp AS $$
DECLARE tid bigint:=public.sf_tenant(); profile text:=public.sf_visible_business_profile();
BEGIN
 IF tid<=0 OR public.sf_actor()<=0 OR profile IS NULL THEN RETURN false; END IF;
 IF kind='CUSTOMER' THEN
  RETURN EXISTS(SELECT 1 FROM public.sales s WHERE s.tenant_id=tid AND s.customer_id=contact_id AND s.business_profile_code=profile)
   OR EXISTS(SELECT 1 FROM public.service_requests s JOIN public.products p USING(tenant_id,product_id)
    JOIN public.categories c USING(tenant_id,category_id) WHERE s.tenant_id=tid AND s.customer_id=contact_id AND c.business_profile_code=profile);
 ELSIF kind='SUPPLIER' THEN
  RETURN EXISTS(SELECT 1 FROM public.purchases p WHERE p.tenant_id=tid AND p.supplier_id=contact_id AND p.business_profile_code=profile);
 END IF;
 RETURN false;
END $$;
REVOKE ALL ON FUNCTION public.sf_shared_contact_profile(text,integer) FROM PUBLIC;
GRANT EXECUTE ON FUNCTION public.sf_shared_contact_profile(text,integer) TO stockflow_runtime;

-- All record reads and mutations intersect existing tenant/location/permission
-- policies. Parent EXISTS runs as invoker, so its own profile policy also applies.
DO $$ DECLARE tab text; parent text[]; expression text; BEGIN
 FOREACH tab IN ARRAY ARRAY['categories','products','product_variants','variant_barcodes','product_units','variant_location_settings',
  'inventory_transactions','sales','sale_items','payments','purchases','purchase_items','purchase_receipts','purchase_receipt_items',
  'stock_intakes','stock_intake_items','stock_transfers','stock_transfer_items','stock_transfer_units','stock_transfer_operations',
  'stock_counts','stock_count_items','sale_returns','sale_return_items','purchase_returns','purchase_return_items','supplier_payments',
  'service_requests','service_parts','service_history','service_operations','service_payments','warranties','warranty_claims','warranty_events','warranty_rmas',
  'sale_fulfilments','trade_ins','cash_closings','billing_drafts','billing_approvals','customers','suppliers','brands','brand_categories',
  'supplier_product_catalog','customer_credit_limits','notification_outbox','notifications','notification_customer_consents','notification_consent_history'] LOOP
  parent:=public.sf_profile_parent(tab);
  IF parent IS NULL THEN expression:='business_profile_code=(SELECT public.sf_visible_business_profile())';
  ELSE expression:=format('EXISTS(SELECT 1 FROM public.%I p WHERE p.tenant_id=%I.tenant_id AND p.%I=%I.%I)',parent[1],tab,COALESCE(parent[3],parent[2]),tab,parent[2]); END IF;
  -- A contact used in both catalogs is shared, but every balance/document remains
  -- scoped by its own header. Unrelated Electrical contacts stay hidden in Bike.
  IF tab='customers' THEN expression:=expression||' OR public.sf_shared_contact_profile(''CUSTOMER'',customer_id)'; END IF;
  IF tab='suppliers' THEN expression:=expression||' OR public.sf_shared_contact_profile(''SUPPLIER'',supplier_id)'; END IF;
  IF tab='brands' THEN expression:=expression||' OR EXISTS(SELECT 1 FROM public.brand_categories b WHERE b.tenant_id=brands.tenant_id AND b.brand_id=brands.brand_id)'; END IF;
  IF tab='notifications' THEN expression:='public.sf_notification_profile_visible(outbox_id)'; END IF;
  EXECUTE format('CREATE POLICY selected_business_profile ON public.%I AS RESTRICTIVE FOR ALL TO stockflow_runtime USING(%s) WITH CHECK(%s)',tab,expression,expression);
 END LOOP;
END $$;

-- Definer transfer functions bypass RLS. Guard the final row as well, and serialize
-- all postings against profile changes. Product identity must match header profile.
CREATE FUNCTION public.sf_profile_posting_guard() RETURNS trigger
LANGUAGE plpgsql SECURITY DEFINER SET search_path=pg_catalog,public,pg_temp AS $$
DECLARE doc jsonb:=to_jsonb(NEW); selected text; record_profile text; item_profile text; item jsonb; draft_item jsonb; draft_body jsonb;
BEGIN
 PERFORM pg_advisory_xact_lock(hashtextextended('catalog-registration:'||NEW.tenant_id::text,0));
 SELECT business_profile_code INTO selected FROM public.tenants WHERE tenant_id=NEW.tenant_id;
 IF NEW.tenant_id IS DISTINCT FROM public.sf_tenant() OR public.sf_actor()<=0
  OR selected IS DISTINCT FROM public.sf_visible_business_profile() THEN RAISE EXCEPTION 'Shop setup changed; reload before posting' USING ERRCODE='23514'; END IF;
 IF TG_OP='UPDATE' AND doc ? 'business_profile_code' AND to_jsonb(OLD)->>'business_profile_code' IS DISTINCT FROM doc->>'business_profile_code' THEN
  RAISE EXCEPTION 'Saved record profile cannot change' USING ERRCODE='23514'; END IF;
 record_profile:=public.sf_record_business_profile(TG_TABLE_NAME,doc);
 -- Shared contact/brand edits preserve original ownership. They are authorized by
 -- their invoker policies; transactions always require their own selected profile.
 IF TG_OP='UPDATE' AND TG_TABLE_NAME IN('customers','suppliers','brands') THEN RETURN NEW; END IF;
 IF record_profile IS DISTINCT FROM selected THEN RAISE EXCEPTION 'Record belongs to another shop setup; switch back to work with it' USING ERRCODE='23514'; END IF;
 IF TG_TABLE_NAME IN('billing_drafts','billing_approvals') THEN
  draft_body:=COALESCE(doc->'payload',doc->'summary');
  FOR draft_item IN SELECT value FROM jsonb_array_elements(COALESCE(draft_body->'cart',draft_body->'items',draft_body->'Items','[]'::jsonb)) LOOP
   SELECT to_jsonb(p) INTO item FROM public.products p WHERE p.tenant_id=NEW.tenant_id
    AND p.product_id=COALESCE(draft_item->>'productId',draft_item->>'ProductId')::integer;
   IF public.sf_record_business_profile('products',item) IS DISTINCT FROM selected THEN
    RAISE EXCEPTION 'Held bill contains products from another shop setup; reload' USING ERRCODE='23514';
   END IF;
  END LOOP;
 END IF;
 IF doc->>'product_id' IS NOT NULL THEN
  SELECT to_jsonb(p) INTO item FROM public.products p WHERE p.tenant_id=NEW.tenant_id AND p.product_id=(doc->>'product_id')::integer;
  item_profile:=public.sf_record_business_profile('products',item);
  IF item_profile IS DISTINCT FROM selected THEN RAISE EXCEPTION 'Product belongs to another shop setup' USING ERRCODE='23514'; END IF;
 END IF;
 IF doc->>'variant_id' IS NOT NULL THEN
  SELECT to_jsonb(v) INTO item FROM public.product_variants v WHERE v.tenant_id=NEW.tenant_id AND v.variant_id=(doc->>'variant_id')::bigint;
  item_profile:=public.sf_record_business_profile('product_variants',item);
  IF item_profile IS DISTINCT FROM selected THEN RAISE EXCEPTION 'Variant belongs to another shop setup' USING ERRCODE='23514'; END IF;
 END IF;
 RETURN NEW;
END $$;
REVOKE ALL ON FUNCTION public.sf_profile_posting_guard() FROM PUBLIC;
DO $$ DECLARE tab text; BEGIN
 FOREACH tab IN ARRAY ARRAY['sales','sale_items','payments','purchases','purchase_items','purchase_receipts','purchase_receipt_items',
  'stock_intakes','stock_intake_items','stock_transfers','stock_transfer_items','stock_transfer_units','stock_transfer_operations',
  'stock_counts','stock_count_items','sale_returns','sale_return_items','purchase_returns','purchase_return_items','supplier_payments',
  'service_requests','service_parts','service_history','service_operations','service_payments','warranties','warranty_claims','warranty_events','warranty_rmas',
  'sale_fulfilments','trade_ins','cash_closings','billing_drafts','billing_approvals','customers','suppliers','brands','supplier_product_catalog'] LOOP
  EXECUTE format('CREATE TRIGGER a_profile_posting_guard BEFORE INSERT OR UPDATE ON public.%I FOR EACH ROW EXECUTE FUNCTION public.sf_profile_posting_guard()',tab);
 END LOOP;
END $$;

-- Background delivery inherits the original event profile, even after selection changes.
CREATE FUNCTION public.sf_notification_profile_stamp() RETURNS trigger
LANGUAGE plpgsql SECURITY DEFINER SET search_path=pg_catalog,public,pg_temp AS $$ BEGIN
 IF TG_OP='UPDATE' THEN NEW.business_profile_code:=OLD.business_profile_code;
 ELSE
  NEW.business_profile_code:=COALESCE(public.sf_notification_record_profile(NEW.tenant_id,NEW.url,NEW.event_key),
   (SELECT business_profile_code FROM public.tenants WHERE tenant_id=NEW.tenant_id));
 END IF;
 RETURN NEW;
END $$;
CREATE TRIGGER a_notification_profile_stamp BEFORE INSERT OR UPDATE ON public.notification_outbox FOR EACH ROW EXECUTE FUNCTION public.sf_notification_profile_stamp();
REVOKE ALL ON FUNCTION public.sf_notification_profile_stamp() FROM PUBLIC;

-- Credit exposure is still tenant-wide across branches, but follows the selected
-- business setup. Existing per-customer limits remain administrative configuration.
CREATE OR REPLACE FUNCTION public.sf_customer_credit_balance(p_customer integer) RETURNS numeric
LANGUAGE sql SECURITY DEFINER SET search_path=pg_catalog,public,pg_temp AS $$
 SELECT coalesce(sum(greatest(s.total_amount
  -coalesce((SELECT sum(return_total) FROM public.sale_returns r WHERE r.tenant_id=s.tenant_id AND r.sale_id=s.sale_id),0)
  -coalesce((SELECT sum(amount) FROM public.payments p WHERE p.tenant_id=s.tenant_id AND p.sale_id=s.sale_id AND payment_mode<>'CREDIT'),0)
  +coalesce((SELECT sum(refund_amount) FROM public.sale_returns r WHERE r.tenant_id=s.tenant_id AND r.sale_id=s.sale_id),0),0)),0)
 FROM public.sales s WHERE s.tenant_id=public.sf_tenant() AND s.customer_id=p_customer AND s.status='COMPLETED'
 AND s.business_profile_code=public.sf_visible_business_profile()
$$;

-- Cash versions restart within each setup; request UUIDs and document numbering
-- stay tenant-wide so old identifiers can never be silently reused.
ALTER TABLE public.cash_closings DROP CONSTRAINT cash_closings_tenant_id_location_id_business_date_version_key;
ALTER TABLE public.cash_closings ADD UNIQUE(tenant_id,location_id,business_profile_code,business_date,version);

-- Retain existing permission, location, idempotency and stock checks in definer functions.
CREATE OR REPLACE FUNCTION public.sf_create_transfer(source_id bigint,destination_id bigint,lines jsonb,notes text,retry uuid) RETURNS bigint LANGUAGE plpgsql SECURITY DEFINER SET search_path=pg_catalog,public,pg_temp AS $$
DECLARE tid bigint:=public.sf_tenant(); actor int:=public.sf_actor(); id bigint; transit bigint; line jsonb; old public.stock_transfer_operations; body jsonb;
BEGIN
 PERFORM pg_advisory_xact_lock(hashtextextended('catalog-registration:'||tid::text,0));
 IF public.sf_visible_business_profile() IS NULL THEN RAISE EXCEPTION 'Shop setup changed; reload' USING ERRCODE='23514'; END IF;
 IF NOT public.sf_transfer_permission('STOCK_TRANSFER','CREATE') THEN RAISE EXCEPTION 'Transfer creation is not permitted' USING ERRCODE='42501'; END IF;
 IF retry IS NULL OR retry='00000000-0000-0000-0000-000000000000'::uuid THEN RAISE EXCEPTION 'A request ID is required' USING ERRCODE='23514'; END IF;
 body:=jsonb_build_object('source',source_id,'destination',destination_id,'lines',lines,'notes',notes);
 PERFORM pg_advisory_xact_lock(hashtextextended(tid||':transfer:'||retry,0));
 SELECT * INTO old FROM public.stock_transfer_operations WHERE tenant_id=tid AND request_id=retry;
 IF FOUND THEN IF NOT EXISTS(SELECT 1 FROM public.stock_transfers h WHERE h.tenant_id=tid AND h.transfer_id=old.transfer_id AND h.business_profile_code=public.sf_visible_business_profile()) THEN RAISE EXCEPTION 'Transfer not found in selected setup' USING ERRCODE='23514'; END IF; IF old.action_name<>'CREATE' OR old.payload<>body OR old.created_by<>actor THEN RAISE EXCEPTION 'Request ID was already used' USING ERRCODE='23514'; END IF; RETURN old.transfer_id; END IF;
 IF source_id=destination_id OR NOT (public.sf_can_location(source_id) OR public.sf_can_location(destination_id)) OR
 (SELECT count(*) FROM public.store_locations WHERE tenant_id=tid AND location_id IN(source_id,destination_id) AND is_active AND location_type<>'TRANSIT')<>2 THEN
 RAISE EXCEPTION 'Choose different active locations in your business and an assigned source or destination' USING ERRCODE='42501'; END IF;
 IF jsonb_typeof(lines)<>'array' OR jsonb_array_length(lines) NOT BETWEEN 1 AND 200 OR length(notes)>2000 THEN RAISE EXCEPTION 'Invalid transfer lines or notes' USING ERRCODE='23514'; END IF;
 INSERT INTO public.store_locations(tenant_id,location_name,location_type) VALUES(tid,'Transfer in transit','TRANSIT') RETURNING location_id INTO transit;
 id:=nextval(pg_get_serial_sequence('public.stock_transfers','transfer_id'));
 INSERT INTO public.stock_transfers(transfer_id,tenant_id,transfer_number,from_location_id,to_location_id,transit_location_id,requested_by,remarks)
 VALUES(id,tid,'TRF-'||id,source_id,destination_id,transit,actor,notes);
 UPDATE public.store_locations SET location_name='In transit / TRF-'||id WHERE location_id=transit;
 FOR line IN SELECT value FROM jsonb_array_elements(lines) LOOP
  IF NOT EXISTS(SELECT 1 FROM public.product_variants v JOIN public.products p ON p.product_id=v.product_id AND p.tenant_id=v.tenant_id WHERE v.tenant_id=tid AND v.variant_id=(line->>'variantId')::bigint AND v.is_active AND p.is_active) THEN RAISE EXCEPTION 'Invalid variant' USING ERRCODE='23514'; END IF;
  INSERT INTO public.stock_transfer_items(tenant_id,transfer_id,variant_id,requested_quantity) VALUES(tid,id,(line->>'variantId')::bigint,(line->>'quantity')::int);
 END LOOP;
 INSERT INTO public.stock_transfer_operations VALUES(tid,retry,id,'CREATE',body,actor,now());
 RETURN id;
END $$;

CREATE OR REPLACE FUNCTION public.sf_transfer_action(id bigint,act text,body jsonb,retry uuid) RETURNS bigint LANGUAGE plpgsql SECURITY DEFINER SET search_path=pg_catalog,public,pg_temp AS $$
<<transfer_action>>
DECLARE tid bigint:=public.sf_tenant(); actor int:=public.sf_actor(); h public.stock_transfers; old public.stock_transfer_operations;
 item record; line jsonb; unitid bigint; ids bigint[]; n int; good int; damage int; lost int; resolution text; dest bigint; notes text;
BEGIN
 PERFORM pg_advisory_xact_lock(hashtextextended('catalog-registration:'||tid::text,0));
 IF NOT EXISTS(SELECT 1 FROM public.stock_transfers h WHERE h.tenant_id=tid AND h.transfer_id=id AND h.business_profile_code=public.sf_visible_business_profile()) THEN RAISE EXCEPTION 'Transfer not found in selected setup' USING ERRCODE='23514'; END IF;
 IF public.sf_location()<=0 OR NOT public.sf_can_location(public.sf_location()) THEN RAISE EXCEPTION 'Select an assigned stock location' USING ERRCODE='42501'; END IF;
 IF retry IS NULL OR retry='00000000-0000-0000-0000-000000000000'::uuid THEN RAISE EXCEPTION 'A request ID is required' USING ERRCODE='23514'; END IF;
 IF NOT public.sf_transfer_permission('STOCK_TRANSFER','VIEW') THEN RAISE EXCEPTION 'Transfer access denied' USING ERRCODE='42501'; END IF;
 IF NOT public.sf_transfer_permission(CASE act WHEN 'dispatch' THEN 'TRANSFER_DISPATCH' WHEN 'receive' THEN 'TRANSFER_RECEIVE' WHEN 'cancel' THEN 'TRANSFER_CANCEL' ELSE 'STOCK_TRANSFER' END,
 CASE act WHEN 'request' THEN 'CREATE' ELSE 'APPROVE' END) THEN RAISE EXCEPTION 'Transfer action is not permitted' USING ERRCODE='42501'; END IF;
 PERFORM pg_advisory_xact_lock(hashtextextended(tid||':transfer:'||retry,0));
 SELECT * INTO old FROM public.stock_transfer_operations WHERE tenant_id=tid AND request_id=retry;
 IF FOUND THEN IF old.transfer_id<>id OR old.action_name<>act OR old.payload<>body OR old.created_by<>actor THEN RAISE EXCEPTION 'Request ID was already used' USING ERRCODE='23514'; END IF; RETURN id; END IF;
 SELECT * INTO h FROM public.stock_transfers WHERE tenant_id=tid AND transfer_id=id FOR UPDATE;
 IF NOT FOUND THEN RAISE EXCEPTION 'Transfer not found' USING ERRCODE='23514'; END IF;
 IF NOT (public.sf_can_location(h.from_location_id) OR public.sf_can_location(h.to_location_id)) THEN RAISE EXCEPTION 'Store access denied' USING ERRCODE='42501'; END IF;
 IF act IN('approve','dispatch','cancel') AND (public.sf_location()<>h.from_location_id OR NOT public.sf_can_location(h.from_location_id)) THEN RAISE EXCEPTION 'Source location access is required' USING ERRCODE='42501'; END IF;
 IF act IN('receive','discrepancy') AND (public.sf_location()<>h.to_location_id OR NOT public.sf_can_location(h.to_location_id)) THEN RAISE EXCEPTION 'Destination access is required' USING ERRCODE='42501'; END IF;
 IF public.sf_location() NOT IN(h.from_location_id,h.to_location_id) THEN RAISE EXCEPTION 'Use the source or destination location' USING ERRCODE='42501'; END IF;
 IF act='request' AND h.status='DRAFT' THEN UPDATE public.stock_transfers SET status='REQUESTED' WHERE transfer_id=id;
 ELSIF act='approve' AND h.status='REQUESTED' THEN UPDATE public.stock_transfers SET status='APPROVED',approved_by=actor WHERE transfer_id=id;
 ELSIF act='cancel' AND h.status IN('DRAFT','REQUESTED','APPROVED') THEN UPDATE public.stock_transfers SET status='CANCELLED' WHERE transfer_id=id;
 ELSIF (act='dispatch' AND h.status='APPROVED') OR (act IN('receive','discrepancy') AND h.status IN('DISPATCHED','PARTIALLY_RECEIVED')) THEN
  -- Same product lock order as existing sales/receipts/adjustments; source stock cannot be oversold.
  PERFORM p.product_id FROM public.products p WHERE p.tenant_id=tid AND p.product_id IN(SELECT v.product_id FROM public.product_variants v JOIN public.stock_transfer_items i ON i.variant_id=v.variant_id WHERE i.transfer_id=id AND i.tenant_id=tid) ORDER BY p.product_id FOR UPDATE;
  IF jsonb_typeof(body->'items')<>'array' OR jsonb_array_length(body->'items') NOT BETWEEN 1 AND 200 THEN RAISE EXCEPTION 'Supply transfer lines' USING ERRCODE='23514'; END IF;
  IF (SELECT count(DISTINCT x->>'transferItemId') FROM jsonb_array_elements(body->'items') x)<>jsonb_array_length(body->'items') THEN RAISE EXCEPTION 'Duplicate transfer lines' USING ERRCODE='23514'; END IF;
  IF act='dispatch' AND jsonb_array_length(body->'items')<>(SELECT count(*) FROM public.stock_transfer_items WHERE transfer_id=id) THEN RAISE EXCEPTION 'Dispatch every requested line once' USING ERRCODE='23514'; END IF;
  notes:=COALESCE(body->>'notes','');
  IF length(notes)>2000 OR (act='discrepancy' AND length(btrim(notes))=0) THEN RAISE EXCEPTION 'A discrepancy reason is required (maximum 2000 characters)' USING ERRCODE='23514'; END IF;
  FOR line IN SELECT value FROM jsonb_array_elements(body->'items') LOOP
   SELECT i.*,v.product_id,v.is_serial_tracked INTO item FROM public.stock_transfer_items i JOIN public.product_variants v ON v.variant_id=i.variant_id AND v.tenant_id=i.tenant_id WHERE i.tenant_id=tid AND i.transfer_id=id AND i.transfer_item_id=(line->>'transferItemId')::bigint;
   IF NOT FOUND THEN RAISE EXCEPTION 'Line does not belong to transfer' USING ERRCODE='23514'; END IF;
   n:=(line->>'quantity')::int;resolution:=COALESCE(line->>'resolution','RECEIVED');
   IF n IS NULL OR n<=0 OR n>10000 THEN RAISE EXCEPTION 'Invalid quantity' USING ERRCODE='23514'; END IF;
   IF act='dispatch' AND (n<>item.requested_quantity OR public.sf_stock(tid,h.from_location_id,item.variant_id)<n) THEN RAISE EXCEPTION 'Insufficient source stock or incorrect dispatch quantity' USING ERRCODE='23514'; END IF;
   IF act<>'dispatch' AND n>item.dispatched_quantity-item.received_quantity-item.damaged_quantity-item.lost_quantity THEN RAISE EXCEPTION 'Receipt exceeds outstanding transit quantity' USING ERRCODE='23514'; END IF;
   IF act='receive' AND resolution<>'RECEIVED' OR act='discrepancy' AND resolution NOT IN('DAMAGED','LOST') THEN RAISE EXCEPTION 'Use the discrepancy action to record damage/loss' USING ERRCODE='23514'; END IF;
   SELECT COALESCE(array_agg(x::bigint),'{}'::bigint[]) INTO ids FROM jsonb_array_elements_text(COALESCE(line->'unitIds','[]')) x;
   IF item.is_serial_tracked AND (cardinality(ids)<>n OR (SELECT count(DISTINCT x) FROM unnest(ids) x)<>n) OR NOT item.is_serial_tracked AND cardinality(ids)<>0 THEN RAISE EXCEPTION 'Select exactly one distinct serial per serialized unit' USING ERRCODE='23514'; END IF;
   IF item.is_serial_tracked THEN
    PERFORM product_unit_id FROM public.product_units WHERE tenant_id=tid AND product_unit_id=ANY(ids) ORDER BY product_unit_id FOR UPDATE;
    IF act='dispatch' THEN
     IF (SELECT count(*) FROM public.product_units WHERE tenant_id=tid AND variant_id=item.variant_id AND location_id=h.from_location_id AND unit_status='IN_STOCK' AND sale_id IS NULL AND product_unit_id=ANY(ids))<>n THEN RAISE EXCEPTION 'A source unit is unavailable' USING ERRCODE='23514'; END IF;
    ELSE
     IF (SELECT count(*) FROM public.stock_transfer_units tu JOIN public.product_units u ON u.product_unit_id=tu.product_unit_id WHERE tu.tenant_id=tid AND tu.transfer_item_id=item.transfer_item_id AND tu.resolution='IN_TRANSIT' AND u.location_id=h.transit_location_id AND u.unit_status='IN_TRANSIT' AND tu.product_unit_id=ANY(ids))<>n THEN RAISE EXCEPTION 'A unit is not outstanding on this transfer' USING ERRCODE='23514'; END IF;
    END IF;
   ELSE ids:=ARRAY[NULL::bigint]; END IF;
   FOREACH unitid IN ARRAY ids LOOP
    IF act='dispatch' THEN
     INSERT INTO public.inventory_transactions(tenant_id,location_id,product_id,variant_id,product_unit_id,transaction_type,quantity,reference_type,reference_id,transfer_id,created_by)
     VALUES(tid,h.from_location_id,item.product_id,item.variant_id,unitid,'TRANSFER_OUT',CASE WHEN item.is_serial_tracked THEN 1 ELSE n END,'TRANSFER',id,id,actor),
           (tid,h.transit_location_id,item.product_id,item.variant_id,unitid,'TRANSFER_IN',CASE WHEN item.is_serial_tracked THEN 1 ELSE n END,'TRANSFER',id,id,actor);
     IF unitid IS NOT NULL THEN
      INSERT INTO public.stock_transfer_units(tenant_id,transfer_item_id,product_unit_id) VALUES(tid,item.transfer_item_id,unitid);
      UPDATE public.product_units SET location_id=h.transit_location_id,unit_status='IN_TRANSIT',updated_at=now() WHERE tenant_id=tid AND product_unit_id=unitid;
     END IF;
    ELSE
     INSERT INTO public.inventory_transactions(tenant_id,location_id,product_id,variant_id,product_unit_id,transaction_type,quantity,reference_type,reference_id,transfer_id,created_by,remarks)
     VALUES(tid,h.transit_location_id,item.product_id,item.variant_id,unitid,CASE WHEN resolution='LOST' THEN 'TRANSFER_LOSS' ELSE 'TRANSFER_OUT' END,CASE WHEN item.is_serial_tracked THEN 1 ELSE n END,'TRANSFER',id,id,actor,notes);
     IF resolution<>'LOST' THEN
      INSERT INTO public.inventory_transactions(tenant_id,location_id,product_id,variant_id,product_unit_id,transaction_type,quantity,reference_type,reference_id,transfer_id,created_by,remarks)
      VALUES(tid,h.to_location_id,item.product_id,item.variant_id,unitid,'TRANSFER_IN',CASE WHEN item.is_serial_tracked THEN 1 ELSE n END,'TRANSFER',id,id,actor,notes);
      IF resolution='DAMAGED' THEN INSERT INTO public.inventory_transactions(tenant_id,location_id,product_id,variant_id,product_unit_id,transaction_type,quantity,reference_type,reference_id,transfer_id,created_by,remarks)
       VALUES(tid,h.to_location_id,item.product_id,item.variant_id,unitid,'DAMAGED',CASE WHEN item.is_serial_tracked THEN 1 ELSE n END,'TRANSFER',id,id,actor,notes); END IF;
     END IF;
     IF unitid IS NOT NULL THEN
      UPDATE public.stock_transfer_units SET resolution=transfer_action.resolution WHERE tenant_id=tid AND transfer_item_id=item.transfer_item_id AND product_unit_id=unitid;
      UPDATE public.product_units SET location_id=CASE WHEN resolution='LOST' THEN h.transit_location_id ELSE h.to_location_id END,
       unit_status=CASE WHEN resolution='RECEIVED' THEN 'IN_STOCK' ELSE resolution END,updated_at=now() WHERE tenant_id=tid AND product_unit_id=unitid;
     END IF;
    END IF;
   END LOOP;
   IF act='dispatch' THEN UPDATE public.stock_transfer_items SET dispatched_quantity=n WHERE transfer_item_id=item.transfer_item_id;
   ELSE UPDATE public.stock_transfer_items SET received_quantity=received_quantity+CASE WHEN resolution='RECEIVED' THEN n ELSE 0 END,
    damaged_quantity=damaged_quantity+CASE WHEN resolution='DAMAGED' THEN n ELSE 0 END,lost_quantity=lost_quantity+CASE WHEN resolution='LOST' THEN n ELSE 0 END WHERE transfer_item_id=item.transfer_item_id; END IF;
  END LOOP;
  IF act='dispatch' THEN UPDATE public.stock_transfers SET status='DISPATCHED',dispatched_by=actor,dispatched_at=now() WHERE transfer_id=id;
  ELSE UPDATE public.stock_transfers SET status=CASE WHEN EXISTS(SELECT 1 FROM public.stock_transfer_items WHERE transfer_id=id AND received_quantity+damaged_quantity+lost_quantity<dispatched_quantity) THEN 'PARTIALLY_RECEIVED' ELSE 'RECEIVED' END,received_by=actor,received_at=now() WHERE transfer_id=id; END IF;
 ELSE RAISE EXCEPTION 'Invalid transfer state transition; reload the transfer' USING ERRCODE='23514'; END IF;
 INSERT INTO public.stock_transfer_operations VALUES(tid,retry,id,act,body,actor,now());
 INSERT INTO public.audit_logs(tenant_id,location_id,user_id,module_name,action_name,table_name,record_id,new_values) VALUES(tid,NULL,actor,'Stock transfers',upper(act),'stock_transfers',id,body);
 RETURN id;
END $$;

CREATE OR REPLACE FUNCTION public.sf_transfer_unit_details(id bigint) RETURNS TABLE(transfer_item_id bigint,product_unit_id bigint,serial_number varchar,resolution varchar)
LANGUAGE sql STABLE SECURITY DEFINER SET search_path=pg_catalog,public,pg_temp AS $$
 SELECT tu.transfer_item_id,tu.product_unit_id,u.serial_number,tu.resolution FROM public.stock_transfer_units tu
 JOIN public.stock_transfer_items i ON i.transfer_item_id=tu.transfer_item_id AND i.tenant_id=tu.tenant_id
 JOIN public.stock_transfers h ON h.transfer_id=i.transfer_id AND h.tenant_id=i.tenant_id
 JOIN public.product_units u ON u.product_unit_id=tu.product_unit_id AND u.tenant_id=tu.tenant_id
 WHERE h.business_profile_code=public.sf_visible_business_profile() AND h.tenant_id=public.sf_tenant() AND h.transfer_id=id AND public.sf_transfer_permission('STOCK_TRANSFER','VIEW')
 AND (public.sf_can_location(h.from_location_id) OR public.sf_can_location(h.to_location_id)) $$;

CREATE OR REPLACE FUNCTION public.sf_transfer_unit_labels(id bigint)
RETURNS TABLE(transfer_item_id bigint,product_unit_id bigint,serial_number varchar,resolution varchar,barcodes text[])
LANGUAGE sql STABLE SECURITY DEFINER SET search_path=pg_catalog,public,pg_temp AS $$
 SELECT tu.transfer_item_id,tu.product_unit_id,u.serial_number,tu.resolution,
 ARRAY[u.serial_number::text,u.barcode::text,u.internal_barcode::text,u.manufacturer_barcode::text]
 FROM public.stock_transfer_units tu
 JOIN public.stock_transfer_items i ON i.transfer_item_id=tu.transfer_item_id AND i.tenant_id=tu.tenant_id
 JOIN public.stock_transfers h ON h.transfer_id=i.transfer_id AND h.tenant_id=i.tenant_id
 JOIN public.product_units u ON u.product_unit_id=tu.product_unit_id AND u.tenant_id=tu.tenant_id
 WHERE h.business_profile_code=public.sf_visible_business_profile() AND h.tenant_id=public.sf_tenant() AND h.transfer_id=id AND public.sf_transfer_permission('STOCK_TRANSFER','VIEW')
 AND (public.sf_can_location(h.from_location_id) OR public.sf_can_location(h.to_location_id))
 AND (public.sf_location()=0 OR public.sf_location() IN(h.from_location_id,h.to_location_id)) $$;

CREATE OR REPLACE FUNCTION public.sf_notification_digest() RETURNS integer LANGUAGE plpgsql SECURITY DEFINER SET search_path=pg_catalog,public,pg_temp AS $$
DECLARE loc record; digest_day date; low integer; empty integer; body_text text; done integer:=0;
BEGIN
 FOR loc IN SELECT l.*,s.time_zone,s.digest_hour FROM public.store_locations l JOIN public.notification_delivery_settings s USING(tenant_id) JOIN public.notification_settings n USING(tenant_id) JOIN public.tenants t USING(tenant_id)
 WHERE l.is_active AND l.location_type<>'TRANSIT' AND s.digest_enabled AND n.enabled AND t.is_active AND extract(hour FROM now() AT TIME ZONE s.time_zone)>=s.digest_hour
 AND NOT EXISTS(SELECT 1 FROM public.notification_digest_runs r WHERE r.tenant_id=l.tenant_id AND r.location_id=l.location_id AND r.day=(now() AT TIME ZONE s.time_zone)::date) ORDER BY l.tenant_id,l.location_id LIMIT 25 LOOP
  digest_day:=(now() AT TIME ZONE loc.time_zone)::date;
  PERFORM pg_advisory_xact_lock(hashtextextended('stock-digest:'||loc.tenant_id||':'||loc.location_id||':'||digest_day,0));
  IF EXISTS(SELECT 1 FROM public.notification_digest_runs r WHERE r.tenant_id=loc.tenant_id AND r.location_id=loc.location_id AND r.day=digest_day) THEN CONTINUE; END IF;
  PERFORM set_config('app.tenant_id',loc.tenant_id::text,true);PERFORM set_config('app.location_id',loc.location_id::text,true);
  WITH balances AS (
   SELECT p.product_name||' / '||v.variant_name AS name,
    CASE WHEN v.is_serial_tracked THEN (SELECT count(*) FROM public.product_units u WHERE u.tenant_id=v.tenant_id AND u.variant_id=v.variant_id AND u.location_id=loc.location_id AND u.unit_status='IN_STOCK') ELSE stock.current_stock END AS qty,
    public.sf_variant_minimum(v.variant_id,v.minimum_stock_level) AS minimum
   FROM public.products p JOIN public.product_variants v ON v.tenant_id=p.tenant_id AND v.product_id=p.product_id
   JOIN public.vw_location_current_stock stock ON stock.tenant_id=v.tenant_id AND stock.variant_id=v.variant_id AND stock.location_id=loc.location_id WHERE p.tenant_id=loc.tenant_id AND p.is_active AND v.is_active AND EXISTS(SELECT 1 FROM public.categories c JOIN public.tenants t ON t.tenant_id=c.tenant_id WHERE c.tenant_id=p.tenant_id AND c.category_id=p.category_id AND c.business_profile_code=t.business_profile_code)
  ), alerts AS(SELECT * FROM balances WHERE qty<=minimum)
  SELECT count(*)::integer,count(*) FILTER(WHERE qty<=0)::integer,
   (SELECT string_agg(name||': '||qty||' (limit '||minimum||')','; ') FROM(SELECT * FROM alerts ORDER BY qty,name LIMIT 5) preview)
  INTO low,empty,body_text FROM alerts;
  INSERT INTO public.notification_digest_runs(tenant_id,location_id,day,low_count,out_count) VALUES(loc.tenant_id,loc.location_id,digest_day,low,empty) ON CONFLICT DO NOTHING;
  IF low>0 THEN
   INSERT INTO public.notification_outbox(tenant_id,event,event_key,reference,status_text,module,location_id,url,title,body)
   VALUES(loc.tenant_id,'stock.digest','stock.digest:'||loc.location_id||':'||digest_day,digest_day::text,'LOW_STOCK','INVENTORY',loc.location_id,'/Inventory/LowStock','Daily stock alert',left(loc.location_name||': '||low||' items at/below limit, including '||empty||' out of stock. '||COALESCE(body_text,''),500)) ON CONFLICT DO NOTHING;
   done:=done+1;
  END IF;
 END LOOP;
 RETURN done;
END $$;

ALTER TABLE public.tenants ADD COLUMN profile_data_visibility_enabled boolean NOT NULL DEFAULT true;
COMMENT ON COLUMN public.tenants.profile_data_visibility_enabled IS '051: business documents and aggregates follow selected profile; original data retained.';
COMMIT;
