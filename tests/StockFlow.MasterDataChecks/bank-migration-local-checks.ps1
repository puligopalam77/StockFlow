param(
 [Parameter(Mandatory=$true)][string]$DataDirectory,
 [Parameter(Mandatory=$true)][string]$ClusterMarker,
 [Parameter(Mandatory=$true)][string]$RunId,
 [int]$Port=55447
)
$ErrorActionPreference='Stop'
$repoRoot=[IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
$artifactRoot=Join-Path $repoRoot '.artifacts'
$expectedDirectory=Join-Path $artifactRoot ('bank-review-pg-'+$RunId)
if($RunId -notmatch '^[a-f0-9]{32}$' -or $ClusterMarker -ne ('stockflow_bank_review_'+$RunId) -or
 [IO.Path]::GetFullPath($DataDirectory) -ne [IO.Path]::GetFullPath($expectedDirectory) -or $Port -ne 55447){throw 'Unrecognized isolated test target'}
$psql='C:\Program Files\PostgreSQL\18\bin\psql.exe'
$output=Join-Path $artifactRoot ('bank-review-results-'+$RunId)
[void][IO.Directory]::CreateDirectory($output)
$env:PGOPTIONS='';$env:PGSERVICE='';$env:PGPASSWORD='';$env:PGCONNECT_TIMEOUT='5';$env:PGCLIENTENCODING='UTF8'
$database='sf_bank_review_'+$RunId
$results=[Collections.Generic.List[string]]::new()
function Assert-LocalTarget([string]$Db){
 if($Db -ne 'postgres' -and $Db -ne $database){throw 'Database not owned by this test run'}
 $identity=& $psql -h 127.0.0.1 -p $Port -U postgres -d $Db -w -X -A -t -v ON_ERROR_STOP=1 -c "SELECT json_build_object('database',current_database(),'address',inet_server_addr(),'port',inet_server_port(),'directory',current_setting('data_directory'),'marker',current_setting('cluster_name'),'superuser',(SELECT rolsuper FROM pg_roles WHERE rolname=current_user));"
 if($LASTEXITCODE -ne 0){throw 'Local target identity query failed'}
 $target=($identity -join '')|ConvertFrom-Json
 if($target.database -ne $Db -or $target.address -ne '127.0.0.1' -or $target.port -ne $Port -or
 $target.marker -ne $ClusterMarker -or !$target.superuser -or
 [IO.Path]::GetFullPath($target.directory) -ne [IO.Path]::GetFullPath($DataDirectory)){throw 'Isolated PostgreSQL target identity mismatch'}
}
function Invoke-LocalFile([string]$Db,[string]$File,[string]$ExpectedError=''){
 Assert-LocalTarget $Db
 $sql=[IO.File]::ReadAllText($File)
 if($sql -match '(?im)^\s*\\(?:c|connect|i|ir|!)\b' -or $sql -match '(?i)\bdblink\b|CREATE\s+SERVER|COPY[\s\S]*?PROGRAM'){throw 'External SQL target/command in fixture'}
 $log=Join-Path $output (([IO.Path]::GetFileName($File))+'.log')
 # cmdlets treat native stderr as errors in Windows PowerShell; preserve psql exit.
 $previous=$ErrorActionPreference;$ErrorActionPreference='Continue'
 $lines=& $psql -h 127.0.0.1 -p $Port -U postgres -d $Db -w -X -v ON_ERROR_STOP=1 -v VERBOSITY=verbose -f $File 2>&1
 $code=$LASTEXITCODE;$ErrorActionPreference=$previous
 [IO.File]::WriteAllLines($log,[string[]]@($lines))
 if($ExpectedError){if($code -eq 0 -or ($lines -join "`n") -notmatch $ExpectedError){throw ('Expected SQL error absent: '+$File+"`n"+($lines -join "`n"))}}
 elseif($code -ne 0){throw ('SQL failed: '+$File+"`n"+(($lines|Select-Object -Last 12)-join "`n"))}
}
function Invoke-LocalSql([string]$Db,[string]$Name,[string]$Sql,[string]$ExpectedError=''){
 $file=Join-Path $output ($Name+'.sql');[IO.File]::WriteAllText($file,$Sql,[Text.UTF8Encoding]::new($false))
 Invoke-LocalFile $Db $file $ExpectedError
}
function Passed([string]$Message){$results.Add($Message);Write-Output ('PASS '+$Message)}
function Test-UniqueRace([string]$Name,[string]$Statement){
 $processes=@();$errorFiles=@();$outputFiles=@()
 for($i=0;$i -lt 2;$i++){
  Assert-LocalTarget $database
  $file=Join-Path $output ($Name+'-'+$i+'.sql')
  $guard="DO `$`$ BEGIN IF current_database()<>'$database' OR inet_server_addr()<>'127.0.0.1'::inet OR inet_server_port()<>$Port OR current_setting('cluster_name')<>'$ClusterMarker' THEN RAISE EXCEPTION 'Race target mismatch'; END IF; END `$`$;`n"
  [IO.File]::WriteAllText($file,$guard+"BEGIN;`n"+$Statement+"`nSELECT pg_sleep(0.3);COMMIT;SELECT 'BANK_RACE_COMMITTED';",[Text.UTF8Encoding]::new($false))
  $errorFile=Join-Path $output ($Name+'-'+$i+'.stderr');$errorFiles+=$errorFile
  $outputFile=Join-Path $output ($Name+'-'+$i+'.stdout');$outputFiles+=$outputFile
  $processes+=Start-Process -FilePath $psql -WindowStyle Hidden -PassThru -ArgumentList @('-h','127.0.0.1','-p',$Port,'-U','postgres','-d',$database,'-w','-X','-v','ON_ERROR_STOP=1','-v','VERBOSITY=verbose','-f',('"'+$file+'"')) -RedirectStandardOutput $outputFile -RedirectStandardError $errorFile
 }
 foreach($process in $processes){$process.WaitForExit();$process.Refresh()}
 $commits=@($outputFiles|Where-Object {[IO.File]::ReadAllText($_) -match 'BANK_RACE_COMMITTED'})
 $duplicates=@($errorFiles|Where-Object {[IO.File]::ReadAllText($_) -match '23505'})
 if($commits.Count -ne 1 -or $duplicates.Count -ne 1){throw ('Unique race failed: '+$Name)}
 Passed ('concurrent '+$Name+': exactly one committed; duplicate rejected with 23505')
}

Assert-LocalTarget 'postgres'
Write-Output ('VERIFIED local target 127.0.0.1:'+$Port+'; dedicated directory='+$DataDirectory+'; marker='+$ClusterMarker)
$created=$false
try {
 Invoke-LocalSql 'postgres' 'create-dedicated-database' ('CREATE DATABASE '+$database+';');$created=$true
 # Actual full-chain rehearsal failed at 050: checked-in migration 049 is empty.
 # Do not fabricate its historical snapshot implementation or use real DB dumps.
 Invoke-LocalSql $database 'local-runtime-role' "DO `$`$ BEGIN IF NOT EXISTS(SELECT 1 FROM pg_roles WHERE rolname='stockflow_runtime') THEN CREATE ROLE stockflow_runtime NOLOGIN NOBYPASSRLS; END IF; END `$`$;"
 Invoke-LocalFile $database (Join-Path $PSScriptRoot 'CashInHandFixture.sql')
 Invoke-LocalFile $database (Join-Path $PSScriptRoot 'BankMigrationPre089Fixture.sql')
 foreach($name in @('087_cash_in_hand.sql','088_cash_entry_release_scope.sql','089_cash_entry_reasons.sql')){
  Invoke-LocalFile $database (Join-Path $repoRoot ('database/migrations/'+$name))
 }
 Passed 'representative parent/payment/permission fixture plus actual 087-089; full historical chain BLOCKED by empty migration 049'
 Invoke-LocalFile $database (Join-Path $PSScriptRoot 'BankMigrationSyntheticSeed.sql')
 Passed 'synthetic tenants, shops, actors and existing sale/payment seeded before bank migration'
 $drafts=@('090_bank_accounts_schema.sql','091_bank_permissions.sql')
 foreach($draft in $drafts){Invoke-LocalFile $database (Join-Path $repoRoot ('database/review/bank-accounts/'+$draft)) 'P0001'}
 Invoke-LocalSql $database 'guard-rollback-check' "DO `$`$ BEGIN IF to_regclass('public.bank_accounts') IS NOT NULL THEN RAISE EXCEPTION 'Guard left schema'; END IF; END `$`$;"
 Passed 'both unapproved drafts rejected; failed-transaction disconnect rolled back without creating bank schema'
 # Enable only in this dedicated cluster: never set or read application config.
 $schema=[IO.File]::ReadAllText((Join-Path $repoRoot ('database/review/bank-accounts/'+$drafts[0])))
 $permissions=[IO.File]::ReadAllText((Join-Path $repoRoot ('database/review/bank-accounts/'+$drafts[1])))
 $approve="SET stockflow.bank_schema_review_approved='yes';`n"
 # Abort AFTER the complete schema body, before its commit, then reapply unmodified.
 $rollback=$schema -replace 'COMMIT;\s*$',"SELECT 1/0;`nCOMMIT;"
 Invoke-LocalSql $database 'forced-schema-rollback' ($approve+$rollback) '22012'
 Invoke-LocalSql $database 'schema-rollback-check' "DO `$`$ BEGIN IF to_regclass('public.bank_accounts') IS NOT NULL OR to_regprocedure('public.sf_bank_review_immutable()') IS NOT NULL THEN RAISE EXCEPTION 'DDL rollback incomplete'; END IF; END `$`$;"
 Passed 'forced late-schema SQL error rolls back all tables, indexes and trigger function'
 Invoke-LocalSql $database 'apply-090' ($approve+$schema)
 $rollbackPermissions=$permissions -replace 'COMMIT;\s*$',"SELECT 1/0;`nCOMMIT;"
 Invoke-LocalSql $database 'forced-permission-rollback' ($approve+$rollbackPermissions) '22012'
 Invoke-LocalSql $database 'permission-rollback-check' "DO `$`$ BEGIN IF EXISTS(SELECT 1 FROM modules WHERE module_code='BANK_ACCOUNTS') THEN RAISE EXCEPTION 'Permission rollback incomplete'; END IF; END `$`$;"
 Invoke-LocalSql $database 'apply-091' ($approve+$permissions)
 Passed 'both real SQL drafts execute successfully after rollback; inactive permission metadata reapplies cleanly'
 Invoke-LocalFile $database (Join-Path $PSScriptRoot 'BankMigrationSchemaChecks.sql')
 Invoke-LocalSql $database 'concurrency-account-seed' @'
INSERT INTO bank_accounts(tenant_id,account_id,bank_name,display_name,holder_name,account_type,account_last_four,ifsc,created_by)
VALUES(1,md5('account-race')::uuid,'Synthetic Bank','Synthetic race account','Synthetic Business','CURRENT','0004','TEST0000001',1);
INSERT INTO bank_account_shops(tenant_id,account_id,store_id) VALUES(1,md5('account-race')::uuid,1);
'@
 Test-UniqueRace 'opening-initialization' @'
INSERT INTO bank_openings(tenant_id,account_id,opening_id,amount,tracking_started_at,evidence_description,bank_reference,request_id,request_fingerprint,initialized_by)
VALUES(1,md5('account-race')::uuid,gen_random_uuid(),0,'2026-11-10 00:00+05:30','Synthetic concurrent zero opening','SYNTHETIC-RACE-OPEN',gen_random_uuid(),decode(repeat('09',32),'hex'),1);
'@
 Test-UniqueRace 'same-bank-reference-posting' @'
WITH inserted AS (
INSERT INTO bank_movements(tenant_id,movement_id,account_id,entry_kind,signed_amount,effective_date,bank_reference,normalized_bank_reference,evidence_description,verified_by,request_id,request_fingerprint)
VALUES(1,gen_random_uuid(),md5('account-a')::uuid,'SETTLEMENT',100,'2026-11-12','SYNTHETIC-RACE-UTR','SYNTHETIC-RACE-UTR','Synthetic concurrent verified bank reference',1,gen_random_uuid(),decode(repeat('10',32),'hex')) RETURNING tenant_id,account_id,movement_id)
INSERT INTO bank_movement_allocations SELECT tenant_id,account_id,movement_id,md5('pending')::uuid,100 FROM inserted;
'@
 Assert-LocalTarget $database
 $checks=& $psql -h 127.0.0.1 -p $Port -U postgres -d $database -w -X -A -t -v ON_ERROR_STOP=1 -c 'SELECT label FROM bank_test_results ORDER BY test_order;'
 if($LASTEXITCODE -ne 0){throw 'Could not read isolated test results'}
 foreach($check in $checks){Passed $check}
 $gaps=& $psql -h 127.0.0.1 -p $Port -U postgres -d $database -w -X -A -t -v ON_ERROR_STOP=1 -c 'SELECT label FROM bank_test_gaps ORDER BY label;'
 foreach($gap in $gaps){Write-Output ('BLOCKER '+$gap)}
 [IO.File]::WriteAllLines((Join-Path $output 'passed.txt'),$results)
 Write-Output ('RESULTS '+$output)
} finally {
 if($created){
  # Destructive cleanup is confined to the generated disposable database AFTER
  # repeating host/port/marker/directory/database identity verification.
  Assert-LocalTarget $database
  Invoke-LocalSql 'postgres' 'cleanup-dedicated-database' ('DROP DATABASE '+$database+' WITH (FORCE);')
  Write-Output ('CLEANED disposable local database '+$database)
 }
}
