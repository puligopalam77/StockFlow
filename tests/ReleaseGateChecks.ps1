$ErrorActionPreference='Stop'
Set-StrictMode -Version Latest
Import-Module (Join-Path $PSScriptRoot 'acceptance/ReleaseGates.psm1') -Force
$directory=Join-Path $PSScriptRoot ('bin/release-gate-checks/'+[Guid]::NewGuid().ToString('N'))
[IO.Directory]::CreateDirectory($directory)|Out-Null
Set-Content -LiteralPath (Join-Path $directory 'fixture.log') -Value 'SYNTHETIC classifier test artifact, never shop acceptance.'
$hash=(Get-FileHash -LiteralPath (Join-Path $directory 'fixture.log') -Algorithm SHA256).Hash
$now=[DateTimeOffset]::UtcNow
$definitions=@(
 [pscustomobject]@{id='build';label='Build';kind='automated';mandatory=$true;critical=$true},
 [pscustomobject]@{id='money';label='Financial reconciliation';kind='automated';mandatory=$true;critical=$true},
 [pscustomobject]@{id='owner';label='Owner review';kind='manual';mandatory=$true;critical=$true},
 [pscustomobject]@{id='cosmetic';label='Noncritical issue';kind='manual';mandatory=$true;critical=$false})
function Result([string]$id,[string]$kind='automated'){
 [pscustomobject]@{id=$id;kind=$kind;status='PASSED';reason='Synthetic test only';fingerprint='TEST';applicationVersion='test';environment='ClassifierFixture';shop='NotARealShop';checkedUtc=$now.ToString('o');artifact='fixture.log';sha256=$hash;exitCode=0;reviewer='Synthetic reviewer';authorityReference='Fixture, not an authorization';expectedResult='Synthetic expectation';actualResult='Synthetic observation';mitigation='';acceptedBy=''}
}
function Decide($results){Get-StockFlowReleaseDecision -Definitions $definitions -Results $results -Fingerprint 'TEST' -ApplicationVersion 'test' -EnvironmentName 'ClassifierFixture' -Shop 'NotARealShop' -EvidenceDirectory $directory -Now $now}
function Check([bool]$value,[string]$message){if(!$value){throw 'FAIL: '+$message};Write-Output ('PASS: '+$message)}
Check ((Decide @()).classification -eq 'BLOCKED') 'Empty evidence blocks release'
Check ((Decide @((Result 'build'))).classification -eq 'BLOCKED') 'Build-only evidence never means ready'
$all=@((Result 'build'),(Result 'money'),(Result 'owner' 'manual'),(Result 'cosmetic' 'manual'))
Check ((Decide $all).classification -eq 'READY') 'Complete matching evidence permits READY for its exact fixture scope'
$all[1].status='FAILED'
Check ((Decide $all).classification -eq 'BLOCKED') 'Material financial failure blocks release'
$all[1].status='ACCEPTED_ISSUE';$all[1].mitigation='Pretend waiver';$all[1].acceptedBy='Synthetic reviewer'
Check ((Decide $all).classification -eq 'BLOCKED') 'Critical financial failure cannot be waived'
$all[1]=Result 'money';$all[3].status='ACCEPTED_ISSUE';$all[3].mitigation='Controlled pilot workaround';$all[3].acceptedBy='Synthetic reviewer'
Check ((Decide $all).classification -eq 'CONDITIONAL') 'Only explicitly accepted noncritical issues permit CONDITIONAL'
$all[3].acceptedBy=''
Check ((Decide $all).classification -eq 'BLOCKED') 'Unaccepted mitigation blocks release'
$all[3]=Result 'cosmetic' 'manual';$all[2].reviewer=''
Check ((Decide $all).classification -eq 'BLOCKED') 'Missing reviewer cannot count as sign-off'
$all[2]=Result 'owner' 'manual';$all[1].shop='OtherShop'
Check ((Decide $all).classification -eq 'BLOCKED') 'Another shop evidence cannot satisfy this release'
$all[1]=Result 'money';$all[1].fingerprint='OLD'
Check ((Decide $all).classification -eq 'BLOCKED') 'Older source fingerprint cannot satisfy this release'
$all[1]=Result 'money';$all[1].checkedUtc=$now.AddDays(-2).ToString('o')
Check ((Decide $all).classification -eq 'BLOCKED') 'Stale evidence blocks release'
$all[1]=Result 'money';$all[1].checkedUtc=$now.AddHours(1).ToString('o')
Check ((Decide $all).classification -eq 'BLOCKED') 'Future evidence rejected'
$all[1]=Result 'money';$all[1].exitCode=1
Check ((Decide $all).classification -eq 'BLOCKED') 'Passed label cannot conceal failed process exit'
$all[1]=Result 'money'
Check ((Decide ($all+@((Result 'money')))).classification -eq 'BLOCKED') 'Duplicate conflicting evidence rejected'
$all[1].artifact='../missing.log'
Check ((Decide $all).classification -eq 'BLOCKED') 'Evidence path traversal rejected'
$all[1]=Result 'money';Set-Content -LiteralPath (Join-Path $directory 'fixture.log') -Value 'Changed after acceptance'
Check ((Decide $all).classification -eq 'BLOCKED') 'Changed evidence artifact invalidates previous pass'
Check (!(Decide $all).productionDeploymentAuthorized) 'Classification never authorizes deployment'
Write-Output 'Release-gate policy tests passed. All approvals/data above are synthetic; no shop was signed off.'
