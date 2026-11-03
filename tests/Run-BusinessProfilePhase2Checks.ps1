# Fake repositories and mocked browser APIs only. No SQL or migrations are run.
param([switch]$SkipBrowser)
$ErrorActionPreference = 'Stop'
$taskRoot = Split-Path -Parent $PSScriptRoot
Set-Location -LiteralPath $taskRoot
$taskOutput = Join-Path $PSScriptRoot 'bin/business-profile-phase2-build'
$taskLogs = Join-Path $PSScriptRoot 'bin/business-profile-phase2-results'
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
    # Separate output avoids replacing a DLL loaded by the IDE debugger.
    Invoke-PhaseCheck 'build' 'dotnet' @('build','tests/StockFlow.MasterDataChecks',('-p:OutputPath=' + $taskOutput + '/'),'--verbosity','quiet')
    Invoke-PhaseCheck 'profile-server' 'dotnet' @((Join-Path $taskOutput 'StockFlow.MasterDataChecks.dll'),'--business-profile-phase2-checks')
    Invoke-PhaseCheck 'electrical-server' 'dotnet' @((Join-Path $taskOutput 'StockFlow.MasterDataChecks.dll'),'--electrical-phase1-checks')
    if (!$SkipBrowser) {
        $taskNode = Join-Path $PSScriptRoot 'StockFlow.UiChecks/bin/Debug/net10.0/.playwright/node/win32_x64/node.exe'
        if (!(Test-Path -LiteralPath $taskNode)) { throw 'Build tests/StockFlow.UiChecks once to install the Playwright runtime, then rerun this script.' }
        foreach ($taskSuite in @('business-profile-phase2','product-registration','receive-goods','purchase-receiving')) {
            Invoke-PhaseCheck ($taskSuite + '-browser') $taskNode @("tests/StockFlow.UiChecks/$taskSuite-offline.cjs")
        }
    }
} finally {
    [ordered]@{
        checkedUtc = [DateTime]::UtcNow.ToString('o')
        checks = $taskResults.ToArray()
        databaseScriptsExecuted = $false
        browserChecksRequested = !$SkipBrowser
        manualDatabaseVerification = 'PENDING: owner applies migration 047 and compares before/after baseline.'
        physicalShopValidation = 'PENDING: scanner, printer and real stock acceptance at the shop.'
    } | ConvertTo-Json -Depth 6 | Set-Content -LiteralPath (Join-Path $taskLogs 'results.json') -Encoding UTF8
}
Write-Output "Phase 2 automated checks passed. Results: $taskLogs. No SQL was executed."
