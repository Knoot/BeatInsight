#requires -Version 7.0
[CmdletBinding()]
param([string]$GameRoot,[string]$OutputDirectory)
$ErrorActionPreference='Stop'
$workspace=Split-Path $PSScriptRoot -Parent
. (Join-Path $PSScriptRoot 'LocalSettings.ps1')
$GameRoot=Get-BeatInsightSetting GameRoot $GameRoot
if(!$OutputDirectory){$OutputDirectory=Join-Path $workspace ('.local/mod-builds/'+(Get-Date -Format 'yyyyMMdd-HHmmss'))}
$OutputDirectory=[IO.Path]::GetFullPath($OutputDirectory)
$prefix=[IO.Path]::GetFullPath($workspace).TrimEnd('\')+'\'
if(!$OutputDirectory.StartsWith($prefix,[StringComparison]::OrdinalIgnoreCase)){throw 'OutputDirectory must stay inside the workspace.'}
if(Test-Path -LiteralPath $OutputDirectory){throw 'Choose a new output directory; existing packages will not be overwritten.'}
New-Item -ItemType Directory -Path $OutputDirectory|Out-Null
$artifacts=Join-Path $OutputDirectory '.build'
& dotnet build (Join-Path $workspace 'src/BeatInsight.Mod') -c Release --artifacts-path $artifacts --source (Get-BeatInsightSetting NuGetPackageRoot) -p:NuGetAudit=false "-p:GameRoot=$GameRoot" --verbosity quiet
if($LASTEXITCODE){throw 'Mod build failed.'}
$package=Join-Path $OutputDirectory 'package'
New-Item -ItemType Directory -Path (Join-Path $package 'Plugins'),(Join-Path $package 'Libs')|Out-Null
$binaryRoot=Join-Path $artifacts 'bin/BeatInsight.Mod/release'
Copy-Item -LiteralPath (Join-Path $binaryRoot 'BeatInsight.dll') -Destination (Join-Path $package 'Plugins/BeatInsight.dll')
foreach($name in @('BeatInsight.Core.dll','BeatInsight.Formats.dll','BeatInsight.WallAlert.dll')){
    Copy-Item -LiteralPath (Join-Path $binaryRoot $name) -Destination (Join-Path $package "Libs/$name")
}
& (Join-Path $PSScriptRoot 'Test-ModPackage.ps1') -GameRoot $GameRoot -PackageDirectory $package -Report (Join-Path $OutputDirectory 'package-check.json')
$manifest=Get-Content -LiteralPath (Join-Path $workspace 'src/BeatInsight.Mod/manifest.json') -Raw|ConvertFrom-Json
$archive=Join-Path $OutputDirectory ("BeatInsight-$($manifest.gameVersion).zip")
[IO.Compression.ZipFile]::CreateFromDirectory($package,$archive)
Write-Host "Package: $archive"
Write-Host 'Package was not installed. Game runtime behavior still requires live verification.'
