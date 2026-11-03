# Fake repositories and mocked browser APIs only. Never executes database scripts.
param([switch]$SkipBrowser)
$ErrorActionPreference = 'Stop'
$taskRoot = Split-Path -Parent $PSScriptRoot
Set-Location -LiteralPath $taskRoot
$taskOutput = Join-Path $PSScriptRoot 'bin/category-attributes-build'
$taskLogs = Join-Path $PSScriptRoot 'bin/category-attributes-results'
[IO.Directory]::CreateDirectory($taskLogs) | Out-Null
$taskResults = [Collections.Generic.List[object]]::new()
function Invoke-PhaseCheck([string]$Name, [string]$Executable, [string[]]$Arguments) {
    Write-Output "Checking $Name"
    $taskLog = Join-Path $taskLogs ($Name + '.log')
    $ErrorActionPreference = 'Continue'
    try { & $Executable @Arguments *> $taskLog; $taskPassed = $LASTEXITCODE -eq 0 }
    finally { $ErrorActionPreference = 'Stop' }
    $taskResults.Add([ordered]@{ check = $Name; passed = $taskPassed; log = $taskLog })
    if (!$taskPassed) { Get-Content -LiteralPath $taskLog -Tail 30; throw "Check failed: $Name" }
}
try {
    Invoke-PhaseCheck 'build' 'dotnet' @('build','tests/StockFlow.MasterDataChecks',('-p:OutputPath=' + $taskOutput + '/'),'--verbosity','quiet')
    Invoke-PhaseCheck 'attributes-server' 'dotnet' @((Join-Path $taskOutput 'StockFlow.MasterDataChecks.dll'),'--category-attribute-checks')
    Invoke-PhaseCheck 'profile-server' 'dotnet' @((Join-Path $taskOutput 'StockFlow.MasterDataChecks.dll'),'--business-profile-phase2-checks')
    Invoke-PhaseCheck 'electrical-server' 'dotnet' @((Join-Path $taskOutput 'StockFlow.MasterDataChecks.dll'),'--electrical-phase1-checks')
    if (!$SkipBrowser) {
        $taskNode = Join-Path $PSScriptRoot 'StockFlow.UiChecks/bin/Debug/net10.0/.playwright/node/win32_x64/node.exe'
        if (!(Test-Path -LiteralPath $taskNode)) { throw 'Build tests/StockFlow.UiChecks once to install the Playwright runtime, then rerun this script.' }
        foreach ($taskSuite in @('category-attributes','business-profile-phase2','product-registration','receive-goods','purchase-receiving')) {
            Invoke-PhaseCheck ($taskSuite + '-browser') $taskNode @("tests/StockFlow.UiChecks/$taskSuite-offline.cjs")
        }
    }
} finally {
    [ordered]@{
        checkedUtc = [DateTime]::UtcNow.ToString('o')
        checks = $taskResults.ToArray()
        databaseScriptsExecuted = $false
        browserChecksRequested = !$SkipBrowser
        manualDatabaseVerification = 'PENDING: owner applies 048 and verifies RLS, schema locking, deferred constraints and real catalog persistence.'
    } | ConvertTo-Json -Depth 6 | Set-Content -LiteralPath (Join-Path $taskLogs 'results.json') -Encoding UTF8
}
Write-Output "Phases 3 and 4 automated checks passed. Results: $taskLogs. No SQL was executed."
