param(
    [Parameter(Mandatory)][ValidatePattern('^p0_reconciliation_[a-f0-9]{32}$')][string]$DatabaseName,
    [Parameter(Mandatory)][int]$ProductId,
    [Parameter(Mandatory)][int]$CustomerId,
    [Parameter(Mandatory)][int]$SupplierId
)
$ErrorActionPreference='Stop'
Set-StrictMode -Version Latest
$root=Split-Path -Parent $PSScriptRoot
$output=Join-Path $PSScriptRoot ('bin/ownership-review/'+[Guid]::NewGuid().ToString('N'))
$exporter=Join-Path $root 'database/maintenance/Export-StockFlowOwnershipReview.ps1'
& $exporter -DatabaseName $DatabaseName -TenantId 1 -OutputDirectory $output -Port 55443
$rows=@(Import-Csv -LiteralPath (Join-Path $output 'master-usage.csv'))
$product=@($rows|Where-Object{$_.entity -eq 'PRODUCT' -and $_.master_id -eq $ProductId})
if($product.Count -ne 1 -or $product[0].referenced_store_count -ne '2' -or $product[0].review_state -ne 'MULTI_SHOP_REVIEW'){throw 'Transfer source/destination usage must require multi-shop review.'}
foreach($master in @(@('CUSTOMER',$CustomerId),@('SUPPLIER',$SupplierId))){
    $row=@($rows|Where-Object{$_.entity -eq $master[0] -and $_.master_id -eq $master[1]})
    if($row.Count -ne 1 -or $row[0].referenced_store_count -ne '1' -or $row[0].owner_decision -ne 'PENDING'){throw 'Source-document usage must remain evidence only, not an ownership decision.'}
}
if(@($rows|Where-Object{$_.tenant_id -ne '1' -or $_.owner_decision -ne 'PENDING'}).Count){throw 'Tenant scope or pending decision contract failed.'}
if(($rows|ConvertTo-Csv -NoTypeInformation|Out-String) -match 'P0 synthetic customer|P0 synthetic supplier'){throw 'Customer/supplier names leaked into ID-only review.'}
$manifest=Get-Content -LiteralPath (Join-Path $output 'review-evidence.json') -Raw|ConvertFrom-Json
if($manifest.databaseName -ne $DatabaseName -or $manifest.privacyVerified -or $manifest.ownershipChanged -or $manifest.productionApproval -or $manifest.sha256 -ne (Get-FileHash -LiteralPath (Join-Path $output 'master-usage.csv')).Hash -or $manifest.querySha256 -ne (Get-FileHash -LiteralPath (Join-Path $output 'review-query.sql')).Hash){throw 'Manifest scope, claims or checksum failed.'}
$refused=$false
try{& $exporter -DatabaseName $DatabaseName -TenantId 1 -OutputDirectory $output -Port 55443}catch{$refused=$true}
if(!$refused){throw 'Existing evidence could be overwritten.'}
$refused=$false
try{& $exporter -DatabaseName $DatabaseName -TenantId 1 -OutputDirectory (Join-Path $output 'wwwroot/review') -Port 55443}catch{$refused=$true}
if(!$refused){throw 'Sensitive ownership evidence could be written into public web content.'}
$priorOptions=$env:PGOPTIONS
try{
    $env:PGOPTIONS='-c role=stockflow_runtime'
    $refused=$false
    try{& $exporter -DatabaseName $DatabaseName -TenantId 1 -OutputDirectory ($output+'-restricted') -Port 55443}catch{$refused=$true}
    if(!$refused -or (Test-Path -LiteralPath (Join-Path ($output+'-restricted') 'review-evidence.json'))){throw 'A restricted RLS reader must not certify a full-history manifest.'}
}finally{$env:PGOPTIONS=$priorOptions}
Write-Output 'PASS: ID-only read-only usage triage, two-shop transfer references, tenant scope, pending decisions, checksum, no overwrite and restricted-reader refusal.'
