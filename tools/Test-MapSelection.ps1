#requires -Version 7.0
[CmdletBinding()]
param([string]$OutputDirectory)
$ErrorActionPreference='Stop'
$workspace=Split-Path $PSScriptRoot -Parent
. (Join-Path $PSScriptRoot 'LocalSettings.ps1')
. (Join-Path $PSScriptRoot 'MapInventory.ps1')
if(!$OutputDirectory){$OutputDirectory=Join-Path $workspace ('.local/map-selection/'+[Guid]::NewGuid().ToString('N'))}
$OutputDirectory=[IO.Path]::GetFullPath($OutputDirectory)
if(!$OutputDirectory.StartsWith(([IO.Path]::GetFullPath($workspace).TrimEnd('\')+'\'),[StringComparison]::OrdinalIgnoreCase)){throw 'OutputDirectory must stay inside the workspace.'}
if(Test-Path -LiteralPath $OutputDirectory){throw 'Choose a new output directory.'}
New-Item -ItemType Directory -Path $OutputDirectory|Out-Null
$artifacts=Join-Path $OutputDirectory '.build'
& dotnet build (Join-Path $PSScriptRoot 'BeatInsight.Cli') --artifacts-path $artifacts --source (Get-BeatInsightSetting NuGetPackageRoot) -p:NuGetAudit=false --verbosity quiet
if($LASTEXITCODE){throw 'CLI build failed.'}
$binary=Join-Path $artifacts 'bin/BeatInsight.Cli/debug/BeatInsight.Cli.dll'
$selectionChecks=0
function Assert-Selection([bool]$Condition,[string]$Message){if(!$Condition){throw $Message};$script:selectionChecks++}
function Invoke-SelectionCli([string[]]$CliArguments){
    $start=[Diagnostics.ProcessStartInfo]::new((Get-Command dotnet).Source)
    $start.UseShellExecute=$false;$start.CreateNoWindow=$true
    $start.RedirectStandardOutput=$true;$start.RedirectStandardError=$true
    $start.ArgumentList.Add($binary)
    foreach($argument in $CliArguments){$start.ArgumentList.Add($argument)}
    $process=[Diagnostics.Process]::new();$process.StartInfo=$start
    try{
        $null=$process.Start();$stdout=$process.StandardOutput.ReadToEndAsync();$stderr=$process.StandardError.ReadToEndAsync()
        if(!$process.WaitForExit(15000)){$process.Kill($true);$process.WaitForExit();throw 'CLI check timed out.'}
        [pscustomobject]@{exitCode=$process.ExitCode;stdout=$stdout.GetAwaiter().GetResult();stderr=$stderr.GetAwaiter().GetResult()}
    }finally{$process.Dispose()}
}
$folder=Join-Path $OutputDirectory 'map'
New-Item -ItemType Directory -Path (Join-Path $folder 'maps')|Out-Null
$entries=@(
    @{characteristic='Standard';difficulty='Expert';beatmapDataFilename='maps/A.dat'},
    @{characteristic='Standard';difficulty='Expert';beatmapDataFilename='maps/./A.dat'},
    @{characteristic='Standard';difficulty='Expert';beatmapDataFilename='maps/B map.dat'},
    @{characteristic='Standard';difficulty='Hard';beatmapDataFilename='maps/A.dat'})
$info=@{version='4.0.1';audio=@{bpm=120;songDuration=12};difficultyBeatmaps=$entries}
$info | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath (Join-Path $folder 'Info.dat')
$notes='{"version":"3.3.0","colorNotes":[{"b":2,"x":0,"y":0,"c":0,"d":1},{"b":4,"x":3,"y":0,"c":1,"d":1}]}'
foreach($file in @('A.dat','B map.dat')){Set-Content -LiteralPath (Join-Path $folder "maps/$file") -Value $notes}
$ambiguous=Invoke-SelectionCli @($folder,'Standard','Expert')
Assert-Selection ($ambiguous.exitCode -eq 1 -and !$ambiguous.stdout -and $ambiguous.stderr.Contains('Ambiguous') -and $ambiguous.stderr.Contains('--file') -and $ambiguous.stderr.Contains('B map.dat')) 'Ambiguous selection must explain how to choose a file.'
$exact=Invoke-SelectionCli @($folder,'Standard','Expert','--file','maps/A.dat')
$json=$exact.stdout|ConvertFrom-Json
Assert-Selection ($exact.exitCode -eq 0 -and $json.File -eq 'maps/A.dat') 'Exact CLI file selection failed.'
Assert-Selection ($json.selectionDiagnostics.Count -eq 1 -and $json.selectionDiagnostics[0].Occurrences -eq 2 -and $exact.stderr.Contains('duplicate map declaration')) 'Repeated declarations must be diagnosed separately from ambiguity.'
$spaced=Invoke-SelectionCli @($folder,'Standard','Expert','--file=maps/B map.dat')
Assert-Selection ($spaced.exitCode -eq 0 -and ($spaced.stdout|ConvertFrom-Json).File -eq 'maps/B map.dat') 'File names containing spaces must survive argument parsing.'
$unique=Invoke-SelectionCli @($folder,'Standard','Hard')
Assert-Selection ($unique.exitCode -eq 0 -and ($unique.stdout|ConvertFrom-Json).Difficulty -eq 'Hard') 'Existing unambiguous CLI calls must remain valid.'
$legacy=Join-Path $OutputDirectory 'SongPlayData.json'
'{"selection-test___3___Standard":[{"LastNote":1,"Date":1000}]}' | Set-Content -LiteralPath $legacy
$before=(Get-FileHash -LiteralPath $legacy).Hash
$history=Invoke-SelectionCli @($folder,'Standard','Expert',$legacy,'--file','maps/A.dat','selection-test',(Join-Path $OutputDirectory 'BeatInsight.Deaths.json'))
$historyJson=$history.stdout|ConvertFrom-Json
Assert-Selection ($history.exitCode -eq 0 -and $historyJson.history.status -eq 'available' -and $historyJson.deathMarkers.ownStatus -eq 'source-missing') 'The --file option must not displace existing history arguments.'
Assert-Selection ((Get-FileHash -LiteralPath $legacy).Hash -eq $before) 'Selection checks must not modify history.'
foreach($extra in @(
    @('--file'),@('--file='),@('--unknown'),@('--file','maps/A.dat','--file','maps/A.dat'),
    @('--file','../outside.dat'),@('--file','maps/missing.dat'))){
    $invalid=Invoke-SelectionCli (@($folder,'Standard','Expert')+$extra)
    Assert-Selection ($invalid.exitCode -eq 1 -and !$invalid.stdout) ('Invalid selection must fail explicitly: '+($extra -join ' '))
}
$cases=@($entries|ForEach-Object {[pscustomobject]@{Folder=$folder;Characteristic=$_.characteristic;Difficulty=$_.difficulty;File=$_.beatmapDataFilename}})
$warnings=@(Get-BeatInsightDuplicateMapDeclarations $cases)
Assert-Selection ($warnings.Count -eq 1 -and $warnings[0].occurrences -eq 2 -and $warnings[0].difficulty -eq 'Expert') 'Inventory and CLI must agree about duplicate path aliases.'
Assert-Selection ($cases.Count -eq 4) 'Inventory diagnostics must not remove declared inputs.'
$differentFolder=@($cases[0],[pscustomobject]@{Folder=(Join-Path $OutputDirectory 'another-map');Characteristic='Standard';Difficulty='Expert';File='maps/A.dat'})
Assert-Selection (@(Get-BeatInsightDuplicateMapDeclarations $differentFolder).Count -eq 0) 'Different map roots must never be merged into one duplicate.'
$v2folder=Join-Path $OutputDirectory 'old-info'
New-Item -ItemType Directory -Path $v2folder|Out-Null
Set-Content -LiteralPath (Join-Path $v2folder 'A.dat') -Value $notes
@{_version='2.1.0';_beatsPerMinute=120;audio=@{songDuration=12};_difficultyBeatmapSets=@(@{_beatmapCharacteristicName='Standard';_difficultyBeatmaps=@(@{_difficulty='Hard';_beatmapFilename='A.dat'},@{_difficulty='Hard';_beatmapFilename='A.dat'})})}|
    ConvertTo-Json -Depth 8 | Set-Content -LiteralPath (Join-Path $v2folder 'Info.dat')
$v2=Invoke-SelectionCli @($v2folder,'Standard','Hard')
Assert-Selection ($v2.exitCode -eq 0 -and ($v2.stdout|ConvertFrom-Json).selectionDiagnostics[0].Occurrences -eq 2) 'Old Info format must diagnose repeats without failing a unique file selection.'
$report=@{checks=$selectionChecks;status='pass';gameStarted=$false;userHistoryRead=$false;scope='CLI selection and inventory diagnostics on synthetic maps/history'}
$report|ConvertTo-Json|Set-Content -LiteralPath (Join-Path $OutputDirectory 'checks.json')
Write-Host "Passed $selectionChecks CLI/inventory checks."
