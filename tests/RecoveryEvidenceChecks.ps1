param([switch]$RunDatabaseIntegration)
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$directory = Join-Path $PSScriptRoot ('bin/recovery-evidence/' + [Guid]::NewGuid().ToString('N'))
[IO.Directory]::CreateDirectory($directory) | Out-Null
$collector = Join-Path $root 'database/maintenance/Get-StockFlowRecoveryStatus.ps1'
$archive = Join-Path $directory 'fixture.dump'
Set-Content -LiteralPath $archive -Value 'synthetic checksum fixture, not a database backup' -Encoding UTF8
$hash = (Get-FileHash -LiteralPath $archive -Algorithm SHA256).Hash
@{createdUtc=[DateTimeOffset]::UtcNow.AddMinutes(-1).ToString('o');sha256=$hash} | ConvertTo-Json | Set-Content -LiteralPath ($archive + '.json') -Encoding UTF8
$statusFile = Join-Path $directory 'status.json'
& $collector -Archive $archive -OutputFile $statusFile
$status = Get-Content -LiteralPath $statusFile -Raw | ConvertFrom-Json
if ($null -ne $status.restoreTestUtc) { throw 'Archive alone claimed a restore.' }
$rejected=$false
try { & $collector -Archive $archive -OutputFile ($archive + '.json') } catch { $rejected=$true }
if (!$rejected) { throw 'Collector could overwrite its input evidence.' }
Set-Content -LiteralPath $archive -Value 'changed checksum fixture' -Encoding UTF8
$rejected=$false
try { & $collector -Archive $archive -OutputFile $statusFile } catch { $rejected=$true }
if (!$rejected) { throw 'Checksum mismatch was accepted.' }
Write-Output 'PASS: archive-only status, reserved output protection and checksum mismatch rejection.'
if ($RunDatabaseIntegration) {
    # Dedicated existing test cluster/template only. Never use application configuration here.
    & (Join-Path $root 'database/maintenance/Backup-StockFlow.ps1') -DatabaseName product_edit_069_test -BackupDirectory $directory -Port 55443
    $actual = @(Get-ChildItem -LiteralPath $directory -Filter 'stockflow-product_edit_069_test-*.dump')
    if ($actual.Count -ne 1) { throw 'Expected exactly one real test archive.' }
    & $collector -Archive $actual[0].FullName -OutputFile $statusFile
    if ((Get-Content -LiteralPath $statusFile -Raw | ConvertFrom-Json).restoreTestUtc) { throw 'Fresh archive claimed a restore before pg_restore.' }
    & (Join-Path $root 'database/maintenance/Test-StockFlowRestore.ps1') -Archive $actual[0].FullName -Port 55443
    & $collector -Archive $actual[0].FullName -OutputFile $statusFile
    if (!(Get-Content -LiteralPath $statusFile -Raw | ConvertFrom-Json).restoreTestUtc) { throw 'Successful actual restore evidence was not published.' }
    Write-Output 'PASS: actual pg_dump, checksum, isolated pg_restore, schema/index/RLS verification, disposable restore cleanup and matching evidence publication. Test database only.'
}
