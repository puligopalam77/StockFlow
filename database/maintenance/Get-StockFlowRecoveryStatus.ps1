param(
    [Parameter(Mandatory)][string]$Archive,
    [Parameter(Mandatory)][string]$OutputFile
)
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
# Run as the backup operator. The output must be ACL protected and outside wwwroot.
# Never publish the archive, manifest or raw restore output through the web application.
$archivePath = (Resolve-Path -LiteralPath $Archive).Path
$target = [IO.Path]::GetFullPath($OutputFile)
foreach ($reserved in @($archivePath, ($archivePath + '.json'), ($archivePath + '.restore-test.json'))) {
    if ($target -eq $reserved) { throw 'Evidence output cannot overwrite a backup or its evidence.' }
}
$manifest = Get-Content -LiteralPath ($archivePath + '.json') -Raw | ConvertFrom-Json
$hash = (Get-FileHash -LiteralPath $archivePath -Algorithm SHA256).Hash
if ($hash -ne $manifest.sha256) { throw 'Archive checksum mismatch; no status published.' }
$backupUtc = [DateTimeOffset]::Parse($manifest.createdUtc).ToUniversalTime()
$restoreUtc = $null
$restoreFile = $archivePath + '.restore-test.json'
if (Test-Path -LiteralPath $restoreFile) {
    $restore = Get-Content -LiteralPath $restoreFile -Raw | ConvertFrom-Json
    if ($restore.passed -eq $true -and $restore.sha256 -eq $hash -and
        $restore.testDatabase -match '^stockflow_restore_test_[a-f0-9]{32}$') {
        $restoreUtc = [DateTimeOffset]::Parse($restore.checkedUtc).ToUniversalTime()
    }
}
$observed = [DateTimeOffset]::UtcNow
if ($backupUtc -gt $observed -or ($restoreUtc -and ($restoreUtc -lt $backupUtc -or $restoreUtc -gt $observed))) {
    throw 'Evidence dates are invalid; no status published.'
}
$status = [ordered]@{schemaVersion=1;observedUtc=$observed.ToString('o');backupUtc=$backupUtc.ToString('o');restoreTestUtc=$null}
if ($restoreUtc) { $status.restoreTestUtc = $restoreUtc.ToString('o') }
[IO.Directory]::CreateDirectory([IO.Path]::GetDirectoryName($target)) | Out-Null
$temporary = $target + '.' + [Guid]::NewGuid().ToString('N') + '.tmp'
try {
    $status | ConvertTo-Json | Set-Content -LiteralPath $temporary -Encoding UTF8
    Move-Item -LiteralPath $temporary -Destination $target -Force
} finally {
    if (Test-Path -LiteralPath $temporary) { Remove-Item -LiteralPath $temporary }
}
Write-Output 'Recovery status published. A database restore test does not verify application assets or business reconciliation.'
