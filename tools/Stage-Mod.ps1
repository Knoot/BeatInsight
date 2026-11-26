#requires -Version 7.0
[CmdletBinding()]
param([Parameter(Mandatory)][string]$PackageDirectory,[string]$GameRoot)
$ErrorActionPreference='Stop'
$workspace=Split-Path $PSScriptRoot -Parent
. (Join-Path $PSScriptRoot 'LocalSettings.ps1')
$previousArtifactRoot=$env:BEATINSIGHT_LOCAL_ROOT
try {
$env:BEATINSIGHT_LOCAL_ROOT=Get-BeatInsightLocalRoot
$GameRoot=[IO.Path]::GetFullPath((Get-BeatInsightSetting GameRoot $GameRoot))
$PackageDirectory=(Resolve-Path -LiteralPath $PackageDirectory).Path
& (Join-Path $PSScriptRoot 'Test-ModPackage.ps1') -GameRoot $GameRoot -PackageDirectory $PackageDirectory
$pending=[IO.Path]::GetFullPath((Join-Path $GameRoot 'IPA/Pending'))
$allowedPrefix=$pending.TrimEnd('\')+'\'
$files=@('Plugins/BeatInsight.dll','Libs/BeatInsight.Core.dll','Libs/BeatInsight.Formats.dll','Libs/BeatInsight.WallAlert.dll')
$backup=Join-Path (Get-BeatInsightLocalRoot) ('staging/'+(Get-Date -Format 'yyyyMMdd-HHmmss')+'-'+[Guid]::NewGuid().ToString('N').Substring(0,8))
New-Item -ItemType Directory -Path $backup|Out-Null
$entries=[Collections.Generic.List[object]]::new()
foreach($relative in $files){
    $target=[IO.Path]::GetFullPath((Join-Path $pending $relative))
    if(!$target.StartsWith($allowedPrefix,[StringComparison]::OrdinalIgnoreCase)){throw 'Staging target escapes IPA/Pending.'}
    for($check=Split-Path $target -Parent;$check -and $check.StartsWith($pending,[StringComparison]::OrdinalIgnoreCase);$check=Split-Path $check -Parent){
        if((Test-Path -LiteralPath $check) -and ((Get-Item -LiteralPath $check -Force).Attributes -band [IO.FileAttributes]::ReparsePoint)){throw "Staging directory is a link: $check"}
    }
    if((Test-Path -LiteralPath $target) -and ((Get-Item -LiteralPath $target -Force).Attributes -band [IO.FileAttributes]::ReparsePoint)){throw "Staging file is a link: $target"}
    $existed=Test-Path -LiteralPath $target
    if($existed){
        $saved=Join-Path $backup $relative;New-Item -ItemType Directory -Path (Split-Path $saved -Parent) -Force|Out-Null
        Copy-Item -LiteralPath $target -Destination $saved
    }
    $entries.Add(@{file=$relative;target=$target;existed=$existed;sha256=(Get-FileHash -LiteralPath (Join-Path $PackageDirectory $relative)).Hash})
}
$copied=[Collections.Generic.List[object]]::new()
try {
    foreach($entry in $entries){
        New-Item -ItemType Directory -Path (Split-Path $entry.target -Parent) -Force|Out-Null
        $copied.Add($entry)
        Copy-Item -LiteralPath (Join-Path $PackageDirectory $entry.file) -Destination $entry.target -Force
        if((Get-FileHash -LiteralPath $entry.target).Hash -ne $entry.sha256){throw "Staged hash differs: $($entry.file)"}
    }
    @{status='staged';pending=$pending;gameStarted=$false;files=$entries.ToArray()}|ConvertTo-Json -Depth 6|Set-Content -LiteralPath (Join-Path $backup 'staging.json')
} catch {
    foreach($entry in $copied){
        if($entry.existed){Copy-Item -LiteralPath (Join-Path $backup $entry.file) -Destination $entry.target -Force}
        elseif(Test-Path -LiteralPath $entry.target){Remove-Item -LiteralPath $entry.target}
    }
    throw
}
Write-Host "Staged $($entries.Count) own assemblies under IPA/Pending. Other pending files were left in place."
Write-Host "Backup and report: $backup"

} finally { $env:BEATINSIGHT_LOCAL_ROOT=$previousArtifactRoot }
