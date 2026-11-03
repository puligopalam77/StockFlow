# Server fakes and offline browser mocks only. Never executes migrations or app database writes.
param([switch]$SkipBrowser)
$ErrorActionPreference='Stop'
$taskRoot=Split-Path -Parent $PSScriptRoot
Set-Location -LiteralPath $taskRoot
$taskLogs=Join-Path $PSScriptRoot 'bin/catalog-review-results'
[IO.Directory]::CreateDirectory($taskLogs)|Out-Null
$taskChecks=[Collections.Generic.List[object]]::new()
function Invoke-CatalogCheck([string]$Name,[string]$Exe,[string[]]$Arguments){
    $taskLog=Join-Path $taskLogs ($Name+'.log')
    Write-Output "Checking $Name"
    & $Exe @Arguments *> $taskLog
    if($LASTEXITCODE -ne 0){Get-Content -LiteralPath $taskLog -Tail 35;throw "Check failed: $Name"}
    $taskChecks.Add([ordered]@{check=$Name;passed=$true;log=$taskLog})
}
try{
    if($SkipBrowser){& "$PSScriptRoot/Run-BikeDemoSetupChecks.ps1" -SkipBrowser}else{& "$PSScriptRoot/Run-BikeDemoSetupChecks.ps1"}
    if(!$?){throw 'Existing retail/Electrical regression checks failed'}
    $taskChecks.Add([ordered]@{check='retail-electrical-bike-regressions';passed=$true})
    $taskDll=Join-Path $PSScriptRoot 'bin/retail-phase56-build/StockFlow.MasterDataChecks.dll'
    Invoke-CatalogCheck 'server' 'dotnet' @($taskDll,'--catalog-review-checks')
    if(!$SkipBrowser){
        $taskNode=Join-Path $PSScriptRoot 'StockFlow.UiChecks/bin/Debug/net10.0/.playwright/node/win32_x64/node.exe'
        Invoke-CatalogCheck 'browser' $taskNode @('tests/StockFlow.UiChecks/catalog-review-offline.cjs')
    }
}finally{
    [ordered]@{checkedUtc=[DateTime]::UtcNow.ToString('o');checks=$taskChecks.ToArray();databaseScriptsExecuted=$false;
        databaseAcceptance='PENDING: manual 052, original-data fingerprint comparison, SQL verification and real two-shop stock acceptance.'}|ConvertTo-Json -Depth 5|Set-Content (Join-Path $taskLogs 'results.json') -Encoding UTF8
}
Write-Output "Catalog registration checks passed. Logs: $taskLogs. No SQL executed."
