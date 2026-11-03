# No database scripts are executed. Existing regressions use fake repos/mock APIs.
param([switch]$SkipBrowser)
$ErrorActionPreference='Stop'
$taskRoot=Split-Path -Parent $PSScriptRoot
Set-Location -LiteralPath $taskRoot
$taskLogs=Join-Path $PSScriptRoot 'bin/bike-demo-results'
[IO.Directory]::CreateDirectory($taskLogs)|Out-Null
$taskResults=[Collections.Generic.List[object]]::new()
try {
    if($SkipBrowser){& "$PSScriptRoot/Run-MultiBusinessPhase56Checks.ps1" -SkipBrowser}
    else {& "$PSScriptRoot/Run-MultiBusinessPhase56Checks.ps1"}
    if(!$?){throw 'Retail regression runner failed'}
    $taskResults.Add([ordered]@{check='existing-retail-electrical-regressions';passed=$true})
    $taskDll=Join-Path $PSScriptRoot 'bin/retail-phase56-build/StockFlow.MasterDataChecks.dll'
    & dotnet $taskDll --bike-demo-setup-checks *> (Join-Path $taskLogs 'server.log')
    if($LASTEXITCODE -ne 0){Get-Content (Join-Path $taskLogs 'server.log') -Tail 30;throw 'Bike setup server checks failed'}
    $taskResults.Add([ordered]@{check='populated-bike-setup-server';passed=$true})
    if(!$SkipBrowser){
        $taskNode=Join-Path $PSScriptRoot 'StockFlow.UiChecks/bin/Debug/net10.0/.playwright/node/win32_x64/node.exe'
        & $taskNode tests/StockFlow.UiChecks/bike-demo-setup-offline.cjs *> (Join-Path $taskLogs 'browser.log')
        if($LASTEXITCODE -ne 0){Get-Content (Join-Path $taskLogs 'browser.log') -Tail 30;throw 'Bike setup browser checks failed'}
        $taskResults.Add([ordered]@{check='bike-setup-browser';passed=$true})
    }
}finally{
    [ordered]@{checkedUtc=[DateTime]::UtcNow.ToString('o');checks=$taskResults.ToArray();databaseScriptsExecuted=$false;databaseAcceptance='PENDING: manually apply 050 then 051, compare original-data fingerprints and run profile_data_visibility_verification.sql under both selections. Server fakes/browser mocks do not validate PostgreSQL policies.'}|ConvertTo-Json -Depth 5|Set-Content (Join-Path $taskLogs 'results.json') -Encoding UTF8
}
Write-Output "Bike demo setup checks passed. Logs: $taskLogs. No SQL executed."
