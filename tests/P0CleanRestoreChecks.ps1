# Synthetic fixture only. Creates a fresh PostgreSQL cluster; never loads appsettings.
param([string]$PostgresBin='C:\Program Files\PostgreSQL\18\bin',
      [string]$TestAssembly='E:\StockFlow\.artifacts\p0-reliability\bin\StockFlow.MasterDataChecks\debug\StockFlow.MasterDataChecks.dll')
$ErrorActionPreference='Stop'
Set-StrictMode -Version Latest
$repo=[IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$run=Join-Path $repo ('tests/bin/p0-clean-restore/'+[Guid]::NewGuid().ToString('N'))
[IO.Directory]::CreateDirectory($run)|Out-Null
$data=Join-Path $run 'cluster'
$psql=Join-Path $PostgresBin 'psql.exe'
$ctl=Join-Path $PostgresBin 'pg_ctl.exe'
$init=Join-Path $PostgresBin 'initdb.exe'
$source='p0_restore_source_'+[Guid]::NewGuid().ToString('N')
$src=@('-X','--no-password','-h','127.0.0.1','-p','55443','-U','postgres','-v','ON_ERROR_STOP=1')
$dst=@('-X','--no-password','-h','127.0.0.1','-p','55444','-U','postgres','-v','ON_ERROR_STOP=1')
$started=$false;$created=$false;$oldDatabase=$env:STOCKFLOW_RESTORE_TEST_DATABASE
$clock=[Diagnostics.Stopwatch]::StartNew()
function Sql([string[]]$Connection,[string]$Db,[string]$Query){
 $lines=& $psql @Connection -d $Db -At -c $Query
 if($LASTEXITCODE -ne 0){throw 'Isolated fixture SQL failed.'}
 return $lines
}
function Fingerprint([string[]]$Connection,[string]$Db){
 # Private aggregate digests, not row contents. Fixture is frozen while dumped.
 $tables=@(Sql $Connection $Db "SELECT quote_ident(tablename) FROM pg_tables WHERE schemaname='public' ORDER BY tablename")
 $proof=@{}
 foreach($table in $tables){
  $proof[$table]=[string](Sql $Connection $Db "SELECT count(*)::text||':'||md5(coalesce(string_agg(h,'' ORDER BY h),'')) FROM (SELECT md5(row_to_json(t)::text) h FROM public.$table t) x")
 }
 return $proof
}
try {
 # Abort if the reserved clean target port is in use; never adopt somebody else's server.
 $probe=New-Object Net.Sockets.TcpClient
 try{if($probe.ConnectAsync('127.0.0.1',55444).Wait(300) -and $probe.Connected){throw 'Clean-restore port 55444 is already occupied.'}}catch [AggregateException]{}finally{$probe.Dispose()}
 Sql $src 'postgres' "CREATE DATABASE $source TEMPLATE product_edit_069_test"|Out-Null;$created=$true
 foreach($migration in Get-ChildItem -LiteralPath (Join-Path $repo 'database/migrations') -Filter '*.sql' | Sort-Object Name){
  if([int]$migration.Name.Substring(0,3) -ge 70){& $psql @src -d $source -f $migration.FullName > (Join-Path $run 'migrations.log');if($LASTEXITCODE -ne 0){throw 'Disposable restore-source migration failed.'}}
 }
 # Exercise nonempty new expense/payment/receipt data in the real archive as well
 # as schema. These are synthetic operations through the guarded writer functions.
 $expenseSeed=@'
SELECT set_config('app.tenant_id','1',false),set_config('app.user_id','1',false),set_config('app.store_id','1',false),set_config('app.location_id','1',false),set_config('app.business_profile_code',(SELECT business_profile_code FROM tenants WHERE tenant_id=1),false);
SET ROLE stockflow_runtime;
SELECT sf_expense_master('CATEGORY','11111111-1111-4111-8111-111111111101','Synthetic restored overhead');
SELECT sf_expense_master('PAYEE','11111111-1111-4111-8111-111111111102','Synthetic restored payee');
SELECT sf_expense_write('11111111-1111-4111-8111-111111111111','11111111-1111-4111-8111-111111111103',0,'SAVE',jsonb_build_object('categoryId','11111111-1111-4111-8111-111111111101','payeeId','11111111-1111-4111-8111-111111111102','incurredDate',(now() AT TIME ZONE 'Asia/Kolkata')::date,'amount',25,'description','Synthetic restored expense'));
SELECT sf_expense_write('11111111-1111-4111-8111-111111111111','11111111-1111-4111-8111-111111111104',1,'POST','{}');
SELECT sf_expense_write('11111111-1111-4111-8111-111111111111','11111111-1111-4111-8111-111111111105',2,'PAY',jsonb_build_object('paidDate',(now() AT TIME ZONE 'Asia/Kolkata')::date,'amount',10,'paymentMode','CASH'));
SELECT sf_expense_write('11111111-1111-4111-8111-111111111111','11111111-1111-4111-8111-111111111106',3,'ATTACH',jsonb_build_object('name','synthetic-restored-receipt.pdf','contentType','application/pdf','data',encode(convert_to('%PDF-1.4 synthetic restored expense receipt','UTF8'),'base64')));
'@
 Sql $src $source $expenseSeed|Out-Null
 $before=Fingerprint $src $source
 & (Join-Path $repo 'database/maintenance/Backup-StockFlow.ps1') -DatabaseName $source -BackupDirectory $run -Port 55443 -PostgresBin $PostgresBin
 $archive=(Get-ChildItem -LiteralPath $run -Filter '*.dump').FullName
 # Role prerequisites are deliberately transferred without authentication secrets.
 $roles=@(& (Join-Path $PostgresBin 'pg_dumpall.exe') -h 127.0.0.1 -p 55443 -U postgres --no-password --roles-only --no-role-passwords)
 if($LASTEXITCODE -ne 0){throw 'Fixture role export failed.'}
 $roles|Where-Object{$_ -notmatch '^(CREATE ROLE postgres;|ALTER ROLE postgres WITH )'}|Set-Content -LiteralPath (Join-Path $run 'roles.sql') -Encoding UTF8
 # This loopback trust cluster contains synthetic test data only. Never use it for business dumps.
 & $init -D $data -U postgres -A trust --encoding=UTF8 --no-locale > (Join-Path $run 'init.log')
 if($LASTEXITCODE -ne 0){throw 'Clean fixture initdb failed.'}
 $launch=Start-Process -FilePath $ctl -ArgumentList @('start','-D',$data,'-l',(Join-Path $run 'postgres.log'),'-o','"-h 127.0.0.1 -p 55444"','-w') -WindowStyle Hidden -PassThru
 $deadline=[DateTime]::UtcNow.AddSeconds(30)
 $ready=$false
 do{
  Start-Sleep -Milliseconds 250
  & $ctl status -D $data *> $null;$started=$LASTEXITCODE -eq 0
  & (Join-Path $PostgresBin 'pg_isready.exe') -h 127.0.0.1 -p 55444 -U postgres *> $null;$ready=$LASTEXITCODE -eq 0
 }while(!$ready -and [DateTime]::UtcNow -lt $deadline)
 if(!$ready){throw 'Clean restore cluster did not become ready.'}
 & $psql @dst -d postgres -f (Join-Path $run 'roles.sql') > (Join-Path $run 'roles.log')
 if($LASTEXITCODE -ne 0){throw 'Clean role prerequisite import failed.'}
 & (Join-Path $repo 'database/maintenance/Test-StockFlowRestore.ps1') -Archive $archive -Port 55444 -PostgresBin $PostgresBin -KeepTestDatabase
 $evidence=Get-Content -LiteralPath ($archive+'.restore-test.json') -Raw|ConvertFrom-Json
 if(!$evidence.passed){throw 'Database restore evidence did not pass.'}
 $restored=$evidence.testDatabase
 $after=Fingerprint $dst $restored
 if($before.Count -ne $after.Count){throw 'Restored public table set changed.'}
 foreach($table in $before.Keys){if($before[$table] -ne $after[$table]){throw "Restored fixture digest/count mismatch for $table"}}
 Write-Output "PASS: all $($before.Count) public tables match frozen-source row digests and counts in fresh PostgreSQL cluster."
 # Hash tampering must invalidate a previous success without starting a restore.
 $tampered=Join-Path $run 'tampered.dump'
 Copy-Item -LiteralPath $archive -Destination $tampered
 Copy-Item -LiteralPath ($archive+'.json') -Destination ($tampered+'.json')
 Copy-Item -LiteralPath ($archive+'.restore-test.json') -Destination ($tampered+'.restore-test.json')
 [IO.File]::AppendAllText($tampered,'tampered synthetic archive')
 $rejected=$false
 try{& (Join-Path $repo 'database/maintenance/Test-StockFlowRestore.ps1') -Archive $tampered -Port 55444 -PostgresBin $PostgresBin}catch{$rejected=$true}
 if(!$rejected -or (Get-Content -LiteralPath ($tampered+'.restore-test.json') -Raw|ConvertFrom-Json).passed){throw 'Tampered archive retained successful restore evidence.'}
 Write-Output 'PASS: checksum failure invalidates stale successful restore evidence.'
 $env:STOCKFLOW_RESTORE_TEST_DATABASE=$restored
 & dotnet $TestAssembly --restored-app-checks
 if($LASTEXITCODE -ne 0){throw 'Restored application smoke test failed.'}
 [ordered]@{passed=$true;checkedUtc=[DateTime]::UtcNow.ToString('o');scope='SyntheticFreshClusterDatabaseAndApplicationRead';publicTables=$before.Count;durationSeconds=[Math]::Round($clock.Elapsed.TotalSeconds,3);archiveAgeSeconds=$evidence.archiveAgeSeconds;applicationStartupVerified=$true;assetsVerified=$false;productionVerified=$false;rolesWithoutPasswords=$true}|ConvertTo-Json|Set-Content -LiteralPath (Join-Path $run 'result.json') -Encoding UTF8
 Write-Output "PASS: clean restore evidence $run/result.json. Synthetic data only; no production recovery claim."
}finally{
 $env:STOCKFLOW_RESTORE_TEST_DATABASE=$oldDatabase
 # Stop only this run's uniquely named, workspace-contained cluster; retain artifacts for inspection.
 if([IO.Path]::GetFullPath($data).StartsWith([IO.Path]::GetFullPath((Join-Path $repo 'tests/bin/p0-clean-restore'))+[IO.Path]::DirectorySeparatorChar) -and ($started -or (Test-Path -LiteralPath (Join-Path $data 'postmaster.pid')))){
  # Readiness can become true just after the earlier pg_ctl status probe returned
  # false. Recheck this exact generated data directory; never infer ownership from
  # a listening port or stop a different cluster.
  & $ctl status -D $data *> $null
  if($LASTEXITCODE -eq 0){& $ctl stop -D $data -m fast -w|Out-Null;if($LASTEXITCODE -ne 0){throw 'Disposable restore cluster cleanup failed.'}}
 }
 if($created -and $source -match '^p0_restore_source_[a-f0-9]{32}$'){Sql $src 'postgres' "DROP DATABASE $source WITH (FORCE)"|Out-Null}
}
