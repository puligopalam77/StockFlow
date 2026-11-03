param(
    [Parameter(Mandatory)][string]$Keystore,
    [string]$KeyAlias = 'stockflow',
    [string]$AndroidSdkDirectory = 'E:/StockFlow/.artifacts/android-sdk',
    [string]$JavaSdkDirectory = 'E:/StockFlow/.artifacts/android-jdk'
)
$ErrorActionPreference = 'Stop'
if (!$env:STOCKFLOW_STORE_PASSWORD -or !$env:STOCKFLOW_KEY_PASSWORD) { throw 'Load STOCKFLOW_STORE_PASSWORD and STOCKFLOW_KEY_PASSWORD from your private signing secret store.' }
$root = Split-Path -Parent $PSScriptRoot
Set-Location -LiteralPath $root
$keyPath = (Resolve-Path -LiteralPath $Keystore).Path
$sdkPath = (Resolve-Path -LiteralPath $AndroidSdkDirectory).Path
$jdkPath = (Resolve-Path -LiteralPath $JavaSdkDirectory).Path
$output = Join-Path $root '.artifacts/release'
[IO.Directory]::CreateDirectory($output) | Out-Null
& dotnet publish src/StockFlow.Web/StockFlow.Web.csproj -c Release -o (Join-Path $output 'web') -p:StockFlowReleaseBundle=true
if ($LASTEXITCODE -ne 0) { throw 'Web publish failed.' }
if (Get-ChildItem -LiteralPath (Join-Path $output 'web') -Filter 'appsettings*.json') { throw 'Local settings remain in the staging bundle. Use a clean staging directory; do not distribute these files.' }
& dotnet build src/StockFlow.Android/StockFlow.Android.csproj -c Release -t:SignAndroidPackage "-p:AndroidSdkDirectory=$sdkPath" "-p:JavaSdkDirectory=$jdkPath" -p:AndroidKeyStore=true "-p:AndroidSigningKeyStore=$keyPath" "-p:AndroidSigningKeyAlias=$KeyAlias" -p:AndroidSigningStorePass=env:STOCKFLOW_STORE_PASSWORD -p:AndroidSigningKeyPass=env:STOCKFLOW_KEY_PASSWORD
if ($LASTEXITCODE -ne 0) { throw 'APK publish failed.' }
$apk = Join-Path $root 'src/StockFlow.Android/bin/Release/net10.0-android/com.stockflow.mobile-Signed.apk'
if (!(Test-Path -LiteralPath $apk)) { throw 'Signed Release APK not found.' }
$signer = Get-ChildItem -LiteralPath (Join-Path $sdkPath 'build-tools') -Directory | Sort-Object Name -Descending | Select-Object -First 1
& (Join-Path $jdkPath 'bin/java.exe') -jar (Join-Path $signer.FullName 'lib/apksigner.jar') verify --verbose $apk
if ($LASTEXITCODE -ne 0) { throw 'APK signature verification failed.' }
Copy-Item -LiteralPath $apk -Destination (Join-Path $output 'StockFlow.apk')
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'Caddyfile'),(Join-Path $PSScriptRoot 'Start-StockFlow.ps1') -Destination $output
$hash = (Get-FileHash -LiteralPath (Join-Path $output 'StockFlow.apk') -Algorithm SHA256).Hash
Set-Content -LiteralPath (Join-Path $output 'StockFlow.apk.sha256') -Value "$hash  StockFlow.apk" -Encoding ASCII
Write-Output "Release files: $output. Back up the release key before distributing the APK."
