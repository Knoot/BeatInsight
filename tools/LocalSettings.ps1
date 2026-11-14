# Shared path resolution. Explicit arguments take priority over environment and local settings.
function Get-BeatInsightSetting([string]$Name, [string]$ExplicitValue, [string]$ResolvedGameRoot) {
    if ($ExplicitValue) { return $ExplicitValue }
    $environmentName = @{ GameRoot='BEATINSIGHT_GAME_ROOT'; MapsRoot='BEATINSIGHT_MAPS_ROOT'; CecilPath='BEATINSIGHT_CECIL_PATH'; NuGetPackageRoot='NUGET_PACKAGES'; ReferenceAnalysisAssembly='BEATINSIGHT_REFERENCE_ANALYSIS_ASSEMBLY'; ReferencePreviewAssembly='BEATINSIGHT_REFERENCE_PREVIEW_ASSEMBLY'; ReferenceUtilityAssembly='BEATINSIGHT_REFERENCE_UTILITY_ASSEMBLY' }[$Name]
    if ($environmentName) {
        $value = [Environment]::GetEnvironmentVariable($environmentName)
        if ($value) { return $value }
    }
    $file = Join-Path (Split-Path $PSScriptRoot -Parent) 'paths.props'
    if (Test-Path -LiteralPath $file) {
        [xml]$settings = Get-Content -LiteralPath $file -Raw
        $node = $settings.SelectSingleNode("/Project/PropertyGroup/$Name")
        if ($node -and $node.InnerText) { return $node.InnerText }
    }
    if ($Name -eq 'NuGetPackageRoot') { return Join-Path ([Environment]::GetFolderPath('UserProfile')) '.nuget/packages' }
    if ($Name -eq 'CecilPath') { return Join-Path (Get-BeatInsightSetting 'NuGetPackageRoot') 'mono.cecil/0.11.6/lib/netstandard2.0/Mono.Cecil.dll' }
    if ($Name -eq 'MapsRoot') {
        $game = $ResolvedGameRoot
        if (!$game) { $game = Get-BeatInsightSetting 'GameRoot' }
        return Join-Path $game 'Beat Saber_Data/CustomLevels'
    }
    throw "Set $Name using an argument, $environmentName or paths.props in the project root. See README.md."
}

function Get-BeatInsightReferenceAssembly([string]$Role,[string]$GameRoot) {
    $value=Get-BeatInsightSetting ('Reference'+$Role+'Assembly')
    if([IO.Path]::IsPathRooted($value)){return $value}
    return Join-Path $GameRoot $value
}
