param([switch]$WithDatabase,[switch]$WithBrowser,[switch]$WithCleanRestore,
      [string]$ApplicationVersion,[string]$EnvironmentName='LocalSynthetic',[string]$Shop='DISPOSABLE-FIXTURES',
      [string]$SignOffFile,[string[]]$Only)
$ErrorActionPreference='Stop'
Set-StrictMode -Version Latest
$root=[IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
Set-Location -LiteralPath $root
Import-Module (Join-Path $PSScriptRoot 'ReleaseGates.psm1') -Force
$catalog=Get-Content -LiteralPath (Join-Path $PSScriptRoot 'gates.json') -Raw|ConvertFrom-Json
if($Only -and @($Only|Where-Object{$_ -notin $catalog.gates.id}).Count){throw 'Unknown acceptance gate selected.'}
$runId=[Guid]::NewGuid().ToString('N')
$output=Join-Path $root ('tests/bin/release-acceptance/'+$runId)
[IO.Directory]::CreateDirectory($output)|Out-Null
$fingerprint=Get-StockFlowSourceFingerprint $root
if(!$ApplicationVersion){$ApplicationVersion='source-'+$fingerprint.Substring(0,12)}
$artifacts=Join-Path $root '.artifacts/release-acceptance'
$assembly=Join-Path $artifacts 'bin/StockFlow.MasterDataChecks/debug/StockFlow.MasterDataChecks.dll'
$node=Join-Path $root 'tests/StockFlow.UiChecks/bin/Debug/net10.0/.playwright/node/win32_x64/node.exe'
$results=[Collections.Generic.List[object]]::new()
$started=[DateTimeOffset]::UtcNow
$decision=$null
function Record($gate,[string]$status,[string]$reason,[string]$artifact='',[int]$code=-1,[double]$seconds=0){
    $entry=[pscustomobject]@{id=$gate.id;kind=$gate.kind;status=$status;reason=$reason;fingerprint=$fingerprint;applicationVersion=$ApplicationVersion;environment=$EnvironmentName;shop=$Shop;checkedUtc=[DateTimeOffset]::UtcNow.ToString('o');artifact=$artifact;sha256='';exitCode=$code;durationSeconds=$seconds;reviewer='';authorityReference='';expectedResult='';actualResult='';mitigation='';acceptedBy=''}
    if($artifact){$entry.sha256=(Get-FileHash -LiteralPath (Join-Path $output $artifact) -Algorithm SHA256).Hash}
    $entry|Add-Member -NotePropertyName layer -NotePropertyValue $gate.run
    $results.Add($entry)
    return $entry
}
function Native([string]$Program,[string[]]$Arguments,[string]$Log){
    if(!(Get-Command -Name $Program -CommandType Application -ErrorAction SilentlyContinue)){
        Set-Content -LiteralPath $Log -Value 'Required native executable is unavailable.'
        return 127
    }
    $ErrorActionPreference='Continue'
    try{& $Program @Arguments *> $Log;return $LASTEXITCODE}finally{$ErrorActionPreference='Stop'}
}
try{
    foreach($gate in $catalog.gates){
        if($gate.kind -eq 'manual'){
            $entry=Record $gate 'UNVERIFIED' 'Relevant authorized review and evidence not recorded'
            if($SignOffFile){
                # Only manual gates can be attested; an attestation cannot replace failed automation.
                $signoffs=Get-Content -LiteralPath $SignOffFile -Raw|ConvertFrom-Json
                $matches=@($signoffs|Where-Object{$_.id -eq $gate.id})
                if($matches.Count -eq 1){
                    $review=$matches[0]
                    if($review.fingerprint -eq $fingerprint -and $review.applicationVersion -eq $ApplicationVersion -and $review.environment -eq $EnvironmentName -and $review.shop -eq $Shop){
                        $proof=[IO.Path]::GetFullPath((Join-Path ([IO.Path]::GetDirectoryName([IO.Path]::GetFullPath($SignOffFile))) $review.evidenceFile))
                        if((Test-Path -LiteralPath $proof -PathType Leaf) -and (Get-FileHash -LiteralPath $proof -Algorithm SHA256).Hash -eq $review.evidenceSha256){
                            $copy=$gate.id+'-review-evidence'+[IO.Path]::GetExtension($proof)
                            Copy-Item -LiteralPath $proof -Destination (Join-Path $output $copy)
                            $entry.artifact=$copy;$entry.sha256=$review.evidenceSha256;$entry.status=$review.status;$entry.checkedUtc=$review.checkedUtc
                            $entry.reviewer=$review.reviewer;$entry.authorityReference=$review.authorityReference;$entry.expectedResult=$review.expectedResult;$entry.actualResult=$review.actualResult;$entry.mitigation=$review.mitigation;$entry.acceptedBy=$review.acceptedBy;$entry.reason='Release-operator supplied review; authority must be independently confirmed'
                        }else{$entry.reason='Manual evidence file missing or checksum differs'}
                    }else{$entry.reason='Manual review does not match this source/version/environment/shop'}
                }elseif($matches.Count -gt 1){$entry.reason='Duplicate manual attestations rejected'}
            }
            continue
        }
        $skip=if($Only -and $gate.id -notin $Only){'Not selected; remains a required gate'}
            elseif($gate.run -eq 'database' -and !$WithDatabase){'Disposable PostgreSQL checks not requested'}
            elseif($gate.run -eq 'browser' -and !$WithBrowser){'Browser checks not requested'}
            elseif($gate.run -eq 'restore' -and !($WithDatabase -and $WithCleanRestore)){'Clean restore not requested with database checks'}
            else{''}
        if(!$skip){foreach($dependency in $gate.dependsOn){if(!@($results|Where-Object{$_.id -eq $dependency -and $_.status -eq 'PASSED'}).Count){$skip='Prerequisite gate did not pass: '+$dependency;break}}}
        if($skip){Record $gate 'NOT_RUN' $skip|Out-Null;continue}
        $log=$gate.id+'.log';$path=Join-Path $output $log
        $clock=[Diagnostics.Stopwatch]::StartNew()
        Write-Output ('Checking '+$gate.id)
        switch($gate.run){
            'build'{$exit=Native 'dotnet' @('build','tests/StockFlow.MasterDataChecks','--artifacts-path',$artifacts,'-v','quiet') $path}
            {$_ -in @('dotnet','database')}{$exit=Native 'dotnet' @($assembly,('--'+$gate.target)) $path}
            'browser'{if(!(Test-Path -LiteralPath $node)){Set-Content -LiteralPath $path -Value 'Playwright runtime missing; build StockFlow.UiChecks first.';$exit=1}else{$exit=Native $node @('tests/StockFlow.UiChecks/'+$gate.target) $path}}
            'powershell'{$exit=Native 'powershell.exe' @('-NoProfile','-ExecutionPolicy','Bypass','-File',$gate.target) $path}
            'restore'{$exit=Native 'powershell.exe' @('-NoProfile','-ExecutionPolicy','Bypass','-File','tests/P0CleanRestoreChecks.ps1','-TestAssembly',$assembly) $path}
            default{throw 'Unknown gate command in trusted repository catalog.'}
        }
        $status=if($exit -eq 0){'PASSED'}else{'FAILED'}
        Record $gate $status ('Process exit code '+$exit) $log $exit ([Math]::Round($clock.Elapsed.TotalSeconds,3))|Out-Null
        Write-Output ($gate.id+': '+$status)
    }
}catch{
    # The report remains blocked even if a tool, parser, or manual evidence load fails.
    Set-Content -LiteralPath (Join-Path $output 'harness-error.log') -Value $_.Exception.Message
}finally{
    $decision=Get-StockFlowReleaseDecision -Definitions $catalog.gates -Results $results.ToArray() -Fingerprint $fingerprint -EnvironmentName $EnvironmentName -Shop $Shop -ApplicationVersion $ApplicationVersion -EvidenceDirectory $output
    if((Test-Path -LiteralPath (Join-Path $output 'harness-error.log')) -or (Get-StockFlowSourceFingerprint $root) -ne $fingerprint){$decision.classification='BLOCKED';$decision.blockers+=@('harness-error-or-source-changed')}
    $report=[ordered]@{schemaVersion=1;runId=$runId;startedUtc=$started.ToString('o');checkedUtc=[DateTimeOffset]::UtcNow.ToString('o');fingerprint=$fingerprint;applicationVersion=$ApplicationVersion;environment=$EnvironmentName;shop=$Shop;releaseScope=$catalog.releaseScope;classification=$decision.classification;productionDeploymentAuthorized=$false;automatedEvidenceIsSynthetic=$true;checks=$decision.checks;blockers=$decision.blockers;evidence=$results.ToArray()}
    $report|ConvertTo-Json -Depth 10|Set-Content -LiteralPath (Join-Path $output 'release-report.json') -Encoding UTF8
    $summary=@(('StockFlow release acceptance: '+$decision.classification),('Version: '+$ApplicationVersion),('Environment: '+$EnvironmentName),('Shop: '+$Shop),('Source: '+$fingerprint),'No production deployment authorized.','')
    foreach($check in $decision.checks){$summary+=($check.id+': '+$check.status+' - '+$check.reason)}
    $summary|Set-Content -LiteralPath (Join-Path $output 'release-report.txt') -Encoding UTF8
}
Write-Output ('Release classification: '+$decision.classification+'; evidence: '+$output)
if($decision.classification -eq 'READY'){exit 0}
if($decision.classification -eq 'CONDITIONAL'){exit 3}
exit 2
