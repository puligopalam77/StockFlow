# File generation only: no database client, connection, migration or activation.
$ErrorActionPreference='Stop'
$sfRoot=[IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../../../..'))
function Source([string]$path){[IO.File]::ReadAllText((Join-Path $sfRoot $path))}
function WriteReview([string]$name,[string]$body){[IO.File]::WriteAllText((Join-Path $PSScriptRoot $name),$body,[Text.UTF8Encoding]::new($false))}
function Section([string]$body,[string]$start,[string]$end){
 $sfFirst=$body.IndexOf($start,[StringComparison]::Ordinal)
 $sfLast=$body.IndexOf($end,$sfFirst,[StringComparison]::Ordinal)
 if($sfFirst -lt 0 -or $sfLast -le $sfFirst){throw ('Cannot locate source section '+$start)}
 $body.Substring($sfFirst,$sfLast-$sfFirst).Trim()
}
$sf90=Source 'database/review/bank-accounts/090_bank_accounts_schema.sql'
$sf91=Source 'database/review/bank-accounts/091_bank_permissions.sql'
$sf92=Source 'database/review/bank-accounts/092_bank_development_workflows.sql'
$sf93=Source 'database/review/bank-accounts/093_bank_january_policy.sql'
$sfGuard=@'
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
'@
$sfFoundation=Section $sf90 'CREATE TABLE public.bank_accounts (' 'COMMIT;'
$sfFresh=@'
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
'@
WriteReview '10_bank_foundation.sql' ($sfGuard+"`n"+$sfFresh+"`n"+$sfFoundation+"`nCOMMIT;`n")
$sfPermission=Section $sf91 'INSERT INTO public.modules(' 'COMMIT;'
$sfBeforePermission=@'
DO $$ BEGIN
 IF to_regclass('public.bank_movement_allocations') IS NULL
 OR EXISTS(SELECT 1 FROM public.modules WHERE module_code='BANK_ACCOUNTS')
 THEN RAISE EXCEPTION 'Foundation required and permission metadata must be absent'; END IF;
END $$;
'@
WriteReview '20_bank_permission_metadata.sql' ($sfGuard+"`n"+$sfBeforePermission+"`n"+$sfPermission+"`nCOMMIT;`n")
$sfControls=Section $sf92 'CREATE TABLE bank_release_control(' 'CREATE FUNCTION sf_bank_allowed('
$sfControls=$sfControls.Replace('posting_enabled boolean NOT NULL DEFAULT false);','posting_enabled boolean NOT NULL DEFAULT false,max_active_accounts integer NOT NULL DEFAULT 1 CHECK(max_active_accounts>0),max_assigned_shops integer NOT NULL DEFAULT 3 CHECK(max_assigned_shops>0));')
if(-not $sfControls.Contains('max_assigned_shops integer')){throw 'Policy control generation failed'}
$sfHelpers=Section $sf92 'CREATE FUNCTION sf_bank_allowed(' 'CREATE FUNCTION sf_bank_write('
$sfLocalGate="SELECT current_database() LIKE 'sf_bank_dev_%' AND current_setting('cluster_name') LIKE 'stockflow_bank_review_%'"
if(-not $sfHelpers.Contains($sfLocalGate)){throw 'Local helper gate source changed'}
# Runtime authorization remains controlled by protected release state and existing
# explicit permissions. No client-supplied deployment GUC is an authorization gate.
$sfHelpers=$sfHelpers.Replace($sfLocalGate,'SELECT true')
$sfWriter=Section $sf93 'CREATE OR REPLACE FUNCTION sf_bank_write(' 'COMMIT;'
$sfBeforeWorkflow=@'
DO $$ DECLARE tab text; BEGIN
 IF to_regclass('public.bank_release_control') IS NOT NULL OR to_regclass('public.bank_requests') IS NOT NULL
 OR NOT EXISTS(SELECT 1 FROM public.modules WHERE module_code='BANK_ACCOUNTS' AND NOT is_active AND NOT show_in_navigation)
 THEN RAISE EXCEPTION 'New inactive foundation/metadata required; existing workflows need incremental review'; END IF;
 FOREACH tab IN ARRAY ARRAY['payments','sales','supplier_payments','purchases',
  'service_payments','service_requests','sale_returns','expense_payments','expenses'] LOOP
  IF to_regclass('public.'||tab) IS NULL THEN RAISE EXCEPTION 'Source table missing: %',tab; END IF;
 END LOOP;
END $$;
'@
$sfGrants=@'
REVOKE ALL ON FUNCTION sf_bank_allowed(text),sf_bank_sources(),sf_bank_write(text,uuid,uuid,jsonb),
 sf_bank_read(uuid,date,integer,text),sf_bank_read(uuid,date,integer,text,bigint) FROM PUBLIC,stockflow_runtime;
GRANT EXECUTE ON FUNCTION sf_bank_write(text,uuid,uuid,jsonb),sf_bank_read(uuid,date,integer,text),
 sf_bank_read(uuid,date,integer,text,bigint) TO stockflow_runtime;
-- No activation, user/role grants, source writes, seed payments or historical import.
COMMIT;
'@
WriteReview '30_bank_workflows.sql' ($sfGuard+"`n"+$sfBeforeWorkflow+"`n"+$sfControls+"`n"+$sfHelpers+"`n"+$sfWriter+"`n"+$sfGrants+"`n")
$sfInputs=@('database/review/bank-accounts/090_bank_accounts_schema.sql','database/review/bank-accounts/091_bank_permissions.sql','database/review/bank-accounts/092_bank_development_workflows.sql','database/review/bank-accounts/093_bank_january_policy.sql')
$sfGenerated=@('10_bank_foundation.sql','20_bank_permission_metadata.sql','30_bank_workflows.sql')
foreach($sfName in $sfGenerated){
 $sfSql=[IO.File]::ReadAllText((Join-Path $PSScriptRoot $sfName))
 # Pin object creation to public; an outer pg_catalog-first search path must
 # never create unqualified objects in pg_catalog. Explicit pg_temp LAST on
 # definers prevents temporary relation shadowing of their unqualified queries.
 $sfSql=[regex]::Replace($sfSql,'CREATE TABLE (bank_[a-z_]+)','CREATE TABLE public.$1')
 $sfSql=[regex]::Replace($sfSql,'CREATE (OR REPLACE )?FUNCTION (sf_bank_[a-z_]+)','CREATE $1FUNCTION public.$2')
 $sfSql=$sfSql.Replace('SET search_path=pg_catalog,public AS','SET search_path=pg_catalog,public,pg_temp AS')
 WriteReview $sfName $sfSql
 if($sfSql -match 'sf_bank_dev_|stockflow_bank_review_|55447|DevelopmentConnection'){throw 'Local target guard leaked into deployment candidate'}
 if($sfSql -match 'CREATE (TABLE bank_|(OR REPLACE )?FUNCTION sf_bank_)' -or $sfSql -match 'SET search_path=pg_catalog,public AS'){throw 'Unsafe object creation or definer search path'}
 if(([regex]::Matches($sfSql,'(?m)^BEGIN;')).Count -ne 1 -or ([regex]::Matches($sfSql,'(?m)^COMMIT;')).Count -ne 1){throw 'Unexpected migration transaction structure'}
}
if(([regex]::Matches((Source 'database/review/bank-accounts/deployment/30_bank_workflows.sql'),"WHEN 'link' THEN 'EDIT'")).Count -ne 1){throw 'Link authorization drift'}
if((Source 'database/review/bank-accounts/deployment/20_bank_permission_metadata.sql').IndexOf($sfPermission,[StringComparison]::Ordinal) -lt 0){throw 'Permission metadata differs from 091'}
$sfManifest=[ordered]@{
 Package='bank-accounts-jan2027-review-v1';Status='REVIEW ONLY; NOT DATABASE-EXECUTED; NOT RELEASE-CERTIFIED'
 Inputs=@($sfInputs|ForEach-Object{@{Path=$_;Sha256=(Get-FileHash -Algorithm SHA256 -LiteralPath (Join-Path $sfRoot $_)).Hash}})
 Migrations=@($sfGenerated|ForEach-Object{@{Path=$_;Sha256=(Get-FileHash -Algorithm SHA256 -LiteralPath (Join-Path $PSScriptRoot $_)).Hash}})
 ReviewFiles=@(@('00_readiness_read_only.sql','40_verify_installation_read_only.sql','REVIEW.txt','CONNECTION-KEYS.txt','RELEASE-TESTS.txt','build-review-package.ps1')|ForEach-Object{@{Path=$_;Sha256=(Get-FileHash -Algorithm SHA256 -LiteralPath (Join-Path $PSScriptRoot $_)).Hash}})
 StaticChecks='PASS: source sections, no local target gates, transaction boundaries, EDIT linking, exact 091 metadata, qualified creation, pg_temp-last definers, generated hashes'
}
WriteReview 'manifest.json' ($sfManifest|ConvertTo-Json -Depth 6)
Write-Output 'PASS: review package files generated and statically checked; no database accessed.'
