# Exercise the real installer with mocked Task Scheduler commands. No task is registered.
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$directory = Join-Path $PSScriptRoot ('bin/backup-task-checks/' + [Guid]::NewGuid().ToString('N'))
[IO.Directory]::CreateDirectory($directory) | Out-Null
$passwordFile = Join-Path $directory "operator's password file.conf"
Set-Content -LiteralPath $passwordFile -Value 'test-only-placeholder' -Encoding UTF8
$global:StockFlowTestRegisteredTask = $null
$global:StockFlowTestTaskExists = $false
function Get-ScheduledTask { param($TaskName,$ErrorAction) if ($global:StockFlowTestTaskExists) { return @{ TaskName = $TaskName } } }
function New-ScheduledTaskAction { param($Execute,$Argument) return @{ Execute = $Execute; Arguments = $Argument } }
function New-ScheduledTaskTrigger { param([switch]$Daily,$At) return @{ Daily = [bool]$Daily; At = $At } }
function New-ScheduledTaskPrincipal { param($UserId,$LogonType,$RunLevel) return @{ UserId = $UserId; LogonType = $LogonType; RunLevel = $RunLevel } }
function New-ScheduledTaskSettingsSet { param([switch]$StartWhenAvailable,$MultipleInstances,$ExecutionTimeLimit) return @{ StartWhenAvailable = [bool]$StartWhenAvailable; MultipleInstances = $MultipleInstances } }
function Register-ScheduledTask {
    param($TaskName,$Action,$Trigger,$Principal,$Settings,$Description)
    $global:StockFlowTestRegisteredTask = @{ TaskName = $TaskName; Action = $Action; Trigger = $Trigger; Principal = $Principal; Settings = $Settings }
}
$installer = Join-Path $root 'database/maintenance/Install-StockFlowBackupTask.ps1'
& $installer -DatabaseName 'test_shop' -BackupDirectory $directory -PasswordFile $passwordFile -DailyAt '22:15'
if (!$global:StockFlowTestRegisteredTask -or $global:StockFlowTestRegisteredTask.TaskName -ne 'StockFlow-Backup-test_shop') { throw 'Expected task was not generated.' }
if ($global:StockFlowTestRegisteredTask.Trigger.At -ne '22:15' -or $global:StockFlowTestRegisteredTask.Principal.LogonType -ne 'S4U') { throw 'Unattended daily task settings are incorrect.' }
if ($global:StockFlowTestRegisteredTask.Settings.MultipleInstances -ne 'IgnoreNew') { throw 'Concurrent backups were not prevented.' }
if ($global:StockFlowTestRegisteredTask.Action.Arguments -notlike '*-NoProfile -NonInteractive*') { throw 'Task could open an interactive prompt.' }
$runner = Join-Path $directory 'Run-StockFlowBackup-test_shop.ps1'
$tokens = $null
$parseErrors = $null
[Management.Automation.Language.Parser]::ParseFile($runner,[ref]$tokens,[ref]$parseErrors) | Out-Null
if ($parseErrors.Count) { throw 'Generated backup runner is invalid PowerShell.' }
$original = Get-Content -LiteralPath $runner -Raw
if (!$original.Contains("operator''s password file.conf")) { throw 'Password-file path was not safely quoted.' }
$global:StockFlowTestTaskExists = $true
$rejected = $false
try { & $installer -DatabaseName 'test_shop' -BackupDirectory $directory -PasswordFile $passwordFile }
catch { $rejected = $_.Exception.Message -like '*already exists*' }
if (!$rejected -or (Get-Content -LiteralPath $runner -Raw) -ne $original) { throw 'Existing task or runner could be overwritten.' }
foreach ($file in Get-ChildItem -LiteralPath (Join-Path $root 'database/maintenance') -Filter '*.ps1') {
    [Management.Automation.Language.Parser]::ParseFile($file.FullName,[ref]$tokens,[ref]$parseErrors) | Out-Null
    if ($parseErrors.Count) { throw "Invalid maintenance script: $($file.Name)" }
}
Write-Output 'PASS: generated daily backup task, unattended settings, quoted paths, refusal to replace existing task and maintenance script syntax. No scheduled task was installed.'
