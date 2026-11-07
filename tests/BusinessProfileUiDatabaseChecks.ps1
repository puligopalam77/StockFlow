# Explicit synthetic clone only. No application connection strings or production DB.
$ErrorActionPreference='Stop'
$taskPg='C:/Program Files/PostgreSQL/18/bin'
$taskDb='profile_ui_fixture_'+[guid]::NewGuid().ToString('N')
$taskArgs=@('-X','-h','127.0.0.1','-p','55443','-U','postgres','-v','ON_ERROR_STOP=1')
& "$taskPg/createdb.exe" -h 127.0.0.1 -p 55443 -U postgres -T product_edit_069_test $taskDb
if($LASTEXITCODE){throw 'Cannot create synthetic clone'}
try {
 foreach($taskMigration in (Get-ChildItem database/migrations -Filter '*.sql' | Where-Object {[int]$_.Name.Substring(0,3) -ge 70} | Sort-Object Name)) {
  & "$taskPg/psql.exe" @taskArgs -d $taskDb -q -f $taskMigration.FullName
  if($LASTEXITCODE){throw "Synthetic migration failed: $($taskMigration.Name)"}
 }
 & "$taskPg/psql.exe" @taskArgs -d $taskDb -f tests/BusinessProfileUiDatabaseChecks.sql
 if($LASTEXITCODE){throw 'Profile preservation fixture failed'}
 & 'tests/StockFlow.UiChecks/bin/Debug/net10.0/.playwright/node/win32_x64/node.exe' tests/StockFlow.UiChecks/business-profile-concurrency.cjs $taskDb
 if($LASTEXITCODE){throw 'Profile concurrency fixture failed'}
} finally {
 if($taskDb -notmatch '^profile_ui_fixture_[a-f0-9]{32}$'){throw 'Unexpected cleanup database name'}
 & "$taskPg/dropdb.exe" -h 127.0.0.1 -p 55443 -U postgres --force $taskDb
}
