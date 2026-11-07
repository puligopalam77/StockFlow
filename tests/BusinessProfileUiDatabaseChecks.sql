-- Isolated synthetic fixture only. Never run on an application database.
DO $$ BEGIN IF current_database() NOT LIKE 'profile_ui_fixture_%' THEN RAISE EXCEPTION 'Disposable profile UI fixture required'; END IF; END $$;
SET app.tenant_id='1'; SET app.user_id='1'; SET app.store_id='1'; SET app.location_id='1'; SET app.business_profile_code='ELECTRICAL';
CREATE TEMP TABLE original_profile_rows(tab text,doc jsonb);
CREATE TEMP TABLE original_profile_counts(tab text,rows bigint);
DO $$ DECLARE tab text; BEGIN
 FOREACH tab IN ARRAY ARRAY['products','product_variants','variant_barcodes','product_units','categories','brands','brand_categories','customers','suppliers','variant_supplier_identifiers','inventory_transactions','sales','sale_items','payments','purchases','purchase_items','purchase_receipts','purchase_receipt_items','stock_intakes','stock_intake_items','stock_transfers','stock_transfer_items','stock_counts','stock_count_items','sale_returns','sale_return_items','purchase_returns','purchase_return_items','supplier_payments','service_requests','service_parts','service_history','service_operations','service_payments','warranties','warranty_claims','warranty_events','warranty_rmas','sale_fulfilments','trade_ins','cash_closings','billing_drafts','billing_approvals','quotations','quotation_revisions','expenses','expense_payments','expense_events','replenishment_policies','users','roles','user_action_permissions','role_action_permissions'] LOOP
  IF to_regclass('public.'||tab) IS NOT NULL THEN
   EXECUTE format('INSERT INTO original_profile_rows SELECT %L,to_jsonb(r) FROM public.%I r',tab,tab);
   EXECUTE format('INSERT INTO original_profile_counts SELECT %L,count(*) FROM public.%I',tab,tab);
  END IF;
 END LOOP;
END $$;
CREATE TEMP TABLE initial_profile AS SELECT business_profile_code code,business_profile_version version FROM public.tenants WHERE tenant_id=1;
SET ROLE stockflow_runtime;
SELECT public.sf_change_business_profile('BIKE_ACCESSORIES',(SELECT business_profile_version FROM public.tenants WHERE tenant_id=1),'Synthetic profile UI preservation check',true);
DO $$ DECLARE v bigint; BEGIN
 SELECT business_profile_version INTO v FROM public.tenants WHERE tenant_id=1;
 BEGIN PERFORM public.sf_change_business_profile('ELECTRICAL',v-1,'Stale synthetic request',true);RAISE EXCEPTION 'Stale version unexpectedly accepted'; EXCEPTION WHEN check_violation THEN NULL; END;
 BEGIN PERFORM public.sf_change_business_profile('GROCERY',v,'Unsupported synthetic request',true);RAISE EXCEPTION 'Invalid profile unexpectedly accepted'; EXCEPTION WHEN check_violation THEN NULL; END;
END $$;
SELECT public.sf_change_business_profile('ELECTRICAL',(SELECT business_profile_version FROM public.tenants WHERE tenant_id=1),'Return synthetic profile to original setup',true);
SET app.tenant_id='999999999';
DO $$ BEGIN BEGIN PERFORM public.sf_change_business_profile('BIKE_ACCESSORIES',1,'Forged tenant synthetic request',true);RAISE EXCEPTION 'Foreign tenant unexpectedly accepted'; EXCEPTION WHEN insufficient_privilege THEN NULL; END; END $$;
RESET ROLE; SET app.tenant_id='1';
DO $$ DECLARE entry record; missing bigint; actual bigint; BEGIN
 FOR entry IN SELECT tab,rows FROM original_profile_counts LOOP
  EXECUTE format('SELECT count(*) FROM original_profile_rows o WHERE o.tab=%L AND NOT EXISTS(SELECT 1 FROM public.%I r WHERE to_jsonb(r)=o.doc)',entry.tab,entry.tab) INTO missing;
  IF missing<>0 THEN RAISE EXCEPTION 'Historical rows changed in %',entry.tab; END IF;
  EXECUTE format('SELECT count(*) FROM public.%I',entry.tab) INTO actual;
  IF entry.tab<>'categories' AND actual<>entry.rows THEN RAISE EXCEPTION 'Unintended business writes to %',entry.tab; END IF;
 END LOOP;
 IF (SELECT business_profile_code FROM public.tenants WHERE tenant_id=1)<>'ELECTRICAL' THEN RAISE EXCEPTION 'Profile did not return'; END IF;
 IF (SELECT business_profile_version FROM public.tenants WHERE tenant_id=1)<>(SELECT version+2 FROM initial_profile) THEN RAISE EXCEPTION 'Unexpected profile version'; END IF;
END $$;
SELECT 'PASS: runtime profile switch/return, stale and unsupported rejection, forged tenant denial; original business/master/stock/payment/permission rows unchanged' result, count(*) tables_checked FROM original_profile_counts;
