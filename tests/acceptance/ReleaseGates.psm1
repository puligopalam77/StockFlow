Set-StrictMode -Version Latest
$ErrorActionPreference='Stop'

function Get-StockFlowSourceFingerprint([string]$Root){
    # No secrets, generated files, uploads or business data are read into this identity.
    $files=@(& rg --files src tests database deploy -g '!**/bin/**' -g '!**/obj/**' -g '!**/.playwright/**' -g '!**/wwwroot/uploads/**' -g '!**/wwwroot/lib/**' -g '!**/appsettings*.json' -g '!**/*.dump*' -g '!**/*.log' -g '!**/*.png' -g '!**/*.jpg' -g '!**/*.pdf' -g '!**/*.zip' -g '!**/*.xlsx' -g '!**/*.csv' -g '!**/*.mp3' -g '!**/*.wav')
    if($LASTEXITCODE -ne 0 -or !$files.Count){throw 'Could not enumerate release source inputs.'}
    $lines=foreach($file in ($files|Sort-Object)){ $file.Replace('\','/')+':'+(Get-FileHash -LiteralPath (Join-Path $Root $file) -Algorithm SHA256).Hash }
    $sha=[Security.Cryptography.SHA256]::Create()
    try{return ([BitConverter]::ToString($sha.ComputeHash([Text.Encoding]::UTF8.GetBytes(($lines -join "`n"))))).Replace('-','')}finally{$sha.Dispose()}
}

function Get-StockFlowReleaseDecision {
    param([Parameter(Mandatory)]$Definitions,[Parameter(Mandatory)][AllowEmptyCollection()]$Results,
          [Parameter(Mandatory)][string]$Fingerprint,[Parameter(Mandatory)][string]$EnvironmentName,
          [Parameter(Mandatory)][string]$Shop,[Parameter(Mandatory)][string]$ApplicationVersion,
          [Parameter(Mandatory)][string]$EvidenceDirectory,[DateTimeOffset]$Now=[DateTimeOffset]::UtcNow)
    $checks=[Collections.Generic.List[object]]::new()
    if(!@($Definitions).Count -or @($Definitions|Group-Object id|Where-Object{$_.Count -ne 1}).Count){throw 'Release gate catalog is empty or has duplicate identifiers.'}
    foreach($definition in $Definitions){
        $matches=@($Results|Where-Object{$_.id -eq $definition.id})
        $status='UNVERIFIED';$reason='No evidence recorded';$accepted=$false
        if($matches.Count -gt 1){$reason='Conflicting duplicate evidence'}
        elseif($matches.Count -eq 1){
            $r=$matches[0];$status=$r.status;$reason=$r.reason
            if($r.fingerprint -ne $Fingerprint -or $r.environment -ne $EnvironmentName -or $r.shop -ne $Shop -or $r.applicationVersion -ne $ApplicationVersion){$status='UNVERIFIED';$reason='Evidence belongs to different source/version/environment/shop'}
            elseif($status -eq 'PASSED' -or $status -eq 'ACCEPTED_ISSUE'){
                $date=[DateTimeOffset]::MinValue
                if(![DateTimeOffset]::TryParse([string]$r.checkedUtc,[ref]$date) -or $date -gt $Now.AddMinutes(5) -or $date -lt $Now.AddHours(-24)){$status='UNVERIFIED';$reason='Evidence is stale or has an invalid date'}
                elseif($r.kind -ne $definition.kind){$status='UNVERIFIED';$reason='Wrong evidence layer'}
                else{
                    $base=[IO.Path]::GetFullPath($EvidenceDirectory).TrimEnd('\','/')+[IO.Path]::DirectorySeparatorChar
                    $path=[IO.Path]::GetFullPath((Join-Path $EvidenceDirectory $r.artifact))
                    if(!$path.StartsWith($base,[StringComparison]::OrdinalIgnoreCase) -or !(Test-Path -LiteralPath $path -PathType Leaf)){$status='UNVERIFIED';$reason='Evidence artifact missing or outside this release directory'}
                    elseif((Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash -ne $r.sha256){$status='UNVERIFIED';$reason='Evidence artifact checksum mismatch'}
                    elseif($definition.kind -eq 'automated' -and ($r.exitCode -ne 0 -or $status -ne 'PASSED')){$status='FAILED';$reason='Automated process did not pass'}
                    elseif($definition.kind -eq 'manual' -and (!$r.reviewer -or !$r.authorityReference -or !$r.expectedResult -or !$r.actualResult)){$status='UNVERIFIED';$reason='Reviewer, authority reference and actual/expected results required'}
                    elseif($status -eq 'ACCEPTED_ISSUE'){
                        if($definition.critical -or !$r.mitigation -or !$r.acceptedBy){$status='UNVERIFIED';$reason='Critical failures cannot be waived; noncritical mitigation needs explicit acceptance'}
                        else{$accepted=$true}
                    }
                }
            }elseif($status -notin @('FAILED','UNVERIFIED','NOT_RUN')){$status='UNVERIFIED';$reason='Unknown evidence status'}
        }
        $checks.Add([pscustomobject]@{id=$definition.id;label=$definition.label;critical=$definition.critical;mandatory=$definition.mandatory;status=$status;reason=$reason;acceptedMitigation=$accepted})
    }
    $blockers=@($checks|Where-Object{($_.mandatory -or $_.critical) -and $_.status -ne 'PASSED' -and !$_.acceptedMitigation})
    $open=@($checks|Where-Object{$_.status -ne 'PASSED'})
    $classification=if($blockers.Count){'BLOCKED'}elseif($open.Count -and @($open|Where-Object{!$_.acceptedMitigation}).Count){'BLOCKED'}elseif($open.Count){'CONDITIONAL'}else{'READY'}
    [pscustomobject]@{classification=$classification;checks=$checks.ToArray();blockers=@($blockers|ForEach-Object{$_.id});productionDeploymentAuthorized=$false}
}
Export-ModuleMember -Function Get-StockFlowSourceFingerprint,Get-StockFlowReleaseDecision
