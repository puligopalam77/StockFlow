param(
    [Parameter(Mandatory)][string]$DatabaseName,
    [Parameter(Mandatory)][string]$BackupDirectory,
    [Parameter(Mandatory)][string]$PasswordFile,
    [string]$ServerHost = '127.0.0.1',
    [ValidateRange(1,65535)][int]$Port = 5432,
    [string]$DatabaseUser = 'postgres',
    [string]$PostgresBin = 'C:\Program Files\PostgreSQL\18\bin',
    [ValidatePattern('^([01][0-9]|2[0-3]):[0-5][0-9]$')][string]$DailyAt = '21:30',
    [ValidateRange(1,3650)][int]$RetentionDays = 30
)
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
if ($DatabaseName -notmatch '^[A-Za-z0-9_][A-Za-z0-9_.-]{0,62}$') { throw 'Use a plain database name.' }
foreach ($value in @($DatabaseName,$BackupDirectory,$PasswordFile,$ServerHost,$DatabaseUser,$PostgresBin)) {
    if ($value -match '["\r\n]') { throw 'Task arguments cannot contain quotes or newlines.' }
}
$passwordPath = (Resolve-Path -LiteralPath $PasswordFile).Path
$backupScript = Join-Path $PSScriptRoot 'Run-StockFlowBackupWithAlerts.ps1'
$taskName = 'StockFlow-Backup-' + $DatabaseName
if (Get-ScheduledTask -TaskName $taskName -ErrorAction SilentlyContinue) { throw "Task $taskName already exists. Review its settings in Task Scheduler before replacing it." }
$runner = Join-Path ([IO.Path]::GetFullPath($BackupDirectory)) ('Run-StockFlowBackup-' + $DatabaseName + '.ps1')
[IO.Directory]::CreateDirectory([IO.Path]::GetDirectoryName($runner)) | Out-Null
function Quote-PowerShell([string]$value) { return "'" + $value.Replace("'", "''") + "'" }
$runnerText = @"
`$ErrorActionPreference = 'Stop'
`$env:PGPASSFILE = $(Quote-PowerShell $passwordPath)
& $(Quote-PowerShell $backupScript) -DatabaseName $(Quote-PowerShell $DatabaseName) -BackupDirectory $(Quote-PowerShell ([IO.Path]::GetFullPath($BackupDirectory))) -ServerHost $(Quote-PowerShell $ServerHost) -Port $Port -DatabaseUser $(Quote-PowerShell $DatabaseUser) -PostgresBin $(Quote-PowerShell $PostgresBin) -RetentionDays $RetentionDays
"@
Set-Content -LiteralPath $runner -Value $runnerText -Encoding UTF8
$action = New-ScheduledTaskAction -Execute 'powershell.exe' -Argument ('-NoProfile -NonInteractive -ExecutionPolicy Bypass -File "' + $runner + '"')
$trigger = New-ScheduledTaskTrigger -Daily -At $DailyAt
$principal = New-ScheduledTaskPrincipal -UserId ([Security.Principal.WindowsIdentity]::GetCurrent().Name) -LogonType S4U -RunLevel Limited
$settings = New-ScheduledTaskSettingsSet -StartWhenAvailable -MultipleInstances IgnoreNew -ExecutionTimeLimit (New-TimeSpan -Hours 2)
Register-ScheduledTask -TaskName $taskName -Action $action -Trigger $trigger -Principal $principal -Settings $settings -Description 'StockFlow daily database archive with checksum; isolated restore testing is separate; local password file; no interactive password prompts.' | Out-Null
Write-Output "Installed $taskName at $DailyAt local Windows time. Run the task once and check LastTaskResult, the archive and its manifest."
