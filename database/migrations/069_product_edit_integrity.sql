-- MANUAL ONLY. Back up the complete database and stop application/workers.
-- Apply after 068 as PostgreSQL superuser. No identifiers, prices, stock or history are rewritten.
BEGIN;
DO $$ BEGIN
 IF NOT EXISTS(SELECT 1 FROM pg_roles WHERE rolname=current_user AND rolsuper) THEN RAISE EXCEPTION 'Apply 069 as PostgreSQL superuser'; END IF;
 IF to_regclass('public.unit_identifier_reservations') IS NULL THEN RAISE EXCEPTION 'Apply 068 first'; END IF;
END $$;
LOCK TABLE public.products,public.product_variants IN ACCESS EXCLUSIVE MODE;
-- Catalog settings are shared across locations. A location-filtered history lookup is insufficient.
CREATE OR REPLACE FUNCTION public.sf_product_edit_history(pid integer,vid bigint DEFAULT NULL) RETURNS boolean
 LANGUAGE plpgsql STABLE SECURITY DEFINER SET search_path=pg_catalog,public,pg_temp AS $$
DECLARE tid bigint:=public.sf_tenant();
BEGIN
 IF NOT EXISTS(SELECT 1 FROM public.products p JOIN public.categories c USING(tenant_id,category_id)
  WHERE p.tenant_id=tid AND p.product_id=pid AND c.business_profile_code=public.sf_visible_business_profile())
  OR vid IS NOT NULL AND NOT EXISTS(SELECT 1 FROM public.product_variants WHERE tenant_id=tid AND product_id=pid AND variant_id=vid)
 THEN RAISE EXCEPTION 'Product or variant not found in this shop' USING ERRCODE='23514'; END IF;
 RETURN EXISTS(SELECT 1 FROM public.inventory_transactions WHERE tenant_id=tid AND product_id=pid AND (vid IS NULL OR variant_id=vid))
 OR EXISTS(SELECT 1 FROM public.purchase_items WHERE tenant_id=tid AND product_id=pid AND (vid IS NULL OR variant_id=vid))
 OR EXISTS(SELECT 1 FROM public.sale_items WHERE tenant_id=tid AND product_id=pid AND (vid IS NULL OR variant_id=vid))
 OR EXISTS(SELECT 1 FROM public.product_units WHERE tenant_id=tid AND product_id=pid AND (vid IS NULL OR variant_id=vid))
 OR EXISTS(SELECT 1 FROM public.stock_count_items WHERE tenant_id=tid AND product_id=pid AND (vid IS NULL OR variant_id=vid))
 OR EXISTS(SELECT 1 FROM public.stock_transfer_items i JOIN public.product_variants v USING(tenant_id,variant_id) WHERE v.tenant_id=tid AND v.product_id=pid AND (vid IS NULL OR v.variant_id=vid));
END $$;
REVOKE ALL ON FUNCTION public.sf_product_edit_history(integer,bigint) FROM PUBLIC;
GRANT EXECUTE ON FUNCTION public.sf_product_edit_history(integer,bigint) TO stockflow_runtime;
CREATE OR REPLACE FUNCTION public.sf_product_category_edit_locked(pid integer) RETURNS boolean
 LANGUAGE plpgsql STABLE SECURITY DEFINER SET search_path=pg_catalog,public,pg_temp AS $$
BEGIN
 IF NOT EXISTS(SELECT 1 FROM public.products p JOIN public.categories c USING(tenant_id,category_id) WHERE p.tenant_id=public.sf_tenant() AND p.product_id=pid AND c.business_profile_code=public.sf_visible_business_profile()) THEN RAISE EXCEPTION 'Product not found in this shop' USING ERRCODE='23514'; END IF;
 RETURN EXISTS(SELECT 1 FROM public.catalog_identifier_ledger l JOIN public.products p USING(tenant_id,product_id) WHERE p.tenant_id=public.sf_tenant() AND p.product_id=pid AND l.code=upper(btrim(p.parent_product_code)) AND l.category_id IS NOT NULL);
END $$;
REVOKE ALL ON FUNCTION public.sf_product_category_edit_locked(integer) FROM PUBLIC;
GRANT EXECUTE ON FUNCTION public.sf_product_category_edit_locked(integer) TO stockflow_runtime;

CREATE OR REPLACE FUNCTION public.sf_protect_product_edit() RETURNS trigger
 LANGUAGE plpgsql SECURITY DEFINER SET search_path=pg_catalog,public,pg_temp AS $$
DECLARE vid bigint; used boolean;
BEGIN
 IF TG_TABLE_NAME='products' THEN
  IF NEW.parent_product_code IS DISTINCT FROM OLD.parent_product_code THEN
   IF upper(btrim(NEW.parent_product_code))=upper(btrim(OLD.parent_product_code)) THEN NEW.parent_product_code:=OLD.parent_product_code;
   ELSE RAISE EXCEPTION 'Saved Product Codes are permanent; use a reviewed correction' USING ERRCODE='23514'; END IF;
  END IF;
  IF NEW.unit_of_measure IS NOT DISTINCT FROM OLD.unit_of_measure AND NEW.is_serial_tracked IS NOT DISTINCT FROM OLD.is_serial_tracked AND NEW.has_variants IS NOT DISTINCT FROM OLD.has_variants THEN RETURN NEW; END IF;
 ELSE
  vid:=OLD.variant_id;
  -- 067 normalizes identifiers on writes. Restore the exact grandfathered spelling when
  -- that normalization is the only difference; its earlier guard still rejects relabeling.
  IF NEW.sku IS DISTINCT FROM OLD.sku AND upper(btrim(NEW.sku))=upper(btrim(OLD.sku)) THEN NEW.sku:=OLD.sku; END IF;
  -- The default variant is initialized inside the product's original registration transaction.
  IF NEW.sku IS DISTINCT FROM OLD.sku AND NOT(OLD.sku='LEGACY-'||OLD.product_id::text AND EXISTS(SELECT 1 FROM public.catalog_identifier_ledger WHERE tenant_id=OLD.tenant_id AND variant_id=OLD.variant_id AND code=OLD.sku AND created_xid=pg_current_xact_id())) THEN RAISE EXCEPTION 'Saved SKUs are permanent; add a new variant' USING ERRCODE='23514'; END IF;
  IF OLD.is_active AND NOT NEW.is_active AND (
   EXISTS(SELECT 1 FROM public.inventory_transactions WHERE tenant_id=OLD.tenant_id AND variant_id=vid GROUP BY location_id HAVING SUM(CASE WHEN transaction_type IN('PURCHASE','CUSTOMER_RETURN','ADJUSTMENT_IN','SERVICE_IN','TRANSFER_IN') THEN quantity::bigint WHEN transaction_type IN('SALE','SUPPLIER_RETURN','DAMAGED','ADJUSTMENT_OUT','SERVICE_OUT','WARRANTY_REPLACEMENT','TRANSFER_OUT','TRANSFER_LOSS') THEN -quantity::bigint ELSE 0 END)<>0)
   OR EXISTS(SELECT 1 FROM public.purchase_items i JOIN public.purchases p USING(tenant_id,purchase_id) WHERE i.tenant_id=OLD.tenant_id AND i.variant_id=vid AND i.received_quantity+i.damaged_quantity<i.quantity AND p.status NOT IN('CANCELLED','COMPLETED','RETURNED'))
   OR EXISTS(SELECT 1 FROM public.sale_items i JOIN public.sale_fulfilments f USING(tenant_id,sale_id) WHERE i.tenant_id=OLD.tenant_id AND i.variant_id=vid AND f.status NOT IN('COMPLETED','CANCELLED'))
   OR EXISTS(SELECT 1 FROM public.stock_transfer_items i JOIN public.stock_transfers t USING(tenant_id,transfer_id) WHERE i.tenant_id=OLD.tenant_id AND i.variant_id=vid AND t.status NOT IN('RECEIVED','CANCELLED')))
  THEN RAISE EXCEPTION 'Resolve remaining stock, open purchases, sales fulfilments or transfers before deactivating this variant' USING ERRCODE='23514'; END IF;
  IF NEW.pack_quantity IS NOT DISTINCT FROM OLD.pack_quantity AND NEW.is_serial_tracked IS NOT DISTINCT FROM OLD.is_serial_tracked THEN RETURN NEW; END IF;
 END IF;
 used:=EXISTS(SELECT 1 FROM public.inventory_transactions WHERE tenant_id=OLD.tenant_id AND product_id=OLD.product_id AND (vid IS NULL OR variant_id=vid))
 OR EXISTS(SELECT 1 FROM public.purchase_items WHERE tenant_id=OLD.tenant_id AND product_id=OLD.product_id AND (vid IS NULL OR variant_id=vid))
 OR EXISTS(SELECT 1 FROM public.sale_items WHERE tenant_id=OLD.tenant_id AND product_id=OLD.product_id AND (vid IS NULL OR variant_id=vid))
 OR EXISTS(SELECT 1 FROM public.product_units WHERE tenant_id=OLD.tenant_id AND product_id=OLD.product_id AND (vid IS NULL OR variant_id=vid))
 OR EXISTS(SELECT 1 FROM public.stock_count_items WHERE tenant_id=OLD.tenant_id AND product_id=OLD.product_id AND (vid IS NULL OR variant_id=vid))
 OR EXISTS(SELECT 1 FROM public.stock_transfer_items i JOIN public.product_variants v USING(tenant_id,variant_id) WHERE v.tenant_id=OLD.tenant_id AND v.product_id=OLD.product_id AND (vid IS NULL OR v.variant_id=vid));
 IF used THEN RAISE EXCEPTION 'Stock unit, tracking method, variant structure and pack conversion cannot change after stock or transaction history; use an authorized inventory correction' USING ERRCODE='23514'; END IF;
 RETURN NEW;
END $$;
DROP TRIGGER IF EXISTS product_edit_integrity ON public.products;
CREATE TRIGGER product_edit_integrity BEFORE UPDATE ON public.products FOR EACH ROW EXECUTE FUNCTION public.sf_protect_product_edit();
DROP TRIGGER IF EXISTS variant_edit_integrity ON public.product_variants;
CREATE TRIGGER variant_edit_integrity BEFORE UPDATE ON public.product_variants FOR EACH ROW EXECUTE FUNCTION public.sf_protect_product_edit();
REVOKE ALL ON FUNCTION public.sf_protect_product_edit() FROM PUBLIC;
COMMIT;
