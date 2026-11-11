param([Parameter(Mandatory=$true)][string]$TargetFile)
$ErrorActionPreference='Stop'
$root=[IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
$target=Get-Content -LiteralPath $TargetFile -Raw|ConvertFrom-Json
$expected=Join-Path $root ('.artifacts\bank-review-pg-'+$target.RunId)
if($target.RunId -notmatch '^[a-f0-9]{32}$' -or $target.Port -ne 55447 -or $target.ClusterMarker -ne ('stockflow_bank_review_'+$target.RunId) -or [IO.Path]::GetFullPath($target.DataDirectory) -ne $expected){throw 'Unrecognized local development target'}
$psql='C:\Program Files\PostgreSQL\18\bin\psql.exe';$pgctl='C:\Program Files\PostgreSQL\18\bin\pg_ctl.exe'
$db='sf_bank_dev_'+$target.RunId;$out=Join-Path $root ('.artifacts\bank-development-'+$target.RunId);[void][IO.Directory]::CreateDirectory($out)
$env:PGOPTIONS='';$env:PGSERVICE='';$env:PGPASSWORD='';$env:PGCONNECT_TIMEOUT='5'
& $pgctl -D $target.DataDirectory status
if($LASTEXITCODE -ne 0){& $pgctl -D $target.DataDirectory -o "-h 127.0.0.1 -p 55447 -c cluster_name=$($target.ClusterMarker)" -l (Join-Path $target.DataDirectory 'bank-development.log') start;if($LASTEXITCODE -ne 0){throw 'Local development startup failed'}}
function Verify([string]$database){
 $value=& $psql -h 127.0.0.1 -p 55447 -U postgres -d $database -w -X -A -t -c "SELECT json_build_object('db',current_database(),'ip',inet_server_addr(),'port',inet_server_port(),'marker',current_setting('cluster_name'),'directory',current_setting('data_directory'));"
 if($LASTEXITCODE -ne 0){throw 'Identity query failed'};$v=($value-join '')|ConvertFrom-Json
 if($v.db -ne $database -or $v.ip -ne '127.0.0.1' -or $v.port -ne 55447 -or $v.marker -ne $target.ClusterMarker -or [IO.Path]::GetFullPath($v.directory) -ne $expected){throw 'Local identity mismatch'}
}
function Sql([string]$database,[string]$name,[string]$body){
 Verify $database;$file=Join-Path $out ($name+'.sql');[IO.File]::WriteAllText($file,$body,[Text.UTF8Encoding]::new($false))
 & $psql -h 127.0.0.1 -p 55447 -U postgres -d $database -w -X -v ON_ERROR_STOP=1 -f $file
 if($LASTEXITCODE -ne 0){throw ('Local development SQL failed: '+$name)}
}
Verify 'postgres'
$exists=& $psql -h 127.0.0.1 -p 55447 -U postgres -d postgres -w -X -A -t -c "SELECT count(*) FROM pg_database WHERE datname='$db';"
if(($exists-join '').Trim() -ne '0'){throw 'Development database already exists; 090/091 will NOT be rerun'}
Sql 'postgres' 'create-development' ("CREATE DATABASE $db;")
foreach($file in @('tests/StockFlow.MasterDataChecks/CashInHandFixture.sql','tests/StockFlow.MasterDataChecks/BankMigrationPre089Fixture.sql','database/migrations/087_cash_in_hand.sql','database/migrations/088_cash_entry_release_scope.sql','database/migrations/089_cash_entry_reasons.sql','tests/StockFlow.MasterDataChecks/BankMigrationSyntheticSeed.sql','tests/StockFlow.MasterDataChecks/BankDevelopmentFixture.sql')){Sql $db ([IO.Path]::GetFileName($file)) ([IO.File]::ReadAllText((Join-Path $root $file)))}
foreach($file in @('090_bank_accounts_schema.sql','091_bank_permissions.sql','092_bank_development_workflows.sql')){
 Sql $db $file ("SET stockflow.bank_schema_review_approved='yes';`n"+[IO.File]::ReadAllText((Join-Path $root ('database/review/bank-accounts/'+$file))))
}
# Explicit synthetic development grants only, no changes to the application DB.
Sql $db 'development-only-controls' ([IO.File]::ReadAllText((Join-Path $PSScriptRoot 'BankDevelopmentEnable.sql')))
[pscustomobject]@{Database=$db;Port=55447;Marker=$target.ClusterMarker;DataDirectory=$target.DataDirectory;Output=$out}|ConvertTo-Json|Set-Content (Join-Path $root '.artifacts/bank-development-target.json')
Write-Output ('READY retained synthetic local development database '+$db)
