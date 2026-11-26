#requires -Version 7.0
[CmdletBinding()]
param([Parameter(Mandatory)][string]$Binary,[Parameter(Mandatory)][string]$GameRoot,[Parameter(Mandatory)][string]$OutputDirectory)
$ErrorActionPreference='Stop'
if(Test-Path -LiteralPath $OutputDirectory){throw 'Choose a new output directory.'}
New-Item -ItemType Directory -Path $OutputDirectory|Out-Null
$checks=0
function Assert-Report([bool]$Condition,[string]$Message){if(!$Condition){throw $Message};$script:checks++}
$reports=@{}
foreach($name in @('plain','moved','chain-player','unknown','missing')){
    $folder=Join-Path $OutputDirectory $name
    New-Item -ItemType Directory -Path $folder|Out-Null
    $requirements=@('Noodle Extensions');if($name -eq 'unknown'){$requirements+='Unverified Extension'}
    @{version='4.0.1';audio=@{bpm=120;songDuration=12};difficultyBeatmaps=@(@{characteristic='Standard';difficulty='Expert';beatmapDataFilename='map.dat';customData=@{requirements=$requirements}})}|ConvertTo-Json -Depth 12|Set-Content -LiteralPath (Join-Path $folder 'Info.dat') -Encoding utf8NoBOM
    $map=@{version='3.3.0';colorNotes=@(@{b=2;x=0;y=0;c=0;d=1},@{b=4;x=3;y=0;c=1;d=1})}
    if($name -eq 'moved'){$map.colorNotes[1].b=6}
    if($name -eq 'chain-player'){
        $map.burstSliders=@(@{b=2;x=0;y=0;c=0;d=1;tb=3;tx=1;ty=1;sc=4;s=1})
        $map.customData=@{customEvents=@(@{b=0;t='AssignPlayerToTrack';d=@{track='player'}})}
    }
    $mapFile=Join-Path $folder 'map.dat'
    if($name -ne 'missing'){$map|ConvertTo-Json -Depth 12|Set-Content -LiteralPath $mapFile -Encoding utf8NoBOM}
    $before=if(Test-Path -LiteralPath $mapFile){(Get-FileHash -LiteralPath $mapFile).Hash}else{$null}
    $request=Join-Path $folder 'request.json';$output=Join-Path $folder 'result.json'
    @{Folder=[IO.Path]::GetFullPath($folder);Characteristic='Standard';Difficulty='Expert';File='map.dat'}|ConvertTo-Json|Set-Content -LiteralPath $request -Encoding utf8NoBOM
    & dotnet $Binary $GameRoot --case-with-menu $request $output
    $code=$LASTEXITCODE;$report=Get-Content -LiteralPath $output -Raw|ConvertFrom-Json
    $reports[$name]=$report
    $expectedCode=if($name -in @('plain','moved')){0}elseif($name -eq 'missing'){4}else{3}
    Assert-Report ($code -eq $expectedCode) "Full analysis exit code changed: $name"
    $expectedMenu=if($name -in @('plain','moved')){'ready'}elseif($name -eq 'chain-player'){'partial'}elseif($name -eq 'missing'){'unavailable'}else{'unsupported'}
    Assert-Report ($report.menu.status -eq $expectedMenu) "Selective menu status is incorrect: $name"
    if($before){Assert-Report ((Get-FileHash -LiteralPath $mapFile).Hash -eq $before) "Report changed input: $name"}
}
$partial=$reports['chain-player']
Assert-Report ($partial.status -eq 'unsupported' -and $partial.menu.densityAvailable -and $partial.menu.independentDensity -and !$partial.menu.noteMetricsAvailable -and $null -eq $partial.menu.metrics -and $null -eq $partial.menu.median) 'A partial graph must not promote full geometry or expose fabricated metrics.'
Assert-Report ($partial.menu.squatsAvailable -and $partial.menu.squats -eq 0 -and $null -eq $partial.menu.squatError) 'A genuinely empty obstacle collection may expose zero without promoting unsupported notes.'
Assert-Report ($partial.menu.density.values.Count -eq 90 -and $partial.menu.history -eq 'not-read' -and $partial.menu.mapSha256.Length -eq 64) 'Partial report must preserve graph/source identity without reading history.'
Assert-Report ($reports['plain'].menu.density.values.Count -eq $reports['moved'].menu.density.values.Count -and $reports['plain'].menu.density.sha256 -ne $reports['moved'].menu.density.sha256) 'Changing graph values without changing segment count must change the density digest.'
Assert-Report (!$reports['plain'].PSObject.Properties['original'] -and $reports['plain'].validation.referenceComparison -eq 'not-performed') 'Menu snapshot must not overstate reference validation.'
$coverageInput=Join-Path $OutputDirectory 'coverage-input'
New-Item -ItemType Directory -Path $coverageInput|Out-Null
$caseIndex=0
foreach($name in @('plain','chain-player','unknown','missing')){
    Copy-Item -LiteralPath (Join-Path $OutputDirectory "$name/result.json") -Destination (Join-Path $coverageInput ('{0:D3}.result.json' -f $caseIndex))
    $caseIndex++
}
$coverageOutput=Join-Path $OutputDirectory 'coverage.json'
$coverageSummary=Join-Path $OutputDirectory 'coverage-summary.json'
& dotnet $Binary --check-noodle-menu-coverage $coverageInput $coverageOutput $coverageSummary
if($LASTEXITCODE){throw 'Synthetic coverage report failed.'}
$coverage=Get-Content -LiteralPath $coverageSummary -Raw|ConvertFrom-Json
Assert-Report ($coverage.cases -eq 4 -and $coverage.statuses.ready -eq 1 -and $coverage.statuses.partial -eq 1 -and $coverage.statuses.unsupported -eq 1 -and $coverage.statuses.unavailable -eq 1) 'Coverage statuses must distinguish partial data, unsupported format and missing input.'
Assert-Report ($coverage.availability.noteMetrics -eq 1 -and $coverage.availability.squats -eq 2 -and $coverage.availability.density -eq 2 -and $coverage.availability.independentDensity -eq 1) 'Missing/unsupported cases must not be counted as available metrics.'
Assert-Report ($coverage.limitations.noteError.Count -eq 1 -and $coverage.limitations.noteError[0].cases -eq 1 -and $coverage.limitations.squatError.Count -eq 0 -and $coverage.limitations.densityError.Count -eq 0) 'Partial-result limitation groups must retain their independent metric scope.'
Assert-Report ($coverage.unavailableSources.Count -eq 1 -and $coverage.unavailableSources[0] -eq '003.result.json') 'Coverage must identify the missing source explicitly.'
@{checks=$checks;status='pass';scope='bounded synthetic report cases; full-analysis status and selective menu status remain separate; no user history or game process'}|ConvertTo-Json|Set-Content -LiteralPath (Join-Path $OutputDirectory 'checks.json') -Encoding utf8NoBOM
Write-Host "Passed $checks menu report checks."
