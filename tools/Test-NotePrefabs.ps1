[CmdletBinding()]
param(
    [string]$GameRoot,
    [string]$OutputDirectory,
    [string]$PythonDependencies
)

$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'LocalSettings.ps1')
$previousArtifactRoot=$env:BEATINSIGHT_LOCAL_ROOT
try {
$env:BEATINSIGHT_LOCAL_ROOT=Get-BeatInsightLocalRoot
if(!$OutputDirectory){$OutputDirectory=Join-Path (Get-BeatInsightLocalRoot) 'noodle-note-prefabs'}
$OutputDirectory=Resolve-BeatInsightOutputDirectory $OutputDirectory
if(!$PythonDependencies){$PythonDependencies=Join-Path (Get-BeatInsightLocalRoot) 'unity-py'}
$prefabGameRoot = Get-BeatInsightSetting GameRoot $GameRoot
$prefabAssetDirectory = Join-Path $prefabGameRoot 'Beat Saber_Data/StreamingAssets/aa/StandaloneWindows64'
$prefabBundles = @(Get-ChildItem -LiteralPath $prefabAssetDirectory -Filter 'maincore_assets_all_*.bundle')
if ($prefabBundles.Count -ne 1) {
    throw 'Expected exactly one maincore asset bundle; inspect the installed asset catalog.'
}

$previousPythonPath = $env:PYTHONPATH
try {
    $dependencyPath = (Resolve-Path -LiteralPath $PythonDependencies).Path
    $env:PYTHONPATH = $dependencyPath
    if ($previousPythonPath) {
        $env:PYTHONPATH += [IO.Path]::PathSeparator + $previousPythonPath
    }
    & python (Join-Path $PSScriptRoot 'Inspect-NotePrefabs.py') `
        --bundle $prefabBundles[0].FullName `
        --output (Join-Path $OutputDirectory 'hierarchy.json')
    if ($LASTEXITCODE -ne 0) { throw 'Note prefab hierarchy verification failed.' }
}
finally {
    $env:PYTHONPATH = $previousPythonPath
}

} finally { $env:BEATINSIGHT_LOCAL_ROOT=$previousArtifactRoot }
