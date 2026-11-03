param(
 [Parameter(Mandatory)][string]$DatabaseName,
 [Parameter(Mandatory)][string]$BackupDirectory,
 [string]$ServerHost='127.0.0.1', [int]$Port=5432,
 [string]$DatabaseUser='postgres',
 [string]$PostgresBin='C:\Program Files\PostgreSQL\18\bin',
 [int]$RetentionDays=30
)
$ErrorActionPreference='Stop'
$runId=[Guid]::NewGuid().ToString()
try {
 & (Join-Path $PSScriptRoot 'Backup-StockFlow.ps1') @PSBoundParameters
} catch {
 $backupFailure=$_
 # This reports an actual failed backup. Never transmit database credentials or error details.
 if($env:STOCKFLOW_JOB_ALERT_URL -and $env:STOCKFLOW_JOB_ALERT_TOKEN -and $env:STOCKFLOW_JOB_TENANT_ID) {
  try {
   $endpoint=[Uri]$env:STOCKFLOW_JOB_ALERT_URL
   if($endpoint.Scheme -ne 'https' -and !($endpoint.IsLoopback -and $endpoint.Scheme -eq 'http')) { throw 'Use HTTPS or loopback for the alert endpoint.' }
   $body=@{tenantId=[long]$env:STOCKFLOW_JOB_TENANT_ID;runId=$runId;job='backup'} | ConvertTo-Json
   Invoke-RestMethod -Uri $endpoint -Method Post -Headers @{Authorization='Bearer '+$env:STOCKFLOW_JOB_ALERT_TOKEN} -ContentType 'application/json' -Body $body -TimeoutSec 5 | Out-Null
  } catch { Write-Warning 'Backup failed and its alert could not be queued. Check the scheduled task log.' }
 }
 throw $backupFailure
}
