#requires -Version 7.0
[CmdletBinding()]
param([string]$GameRoot,[string]$MapsRoot,[string]$OutputDirectory)
$ErrorActionPreference='Stop'
$workspace=Split-Path $PSScriptRoot -Parent
. (Join-Path $PSScriptRoot 'LocalSettings.ps1')
$GameRoot=Get-BeatInsightSetting GameRoot $GameRoot
$MapsRoot=Get-BeatInsightSetting MapsRoot $MapsRoot
if(!$OutputDirectory){$OutputDirectory=Join-Path $workspace ('.local/modifiers/'+(Get-Date -Format 'yyyyMMdd-HHmmss'))}
$OutputDirectory=[IO.Path]::GetFullPath($OutputDirectory)
$prefix=[IO.Path]::GetFullPath($workspace).TrimEnd('\')+'\'
if(!$OutputDirectory.StartsWith($prefix,[StringComparison]::OrdinalIgnoreCase)){throw 'OutputDirectory must stay inside the workspace.'}
if(Test-Path -LiteralPath $OutputDirectory){throw 'Choose a new output directory.'}
New-Item -ItemType Directory -Path $OutputDirectory|Out-Null
$artifacts=Join-Path $OutputDirectory '.build'
& dotnet build (Join-Path $PSScriptRoot 'BeatInsight.Compare') --artifacts-path $artifacts --source (Get-BeatInsightSetting NuGetPackageRoot) -p:NuGetAudit=false "-p:GameRoot=$GameRoot" --verbosity quiet
if($LASTEXITCODE){throw 'Build failed.'}
$binary=Join-Path $artifacts 'bin/BeatInsight.Compare/debug/BeatInsight.Compare.dll'
$report=Join-Path $OutputDirectory 'results.json'
& dotnet $binary $GameRoot --check-modifiers $MapsRoot (Join-Path $workspace 'tests/checkpoint-maps.json') $report
if($LASTEXITCODE){throw "Modifier checks failed; inspect $report and stderr."}
$summary=Get-Content -LiteralPath $report -Raw|ConvertFrom-Json
Write-Host "Maps: $($summary.maps); cases: $($summary.cases); failed: $($summary.failed)"
Write-Host "Report: $report"
