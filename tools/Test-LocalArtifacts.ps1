#requires -Version 7.0
[CmdletBinding()]
param([string]$Binary)
$ErrorActionPreference='Stop'
if($Binary){$Binary=(Resolve-Path -LiteralPath $Binary).Path}
$originalEnvironment=$env:BEATINSIGHT_LOCAL_ROOT
$fixtureRoot=Join-Path ([IO.Path]::GetTempPath()) ('BeatInsight-LocalArtifacts-'+[Guid]::NewGuid().ToString('N'))
$checks=0
try{
    $toolsFolder=Join-Path $fixtureRoot 'tools'
    New-Item -ItemType Directory -Path $toolsFolder|Out-Null
    Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'LocalSettings.ps1') -Destination $toolsFolder
    '<Project />'|Set-Content -LiteralPath (Join-Path $fixtureRoot 'Directory.Build.props')
    & {
        . (Join-Path $toolsFolder 'LocalSettings.ps1')
        function Assert-ArtifactRoot([string]$Expected){
            $before=$env:BEATINSIGHT_LOCAL_ROOT
            $actual=Get-BeatInsightLocalRoot
            if($actual -ne $Expected){throw 'PowerShell artifact root mismatch'}
            $script:checks++
            if($Binary){
                $env:BEATINSIGHT_LOCAL_ROOT=$before
                Push-Location $fixtureRoot
                try{$actual=& dotnet $Binary --local-artifacts-root;if($LASTEXITCODE -or $actual -ne $Expected){throw 'C# artifact root mismatch'};$script:checks++}
                finally{Pop-Location}
            }
        }
        $env:BEATINSIGHT_LOCAL_ROOT=$null
        Assert-ArtifactRoot (Join-Path $fixtureRoot '.local')
        if(Test-Path -LiteralPath (Join-Path $fixtureRoot '.local')){throw 'Resolving a root must not create it'}
        $script:checks++
        '<Project><PropertyGroup /><PropertyGroup><LocalArtifactsRoot>custom-output</LocalArtifactsRoot></PropertyGroup></Project>'|Set-Content -LiteralPath (Join-Path $fixtureRoot 'paths.props')
        $env:BEATINSIGHT_LOCAL_ROOT=$null
        Assert-ArtifactRoot (Join-Path $fixtureRoot 'custom-output')
        $override=Join-Path ([IO.Path]::GetTempPath()) ('BeatInsight-Override-'+[Guid]::NewGuid().ToString('N'))
        $env:BEATINSIGHT_LOCAL_ROOT=$override
        Assert-ArtifactRoot $override
        if((Resolve-BeatInsightOutputDirectory (Join-Path $override 'child')) -ne (Join-Path $override 'child')){throw 'Configured output was rejected'}
        $script:checks++
        if((Resolve-BeatInsightOutputDirectory (Join-Path $fixtureRoot 'child')) -ne (Join-Path $fixtureRoot 'child')){throw 'Workspace output was rejected'}
        $script:checks++
        foreach($outside in @(($override+'-sibling'),([IO.Path]::GetPathRoot($override)))){
            $rejected=$false;try{Resolve-BeatInsightOutputDirectory $outside|Out-Null}catch{$rejected=$true}
            if(!$rejected){throw 'Output boundary accepted an unrelated root'}
            $script:checks++
        }
        $env:BEATINSIGHT_LOCAL_ROOT='relative-output'
        Assert-ArtifactRoot (Join-Path $fixtureRoot 'relative-output')
    }
    Write-Host "Passed $checks local artifact configuration checks."
}finally{
    $env:BEATINSIGHT_LOCAL_ROOT=$originalEnvironment
    $resolved=[IO.Path]::GetFullPath($fixtureRoot)
    $prefix=[IO.Path]::GetFullPath([IO.Path]::GetTempPath()).TrimEnd([IO.Path]::DirectorySeparatorChar)+[IO.Path]::DirectorySeparatorChar
    if(!$resolved.StartsWith($prefix,[StringComparison]::OrdinalIgnoreCase) -or [IO.Path]::GetFileName($resolved) -notlike 'BeatInsight-LocalArtifacts-*'){throw 'Unsafe temporary fixture cleanup target'}
    if(Test-Path -LiteralPath $resolved){Remove-Item -LiteralPath $resolved -Recurse -Force}
}
