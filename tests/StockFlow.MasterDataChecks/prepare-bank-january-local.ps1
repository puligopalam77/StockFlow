$ErrorActionPreference='Stop'
$sfRoot=[IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
$sfTarget=Get-Content -LiteralPath (Join-Path $sfRoot '.artifacts/bank-development-target.json') -Raw|ConvertFrom-Json
$sfRun='1743b72ab490475980598a6e845cc04c'
$sfDirectory=Join-Path $sfRoot ('.artifacts\bank-review-pg-'+$sfRun)
if($sfTarget.Database -ne ('sf_bank_dev_'+$sfRun) -or $sfTarget.Marker -ne ('stockflow_bank_review_'+$sfRun) -or
 [IO.Path]::GetFullPath($sfTarget.DataDirectory) -ne $sfDirectory -or $sfTarget.Port -ne 55447){throw 'Unrecognized synthetic template target'}
$sfPsql='C:\Program Files\PostgreSQL\18\bin\psql.exe';$sfCtl='C:\Program Files\PostgreSQL\18\bin\pg_ctl.exe'
$env:PGOPTIONS='';$env:PGSERVICE='';$env:PGPASSWORD='';$env:PGCONNECT_TIMEOUT='5'
& $sfCtl -D $sfDirectory status
if($LASTEXITCODE -ne 0){& $sfCtl -D $sfDirectory -o "-h 127.0.0.1 -p 55447 -c cluster_name=$($sfTarget.Marker)" -l (Join-Path $sfDirectory 'january-development.log') start;if($LASTEXITCODE -ne 0){throw 'Local server startup failed'}}
function Identity([string]$sfDatabase){
 $sfReply=& $sfPsql -X -w -h 127.0.0.1 -p 55447 -U postgres -d $sfDatabase -At -c "SELECT json_build_object('database',current_database(),'address',inet_server_addr(),'port',inet_server_port(),'marker',current_setting('cluster_name'),'directory',current_setting('data_directory'));"
 if($LASTEXITCODE -ne 0){throw 'Identity query failed'};$sfIdentity=($sfReply-join '')|ConvertFrom-Json
 if($sfIdentity.database -ne $sfDatabase -or $sfIdentity.address -ne '127.0.0.1' -or $sfIdentity.port -ne 55447 -or $sfIdentity.marker -ne $sfTarget.Marker -or [IO.Path]::GetFullPath($sfIdentity.directory) -ne $sfDirectory){throw 'Target identity mismatch'}
}
Identity $sfTarget.Database
$sfDatabase='sf_bank_dev_'+[guid]::NewGuid().ToString('N')
Identity 'postgres'
# Template lineage: only the retained synthetic bank fixture, never application data.
& $sfPsql -X -w -v ON_ERROR_STOP=1 -h 127.0.0.1 -p 55447 -U postgres -d postgres -c "CREATE DATABASE $sfDatabase TEMPLATE $($sfTarget.Database);"
if($LASTEXITCODE -ne 0){throw 'Synthetic database clone failed'}
Identity $sfDatabase
$sfScript=Join-Path $sfRoot '.artifacts/bank-january-approved.sql'
[IO.File]::WriteAllText($sfScript,"SET stockflow.bank_schema_review_approved='yes';`n"+[IO.File]::ReadAllText((Join-Path $sfRoot 'database/review/bank-accounts/093_bank_january_policy.sql')))
& $sfPsql -X -w -v ON_ERROR_STOP=1 -h 127.0.0.1 -p 55447 -U postgres -d $sfDatabase -f $sfScript
if($LASTEXITCODE -ne 0){throw 'Local 093 failed; template is unchanged'}
[pscustomobject]@{Database=$sfDatabase;Marker=$sfTarget.Marker;DataDirectory=$sfDirectory;Port=55447}|ConvertTo-Json|Set-Content (Join-Path $sfRoot '.artifacts/bank-january-target.json')
Write-Output ('Prepared synthetic January test target '+$sfDatabase+'; 049/090/091/092 were NOT executed')
