param(
    [Parameter(Mandatory)][ValidatePattern('^[A-Za-z0-9_][A-Za-z0-9_.-]{0,62}$')][string]$DatabaseName,
    [Parameter(Mandatory)][ValidateRange(1,9223372036854775807)][long]$TenantId,
    [Parameter(Mandatory)][string]$OutputDirectory,
    [string]$ServerHost='127.0.0.1',
    [ValidateRange(1,65535)][int]$Port=5432,
    [string]$DatabaseUser='postgres',
    [string]$PostgresBin='C:\Program Files\PostgreSQL\18\bin'
)
# Explicit administrative connection only. No application configuration or passwords read.
# IDs and usage hints are sensitive operator evidence; keep this directory private.
$ErrorActionPreference='Stop'
Set-StrictMode -Version Latest
$tool=Join-Path $PostgresBin 'psql.exe'
if(!(Test-Path -LiteralPath $tool -PathType Leaf)){throw 'PostgreSQL psql is required.'}
$directory=[IO.Path]::GetFullPath($OutputDirectory)
if(($directory -split '[\\/]') -contains 'wwwroot'){throw 'Ownership evidence must be stored outside public web content.'}
if(Test-Path -LiteralPath $directory){throw 'Choose a new private output directory; existing evidence will not be overwritten.'}
[IO.Directory]::CreateDirectory($directory)|Out-Null
$query=Join-Path $directory 'review-query.sql'
$csv=Join-Path $directory 'master-usage.csv'
$diagnostics=Join-Path $directory 'diagnostics.log'
$sql=@'
BEGIN ISOLATION LEVEL REPEATABLE READ READ ONLY;
SET LOCAL statement_timeout='60s';
SET LOCAL search_path=pg_catalog,public;
SET LOCAL row_security=off;
DO $$ BEGIN
 IF NOT EXISTS(SELECT 1 FROM pg_roles WHERE rolname=current_user AND (rolsuper OR rolbypassrls))
 THEN RAISE EXCEPTION 'An explicitly authorized full-history administrative reader is required'; END IF;
END $$;
COPY (
 WITH product_locations AS (
  SELECT tenant_id,product_id,location_id,'INVENTORY' basis FROM inventory_transactions
  UNION ALL SELECT tenant_id,product_id,location_id,'SALE' FROM sale_items
  UNION ALL SELECT tenant_id,product_id,location_id,'PURCHASE' FROM purchase_items
  UNION ALL SELECT tenant_id,product_id,location_id,'INTAKE' FROM stock_intake_items
  UNION ALL SELECT tenant_id,product_id,location_id,'SERVICE' FROM service_requests
  UNION ALL SELECT tenant_id,product_id,location_id,'UNIT' FROM product_units
  UNION ALL SELECT i.tenant_id,v.product_id,t.from_location_id,'TRANSFER_FROM' FROM stock_transfer_items i
   JOIN stock_transfers t USING(tenant_id,transfer_id) JOIN product_variants v USING(tenant_id,variant_id)
  UNION ALL SELECT i.tenant_id,v.product_id,t.to_location_id,'TRANSFER_TO' FROM stock_transfer_items i
   JOIN stock_transfers t USING(tenant_id,transfer_id) JOIN product_variants v USING(tenant_id,variant_id)
 ), usage AS (
  SELECT p.tenant_id,'PRODUCT'::text entity,p.product_id::bigint master_id,l.store_id,p.basis
   FROM product_locations p LEFT JOIN store_locations l USING(tenant_id,location_id)
  UNION ALL SELECT s.tenant_id,'CUSTOMER',s.customer_id,l.store_id,'SALE'
   FROM sales s LEFT JOIN store_locations l USING(tenant_id,location_id) WHERE s.customer_id IS NOT NULL
  UNION ALL SELECT s.tenant_id,'CUSTOMER',s.customer_id,l.store_id,'SERVICE'
   FROM service_requests s LEFT JOIN store_locations l USING(tenant_id,location_id)
  UNION ALL SELECT p.tenant_id,'SUPPLIER',p.supplier_id,l.store_id,'PURCHASE'
   FROM purchases p LEFT JOIN store_locations l USING(tenant_id,location_id)
  UNION ALL SELECT u.tenant_id,'CATEGORY',p.category_id,l.store_id,u.basis
   FROM product_locations u JOIN products p USING(tenant_id,product_id) LEFT JOIN store_locations l USING(tenant_id,location_id)
  UNION ALL SELECT u.tenant_id,'BRAND',p.brand_id,l.store_id,u.basis
   FROM product_locations u JOIN products p USING(tenant_id,product_id) LEFT JOIN store_locations l USING(tenant_id,location_id)
   WHERE p.brand_id IS NOT NULL
 ), masters AS (
  SELECT p.tenant_id,'PRODUCT'::text entity,p.product_id::bigint master_id,c.business_profile_code profile
   FROM products p JOIN categories c USING(tenant_id,category_id)
  UNION ALL SELECT tenant_id,'CUSTOMER',customer_id,business_profile_code FROM customers
  UNION ALL SELECT tenant_id,'SUPPLIER',supplier_id,business_profile_code FROM suppliers
  UNION ALL SELECT tenant_id,'CATEGORY',category_id,business_profile_code FROM categories
  UNION ALL SELECT tenant_id,'BRAND',brand_id,business_profile_code FROM brands
 )
 SELECT m.tenant_id,m.entity,m.master_id,m.profile,
  coalesce(string_agg(DISTINCT u.store_id::text,'|' ORDER BY u.store_id::text),'') referenced_store_ids,
  count(DISTINCT u.store_id) referenced_store_count,
  coalesce(string_agg(DISTINCT u.basis,'|' ORDER BY u.basis),'') reference_basis,
  CASE WHEN count(DISTINCT u.store_id)>1 THEN 'MULTI_SHOP_REVIEW'
       WHEN count(DISTINCT u.store_id)=1 THEN 'SINGLE_SHOP_EVIDENCE_ONLY'
       ELSE 'NO_RESOLVED_SHOP_EVIDENCE' END review_state,
  'PENDING' owner_decision,
  'DRAFT_QUOTATION_JSON_AND_CONFIGURATION_NOT_ANALYZED;USAGE_IS_NOT_OWNERSHIP' limitations
 FROM masters m LEFT JOIN usage u USING(tenant_id,entity,master_id)
 WHERE m.tenant_id=:'tenant'::bigint
 GROUP BY m.tenant_id,m.entity,m.master_id,m.profile ORDER BY m.entity,m.master_id
) TO STDOUT WITH (FORMAT CSV,HEADER TRUE);
COMMIT;
'@
[IO.File]::WriteAllText($query,$sql,[Text.UTF8Encoding]::new($false))
try{
    & $tool -X --no-password --quiet --host $ServerHost --port $Port --username $DatabaseUser --dbname $DatabaseName --set ON_ERROR_STOP=1 --set "tenant=$TenantId" --file $query --output $csv 2> $diagnostics
    if($LASTEXITCODE -ne 0){throw 'Read-only ownership query did not complete.'}
    $rows=@(Import-Csv -LiteralPath $csv)
    [ordered]@{
        schemaVersion=1; checkedUtc=[DateTime]::UtcNow.ToString('o'); tenantId=$TenantId
        databaseName=$DatabaseName; serverHost=$ServerHost; port=$Port
        querySha256=(Get-FileHash -LiteralPath $query -Algorithm SHA256).Hash
        scope='ReadOnlyUsageTriage'; rows=$rows.Count; ownerDecision='PENDING'
        privacyVerified=$false; ownershipChanged=$false; productionApproval=$false
        artifact='master-usage.csv'; sha256=(Get-FileHash -LiteralPath $csv -Algorithm SHA256).Hash
        limitation='Historical use is not ownership. Draft/quotation JSON, configuration and identifier-conflict review remain required. No automatic allocation.'
    }|ConvertTo-Json|Set-Content -LiteralPath (Join-Path $directory 'review-evidence.json') -Encoding UTF8
    Write-Output 'Read-only usage triage exported. Ownership decisions and private-shop enforcement remain pending.'
}catch{
    # Failed/incomplete exports never receive a successful evidence manifest.
    throw 'Ownership review export failed. Inspect the private diagnostics; no ownership was changed.'
}
