param(
 [Parameter(Mandatory=$true)][string]$Snapshot,
 [string]$PostgresBin='C:\Program Files\PostgreSQL\18\bin'
)
$ErrorActionPreference='Stop'
$workspace=Split-Path $PSScriptRoot -Parent
Set-Location -LiteralPath $workspace
if(!(Test-Path -LiteralPath $Snapshot)){throw 'Supply a pre-067 database snapshot. This script never reads the live database.'}
function Invoke-SkuCheck([string]$Program,[string[]]$Arguments){
 & $Program @Arguments
 if($LASTEXITCODE -ne 0){throw "Check failed: $Program ($LASTEXITCODE)"}
}
# Only this explicitly named disposable database on the isolated loopback test
# cluster is recreated. Start that cluster on port 55439 before running this file.
$testDatabase='sequential_sku_067_v2_test'
$connectionArgs=@('-h','127.0.0.1','-p','55439','-U','postgres')
Invoke-SkuCheck "$PostgresBin\dropdb.exe" ($connectionArgs+@('--if-exists',$testDatabase))
Invoke-SkuCheck "$PostgresBin\createdb.exe" ($connectionArgs+@($testDatabase))
Invoke-SkuCheck "$PostgresBin\pg_restore.exe" ($connectionArgs+@('-d',$testDatabase,'--exit-on-error',$Snapshot))
Invoke-SkuCheck "$PostgresBin\psql.exe" ($connectionArgs+@('-d',$testDatabase,'-X','-v','ON_ERROR_STOP=1','-f','tests/fixtures/sequential-sku-prepare.sql','-f','database/migrations/067_reviewed_local_prefix_plan.sql','-f','database/migrations/067_sequential_catalog_identifiers.sql'))
Invoke-SkuCheck 'dotnet' @('build','tests/StockFlow.MasterDataChecks/StockFlow.MasterDataChecks.csproj','--no-restore','-p:OutputPath=E:/StockFlow/tests/bin/sequential-sku/checks/','-v:q')
Invoke-SkuCheck 'dotnet' @('tests/bin/sequential-sku/checks/StockFlow.MasterDataChecks.dll','--sequential-sku-database-checks')
