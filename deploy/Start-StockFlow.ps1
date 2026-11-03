param(
    [Parameter(Mandatory)][string]$Domain,
    [Parameter(Mandatory)][string]$WebDirectory
)
$ErrorActionPreference = 'Stop'
if ($Domain -notmatch '^(?=.{1,253}$)[a-zA-Z0-9](?:[a-zA-Z0-9.-]*[a-zA-Z0-9])?$' -or !$Domain.Contains('.') -or $Domain.Contains('..')) { throw 'Supply a public hostname, without https:// or a path.' }
if (!$env:ConnectionStrings__DefaultConnection) { throw 'Set ConnectionStrings__DefaultConnection through the hosting secret store before starting.' }
$webRoot = (Resolve-Path -LiteralPath $WebDirectory).Path
if (!(Test-Path -LiteralPath (Join-Path $webRoot 'StockFlow.Web.dll'))) { throw 'Published StockFlow.Web.dll not found.' }
$env:ASPNETCORE_ENVIRONMENT = 'Production'
$env:ASPNETCORE_URLS = 'http://127.0.0.1:5220'
$env:AllowedHosts = $Domain
$env:SessionSecurity__TrustedProxies__0 = '127.0.0.1'
$env:STOCKFLOW_DOMAIN = $Domain
Set-Location -LiteralPath $webRoot
# Run under the hosting service account. Configure its service manager to restart it.
& dotnet (Join-Path $webRoot 'StockFlow.Web.dll')
exit $LASTEXITCODE
