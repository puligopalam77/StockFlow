-- DEPLOYMENT REVIEW CANDIDATE. NOT EXECUTED OR APPROVED FOR EXECUTION.
-- Outside automatic migration discovery. See REVIEW.txt; do not run local 093 here.
BEGIN;
SET LOCAL lock_timeout='5s';
SET LOCAL statement_timeout='60s';
SET LOCAL search_path=pg_catalog,public,pg_temp;
DO $$ DECLARE required text; actual text; BEGIN
 IF current_setting('stockflow.bank_deployment_approved',true) IS DISTINCT FROM 'bank-accounts-jan2027-review-v1'
 OR current_setting('stockflow.bank_schema_contract_reviewed',true) IS DISTINCT FROM 'yes'
 THEN RAISE EXCEPTION 'Separate target-specific deployment and schema review approval required'; END IF;
 IF NOT EXISTS(SELECT 1 FROM pg_roles WHERE rolname=current_user AND rolsuper)
 THEN RAISE EXCEPTION 'Approved maintenance superuser session required'; END IF;
 FOREACH required IN ARRAY ARRAY['database','server_address','server_port','system_identifier','maintenance_role'] LOOP
  actual:=CASE required WHEN 'database' THEN current_database()
   WHEN 'server_address' THEN host(inet_server_addr()) WHEN 'server_port' THEN inet_server_port()::text
   WHEN 'system_identifier' THEN (SELECT system_identifier::text FROM pg_control_system())
   WHEN 'maintenance_role' THEN current_user END;
  IF coalesce(current_setting('stockflow.bank_expected_'||required,true),'')=''
  OR current_setting('stockflow.bank_expected_'||required,true) IS DISTINCT FROM actual
  THEN RAISE EXCEPTION 'Deployment target identity mismatch: %',required; END IF;
 END LOOP;
 IF NOT EXISTS(SELECT 1 FROM pg_roles WHERE rolname='stockflow_runtime' AND NOT rolsuper AND NOT rolbypassrls)
 OR has_schema_privilege('stockflow_runtime','public','CREATE')
 THEN RAISE EXCEPTION 'Review runtime role and public schema CREATE restrictions'; END IF;
 IF to_regprocedure('public.sf_snapshot_retail_variant()') IS NULL
 OR to_regprocedure('public.sf_cash_entry_reason_guard()') IS NULL
 THEN RAISE EXCEPTION 'Verify genuine 049 contract and prerequisites through 089'; END IF;
 -- Existence is necessary, not proof of compatible semantics. External reviewed
 -- contract/type/hash/privilege evidence is required before the approval above.
 FOREACH required IN ARRAY ARRAY['sf_tenant()','sf_actor()','sf_store()','sf_location()',
  'sf_owner()','sf_shop_member(bigint)','sf_can_location(bigint)',
  'sf_visible_business_profile()','sf_transfer_permission(text,text)'] LOOP
  IF to_regprocedure('public.'||required) IS NULL THEN RAISE EXCEPTION 'Required helper missing: %',required; END IF;
 END LOOP;
END $$;
DO $$ BEGIN
 IF EXISTS(SELECT 1 FROM pg_class c JOIN pg_namespace n ON n.oid=c.relnamespace
  WHERE n.nspname='public' AND c.relname IN('bank_accounts','bank_account_secrets',
  'bank_account_shops','bank_openings','bank_payment_links','bank_payment_events',
  'bank_movements','bank_movement_allocations','bank_account_events',
  'bank_release_control','bank_requests'))
 OR EXISTS(SELECT 1 FROM pg_proc p JOIN pg_namespace n ON n.oid=p.pronamespace
  WHERE n.nspname='public' AND p.proname IN('sf_bank_review_immutable','sf_bank_allowed','sf_bank_sources','sf_bank_read','sf_bank_write'))
 OR EXISTS(SELECT 1 FROM public.modules WHERE module_code='BANK_ACCOUNTS')
 THEN RAISE EXCEPTION 'Existing bank objects/metadata: stop for a reviewed incremental upgrade; never drop/reapply'; END IF;
END $$;
CREATE TABLE public.bank_accounts (
 tenant_id bigint NOT NULL, account_id uuid NOT NULL,
 bank_name varchar(120) NOT NULL CHECK(length(btrim(bank_name)) BETWEEN 2 AND 120),
 display_name varchar(120) NOT NULL CHECK(length(btrim(display_name)) BETWEEN 2 AND 120),
 holder_name varchar(150) NOT NULL CHECK(length(btrim(holder_name)) BETWEEN 2 AND 150),
 account_type text NOT NULL CHECK(account_type IN('CURRENT','SAVINGS','OTHER')),
 country_code text NOT NULL DEFAULT 'IN' CHECK(country_code='IN'),
 currency text NOT NULL DEFAULT 'INR' CHECK(currency='INR'),
 account_last_four varchar(4) NOT NULL CHECK(account_last_four ~ '^[0-9]{4}$'),
 ifsc varchar(11) NOT NULL CHECK(ifsc ~ '^[A-Z]{4}0[A-Z0-9]{6}$'),
 is_active boolean NOT NULL DEFAULT true, version bigint NOT NULL DEFAULT 1 CHECK(version>0),
 created_by integer NOT NULL, created_at timestamptz NOT NULL DEFAULT clock_timestamp(),
 PRIMARY KEY(tenant_id,account_id),
 FOREIGN KEY(tenant_id) REFERENCES public.tenants(tenant_id),
 FOREIGN KEY(tenant_id,created_by) REFERENCES public.users(tenant_id,user_id)
);

-- Dedicated sensitive storage: never return this row through ordinary list DTOs.
-- Ciphertext must use durable protected keys; keyed fingerprint is NOT a plain hash.
-- Stable fingerprint identity is tenant + normalized bank identity + account number;
-- IFSC/display name edits must not create a new account identity.
CREATE TABLE public.bank_account_secrets (
 tenant_id bigint NOT NULL, account_id uuid NOT NULL,
 protected_account_number text NOT NULL CHECK(length(protected_account_number)>0),
 protected_upi_id text, identity_fingerprint bytea NOT NULL CHECK(octet_length(identity_fingerprint)=32),
 fingerprint_key_version integer NOT NULL CHECK(fingerprint_key_version>0),
 protection_version integer NOT NULL CHECK(protection_version>0),
 PRIMARY KEY(tenant_id,account_id), UNIQUE(tenant_id,identity_fingerprint),
 FOREIGN KEY(tenant_id,account_id) REFERENCES public.bank_accounts(tenant_id,account_id)
);

CREATE TABLE public.bank_account_shops (
 tenant_id bigint NOT NULL, account_id uuid NOT NULL, store_id bigint NOT NULL,
 is_active boolean NOT NULL DEFAULT true, is_default_receiving boolean NOT NULL DEFAULT false,
 version bigint NOT NULL DEFAULT 1 CHECK(version>0),
 PRIMARY KEY(tenant_id,account_id,store_id), CHECK(NOT is_default_receiving OR is_active),
 FOREIGN KEY(tenant_id,account_id) REFERENCES public.bank_accounts(tenant_id,account_id),
 FOREIGN KEY(tenant_id,store_id) REFERENCES public.stores(tenant_id,store_id)
);
CREATE UNIQUE INDEX bank_one_default_per_shop ON public.bank_account_shops(tenant_id,store_id)
 WHERE is_active AND is_default_receiving;

-- One declared tracking boundary per ACCOUNT, never per shop or per day.
-- Uninitialized is absence of this row, not an assumed zero.
-- The declared boundary is not a fabricated bank transaction time. Date-only
-- same-day evidence requires explicit boundary review before future posting.
CREATE TABLE public.bank_openings (
 tenant_id bigint NOT NULL, account_id uuid NOT NULL, opening_id uuid NOT NULL,
 amount numeric(14,2) NOT NULL CHECK(amount>=0 AND amount<=9999999999.99),
 tracking_started_at timestamptz NOT NULL,
 evidence_description varchar(1000) NOT NULL CHECK(length(btrim(evidence_description)) BETWEEN 10 AND 1000),
 bank_reference varchar(150) NOT NULL CHECK(length(btrim(bank_reference)) BETWEEN 3 AND 150),
 request_id uuid NOT NULL, request_fingerprint bytea NOT NULL CHECK(octet_length(request_fingerprint)=32),
 initialized_by integer NOT NULL, initialized_at timestamptz NOT NULL DEFAULT clock_timestamp(),
 PRIMARY KEY(tenant_id,account_id), UNIQUE(tenant_id,opening_id), UNIQUE(tenant_id,request_id),
 FOREIGN KEY(tenant_id,account_id) REFERENCES public.bank_accounts(tenant_id,account_id),
 FOREIGN KEY(tenant_id,initialized_by) REFERENCES public.users(tenant_id,user_id)
);

-- Destination attribution only. This is NOT another customer/supplier payment.
-- Source keys support both bigint payment IDs and UUID expense-payment IDs.
-- Source existence, amount, method, direction, profile, store/location ownership
-- MUST be resolved from the real source in the future authorized writer, not
-- trusted from these client fields. No writer is supplied/activated in this draft.
CREATE TABLE public.bank_payment_links (
 tenant_id bigint NOT NULL, link_id uuid NOT NULL, account_id uuid NOT NULL,
 store_id bigint NOT NULL, location_id bigint NOT NULL, business_profile_code varchar(40) NOT NULL,
 source_kind text NOT NULL CHECK(source_kind IN('SALE_PAYMENT','SERVICE_PAYMENT','SUPPLIER_PAYMENT','SALE_REFUND','EXPENSE_PAYMENT')),
 source_key varchar(64) NOT NULL CHECK(length(btrim(source_key)) BETWEEN 1 AND 64),
 source_direction text NOT NULL CHECK(source_direction IN('IN','OUT')),
 source_amount numeric(14,2) NOT NULL CHECK(source_amount>0 AND source_amount<=9999999999.99),
 payment_mode text NOT NULL CHECK(payment_mode IN('UPI','CARD','DEBIT_CARD','CREDIT_CARD','BANK','BANK_TRANSFER','CHEQUE')),
 source_date date NOT NULL, source_occurred_at timestamptz,
 recorded_by integer NOT NULL, recorded_at timestamptz NOT NULL DEFAULT clock_timestamp(),
 request_id uuid NOT NULL, request_fingerprint bytea NOT NULL CHECK(octet_length(request_fingerprint)=32),
 PRIMARY KEY(tenant_id,link_id), UNIQUE(tenant_id,account_id,link_id),
 UNIQUE(tenant_id,source_kind,source_key), UNIQUE(tenant_id,request_id),
 FOREIGN KEY(tenant_id,account_id,store_id) REFERENCES public.bank_account_shops(tenant_id,account_id,store_id),
 FOREIGN KEY(tenant_id,location_id) REFERENCES public.store_locations(tenant_id,location_id),
 FOREIGN KEY(tenant_id,recorded_by) REFERENCES public.users(tenant_id,user_id)
);
CREATE INDEX bank_link_shop_date ON public.bank_payment_links(tenant_id,store_id,location_id,business_profile_code,source_date,link_id);

-- Append-only evidence/state events do not themselves move bank money.
-- Every event is linked to a real, authorized source; no historical import occurs.
CREATE TABLE public.bank_payment_events (
 tenant_id bigint NOT NULL, event_id uuid NOT NULL, link_id uuid NOT NULL,
 event_type text NOT NULL CHECK(event_type IN('REPORTED_UNVERIFIED','REPORTED_FAILED','REPORTED_CANCELLED','BOUNDARY_INCLUDED_IN_OPENING','BOUNDARY_AFTER_OPENING')),
 reason varchar(1000) NOT NULL CHECK(length(btrim(reason)) BETWEEN 3 AND 1000),
 request_id uuid NOT NULL, request_fingerprint bytea NOT NULL CHECK(octet_length(request_fingerprint)=32),
 actor_id integer NOT NULL, created_at timestamptz NOT NULL DEFAULT clock_timestamp(),
 PRIMARY KEY(tenant_id,event_id), UNIQUE(tenant_id,request_id),
 FOREIGN KEY(tenant_id,link_id) REFERENCES public.bank_payment_links(tenant_id,link_id),
 FOREIGN KEY(tenant_id,actor_id) REFERENCES public.users(tenant_id,user_id)
);

-- Only manually verified POSTED bank events belong here. Pending/reported payments
-- remain above and contribute zero to Recorded Bank Balance. No Actual Balance.
-- One statement credit/debit can allocate to several sources/shops; this full
-- bank-event amount is owner-only. Shop admins receive allocation projections.
CREATE TABLE public.bank_movements (
 tenant_id bigint NOT NULL, movement_id uuid NOT NULL, account_id uuid NOT NULL,
 posting_order bigint GENERATED ALWAYS AS IDENTITY,
 entry_kind text NOT NULL CHECK(entry_kind IN('SETTLEMENT','VERIFICATION_REVERSAL')),
 signed_amount numeric(14,2) NOT NULL CHECK(signed_amount<>0 AND abs(signed_amount)<=9999999999.99),
 effective_date date NOT NULL, effective_at timestamptz,
 bank_reference varchar(150) NOT NULL CHECK(length(btrim(bank_reference)) BETWEEN 3 AND 150),
 normalized_bank_reference varchar(150) NOT NULL CHECK(length(btrim(normalized_bank_reference)) BETWEEN 3 AND 150),
 verification_method text NOT NULL DEFAULT 'MANUAL' CHECK(verification_method='MANUAL'),
 verification_label text NOT NULL DEFAULT 'Manually Verified' CHECK(verification_label='Manually Verified'),
 evidence_description varchar(1000) NOT NULL CHECK(length(btrim(evidence_description)) BETWEEN 10 AND 1000),
 verified_by integer NOT NULL, verified_at timestamptz NOT NULL DEFAULT clock_timestamp(),
 reverses_id uuid, request_id uuid NOT NULL,
 request_fingerprint bytea NOT NULL CHECK(octet_length(request_fingerprint)=32),
 PRIMARY KEY(tenant_id,movement_id), UNIQUE(tenant_id,account_id,movement_id),
 UNIQUE(tenant_id,request_id), UNIQUE(tenant_id,reverses_id), UNIQUE(posting_order),
 CHECK((entry_kind='VERIFICATION_REVERSAL')=(reverses_id IS NOT NULL)),
 CHECK(reverses_id IS NULL OR reverses_id<>movement_id),
 FOREIGN KEY(tenant_id,account_id) REFERENCES public.bank_openings(tenant_id,account_id),
 FOREIGN KEY(tenant_id,account_id,reverses_id) REFERENCES public.bank_movements(tenant_id,account_id,movement_id),
 FOREIGN KEY(tenant_id,verified_by) REFERENCES public.users(tenant_id,user_id)
);
CREATE UNIQUE INDEX bank_unique_evidenced_posting ON public.bank_movements(tenant_id,account_id,normalized_bank_reference)
 WHERE entry_kind='SETTLEMENT';
CREATE INDEX bank_movement_chronology ON public.bank_movements(tenant_id,account_id,effective_date,posting_order);

CREATE TABLE public.bank_movement_allocations (
 tenant_id bigint NOT NULL, account_id uuid NOT NULL, movement_id uuid NOT NULL, link_id uuid NOT NULL,
 signed_amount numeric(14,2) NOT NULL CHECK(signed_amount<>0 AND abs(signed_amount)<=9999999999.99),
 PRIMARY KEY(tenant_id,movement_id,link_id),
 FOREIGN KEY(tenant_id,account_id,movement_id) REFERENCES public.bank_movements(tenant_id,account_id,movement_id),
 FOREIGN KEY(tenant_id,account_id,link_id) REFERENCES public.bank_payment_links(tenant_id,account_id,link_id)
);
CREATE INDEX bank_allocation_source ON public.bank_movement_allocations(tenant_id,link_id,movement_id);

-- Redacted configuration history; no account plaintext, ciphertext or full request
-- body here. The future configuration writer must append an audit atomically.
CREATE TABLE public.bank_account_events (
 tenant_id bigint NOT NULL, event_id uuid NOT NULL, account_id uuid NOT NULL,
 action text NOT NULL CHECK(action IN('CREATE','EDIT','ASSIGN_SHOP','REMOVE_SHOP','SET_DEFAULT','DEACTIVATE','REACTIVATE')),
 store_id bigint, version bigint NOT NULL CHECK(version>0),
 redacted_changes jsonb NOT NULL CHECK(jsonb_typeof(redacted_changes)='object'),
 reason varchar(1000) NOT NULL CHECK(length(btrim(reason)) BETWEEN 3 AND 1000),
 request_id uuid NOT NULL, request_fingerprint bytea NOT NULL CHECK(octet_length(request_fingerprint)=32),
 actor_id integer NOT NULL, created_at timestamptz NOT NULL DEFAULT clock_timestamp(),
 PRIMARY KEY(tenant_id,event_id), UNIQUE(tenant_id,request_id),
 FOREIGN KEY(tenant_id,account_id) REFERENCES public.bank_accounts(tenant_id,account_id),
 FOREIGN KEY(tenant_id,store_id) REFERENCES public.stores(tenant_id,store_id),
 FOREIGN KEY(tenant_id,actor_id) REFERENCES public.users(tenant_id,user_id)
);

CREATE FUNCTION public.sf_bank_review_immutable() RETURNS trigger
 LANGUAGE plpgsql SET search_path=pg_catalog,public,pg_temp AS $$
BEGIN RAISE EXCEPTION 'Bank financial and audit history is append-only' USING ERRCODE='42501'; END $$;
REVOKE ALL ON FUNCTION public.sf_bank_review_immutable() FROM PUBLIC,stockflow_runtime;

-- Fail closed even if deployment default privileges would otherwise grant access.
-- No permissive RLS policies, grants, callable writers or app activation in this draft.
DO $$ DECLARE tab text; BEGIN
 FOREACH tab IN ARRAY ARRAY['bank_accounts','bank_account_secrets','bank_account_shops','bank_openings','bank_payment_links','bank_payment_events','bank_movements','bank_movement_allocations','bank_account_events'] LOOP
  EXECUTE format('ALTER TABLE public.%I ENABLE ROW LEVEL SECURITY',tab);
  EXECUTE format('ALTER TABLE public.%I FORCE ROW LEVEL SECURITY',tab);
  EXECUTE format('REVOKE ALL ON TABLE public.%I FROM PUBLIC,stockflow_runtime',tab);
 END LOOP;
 FOREACH tab IN ARRAY ARRAY['bank_openings','bank_payment_links','bank_payment_events','bank_movements','bank_movement_allocations','bank_account_events'] LOOP
  EXECUTE format('CREATE TRIGGER bank_history_immutable BEFORE UPDATE OR DELETE ON public.%I FOR EACH ROW EXECUTE FUNCTION public.sf_bank_review_immutable()',tab);
 END LOOP;
END $$;
REVOKE ALL ON SEQUENCE public.bank_movements_posting_order_seq FROM PUBLIC,stockflow_runtime;
COMMIT;
