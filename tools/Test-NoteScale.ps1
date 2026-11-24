#requires -Version 7.0
[CmdletBinding()]
param(
    [string]$GameRoot,
    [string]$OutputDirectory,
    [string]$PythonDependencies = '.local/unity-py'
)

$ErrorActionPreference = 'Stop'
$workspace = Split-Path $PSScriptRoot -Parent
. (Join-Path $PSScriptRoot 'LocalSettings.ps1')
$GameRoot = Get-BeatInsightSetting GameRoot $GameRoot
if (!$OutputDirectory) {
    $OutputDirectory = Join-Path $workspace ('.local/note-scale-validation/' + (Get-Date -Format 'yyyyMMdd-HHmmss'))
}
$OutputDirectory = [IO.Path]::GetFullPath($OutputDirectory)
$workspacePrefix = [IO.Path]::GetFullPath($workspace).TrimEnd('\') + '\'
if (!$OutputDirectory.StartsWith($workspacePrefix, [StringComparison]::OrdinalIgnoreCase)) {
    throw 'OutputDirectory must stay inside the workspace.'
}
if (Test-Path -LiteralPath $OutputDirectory) { throw 'Choose a new output directory.' }
& (Join-Path $PSScriptRoot 'Test-NotePrefabs.ps1') -GameRoot $GameRoot `
    -OutputDirectory $OutputDirectory -PythonDependencies $PythonDependencies
$artifacts = Join-Path $OutputDirectory '.build'
$cache = Get-BeatInsightSetting NuGetPackageRoot
& dotnet build (Join-Path $PSScriptRoot 'BeatInsight.Compare') --artifacts-path $artifacts `
    --source $cache -p:NuGetAudit=false "-p:GameRoot=$GameRoot" --verbosity quiet
if ($LASTEXITCODE) { throw 'Note scale stand build failed.' }
& dotnet (Join-Path $artifacts 'bin/BeatInsight.Compare/debug/BeatInsight.Compare.dll') `
    $GameRoot --check-noodle-note-scale (Join-Path $OutputDirectory 'hierarchy.json') (Join-Path $OutputDirectory 'checks.json')
if ($LASTEXITCODE) { throw 'Note scale/cut checks failed.' }
$report = Get-Content -LiteralPath (Join-Path $OutputDirectory 'checks.json') -Raw | ConvertFrom-Json
Write-Host "Note scale/rotation/cut cases: $($report.cases); failed: $($report.failed)."
