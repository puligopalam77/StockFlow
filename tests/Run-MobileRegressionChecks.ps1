param([switch]$WithDisposableDatabase)
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
Set-Location -LiteralPath $root
$output = Join-Path $PSScriptRoot 'bin/mobile-regression-results'
[IO.Directory]::CreateDirectory($output) | Out-Null
$results = [Collections.Generic.List[object]]::new()
function Run-Check([string]$name, [string]$executable, [string[]]$arguments) {
    Write-Output "Checking $name"
    $log = Join-Path $output ($name + '.log')
    $ErrorActionPreference = 'Continue'
    try { & $executable @arguments *> $log; $passed = $LASTEXITCODE -eq 0 }
    finally { $ErrorActionPreference = 'Stop' }
    $results.Add([ordered]@{ check = $name; passed = $passed; log = $log })
    if (!$passed) { Get-Content -LiteralPath $log -Tail 30; throw "Check failed: $name" }
}
try {
    Run-Check 'web-build' 'dotnet' @('build','StockFlow.sln','-v','quiet')
    Run-Check 'test-build' 'dotnet' @('build','tests/StockFlow.MasterDataChecks','--artifacts-path','tests/bin/assistant-style-artifacts','-v','quiet')
    Run-Check 'android-policy' 'dotnet' @('run','--project','tests/StockFlow.AndroidChecks')
    foreach ($flag in @('configuration-ui-fixtures','billing-checks','receiving-checks','purchase-receiving-checks','inventory-control-checks','idle-session-checks','shop-readiness-checks','help-panel-checks','help-voice-checks','help-business-checks')) {
        Run-Check $flag 'dotnet' @('run','--project','tests/StockFlow.MasterDataChecks','--artifacts-path','tests/bin/assistant-style-artifacts','--no-build','--',('--' + $flag))
    }
    $node = Join-Path $PSScriptRoot 'StockFlow.UiChecks/bin/Debug/net10.0/.playwright/node/win32_x64/node.exe'
    if (!(Test-Path -LiteralPath $node)) { throw 'Build StockFlow.UiChecks to obtain Playwright; Microsoft Edge is required.' }
    foreach ($suite in @('billing','receiving','purchase-receiving','inventory-control','idle-session','shop-readiness','device-features','help-recording')) {
        Run-Check ($suite + '-browser') $node @("tests/StockFlow.UiChecks/$suite-offline.cjs")
    }
    if ($WithDisposableDatabase) {
        # Exclusively a separate test PostgreSQL instance on 127.0.0.1:55441.
        # Suites create GUID-named databases, apply migrations there and drop them.
        # Never point this port at the shop database instance.
        foreach ($flag in @('billing-database-test','receiving-database-test')) {
            Run-Check $flag 'dotnet' @('run','--project','tests/StockFlow.MasterDataChecks','--artifacts-path','tests/bin/assistant-style-artifacts','--no-build','--',('--' + $flag))
        }
    }
} finally {
    [ordered]@{
        checkedUtc = [DateTime]::UtcNow.ToString('o')
        automatedChecks = $results.ToArray()
        databaseConcurrency = $(if (@($results | Where-Object { $_.check -in @('billing-database-test','receiving-database-test') -and $_.passed }).Count -eq 2) { 'PASSED: isolated sales and receiving transaction suites.' } else { 'NOT COMPLETE: requires both disposable database suites to pass on port 55441.' })
        physicalAndroidValidation = 'PENDING: live APK, hosted test shop, camera/mic, file providers and printers.'
    } | ConvertTo-Json -Depth 6 | Set-Content -LiteralPath (Join-Path $output 'results.json') -Encoding UTF8
}
Write-Output "Automated checks passed. Results: $output. Physical Android validation remains separate."
