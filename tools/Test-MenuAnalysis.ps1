#requires -Version 7.0
[CmdletBinding()]
param([string]$GameRoot,[string]$MapsRoot,[string]$OutputDirectory)
$ErrorActionPreference='Stop'
$workspace=Split-Path $PSScriptRoot -Parent
. (Join-Path $PSScriptRoot 'LocalSettings.ps1')
$previousArtifactRoot=$env:BEATINSIGHT_LOCAL_ROOT
try {
$env:BEATINSIGHT_LOCAL_ROOT=Get-BeatInsightLocalRoot
$GameRoot=Get-BeatInsightSetting GameRoot $GameRoot
$MapsRoot=Get-BeatInsightSetting MapsRoot $MapsRoot $GameRoot
if(!$OutputDirectory){$OutputDirectory=Join-Path (Get-BeatInsightLocalRoot) ('menu-validation/'+(Get-Date -Format 'yyyyMMdd-HHmmss'))}
$OutputDirectory=Resolve-BeatInsightOutputDirectory $OutputDirectory
if(Test-Path -LiteralPath $OutputDirectory){throw 'Choose a new output directory.'}
New-Item -ItemType Directory -Path $OutputDirectory|Out-Null
$missingManifestMaps=[Collections.Generic.List[object]]::new()
function Get-AvailableManifestCases([string]$Name){
    $manifest=Get-Content -LiteralPath (Join-Path $workspace "tests/$Name") -Raw|ConvertFrom-Json
    foreach($selected in $manifest.maps){
        $folder=Join-Path $MapsRoot $selected.Folder
        if(Test-Path -LiteralPath (Join-Path $folder $selected.File) -PathType Leaf){
            @{Folder=$folder;Characteristic=$selected.Characteristic;Difficulty=$selected.Difficulty;File=$selected.File}
        }else{
            $missingManifestMaps.Add(@{manifest=$Name;Folder=$selected.Folder;Characteristic=$selected.Characteristic;Difficulty=$selected.Difficulty;File=$selected.File})
            Write-Host "Skipped missing map fixture: $($selected.Folder) / $($selected.Difficulty)"
        }
    }
}
$cache=Get-BeatInsightSetting NuGetPackageRoot
foreach($project in @('BeatInsight.Checks','BeatInsight.MenuChecks','BeatInsight.MemoryChecks')){
    $extra=@();if($project -eq 'BeatInsight.MenuChecks'){$extra=@('--','--preview',(Join-Path $OutputDirectory 'ui-preview.svg'))}
    & dotnet run --project (Join-Path $workspace "tests/$project") --artifacts-path (Join-Path $OutputDirectory '.checks') "-p:RestoreSources=$cache" -p:NuGetAudit=false @extra | Tee-Object -FilePath (Join-Path $OutputDirectory "$project.txt")
    if($LASTEXITCODE){throw "Checks failed: $project"}
}
& (Join-Path $PSScriptRoot 'Test-MapSelection.ps1') -OutputDirectory (Join-Path $OutputDirectory 'map-selection')
$artifacts=Join-Path $OutputDirectory '.oracle'
& dotnet build (Join-Path $PSScriptRoot 'BeatInsight.Compare') --artifacts-path $artifacts --source $cache -p:NuGetAudit=false "-p:GameRoot=$GameRoot" --verbosity quiet
if($LASTEXITCODE){throw 'Comparison build failed.'}
& (Join-Path $PSScriptRoot 'Test-LocalArtifacts.ps1') -Binary (Join-Path $artifacts 'bin/BeatInsight.Compare/debug/BeatInsight.Compare.dll')

& dotnet (Join-Path $artifacts 'bin/BeatInsight.Compare/debug/BeatInsight.Compare.dll') --check-ordinary-warning-cost (Join-Path $OutputDirectory 'ordinary-warning-cost.json')
if($LASTEXITCODE){throw 'Ordinary warning allocation/reset checks failed.'}
& dotnet (Join-Path $artifacts 'bin/BeatInsight.Compare/debug/BeatInsight.Compare.dll') $GameRoot --check-ordinary-warning-timing (Join-Path $OutputDirectory 'ordinary-warning-timing.json')
if($LASTEXITCODE){throw 'Loaded obstacle seconds/beat warning checks failed.'}
& dotnet (Join-Path $artifacts 'bin/BeatInsight.Compare/debug/BeatInsight.Compare.dll') --check-noodle-track-inventory (Join-Path $OutputDirectory 'noodle-track-inventory.json')
if($LASTEXITCODE){throw 'Structural track dependency checks failed.'}
& dotnet (Join-Path $artifacts 'bin/BeatInsight.Compare/debug/BeatInsight.Compare.dll') $GameRoot --check-vanilla-walls (Join-Path $OutputDirectory 'vanilla-walls.json')
if($LASTEXITCODE){throw 'Ordinary wall layer conversion/spawn checks failed.'}
& dotnet (Join-Path $artifacts 'bin/BeatInsight.Compare/debug/BeatInsight.Compare.dll') $GameRoot --check-noodle-row-copy (Join-Path $OutputDirectory 'noodle-row-copy.json')
if($LASTEXITCODE){throw 'Installed row/copy/mirror composition checks failed.'}
& dotnet (Join-Path $artifacts 'bin/BeatInsight.Compare/debug/BeatInsight.Compare.dll') $GameRoot --check-heck-map (Join-Path $OutputDirectory 'heck-map.json')
if($LASTEXITCODE){throw 'Installed Heck map deserialization checks failed.'}
$visualCases=Join-Path $OutputDirectory 'visual-track-cases'
New-Item -ItemType Directory -Path $visualCases|Out-Null
$visualIndex=0
foreach($selected in @(Get-AvailableManifestCases 'noodle-visual-track-maps.json')){
    @{status='visual-track-fixture';request=$selected} |
        ConvertTo-Json -Depth 5|Set-Content -LiteralPath (Join-Path $visualCases ('{0:D3}.result.json' -f $visualIndex++)) -Encoding utf8NoBOM
}
& dotnet (Join-Path $artifacts 'bin/BeatInsight.Compare/debug/BeatInsight.Compare.dll') $GameRoot --check-noodle-visual-tracks (Join-Path $OutputDirectory 'noodle-visual-tracks.json') $visualCases
if($LASTEXITCODE){throw 'Isolated visual note/arc track comparisons failed.'}
& dotnet (Join-Path $artifacts 'bin/BeatInsight.Compare/debug/BeatInsight.Compare.dll') $GameRoot --check-heck-registrar (Join-Path $OutputDirectory 'heck-registrar.json')
if($LASTEXITCODE){throw 'Installed registrar constructor/delegate/Dispose checks failed.'}
& dotnet (Join-Path $artifacts 'bin/BeatInsight.Compare/debug/BeatInsight.Compare.dll') $GameRoot --check-native-field-accessors (Join-Path $OutputDirectory 'native-field-accessors.json')
if($LASTEXITCODE){throw 'Installed byref field accessor checks failed.'}
& dotnet (Join-Path $artifacts 'bin/BeatInsight.Compare/debug/BeatInsight.Compare.dll') $GameRoot --check-noodle-installer (Join-Path $OutputDirectory 'noodle-installer.json')
if($LASTEXITCODE){throw 'Installed Noodle player binding/pool initialization checks failed.'}
$installerReport=Get-Content -LiteralPath (Join-Path $OutputDirectory 'noodle-installer.json') -Raw|ConvertFrom-Json
$stressRows=@($installerReport.results|Where-Object {$_.active -eq $true})
if($stressRows.Count -ne 4){throw 'Installed movement pool stress matrix is incomplete.'}
foreach($row in $stressRows){
    $stress=$row.poolStress
    if(!$stress -or $stress.rounds -ne 8 -or $stress.concurrent -ne 64 -or $stress.spawns -ne 512 -or
       $stress.overrideControls -ne 4096 -or $stress.capacity -lt 64 -or $stress.cleanSweep -ne $stress.capacity -or
       $stress.active -ne 0 -or $stress.inactive -ne $stress.capacity){throw 'Installed movement pool growth/reuse evidence is incomplete.'}
}
& dotnet (Join-Path $artifacts 'bin/BeatInsight.Compare/debug/BeatInsight.Compare.dll') $GameRoot --check-heck-copy (Join-Path $OutputDirectory 'heck-copy.json')
if($LASTEXITCODE){throw 'Installed transformed Heck map and reload binding checks failed.'}
& dotnet (Join-Path $artifacts 'bin/BeatInsight.Compare/debug/BeatInsight.Compare.dll') $GameRoot --check-heck-native-accessors (Join-Path $OutputDirectory 'heck-native-accessors.json')
if($LASTEXITCODE){throw 'Native movement attachment and custom data accessor matrix failed.'}
& dotnet (Join-Path $artifacts 'bin/BeatInsight.Compare/debug/BeatInsight.Compare.dll') --check-native-accessor-parity (Join-Path $OutputDirectory 'heck-copy.json') (Join-Path $OutputDirectory 'heck-native-accessors.json') (Join-Path $OutputDirectory 'native-accessor-parity.json')
if($LASTEXITCODE){throw 'Native accessor results differ from adapter baseline.'}
& dotnet (Join-Path $artifacts 'bin/BeatInsight.Compare/debug/BeatInsight.Compare.dll') $GameRoot --check-noodle-row-loader (Join-Path $OutputDirectory 'noodle-row-loader.json')
if($LASTEXITCODE){throw 'Installed custom loader and Noodle row composition checks failed.'}
$emptyWallCases=Join-Path $OutputDirectory 'empty-wall-cases.json'
$emptyWallRequests=@(Get-AvailableManifestCases 'noodle-empty-wall-maps.json')
ConvertTo-Json -InputObject $emptyWallRequests -Depth 5|Set-Content -LiteralPath $emptyWallCases -Encoding utf8NoBOM
& dotnet (Join-Path $artifacts 'bin/BeatInsight.Compare/debug/BeatInsight.Compare.dll') $GameRoot --check-noodle-empty-walls (Join-Path $OutputDirectory 'noodle-empty-walls.json') $emptyWallCases
if($LASTEXITCODE){throw 'Independent empty wall counter checks failed.'}
& dotnet (Join-Path $artifacts 'bin/BeatInsight.Compare/debug/BeatInsight.Compare.dll') $GameRoot --check-noodle-format-routing (Join-Path $OutputDirectory 'noodle-format-routing.json')
if($LASTEXITCODE){throw 'Installed custom format routing boundary checks failed.'}
& dotnet (Join-Path $artifacts 'bin/BeatInsight.Compare/debug/BeatInsight.Compare.dll') $GameRoot --check-noodle-wall-intervals (Join-Path $OutputDirectory 'noodle-wall-intervals.json')
if($LASTEXITCODE){throw 'Native negative wall interval boundary checks failed.'}
$combinedCases=Join-Path $OutputDirectory 'combined-density-cases.json'
$combinedRequests=@(Get-AvailableManifestCases 'noodle-combined-density-maps.json')
ConvertTo-Json -InputObject $combinedRequests -Depth 5|Set-Content -LiteralPath $combinedCases -Encoding utf8NoBOM
& dotnet (Join-Path $artifacts 'bin/BeatInsight.Compare/debug/BeatInsight.Compare.dll') $GameRoot --check-noodle-combined-density (Join-Path $OutputDirectory 'noodle-combined-density.json') $combinedCases
if ($LASTEXITCODE -ne 0) { throw 'Combined density checks failed' }
$globalRowCases=Join-Path $OutputDirectory 'global-row-cases.json'
$globalRowRequests=@(Get-AvailableManifestCases 'noodle-global-row-audit-maps.json')
ConvertTo-Json -InputObject $globalRowRequests -Depth 5|Set-Content -LiteralPath $globalRowCases -Encoding utf8NoBOM
if($globalRowRequests.Count){
& dotnet (Join-Path $artifacts 'bin/BeatInsight.Compare/debug/BeatInsight.Compare.dll') $GameRoot --audit-noodle-global-rows (Join-Path $OutputDirectory 'noodle-global-rows.json') $globalRowCases
if($LASTEXITCODE){throw 'Global row patch compositions changed observable note results.'}
}else{
    @{status='skipped';reason='No available source fixtures'}|ConvertTo-Json|Set-Content -LiteralPath (Join-Path $OutputDirectory 'noodle-global-rows.json') -Encoding utf8NoBOM
}
$globalChainCases=Join-Path $OutputDirectory 'global-chain-row-cases.json'
$globalChainRequests=@(Get-AvailableManifestCases 'noodle-global-chain-row-maps.json')
ConvertTo-Json -InputObject $globalChainRequests -Depth 5|Set-Content -LiteralPath $globalChainCases -Encoding utf8NoBOM
if($globalChainRequests.Count){
& dotnet (Join-Path $artifacts 'bin/BeatInsight.Compare/debug/BeatInsight.Compare.dll') $GameRoot --audit-noodle-global-chain-rows (Join-Path $OutputDirectory 'noodle-global-chain-rows.json') $globalChainCases
if($LASTEXITCODE){throw 'Global chain row compositions changed nominal groups or observable metrics.'}
}else{
    @{status='skipped';reason='No available source fixtures'}|ConvertTo-Json|Set-Content -LiteralPath (Join-Path $OutputDirectory 'noodle-global-chain-rows.json') -Encoding utf8NoBOM
}
& dotnet (Join-Path $artifacts 'bin/BeatInsight.Compare/debug/BeatInsight.Compare.dll') $GameRoot --check-noodle-arc-density (Join-Path $OutputDirectory 'noodle-arc-density.json')
if($LASTEXITCODE){throw 'Installed Noodle arc density checks failed.'}
& dotnet (Join-Path $artifacts 'bin/BeatInsight.Compare/debug/BeatInsight.Compare.dll') $GameRoot --check-noodle-custom-loader (Join-Path $OutputDirectory 'noodle-custom-loader.json')
if($LASTEXITCODE){throw 'Installed custom map factory and loader pipeline checks failed.'}
& dotnet (Join-Path $artifacts 'bin/BeatInsight.Compare/debug/BeatInsight.Compare.dll') $GameRoot --check-noodle-converters (Join-Path $OutputDirectory 'noodle-converters.json')
if($LASTEXITCODE){throw 'Installed custom converter transpiler checks failed.'}
& dotnet (Join-Path $artifacts 'bin/BeatInsight.Compare/debug/BeatInsight.Compare.dll') $GameRoot --check-noodle-fake-events (Join-Path $OutputDirectory 'noodle-fake-events.json')
if($LASTEXITCODE){throw 'Installed decorative fake event parsing and scalar dispatch checks failed.'}
& dotnet (Join-Path $artifacts 'bin/BeatInsight.Compare/debug/BeatInsight.Compare.dll') $GameRoot --check-noodle-fake-interaction (Join-Path $OutputDirectory 'noodle-fake-interaction.json')
if($LASTEXITCODE){throw 'Controlled decorative fake dynamic interaction checks failed.'}
& dotnet (Join-Path $artifacts 'bin/BeatInsight.Compare/debug/BeatInsight.Compare.dll') $GameRoot --check-noodle-fake-load (Join-Path $OutputDirectory 'noodle-fake-load.json')
if($LASTEXITCODE){throw 'Installed fake-array loading and temporal preparation checks failed.'}
& dotnet (Join-Path $artifacts 'bin/BeatInsight.Compare/debug/BeatInsight.Compare.dll') $GameRoot --check-noodle-fake-tail (Join-Path $OutputDirectory 'noodle-fake-tail.json')
if($LASTEXITCODE){throw 'Controlled decorative fake-tail isolation checks failed.'}
& dotnet (Join-Path $artifacts 'bin/BeatInsight.Compare/debug/BeatInsight.Compare.dll') $GameRoot --check-noodle-v3-aliases (Join-Path $OutputDirectory 'noodle-v3-aliases.json')
if($LASTEXITCODE){throw 'Controlled v3 real-note aliases and linked size checks failed.'}
& dotnet (Join-Path $artifacts 'bin/BeatInsight.Compare/debug/BeatInsight.Compare.dll') $GameRoot --check-noodle-colliders (Join-Path $OutputDirectory 'noodle-colliders.json')
if($LASTEXITCODE){throw 'Controlled dynamic note-collider checks failed.'}
& dotnet (Join-Path $artifacts 'bin/BeatInsight.Compare/debug/BeatInsight.Compare.dll') $GameRoot --check-noodle-look (Join-Path $OutputDirectory 'noodle-look.json')
if($LASTEXITCODE){throw 'Controlled installed note-look checks failed.'}
& dotnet (Join-Path $artifacts 'bin/BeatInsight.Compare/debug/BeatInsight.Compare.dll') $GameRoot --check-noodle-copy-mirror (Join-Path $OutputDirectory 'noodle-copy-mirror.json')
if($LASTEXITCODE){throw 'Controlled whole-map copy/mirror checks failed.'}
& dotnet (Join-Path $artifacts 'bin/BeatInsight.Compare/debug/BeatInsight.Compare.dll') $GameRoot --check-noodle-bomb-angle (Join-Path $OutputDirectory 'noodle-bomb-angle.json')
if($LASTEXITCODE){throw 'Legacy bomb cut angle checks failed.'}
& dotnet (Join-Path $artifacts 'bin/BeatInsight.Compare/debug/BeatInsight.Compare.dll') $GameRoot --check-noodle-note-size (Join-Path $OutputDirectory 'noodle-note-size.json')
if($LASTEXITCODE){throw 'Canonical real-note size checks failed.'}
& dotnet (Join-Path $artifacts 'bin/BeatInsight.Compare/debug/BeatInsight.Compare.dll') $GameRoot --check-noodle-player-tracks (Join-Path $OutputDirectory 'noodle-player-tracks.json')
if($LASTEXITCODE){throw 'Player track consumer checks failed.'}
& dotnet (Join-Path $artifacts 'bin/BeatInsight.Compare/debug/BeatInsight.Compare.dll') $GameRoot --check-noodle-dissolve (Join-Path $OutputDirectory 'noodle-dissolve.json')
if($LASTEXITCODE){throw 'Dissolve property isolation checks failed.'}
& dotnet (Join-Path $artifacts 'bin/BeatInsight.Compare/debug/BeatInsight.Compare.dll') $GameRoot --check-noodle-wall-contact (Join-Path $OutputDirectory 'noodle-wall-contact.json')
if($LASTEXITCODE){throw 'Wall Z contact interval checks failed.'}
& dotnet (Join-Path $artifacts 'bin/BeatInsight.Compare/debug/BeatInsight.Compare.dll') $GameRoot --check-noodle-jump-policy (Join-Path $OutputDirectory 'noodle-jump-policy.json')
if($LASTEXITCODE){throw 'Jump settings and per-object override checks failed.'}
& dotnet (Join-Path $artifacts 'bin/BeatInsight.Compare/debug/BeatInsight.Compare.dll') $GameRoot --check-noodle-wall-clock (Join-Path $OutputDirectory 'noodle-wall-clock.json')
if($LASTEXITCODE){throw 'Obstacle normalized clock checks failed.'}
& dotnet (Join-Path $artifacts 'bin/BeatInsight.Compare/debug/BeatInsight.Compare.dll') $GameRoot --check-noodle-event-parsing (Join-Path $OutputDirectory 'noodle-event-parsing.json')
if($LASTEXITCODE){throw 'Event parsing and target resolution checks failed.'}
& dotnet (Join-Path $artifacts 'bin/BeatInsight.Compare/debug/BeatInsight.Compare.dll') $GameRoot --check-noodle-path-animation (Join-Path $OutputDirectory 'noodle-path-animation.json')
if($LASTEXITCODE){throw 'Path transition and composition checks failed.'}
& dotnet (Join-Path $artifacts 'bin/BeatInsight.Compare/debug/BeatInsight.Compare.dll') $GameRoot --check-noodle-track-dispatch (Join-Path $OutputDirectory 'noodle-track-dispatch.json')
if($LASTEXITCODE){throw 'Track event fast dispatch/replacement checks failed.'}
& dotnet (Join-Path $artifacts 'bin/BeatInsight.Compare/debug/BeatInsight.Compare.dll') $GameRoot --check-noodle-track-clock (Join-Path $OutputDirectory 'noodle-track-clock.json')
if($LASTEXITCODE){throw 'Track event coroutine clock checks failed.'}
& dotnet (Join-Path $artifacts 'bin/BeatInsight.Compare/debug/BeatInsight.Compare.dll') $GameRoot --check-noodle-bounce (Join-Path $OutputDirectory 'noodle-bounce.json')
if($LASTEXITCODE){throw 'Bounce easing boundary checks failed.'}
& dotnet (Join-Path $artifacts 'bin/BeatInsight.Compare/debug/BeatInsight.Compare.dll') $GameRoot --check-noodle-position (Join-Path $OutputDirectory 'noodle-position.json')
if($LASTEXITCODE){throw 'Position vector interpolation checks failed.'}
& dotnet (Join-Path $artifacts 'bin/BeatInsight.Compare/debug/BeatInsight.Compare.dll') $GameRoot --check-noodle-local-rotation (Join-Path $OutputDirectory 'noodle-local-rotation.json')
if($LASTEXITCODE){throw 'Local rotation endpoint checks failed.'}
& dotnet (Join-Path $artifacts 'bin/BeatInsight.Compare/debug/BeatInsight.Compare.dll') $GameRoot --check-noodle-motion-flags (Join-Path $OutputDirectory 'noodle-motion-flags.json')
if($LASTEXITCODE){throw 'Motion flag spawn checks failed.'}
& dotnet (Join-Path $artifacts 'bin/BeatInsight.Compare/debug/BeatInsight.Compare.dll') $GameRoot --check-noodle-arc-opacity (Join-Path $OutputDirectory 'noodle-arc-opacity.json')
if($LASTEXITCODE){throw 'Arc opacity parsing/interpolation checks failed.'}
& dotnet (Join-Path $artifacts 'bin/BeatInsight.Compare/debug/BeatInsight.Compare.dll') $GameRoot --check-noodle-object-opacity (Join-Path $OutputDirectory 'noodle-object-opacity.json')
if($LASTEXITCODE){throw 'Object opacity parsing/interpolation checks failed.'}
& dotnet (Join-Path $artifacts 'bin/BeatInsight.Compare/debug/BeatInsight.Compare.dll') $GameRoot --check-noodle-arc-interaction (Join-Path $OutputDirectory 'noodle-arc-interaction.json')
if($LASTEXITCODE){throw 'Arc/head interaction separation checks failed.'}
& dotnet (Join-Path $artifacts 'bin/BeatInsight.Compare/debug/BeatInsight.Compare.dll') $GameRoot --check-noodle-identity-scale (Join-Path $OutputDirectory 'noodle-identity-scale.json')
if($LASTEXITCODE){throw 'Identity scale animation checks failed.'}
& dotnet (Join-Path $artifacts 'bin/BeatInsight.Compare/debug/BeatInsight.Compare.dll') $GameRoot --check-noodle-scale-composition (Join-Path $OutputDirectory 'noodle-scale-composition.json')
if($LASTEXITCODE){throw 'Scale composition/collision bounds checks failed.'}
& dotnet (Join-Path $artifacts 'bin/BeatInsight.Compare/debug/BeatInsight.Compare.dll') $GameRoot --check-menu-data $MapsRoot (Join-Path $workspace 'tests/checkpoint-maps.json') (Join-Path $OutputDirectory 'checkpoint.json')
if($LASTEXITCODE){throw 'Menu data checkpoint comparison failed.'}
& dotnet (Join-Path $artifacts 'bin/BeatInsight.Compare/debug/BeatInsight.Compare.dll') $GameRoot --check-rare-formats (Join-Path $OutputDirectory 'rare-formats.json')
if($LASTEXITCODE){throw 'Rare-format comparison failed.'}
& dotnet (Join-Path $artifacts 'bin/BeatInsight.Compare/debug/BeatInsight.Compare.dll') $GameRoot --check-audio-data (Join-Path $OutputDirectory 'audio-data.json')
if($LASTEXITCODE){throw 'AudioData comparison failed.'}
& dotnet (Join-Path $artifacts 'bin/BeatInsight.Compare/debug/BeatInsight.Compare.dll') $GameRoot --check-chroma (Join-Path $OutputDirectory 'chroma.json')
if($LASTEXITCODE){throw 'Chroma comparison failed.'}
& dotnet (Join-Path $artifacts 'bin/BeatInsight.Compare/debug/BeatInsight.Compare.dll') $GameRoot --check-noodle-static (Join-Path $OutputDirectory 'noodle-static.json')
if($LASTEXITCODE){throw 'Noodle static subset comparison failed.'}
& dotnet (Join-Path $artifacts 'bin/BeatInsight.Compare/debug/BeatInsight.Compare.dll') $GameRoot --check-noodle-fake (Join-Path $OutputDirectory 'noodle-fake.json')
if($LASTEXITCODE){throw 'Noodle fake/history comparison failed.'}
& dotnet (Join-Path $artifacts 'bin/BeatInsight.Compare/debug/BeatInsight.Compare.dll') $GameRoot --check-noodle-warnings (Join-Path $OutputDirectory 'noodle-warnings.json')
if($LASTEXITCODE){throw 'Static custom warning fixtures failed.'}
& dotnet (Join-Path $artifacts 'bin/BeatInsight.Compare/debug/BeatInsight.Compare.dll') $GameRoot --check-noodle-mirror (Join-Path $OutputDirectory 'noodle-mirror.json')
if($LASTEXITCODE){throw 'Static custom mirror contract failed.'}
& dotnet (Join-Path $artifacts 'bin/BeatInsight.Compare/debug/BeatInsight.Compare.dll') $GameRoot --check-noodle-arcs (Join-Path $OutputDirectory 'noodle-arcs.json')
if($LASTEXITCODE){throw 'Custom arc coordinate contract failed.'}
& dotnet (Join-Path $artifacts 'bin/BeatInsight.Compare/debug/BeatInsight.Compare.dll') $GameRoot --check-noodle-bursts (Join-Path $OutputDirectory 'noodle-bursts.json')
if($LASTEXITCODE){throw 'Custom burst angle/converter contract failed.'}
& dotnet (Join-Path $artifacts 'bin/BeatInsight.Compare/debug/BeatInsight.Compare.dll') $GameRoot --check-noodle-overlaps (Join-Path $OutputDirectory 'noodle-overlaps.json')
if($LASTEXITCODE){throw 'Custom overlap row contract failed.'}
& dotnet (Join-Path $artifacts 'bin/BeatInsight.Compare/debug/BeatInsight.Compare.dll') $GameRoot --check-noodle-spawn (Join-Path $OutputDirectory 'noodle-spawn.json')
if($LASTEXITCODE){throw 'Native burst spawn contract failed.'}
& dotnet (Join-Path $artifacts 'bin/BeatInsight.Compare/debug/BeatInsight.Compare.dll') $GameRoot --check-noodle-metadata (Join-Path $OutputDirectory 'noodle-metadata.json')
if($LASTEXITCODE){throw 'Custom metadata contract failed.'}
& dotnet (Join-Path $artifacts 'bin/BeatInsight.Compare/debug/BeatInsight.Compare.dll') $GameRoot --check-noodle-cut-direction (Join-Path $OutputDirectory 'noodle-cut-direction.json')
if($LASTEXITCODE){throw 'Legacy custom cut direction contract failed.'}
& dotnet (Join-Path $artifacts 'bin/BeatInsight.Compare/debug/BeatInsight.Compare.dll') $GameRoot --check-noodle-cut-flags (Join-Path $OutputDirectory 'noodle-cut-flags.json')
if($LASTEXITCODE){throw 'Native cut flag contract failed.'}
& dotnet (Join-Path $artifacts 'bin/BeatInsight.Compare/debug/BeatInsight.Compare.dll') $GameRoot --check-noodle-wall-interaction (Join-Path $OutputDirectory 'noodle-wall-interaction.json')
if($LASTEXITCODE){throw 'Native wall interaction contract failed.'}
& dotnet (Join-Path $artifacts 'bin/BeatInsight.Compare/debug/BeatInsight.Compare.dll') $GameRoot --check-noodle-zero-world-rotation (Join-Path $OutputDirectory 'noodle-zero-world-rotation.json')
if($LASTEXITCODE){throw 'Zero world rotation contract failed.'}
foreach($scenario in @(@{name='plain';status='analyzed';code=0},@{name='real-uninteractable';status='unsupported';code=3})){
    $requestPath=Join-Path $OutputDirectory ('trust-'+$scenario.name+'.request.json')
    $resultPath=Join-Path $OutputDirectory ('trust-'+$scenario.name+'.result.json')
    @{Folder=(Join-Path $OutputDirectory ('noodle-fake-fixtures/v3-'+$scenario.name));Characteristic='Standard';Difficulty='Expert';File='map.dat'} | ConvertTo-Json | Set-Content -LiteralPath $requestPath -Encoding utf8NoBOM
    & dotnet (Join-Path $artifacts 'bin/BeatInsight.Compare/debug/BeatInsight.Compare.dll') $GameRoot --case $requestPath $resultPath
    if($LASTEXITCODE -ne $scenario.code){throw 'Noodle trust policy exit code changed.'}
    $result=Get-Content -LiteralPath $resultPath -Raw | ConvertFrom-Json
    if($result.status -ne $scenario.status){throw 'Noodle analysis was incorrectly classified as reference validation.'}
    if($scenario.status -eq 'analyzed' -and ($result.validation.referenceComparison -ne 'not-performed' -or $result.validation.mapCorrectness -ne 'not-independently-verified' -or $result.PSObject.Properties['original'])){throw 'Noodle report overstates independent validation.'}
}
& dotnet (Join-Path $artifacts 'bin/BeatInsight.Compare/debug/BeatInsight.Compare.dll') $GameRoot --check-mapping-notes (Join-Path $OutputDirectory 'mapping-notes.json')
if($LASTEXITCODE){throw 'Mapping note comparison failed.'}
& dotnet (Join-Path $artifacts 'bin/BeatInsight.Compare/debug/BeatInsight.Compare.dll') $GameRoot --check-mapping-warnings (Join-Path $OutputDirectory 'mapping-warnings.json')
if($LASTEXITCODE){throw 'Mapping warning comparison failed.'}
if(Test-Path -LiteralPath (Join-Path $GameRoot 'Plugins/MappingExtensions.dll')){
    & dotnet (Join-Path $artifacts 'bin/BeatInsight.Compare/debug/BeatInsight.Compare.dll') $GameRoot --check-mapping-walls (Join-Path $OutputDirectory 'mapping-walls.json')
    if($LASTEXITCODE){throw 'Mapping wall conversion comparison failed.'}
}
& (Join-Path $PSScriptRoot 'Test-MenuReport.ps1') -Binary (Join-Path $artifacts 'bin/BeatInsight.Compare/debug/BeatInsight.Compare.dll') -GameRoot $GameRoot -OutputDirectory (Join-Path $OutputDirectory 'menu-report')
& (Join-Path $PSScriptRoot 'Build-Mod.ps1') -GameRoot $GameRoot -OutputDirectory (Join-Path $OutputDirectory 'mod')
& dotnet (Join-Path $artifacts 'bin/BeatInsight.Compare/debug/BeatInsight.Compare.dll') $GameRoot --check-note-diagnostics (Join-Path $OutputDirectory 'mod/package/Plugins/BeatInsight.dll') (Join-Path $OutputDirectory 'note-diagnostics-cost.json')
if($LASTEXITCODE){throw 'Packaged note diagnostic guard/allocation checks failed.'}
ConvertTo-Json -InputObject $missingManifestMaps.ToArray() -Depth 5|Set-Content -LiteralPath (Join-Path $OutputDirectory 'missing-manifest-maps.json') -Encoding utf8NoBOM
Write-Host "Menu validation complete: $OutputDirectory"
Write-Host 'No game process was started; no user history was read or written.'

} finally { $env:BEATINSIGHT_LOCAL_ROOT=$previousArtifactRoot }
