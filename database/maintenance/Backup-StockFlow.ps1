param(
    [Parameter(Mandatory)][string]$DatabaseName,
    [Parameter(Mandatory)][string]$BackupDirectory,
    [string]$ServerHost = '127.0.0.1',
    [ValidateRange(1,65535)][int]$Port = 5432,
    [string]$DatabaseUser = 'postgres',
    [string]$PostgresBin = 'C:\Program Files\PostgreSQL\18\bin',
    [ValidateRange(1,3650)][int]$RetentionDays = 30
)
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
if ($DatabaseName -notmatch '^[A-Za-z0-9_][A-Za-z0-9_.-]{0,62}$') { throw 'Use a plain database name.' }
$dumpTool = Join-Path $PostgresBin 'pg_dump.exe'
$restoreTool = Join-Path $PostgresBin 'pg_restore.exe'
foreach ($tool in @($dumpTool,$restoreTool)) { if (!(Test-Path -LiteralPath $tool -PathType Leaf)) { throw "Missing PostgreSQL tool: $tool" } }
$directory = [IO.Path]::GetFullPath($BackupDirectory)
[IO.Directory]::CreateDirectory($directory) | Out-Null
$prefix = 'stockflow-' + $DatabaseName + '-'
$fileName = $prefix + [DateTime]::UtcNow.ToString('yyyyMMdd-HHmmss') + '-' + [Guid]::NewGuid().ToString('N').Substring(0,8) + '.dump'
$finalPath = Join-Path $directory $fileName
$partialPath = $finalPath + '.partial'
$connection = @('--host',$ServerHost,'--port',[string]$Port,'--username',$DatabaseUser,'--no-password')
# Use PostgreSQL's password file (PGPASSFILE). Never place a password in task arguments.
try {
    & $dumpTool @connection --dbname $DatabaseName --format custom --file $partialPath
    if ($LASTEXITCODE -ne 0) { throw 'Database backup failed. Existing backups were retained.' }
    & $restoreTool --list $partialPath | Out-Null
    if ($LASTEXITCODE -ne 0) { throw 'Archive validation failed. Existing backups were retained.' }
    Move-Item -LiteralPath $partialPath -Destination $finalPath
    $manifest = [ordered]@{
        database = $DatabaseName; createdUtc = [DateTime]::UtcNow.ToString('o')
        archive = $fileName; bytes = (Get-Item -LiteralPath $finalPath).Length
        sha256 = (Get-FileHash -LiteralPath $finalPath -Algorithm SHA256).Hash
        serverHost = $ServerHost; port = $Port; format = 'PostgreSQL custom archive'
    }
    $manifest | ConvertTo-Json | Set-Content -LiteralPath ($finalPath + '.json') -Encoding UTF8
    # Rotation only touches older archives for this database inside the verified backup directory.
    $cutoff = [DateTime]::UtcNow.AddDays(-$RetentionDays)
    foreach ($old in Get-ChildItem -LiteralPath $directory -File) {
        if ($old.Name.StartsWith($prefix) -and $old.Extension -eq '.dump' -and $old.FullName -ne $finalPath -and $old.LastWriteTimeUtc -lt $cutoff) {
            if ([IO.Path]::GetDirectoryName([IO.Path]::GetFullPath($old.FullName)) -ne $directory.TrimEnd('\')) { throw 'Backup rotation path check failed.' }
            Remove-Item -LiteralPath $old.FullName
            if (Test-Path -LiteralPath ($old.FullName + '.json')) { Remove-Item -LiteralPath ($old.FullName + '.json') }
        }
    }
    Write-Output "Backup archive created and checksum recorded: $finalPath"
    Write-Output 'Recoverability remains unverified until Test-StockFlowRestore.ps1 succeeds for this archive.'
} catch {
    if (Test-Path -LiteralPath $partialPath) { Remove-Item -LiteralPath $partialPath }
    throw
}
