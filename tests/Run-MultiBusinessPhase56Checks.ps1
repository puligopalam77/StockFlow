# Automated server fakes and browser mocks only. No SQL or application DB access.
param([switch]$SkipBrowser)
$ErrorActionPreference = 'Stop'
$taskRoot = Split-Path -Parent $PSScriptRoot
Set-Location -LiteralPath $taskRoot
$taskOutput = Join-Path $PSScriptRoot 'bin/retail-phase56-build'
$taskLogs = Join-Path $PSScriptRoot 'bin/retail-phase56-results'
[IO.Directory]::CreateDirectory($taskLogs) | Out-Null
$taskResults = [Collections.Generic.List[object]]::new()
function Invoke-PhaseCheck([string]$Name, [string]$Executable, [string[]]$Arguments) {
    Write-Output "Checking $Name"
    $taskLog = Join-Path $taskLogs ($Name + '.log')
    $ErrorActionPreference = 'Continue'
    try { & $Executable @Arguments *> $taskLog; $taskPassed = $LASTEXITCODE -eq 0 }
    finally { $ErrorActionPreference = 'Stop' }
    $taskResults.Add([ordered]@{ check = $Name; passed = $taskPassed; log = $taskLog })
    if (!$taskPassed) { Get-Content -LiteralPath $taskLog -Tail 35; throw "Check failed: $Name" }
}
try {
    Invoke-PhaseCheck 'build' 'dotnet' @('build','tests/StockFlow.MasterDataChecks',('-p:OutputPath=' + $taskOutput + '/'),'--verbosity','quiet')
    foreach ($taskSuite in @('retail-phase56','category-attribute','business-profile-phase2','electrical-phase1')) {
        Invoke-PhaseCheck ($taskSuite + '-server') 'dotnet' @((Join-Path $taskOutput 'StockFlow.MasterDataChecks.dll'),('--' + $taskSuite + '-checks'))
    }
    if (!$SkipBrowser) {
        $taskNode = Join-Path $PSScriptRoot 'StockFlow.UiChecks/bin/Debug/net10.0/.playwright/node/win32_x64/node.exe'
        if (!(Test-Path -LiteralPath $taskNode)) { throw 'Build tests/StockFlow.UiChecks to restore Playwright, then rerun.' }
        foreach ($taskSuite in @('retail-phase56','category-attributes','business-profile-phase2','product-registration','receive-goods','purchase-receiving','billing','inventory-control','service')) {
            Invoke-PhaseCheck ($taskSuite + '-browser') $taskNode @("tests/StockFlow.UiChecks/$taskSuite-offline.cjs")
        }
    }
} finally {
    [ordered]@{
        checkedUtc = [DateTime]::UtcNow.ToString('o')
        checks = $taskResults.ToArray()
        databaseScriptsExecuted = $false
        browserChecksRequested = !$SkipBrowser
        databaseAcceptance = 'PENDING: owner manually applies 049, verifies snapshots, tenant/location isolation, rollback, concurrency and real stock totals.'
        physicalAcceptance = 'PENDING: shop scanner, printer and staff workflow verification.'
    } | ConvertTo-Json -Depth 6 | Set-Content -LiteralPath (Join-Path $taskLogs 'results.json') -Encoding UTF8
}
Write-Output "Multi-business Phases 5 and 6 automated checks passed. Results: $taskLogs. No SQL was executed."
