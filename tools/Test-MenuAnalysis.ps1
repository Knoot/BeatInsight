#requires -Version 7.0
[CmdletBinding()]
param([string]$GameRoot,[string]$MapsRoot,[string]$OutputDirectory)
$ErrorActionPreference='Stop'
$workspace=Split-Path $PSScriptRoot -Parent
. (Join-Path $PSScriptRoot 'LocalSettings.ps1')
$GameRoot=Get-BeatInsightSetting GameRoot $GameRoot
$MapsRoot=Get-BeatInsightSetting MapsRoot $MapsRoot $GameRoot
if(!$OutputDirectory){$OutputDirectory=Join-Path $workspace ('.local/menu-validation/'+(Get-Date -Format 'yyyyMMdd-HHmmss'))}
$OutputDirectory=[IO.Path]::GetFullPath($OutputDirectory)
$prefix=[IO.Path]::GetFullPath($workspace).TrimEnd('\')+'\'
if(!$OutputDirectory.StartsWith($prefix,[StringComparison]::OrdinalIgnoreCase)){throw 'OutputDirectory must stay inside the workspace.'}
if(Test-Path -LiteralPath $OutputDirectory){throw 'Choose a new output directory.'}
New-Item -ItemType Directory -Path $OutputDirectory|Out-Null
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
& dotnet (Join-Path $artifacts 'bin/BeatInsight.Compare/debug/BeatInsight.Compare.dll') $GameRoot --check-menu-data $MapsRoot (Join-Path $workspace 'tests/checkpoint-maps.json') (Join-Path $OutputDirectory 'checkpoint.json')
if($LASTEXITCODE){throw 'Menu data checkpoint comparison failed.'}
& (Join-Path $PSScriptRoot 'Build-Mod.ps1') -GameRoot $GameRoot -OutputDirectory (Join-Path $OutputDirectory 'mod')
Write-Host "Menu validation complete: $OutputDirectory"
Write-Host 'No game process was started; no user history was read or written.'
