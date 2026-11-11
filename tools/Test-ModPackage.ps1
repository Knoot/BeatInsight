#requires -Version 7.0
[CmdletBinding()]
param([Parameter(Mandatory)][string]$PackageDirectory,[string]$GameRoot,[string]$Report)
$ErrorActionPreference='Stop'
. (Join-Path $PSScriptRoot 'LocalSettings.ps1')
$GameRoot=Get-BeatInsightSetting GameRoot $GameRoot
Add-Type -Path (Get-BeatInsightSetting CecilPath)
$modules=[Collections.Generic.List[Mono.Cecil.ModuleDefinition]]::new()
try {
    $expected=@('Plugins/BeatInsight.dll','Libs/BeatInsight.Core.dll','Libs/BeatInsight.Formats.dll','Libs/BeatInsight.WallAlert.dll')
    $actual=@(Get-ChildItem -LiteralPath $PackageDirectory -File -Recurse|ForEach-Object {[IO.Path]::GetRelativePath([IO.Path]::GetFullPath($PackageDirectory),$_.FullName).Replace('\','/')})
    if(@(Compare-Object $expected $actual).Count){throw 'Package must contain only the four own assemblies.'}
    $plugin=[Mono.Cecil.ModuleDefinition]::ReadModule((Join-Path $PackageDirectory 'Plugins/BeatInsight.dll'));$modules.Add($plugin)
    $main=[Mono.Cecil.ModuleDefinition]::ReadModule((Join-Path $GameRoot 'Beat Saber_Data/Managed/Main.dll'));$modules.Add($main)
    $resources=@($plugin.Resources|Where-Object Name -like '*.manifest.json')
    if($resources.Count -ne 1){throw 'Expected one embedded BSIPA manifest.'}
    $reader=[IO.StreamReader]::new($resources[0].GetResourceStream())
    try {$manifest=$reader.ReadToEnd()|ConvertFrom-Json}finally{$reader.Dispose()}
    if($manifest.id -ne 'BeatInsight' -or $manifest.gameVersion -ne '1.40.8' -or $manifest.version -ne '0.1.0'){throw 'Unexpected manifest identity or version.'}
    if(!$manifest.dependsOn.BeatSaberMarkupLanguage){throw 'Menu rendering requires a declared BSML dependency.'}
    $entry=@($plugin.Types|Where-Object {@($_.CustomAttributes|Where-Object {$_.AttributeType.FullName -eq 'IPA.PluginAttribute'}).Count})
    if($entry.Count -ne 1){throw 'Expected one BSIPA plugin entry.'}
    if($resources[0].Name -ne ($entry[0].Namespace+'.manifest.json')){throw 'Manifest namespace differs from plugin entry namespace.'}
    foreach($attribute in @('IPA.InitAttribute','IPA.OnEnableAttribute','IPA.OnDisableAttribute')){
        $methods=@($entry[0].Methods|Where-Object {@($_.CustomAttributes|Where-Object {$_.AttributeType.FullName -eq $attribute}).Count})
        if($methods.Count -ne 1){throw "Missing or ambiguous BSIPA lifecycle attribute: $attribute"}
    }
    $patches=[Collections.Generic.List[object]]::new()
    foreach($type in $plugin.Types){
        foreach($attribute in $type.CustomAttributes|Where-Object {$_.AttributeType.FullName -eq 'HarmonyLib.HarmonyPatch'}){
            $targetTypeName=$attribute.ConstructorArguments[0].Value.FullName
            $targetName=$attribute.ConstructorArguments[1].Value
            $targetType=$main.Types|Where-Object FullName -eq $targetTypeName
            $targets=@($targetType.Methods|Where-Object Name -eq $targetName)
            if($targets.Count -ne 1){throw "Missing or ambiguous Harmony target: $targetTypeName::$targetName"}
            $target=$targets[0]
            foreach($method in $type.Methods|Where-Object {@($_.CustomAttributes|Where-Object {$_.AttributeType.FullName -in @('HarmonyLib.HarmonyPrefix','HarmonyLib.HarmonyPostfix')}).Count}){
                foreach($parameter in $method.Parameters){
                    $expectedType=$null
                    if($parameter.Name -eq '__instance'){$expectedType=$targetType.FullName}
                    elseif($parameter.Name -match '^__(\d+)$'){$expectedType=$target.Parameters[[int]$Matches[1]].ParameterType.FullName}
                    elseif($parameter.Name.StartsWith('___')){
                        $fieldName=$parameter.Name.Substring(3)
                        $field=$targetType.Fields|Where-Object Name -eq $fieldName
                        if(!$field){throw "Missing Harmony-injected field: $fieldName"}
                        $expectedType=$field.FieldType.FullName
                    } else {throw "Unvalidated Harmony argument: $($parameter.Name)"}
                    if($expectedType -ne $parameter.ParameterType.FullName){throw "Harmony parameter type mismatch: $($parameter.FullName)"}
                }
            }
            $patches.Add(@{type=$targetTypeName;method=$targetName})
        }
    }
    if($patches.Count -ne 8){throw 'Expected four gameplay and four menu lifecycle patches.'}
    $models=[Mono.Cecil.ModuleDefinition]::ReadModule((Join-Path $GameRoot 'Beat Saber_Data/Managed/DataModels.dll'));$modules.Add($models)
    $hmui=[Mono.Cecil.ModuleDefinition]::ReadModule((Join-Path $GameRoot 'Beat Saber_Data/Managed/HMUI.dll'));$modules.Add($hmui)
    foreach($item in @(
        @{module=$main;type='StandardLevelDetailViewController';field='_contentIsOwnedAndReady';expected='System.Boolean'},
        @{module=$models;type='FileSystemBeatmapLevelData';field='_difficultyBeatmaps';expected='FileDifficultyBeatmap'},
        @{module=$models;type='FileDifficultyBeatmap';field='_beatmapPath';expected='System.String'},
        @{module=$main;type='StandardLevelDetailViewController';field='_standardLevelDetailView';expected='StandardLevelDetailView'},
        @{module=$main;type='StandardLevelDetailView';field='_levelParamsPanel';expected='LevelParamsPanel'},
        @{module=$main;type='StandardLevelDetailView';field='_beatmapDifficultySegmentedControlController';expected='BeatmapDifficultySegmentedControlController'},
        @{module=$main;type='StandardLevelDetailView';field='_buttonsWrapper';expected='UnityEngine.GameObject'},
        @{module=$main;type='StandardLevelDetailViewController';field='_playerDataModel';expected='PlayerDataModel'},
        @{module=$hmui;type='HMUI.HoverHint';field='_hoverHintController';expected='HMUI.HoverHintController'})){
        $owner=$item.module.Types|Where-Object FullName -eq $item.type
        $field=$owner.Fields|Where-Object Name -eq $item.field
        if(!$field -or !$field.FieldType.FullName.Contains($item.expected)){throw "Missing or changed menu source field: $($item.type).$($item.field)"}
    }
    $references=[Collections.Generic.List[object]]::new()
    foreach($relative in $expected){
        $module=[Mono.Cecil.ModuleDefinition]::ReadModule((Join-Path $PackageDirectory $relative));$modules.Add($module)
        foreach($reference in $module.AssemblyReferences){
            $candidates=@((Join-Path $PackageDirectory "Libs/$($reference.Name).dll"),(Join-Path $GameRoot "Beat Saber_Data/Managed/$($reference.Name).dll"),(Join-Path $GameRoot "Libs/$($reference.Name).dll"),(Join-Path $GameRoot "Plugins/$($reference.Name).dll"))
            $path=$candidates|Where-Object {Test-Path -LiteralPath $_}|Select-Object -First 1
            if(!$path){throw "Unresolved assembly reference: $($reference.FullName)"}
            $dependency=[Mono.Cecil.ModuleDefinition]::ReadModule($path)
            try {if($dependency.Assembly.Name.Version -ne $reference.Version){throw "Reference version differs from installed runtime: $($reference.FullName)"}}finally{$dependency.Dispose()}
            $references.Add(@{assembly=$module.Assembly.Name.Name;reference=$reference.FullName})
        }
    }
    $result=@{status='pass';validation='metadata, packaged references and Harmony signatures; game not started';gameVersion=$manifest.gameVersion;patches=$patches.ToArray();references=$references.ToArray();files=@($expected|ForEach-Object {@{file=$_;sha256=(Get-FileHash -LiteralPath (Join-Path $PackageDirectory $_)).Hash}})}
    if($Report){$result|ConvertTo-Json -Depth 8|Set-Content -LiteralPath $Report}
    Write-Host "Package metadata, references and $($patches.Count) Harmony targets verified."
} finally {foreach($module in $modules){$module.Dispose()}}
