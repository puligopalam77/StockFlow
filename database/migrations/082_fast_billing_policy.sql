-- MANUAL ONLY, after 081. Back up and stop app instances; run ENTIRE file as postgres.
-- Extends the existing policy/approval infrastructure. Fast Billing remains OFF.
BEGIN;
SET LOCAL lock_timeout='15s';
DO $$ BEGIN
 IF NOT EXISTS(SELECT 1 FROM pg_roles WHERE rolname=current_user AND rolsuper) THEN RAISE EXCEPTION 'Run as PostgreSQL superuser'; END IF;
 IF NOT EXISTS(SELECT 1 FROM public.module_actions a JOIN public.modules m USING(module_id) WHERE m.module_code='SALES' AND a.action_code='TAX_OVERRIDE') THEN RAISE EXCEPTION 'Apply 081 first'; END IF;
END $$;
ALTER TABLE public.billing_policy ADD COLUMN store_id bigint NOT NULL DEFAULT 0,
 ADD COLUMN version bigint NOT NULL DEFAULT 1 CHECK(version>0),
 ADD COLUMN fast_billing_enabled boolean NOT NULL DEFAULT false,
 ADD COLUMN allow_routine_discounts boolean NOT NULL DEFAULT true,
 ADD COLUMN require_routine_reason boolean NOT NULL DEFAULT true,
 ADD COLUMN require_exceptional_reason boolean NOT NULL DEFAULT true,
 ADD COLUMN require_approval_above_limit boolean NOT NULL DEFAULT true;
ALTER TABLE public.billing_policy DROP CONSTRAINT billing_policy_pkey;
ALTER TABLE public.billing_policy ADD PRIMARY KEY(tenant_id,store_id), ADD CHECK(store_id>=0);
-- store_id=0 retains the saved tenant policy as an immutable inheritance fallback.
CREATE POLICY billing_policy_shop ON public.billing_policy AS RESTRICTIVE
 USING(store_id=0 OR store_id=public.sf_store())
 WITH CHECK(store_id>0 AND store_id=public.sf_store() AND public.sf_transfer_permission('COMPANY_SETTINGS','EDIT'));
CREATE FUNCTION public.sf_billing_policy_guard() RETURNS trigger LANGUAGE plpgsql SET search_path=public,pg_temp AS $$
BEGIN
 IF NEW.store_id>0 AND NOT EXISTS(SELECT 1 FROM public.stores WHERE tenant_id=NEW.tenant_id AND store_id=NEW.store_id) THEN RAISE EXCEPTION 'Invalid policy shop' USING ERRCODE='23514'; END IF;
 IF TG_OP='UPDATE' THEN
  IF NEW.tenant_id<>OLD.tenant_id OR NEW.store_id<>OLD.store_id THEN RAISE EXCEPTION 'Policy shop cannot change' USING ERRCODE='23514'; END IF;
  NEW.version:=OLD.version+1;
 END IF;
 RETURN NEW;
END $$;
CREATE TRIGGER billing_policy_version BEFORE INSERT OR UPDATE ON public.billing_policy FOR EACH ROW EXECUTE FUNCTION public.sf_billing_policy_guard();
CREATE FUNCTION public.sf_billing_policy() RETURNS TABLE("DiscountLimitPercent" numeric,"StoreId" bigint,"Version" bigint,"FastBillingEnabled" boolean,"AllowRoutineDiscounts" boolean,"RequireRoutineReason" boolean,"RequireExceptionalReason" boolean,"RequireApprovalAboveLimit" boolean)
 LANGUAGE plpgsql SECURITY INVOKER SET search_path=public,pg_temp AS $$
BEGIN
 IF public.sf_store()<=0 THEN RAISE EXCEPTION 'Choose one active shop for billing policy' USING ERRCODE='23514'; END IF;
 RETURN QUERY SELECT p.discount_limit_percent,p.store_id,p.version,p.fast_billing_enabled,p.allow_routine_discounts,p.require_routine_reason,p.require_exceptional_reason,p.require_approval_above_limit
 FROM public.billing_policy p WHERE p.tenant_id=public.sf_tenant() AND p.store_id IN(0,public.sf_store()) ORDER BY p.store_id DESC LIMIT 1;
 IF NOT FOUND THEN RETURN QUERY SELECT 10::numeric,0::bigint,1::bigint,false,true,true,true,true; END IF;
END $$;
REVOKE ALL ON FUNCTION public.sf_billing_policy() FROM PUBLIC;
GRANT EXECUTE ON FUNCTION public.sf_billing_policy() TO stockflow_runtime;
ALTER TABLE public.billing_approvals ADD COLUMN rejected_by integer, ADD COLUMN rejected_at timestamptz,
 ADD COLUMN financial_snapshot jsonb,
 ADD FOREIGN KEY(tenant_id,rejected_by) REFERENCES public.users(tenant_id,user_id);
-- The requester cannot approve their own request, even with approval permission.
CREATE FUNCTION public.sf_billing_approval_guard() RETURNS trigger LANGUAGE plpgsql SET search_path=public,pg_temp AS $$
BEGIN
 IF NEW.approved_by IS NOT NULL AND (NEW.approved_by=NEW.requested_by OR NEW.approved_by<>public.sf_actor() OR NOT public.sf_transfer_permission('SALES','DISCOUNT_APPROVE')) THEN
  RAISE EXCEPTION 'An authorized different manager must approve this request' USING ERRCODE='42501';
 END IF;
 IF NEW.rejected_by IS NOT NULL AND (NEW.rejected_by=NEW.requested_by OR NEW.rejected_by<>public.sf_actor() OR NOT public.sf_transfer_permission('SALES','DISCOUNT_APPROVE')) THEN
  RAISE EXCEPTION 'An authorized different manager must reject this request' USING ERRCODE='42501';
 END IF;
 RETURN NEW;
END $$;
CREATE TRIGGER billing_approval_authority BEFORE INSERT OR UPDATE ON public.billing_approvals FOR EACH ROW EXECUTE FUNCTION public.sf_billing_approval_guard();
-- Extend the existing profile guard to recognize transient manual lines in
-- held bills/approval summaries, without skipping catalog/profile checks.
DO $$ DECLARE definition text; needle text; replacement text; BEGIN
 definition:=replace(pg_get_functiondef('public.sf_profile_posting_guard()'::regprocedure),E'\r','');
 needle:='   SELECT to_jsonb(p) INTO item FROM public.products p WHERE p.tenant_id=NEW.tenant_id
    AND p.product_id=COALESCE(draft_item->>''productId'',draft_item->>''ProductId'')::integer;';
 replacement:=$body$
   IF jsonb_typeof(coalesce(draft_item->'manualItem',draft_item->'ManualItem'))='object' THEN
    DECLARE manual jsonb:=coalesce(draft_item->'manualItem',draft_item->'ManualItem'); rule bigint; BEGIN
     IF coalesce(draft_item->>'productId',draft_item->>'ProductId','0')::integer<>0
      OR coalesce(draft_item->>'variantId',draft_item->>'VariantId',draft_item->>'productUnitId',draft_item->>'ProductUnitId') IS NOT NULL THEN
      RAISE EXCEPTION 'Manual item cannot use catalog identity' USING ERRCODE='23514';
     END IF;
     rule:=coalesce(manual->>'taxRuleId',manual->>'TaxRuleId','0')::bigint;
     IF rule<>0 AND NOT EXISTS(SELECT 1 FROM public.gst_tax_rules r WHERE r.tenant_id=NEW.tenant_id AND r.store_id=public.sf_store()
      AND r.business_profile_code=selected AND r.rule_id=rule AND r.review_status='REVIEWED' AND r.status='ACTIVE'
      AND CURRENT_DATE BETWEEN r.effective_from AND coalesce(r.effective_to,'infinity')) THEN
      RAISE EXCEPTION 'Manual tax classification belongs to another shop or needs review' USING ERRCODE='23514';
     END IF;
    END;
    CONTINUE;
   END IF;
$body$||needle;
 IF strpos(definition,needle)=0 THEN RAISE EXCEPTION 'Existing profile guard changed; review migration 082 before applying'; END IF;
 EXECUTE replace(definition,needle,replacement);
END $$;
COMMIT;
