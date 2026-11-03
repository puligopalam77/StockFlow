param([switch]$WithDatabase,[switch]$ReleaseAcceptance,[switch]$WithBrowser,[switch]$WithCleanRestore,
      [string]$ApplicationVersion,[string]$EnvironmentName='LocalSynthetic',[string]$Shop='DISPOSABLE-FIXTURES',
      [string]$SignOffFile,[string[]]$Only)
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
if($ReleaseAcceptance){
    & (Join-Path $PSScriptRoot 'acceptance/Invoke-ReleaseAcceptance.ps1') -WithDatabase:$WithDatabase -WithBrowser:$WithBrowser -WithCleanRestore:$WithCleanRestore -ApplicationVersion $ApplicationVersion -EnvironmentName $EnvironmentName -Shop $Shop -SignOffFile $SignOffFile -Only $Only
    exit $LASTEXITCODE
}
Set-Location -LiteralPath $root
$results = [Collections.Generic.List[object]]::new()
$output = Join-Path $PSScriptRoot 'bin/shop-pilot-results'
[IO.Directory]::CreateDirectory($output) | Out-Null
function Run-Check([string]$name, [string]$executable, [string[]]$arguments) {
    Write-Output "Checking $name"
    $log = Join-Path $output ($name + '.log')
    # Native stderr must be logged before deciding whether the process failed.
    $ErrorActionPreference = 'Continue'
    try { & $executable @arguments *> $log; $passed = $LASTEXITCODE -eq 0 }
    finally { $ErrorActionPreference = 'Stop' }
    $results.Add([ordered]@{ check = $name; passed = $passed; log = $log })
    if (!$passed) { Get-Content -LiteralPath $log -Tail 25; throw "Check failed: $name" }
}
try {
    Run-Check 'build' 'dotnet' @('build','StockFlow.sln','--verbosity','quiet')
    Run-Check 'test-build' 'dotnet' @('build','tests/StockFlow.MasterDataChecks','--verbosity','quiet')
    Run-Check 'backup-task' 'powershell.exe' @('-NoProfile','-ExecutionPolicy','Bypass','-File','tests/BackupTaskChecks.ps1')
    Run-Check 'server' 'dotnet' @('run','--project','tests/StockFlow.MasterDataChecks','--no-build','--')
    # These flags render fixtures needed by the browser suites, using fake repositories.
    foreach ($flag in @('screen-language-checks','configuration-ui-fixtures','variant-ui-fixtures','service-ui-fixtures','security-ui-fixtures','dashboard-ui-fixtures','navigation-checks','reports-checks')) {
        Run-Check $flag 'dotnet' @('run','--project','tests/StockFlow.MasterDataChecks','--no-build','--',('--' + $flag))
    }
    $node = Join-Path $PSScriptRoot 'StockFlow.UiChecks/bin/Debug/net10.0/.playwright/node/win32_x64/node.exe'
    if (!(Test-Path -LiteralPath $node)) { throw 'Build StockFlow.UiChecks to obtain the Playwright Node runtime.' }
    foreach ($suite in @('field-validation','screen-languages','shop-readiness','billing','receiving','stock-receipts','purchase-receiving','inventory-control','labels','registration','product-registration','brand-categories','variants','configuration','service','stores','navigation','reports','period-report','security','rollout-admin','dashboard','invoice-design','idle-session','account-sessions')) {
        Run-Check ($suite + '-browser') $node @("tests/StockFlow.UiChecks/$suite-offline.cjs")
    }
    if ($WithDatabase) {
        # Requires a disposable PostgreSQL server on loopback port 55441.
        # This test creates and removes only GUID-named test databases.
        Run-Check 'database-backup-restore' 'dotnet' @('run','--project','tests/StockFlow.MasterDataChecks','--no-build','--','--shop-readiness-database-test')
    }
} finally {
    [ordered]@{
        checkedUtc = [DateTime]::UtcNow.ToString('o')
        automatedChecks = $results.ToArray()
        physicalShopValidation = 'PENDING: scanner, printer, two workstations and staff acceptance must be checked at the shop.'
    } | ConvertTo-Json -Depth 6 | Set-Content -LiteralPath (Join-Path $output 'results.json') -Encoding UTF8
}
Write-Output "Automated checks passed. Results: $output. Complete /Admin/Pilot at the actual shop before sign-off."
