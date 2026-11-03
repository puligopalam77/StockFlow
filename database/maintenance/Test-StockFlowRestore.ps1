param(
    [Parameter(Mandatory)][string]$Archive,
    [string]$ServerHost = '127.0.0.1',
    [ValidateRange(1,65535)][int]$Port = 5432,
    [string]$DatabaseUser = 'postgres',
    [string]$PostgresBin = 'C:\Program Files\PostgreSQL\18\bin',
    [switch]$KeepTestDatabase
)
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
$elapsed = [Diagnostics.Stopwatch]::StartNew()
$archivePath = (Resolve-Path -LiteralPath $Archive).Path
$evidencePath = $archivePath + '.restore-test.json'
# Invalidate an earlier success before this attempt, including checksum/preflight failures.
[ordered]@{ passed = $false; checkedUtc = [DateTime]::UtcNow.ToString('o'); scope = 'DatabaseOnly'; state = 'Started' } |
    ConvertTo-Json | Set-Content -LiteralPath $evidencePath -Encoding UTF8
$created = $false
try {
$manifestPath = $archivePath + '.json'
if (!(Test-Path -LiteralPath $manifestPath)) { throw 'The backup manifest is missing.' }
$manifest = Get-Content -LiteralPath $manifestPath -Raw | ConvertFrom-Json
if ((Get-FileHash -LiteralPath $archivePath -Algorithm SHA256).Hash -ne $manifest.sha256) { throw 'Backup checksum does not match. Restore was not started.' }
$restoreTool = Join-Path $PostgresBin 'pg_restore.exe'
$sqlTool = Join-Path $PostgresBin 'psql.exe'
foreach ($tool in @($restoreTool,$sqlTool)) { if (!(Test-Path -LiteralPath $tool -PathType Leaf)) { throw "Missing PostgreSQL tool: $tool" } }
$connection = @('--host',$ServerHost,'--port',[string]$Port,'--username',$DatabaseUser,'--no-password')
# This script has no target-database parameter: it can only create its own disposable database.
$testDatabase = 'stockflow_restore_test_' + [Guid]::NewGuid().ToString('N')
    & $sqlTool @connection --dbname postgres --set ON_ERROR_STOP=1 --command "CREATE DATABASE $testDatabase"
    if ($LASTEXITCODE -ne 0) { throw 'Could not create the restore-test database.' }
    $created = $true
    # Owners and runtime roles must already exist on this server, including clean targets.
    & $restoreTool @connection --dbname $testDatabase --exit-on-error $archivePath
    if ($LASTEXITCODE -ne 0) { throw 'Restore failed. Check database roles, PostgreSQL versions and disk space.' }
    $checks = @'
DO $$ BEGIN
 IF to_regclass('public.users') IS NULL OR to_regclass('public.products') IS NULL
 OR to_regclass('public.sales') IS NULL OR to_regclass('public.inventory_transactions') IS NULL
 OR to_regclass('public.auth_protection_keys') IS NULL OR to_regclass('public.cash_closings') IS NULL
 THEN RAISE EXCEPTION 'Required application tables were not restored'; END IF;
 IF NOT EXISTS(SELECT 1 FROM pg_roles WHERE rolname='stockflow_runtime') THEN RAISE EXCEPTION 'Runtime database role is missing'; END IF;
 IF EXISTS(SELECT 1 FROM pg_index WHERE NOT indisvalid) THEN RAISE EXCEPTION 'Invalid index found'; END IF;
 IF NOT EXISTS(SELECT 1 FROM pg_class WHERE oid='cash_closings'::regclass AND relrowsecurity AND relforcerowsecurity)
 THEN RAISE EXCEPTION 'Cash closing row security was not restored'; END IF;
END $$;
-- Report identifiers/counts only. Never persist restored customer or invoice contents.
SELECT 'users' table_name,count(*) records FROM users
UNION ALL SELECT 'products',count(*) FROM products
UNION ALL SELECT 'sales',count(*) FROM sales
UNION ALL SELECT 'stock_movements',count(*) FROM inventory_transactions
UNION ALL SELECT 'cash_closings',count(*) FROM cash_closings;
'@
    $result = & $sqlTool @connection --dbname $testDatabase --set ON_ERROR_STOP=1 --command $checks
    if ($LASTEXITCODE -ne 0) { throw 'Restored database verification failed.' }
    $result | Write-Output
    [ordered]@{ passed = $true; checkedUtc = [DateTime]::UtcNow.ToString('o'); testDatabase = $testDatabase; sha256 = $manifest.sha256;
        scope = 'DatabaseOnly'; serverPrerequisites = 'Existing roles required'; restoreSeconds = [Math]::Round($elapsed.Elapsed.TotalSeconds,3);
        archiveAgeSeconds = [Math]::Round(([DateTimeOffset]::UtcNow-[DateTimeOffset]::Parse($manifest.createdUtc)).TotalSeconds,3);
        applicationStartupVerified = $false; assetsVerified = $false; results = @($result) } |
        ConvertTo-Json -Depth 4 | Set-Content -LiteralPath $evidencePath -Encoding UTF8
    Write-Output "Restore test passed: $testDatabase"
} catch {
    [ordered]@{ passed = $false; checkedUtc = [DateTime]::UtcNow.ToString('o'); scope = 'DatabaseOnly'; state = 'Failed';
        restoreSeconds = [Math]::Round($elapsed.Elapsed.TotalSeconds,3) } | ConvertTo-Json |
        Set-Content -LiteralPath $evidencePath -Encoding UTF8
    throw
} finally {
    if ($created -and !$KeepTestDatabase) {
        if ($testDatabase -notmatch '^stockflow_restore_test_[a-f0-9]{32}$') { throw 'Test database name verification failed.' }
        & $sqlTool @connection --dbname postgres --set ON_ERROR_STOP=1 --command "DROP DATABASE $testDatabase WITH (FORCE)"
        if ($LASTEXITCODE -ne 0) { throw "Could not remove restore-test database: $testDatabase" }
    }
}
