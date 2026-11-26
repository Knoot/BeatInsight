#requires -Version 7.0
[CmdletBinding()]
param([Parameter(Mandatory)][string]$PackageDirectory,[string]$GameRoot,[string]$Report)
$ErrorActionPreference='Stop'
. (Join-Path $PSScriptRoot 'LocalSettings.ps1')
$GameRoot=Get-BeatInsightSetting GameRoot $GameRoot
Add-Type -Path (Get-BeatInsightSetting CecilPath)
$modules=[Collections.Generic.List[Mono.Cecil.ModuleDefinition]]::new()
$resolver=[Mono.Cecil.DefaultAssemblyResolver]::new()
$resolver.AddSearchDirectory((Join-Path $GameRoot 'Libs'))
$resolver.AddSearchDirectory((Join-Path $GameRoot 'Beat Saber_Data/Managed'))
$readerParameters=[Mono.Cecil.ReaderParameters]::new()
$readerParameters.AssemblyResolver=$resolver
try {
    $expected=@('Plugins/BeatInsight.dll','Libs/BeatInsight.Core.dll','Libs/BeatInsight.Formats.dll','Libs/BeatInsight.WallAlert.dll')
    $actual=@(Get-ChildItem -LiteralPath $PackageDirectory -File -Recurse|ForEach-Object {[IO.Path]::GetRelativePath([IO.Path]::GetFullPath($PackageDirectory),$_.FullName).Replace('\','/')})
    if(@(Compare-Object $expected $actual).Count){throw 'Package must contain only the four own assemblies.'}
    $plugin=[Mono.Cecil.ModuleDefinition]::ReadModule((Join-Path $PackageDirectory 'Plugins/BeatInsight.dll'),$readerParameters);$modules.Add($plugin)
    $main=[Mono.Cecil.ModuleDefinition]::ReadModule((Join-Path $GameRoot 'Beat Saber_Data/Managed/Main.dll'));$modules.Add($main)
    $dataModels=[Mono.Cecil.ModuleDefinition]::ReadModule((Join-Path $GameRoot 'Beat Saber_Data/Managed/DataModels.dll'));$modules.Add($dataModels)
    # Bundled documents are read through the public game provider on the main
    # thread; guard every additional API before staging the package.
    $provider=$dataModels.Types|Where-Object Name -eq 'IBeatmapLevelData'
    $readMap=@($provider.Methods|Where-Object {$_.Name -eq 'GetBeatmapString' -and $_.IsPublic -and $_.ReturnType.FullName -eq 'System.String' -and $_.Parameters.Count -eq 1})
    if($readMap.Count -ne 1 -or $readMap[0].Parameters[0].ParameterType.FullName -notlike 'BeatmapKey&*'){throw 'Bundled beatmap document API changed.'}
    if(@($provider.Methods|Where-Object {$_.Name -eq 'GetAudioDataString' -and $_.IsPublic -and $_.Parameters.Count -eq 0 -and $_.ReturnType.FullName -eq 'System.String'}).Count -ne 1){throw 'Bundled audio document API changed.'}
    $bundled=$dataModels.Types|Where-Object Name -eq 'BeatmapLevelDataSO'
    if(@($bundled.Methods|Where-Object {$_.Name -eq 'get_songAudioClip' -and $_.IsPublic -and $_.ReturnType.FullName -eq 'UnityEngine.AudioClip'}).Count -ne 1){throw 'Bundled audio clip API changed.'}
    $levels=$dataModels.Types|Where-Object Name -eq 'BeatmapLevelsModel'
    if(@($levels.Methods|Where-Object {$_.Name -eq 'GetBeatmapLevel' -and $_.IsPublic -and $_.Parameters.Count -eq 1 -and $_.Parameters[0].ParameterType.FullName -eq 'System.String' -and $_.ReturnType.FullName -eq 'BeatmapLevel'}).Count -ne 1){throw 'Bundled level lookup API changed.'}
    $level=$dataModels.Types|Where-Object Name -eq 'BeatmapLevel'
    foreach($field in @('beatsPerMinute','songDuration','songTimeOffset')){
        if(@($level.Fields|Where-Object {$_.Name -eq $field -and $_.IsPublic -and $_.FieldType.FullName -eq 'System.Single'}).Count -ne 1){throw 'Bundled level metadata API changed.'}
    }
    $resources=@($plugin.Resources|Where-Object Name -like '*.manifest.json')
    if($resources.Count -ne 1){throw 'Expected one embedded BSIPA manifest.'}
    $reader=[IO.StreamReader]::new($resources[0].GetResourceStream())
    try {$manifest=$reader.ReadToEnd()|ConvertFrom-Json}finally{$reader.Dispose()}
    if($manifest.id -ne 'BeatInsight' -or $manifest.gameVersion -ne '1.40.8' -or $manifest.version -ne '0.1.0'){throw 'Unexpected manifest identity or version.'}
    if(!$manifest.dependsOn.BeatSaberMarkupLanguage){throw 'Menu rendering requires a declared BSML dependency.'}
    $settingsResource=@($plugin.Resources|Where-Object Name -eq 'BeatInsight.Mod.Settings.bsml')
    if($settingsResource.Count -ne 1){throw 'Missing embedded settings screen.'}
    $reader=[IO.StreamReader]::new($settingsResource[0].GetResourceStream())
    try{[xml]$settingsXml=$reader.ReadToEnd()}finally{$reader.Dispose()}
    $controls=@($settingsXml.SelectNodes('//toggle-setting | //slider-setting'))
    $config=$plugin.Types|Where-Object FullName -eq 'BeatInsight.Mod.PluginConfig'
    $bindings=@($config.Properties|Where-Object {$_.CustomAttributes.AttributeType.FullName -contains 'BeatSaberMarkupLanguage.Attributes.UIValue'})
    if($controls.Count -ne 12 -or $bindings.Count -ne 12){throw 'Expected twelve persisted settings controls.'}
    $ids=@($bindings|ForEach-Object {($_.CustomAttributes|Where-Object {$_.AttributeType.FullName -eq 'BeatSaberMarkupLanguage.Attributes.UIValue'}).ConstructorArguments[0].Value})
    if(@(Compare-Object $ids @($controls|ForEach-Object {$_.GetAttribute('value')})).Count -or @($ids|Sort-Object -Unique).Count -ne 12){throw 'Settings bindings are missing or duplicated.'}
    foreach($property in $bindings){
        $expectedType=if($property.Name -eq 'SquatWarningLeadTime'){'System.Single'}else{'System.Boolean'}
        if(!$property.GetMethod.IsVirtual -or !$property.SetMethod.IsVirtual -or $property.PropertyType.FullName -ne $expectedType){throw 'Settings must be virtual persisted values of the expected type.'}
    }
    $bsml=[Mono.Cecil.ModuleDefinition]::ReadModule((Join-Path $GameRoot 'Plugins/BSML.dll'));$modules.Add($bsml)
    foreach($item in @(
        @{type='BeatSaberMarkupLanguage.GameplaySetup.GameplaySetup';field='menus'},
        @{type='BeatSaberMarkupLanguage.GameplaySetup.GameplaySetupMenu';property='Host'},
        @{type='BeatSaberMarkupLanguage.GameplaySetup.GameplaySetupMenu';field='tabObject'})){
        $type=$bsml.Types|Where-Object FullName -eq $item.type
        $member=if($item.field){$type.Fields|Where-Object Name -eq $item.field}else{$type.Properties|Where-Object Name -eq $item.property}
        if(!$type -or !$member){throw "Gameplay settings cleanup contract changed: $($item.type)"}
    }
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
            if($attribute.ConstructorArguments[1].Type.FullName -eq 'HarmonyLib.MethodType'){
                if([int]$targetName -ne 3){throw 'Only the explicitly validated constructor method type is supported.'}
                $targetName='.ctor'
            }
            $targetType=if($targetTypeName -eq 'PlayerData'){$dataModels.Types|Where-Object FullName -eq $targetTypeName}
                elseif($targetTypeName -eq 'BeatSaberMarkupLanguage.GameplaySetup.GameplaySetup'){$bsml.Types|Where-Object FullName -eq $targetTypeName}
                else{$main.Types|Where-Object FullName -eq $targetTypeName}
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
    if($patches.Count -ne 18){throw 'Expected five gameplay/fingerprint, one seek, seven menu/settings and five squat callback/song/movement lifecycle patches.'}
    $settingsPatch=$plugin.Types|Where-Object FullName -eq 'BeatInsight.Mod.SettingsMenuPatch'
    $settingsTarget=($settingsPatch.CustomAttributes|Where-Object {$_.AttributeType.FullName -eq 'HarmonyLib.HarmonyPatch'}).ConstructorArguments
    if($settingsTarget[0].Value.FullName -ne 'BeatSaberMarkupLanguage.GameplaySetup.GameplaySetup' -or $settingsTarget[1].Value -ne 'Initialize' -or
       !($settingsPatch.Methods.CustomAttributes.AttributeType.FullName -contains 'HarmonyLib.HarmonyPrefix')){throw 'Register settings before BSML initializes its tab tree.'}
    $settingsMenu=$plugin.Types|Where-Object FullName -eq 'BeatInsight.Mod.MenuSettings'
    $settingsEnable=$settingsMenu.Methods|Where-Object Name -eq 'Enable'
    if($settingsEnable.Body.Instructions|Where-Object {$_.Operand -is [Mono.Cecil.MethodReference] -and $_.Operand.Name -in @('EnsureRegistered','AddTab')}){throw 'Plugin enable must not rebuild an existing gameplay tab tree.'}
    $settingsRegister=$settingsMenu.Methods|Where-Object Name -eq 'EnsureRegistered'
    if($settingsRegister.Parameters.Count -ne 1 -or $settingsRegister.Parameters[0].ParameterType.FullName -ne 'BeatSaberMarkupLanguage.GameplaySetup.GameplaySetup' -or
       ($settingsRegister.Body.Instructions|Where-Object {$_.Operand -is [Mono.Cecil.MethodReference] -and $_.Operand.Name -eq 'get_Instance'})){throw 'Settings registration must use the initializing BSML instance.'}
    $diagnostics=$plugin.Types|Where-Object FullName -eq 'BeatInsight.Mod.RuntimeDiagnostics'
    if(!$diagnostics){throw 'Runtime diagnostics must be packaged.'}
    $rotationProbe=$diagnostics.Methods|Where-Object Name -eq 'CaptureRotationMath'
    if(!$rotationProbe){throw 'Bounded Unity rotation diagnostics must be packaged.'}
    $probeCallers=@($diagnostics.Methods|Where-Object {$_.HasBody -and ($_.Body.Instructions|Where-Object {$_.Operand -is [Mono.Cecil.MethodReference] -and $_.Operand.Name -eq 'CaptureRotationMath'})})
    if($probeCallers.Count -ne 1 -or $probeCallers[0].Name -ne 'Enable'){throw 'Rotation math diagnostics must run only at diagnostics enable.'}
    $probeCalls=@($rotationProbe.Body.Instructions|Where-Object {$_.Operand -is [Mono.Cecil.MethodReference]})
    $probeGate=$probeCalls|Where-Object {$_.Operand.Name -eq 'get_CanRecord'}|Select-Object -First 1
    $probeEuler=@($probeCalls|Where-Object {$_.Operand.DeclaringType.FullName -eq 'UnityEngine.Quaternion' -and $_.Operand.Name -eq 'Euler'})
    if(!$probeGate -or $probeEuler.Count -ne 2 -or $probeGate.Offset -gt $probeEuler[0].Offset){throw 'Rotation math diagnostics must be opt-in before native math calls.'}
    $probeArrays=@($rotationProbe.Body.Instructions|Where-Object {$_.OpCode.Name -eq 'newarr' -and $_.Operand.FullName -eq 'UnityEngine.Vector3'})
    if($probeArrays.Count -ne 1 -or $probeArrays[0].Previous.OpCode.Name -ne 'ldc.i4.s' -or [int]$probeArrays[0].Previous.Operand -ne 24){throw 'Rotation math diagnostics must retain the bounded 24-input matrix.'}
    if($probeCalls|Where-Object {$_.Operand.DeclaringType.FullName -eq 'UnityEngine.Transform'}){throw 'Rotation math diagnostics must not touch scene transforms.'}
    $noteObserver=$diagnostics.Methods|Where-Object Name -eq 'NoteInitialized'
    if(!$noteObserver -or !$noteObserver.IsStatic -or $noteObserver.ReturnType.FullName -ne 'System.Void' -or
       $noteObserver.Parameters.Count -ne 1 -or $noteObserver.Parameters[0].Name -ne '__instance' -or $noteObserver.Parameters[0].ParameterType.FullName -ne 'NoteController'){
        throw 'Note diagnostics require a typed observational postfix without argument arrays.'
    }
    if(($diagnostics.Fields|Where-Object Name -eq 'NoteSampleLimit').Constant -ne 3 -or
       ($diagnostics.Fields|Where-Object Name -eq 'NotePoseLimit').Constant -ne 12){throw 'Note pose diagnostics must remain bounded to three notes and twelve snapshots.'}
    $noteGate=$noteObserver.Body.Instructions|Where-Object {$_.Operand -is [Mono.Cecil.MethodReference] -and $_.Operand.Name -eq 'get_CanRecord'}|Select-Object -First 1
    $noteBudget=$noteObserver.Body.Instructions|Where-Object {$_.Operand -is [Mono.Cecil.FieldReference] -and $_.Operand.Name -eq 'noteSamples'}|Select-Object -First 1
    $noteAllocation=$noteObserver.Body.Instructions|Where-Object {$_.OpCode.Name -in @('newobj','newarr')}|Select-Object -First 1
    if(!$noteGate -or !$noteBudget -or !$noteAllocation -or $noteGate.Offset -gt $noteAllocation.Offset -or $noteBudget.Offset -gt $noteAllocation.Offset){throw 'Note observer must check opt-in and budget before allocations.'}
    $noteReferences=$diagnostics.Fields|Where-Object Name -eq 'sampledNotes'
    if(!$noteReferences.FieldType.FullName.Contains('System.WeakReference`1<NoteController>') -or !$noteReferences.FieldType.FullName.Contains('System.WeakReference`1<NoteData>')){throw 'Note diagnostics must retain only weak controller/data identities.'}
    $poseRecorder=$diagnostics.Methods|Where-Object Name -eq 'RecordNotePose'
    $poseGate=$poseRecorder.Body.Instructions|Where-Object {$_.Operand -is [Mono.Cecil.MethodReference] -and $_.Operand.Name -eq 'get_CanRecord'}|Select-Object -First 1
    $poseBudget=$poseRecorder.Body.Instructions|Where-Object {$_.Operand -is [Mono.Cecil.FieldReference] -and $_.Operand.Name -eq 'notePoseSamples'}|Select-Object -First 1
    $poseSnapshot=$poseRecorder.Body.Instructions|Where-Object {$_.Operand -is [Mono.Cecil.MethodReference] -and $_.Operand.Name -eq 'NoteSnapshot'}|Select-Object -First 1
    if(!$poseGate -or !$poseBudget -or !$poseSnapshot -or $poseGate.Offset -gt $poseSnapshot.Offset -or $poseBudget.Offset -gt $poseSnapshot.Offset){throw 'Detailed note pose reads must follow opt-in and snapshot budget checks.'}
    $noteSamplers=@($diagnostics.Methods|Where-Object {$_.HasBody -and ($_.Body.Instructions|Where-Object {$_.Operand -is [Mono.Cecil.MethodReference] -and $_.Operand.Name -eq 'SampleNotes'})})
    if($noteSamplers.Count -ne 1 -or $noteSamplers[0].Name -ne 'TakeSample'){throw 'Note follow-up snapshots must reuse existing event-driven sampling.'}
    $noteReset=$diagnostics.Methods|Where-Object Name -eq 'ResetSamples'
    foreach($field in @('sampledNotes','noteSamples','notePoseSamples','rowPatchesSampled')){
        if(!($noteReset.Body.Instructions|Where-Object {$_.Operand -is [Mono.Cecil.FieldReference] -and $_.Operand.Name -eq $field})){throw 'Note diagnostic references and budgets must reset with attempt/disposal.'}
    }
    $rowCapture=$diagnostics.Methods|Where-Object Name -eq 'CaptureRowPatches'
    $rowCallers=@($diagnostics.Methods|Where-Object {$_.HasBody -and ($_.Body.Instructions|Where-Object {$_.Operand -is [Mono.Cecil.MethodReference] -and $_.Operand.Name -eq 'CaptureRowPatches'})})
    if($rowCallers.Count -ne 1 -or $rowCallers[0].Name -ne 'SongStart'){throw 'Row patch inspection must run only before music starts.'}
    $rowGate=$rowCapture.Body.Instructions|Where-Object {$_.Operand -is [Mono.Cecil.MethodReference] -and $_.Operand.Name -eq 'get_CanRecord'}|Select-Object -First 1
    $rowBudget=$rowCapture.Body.Instructions|Where-Object {$_.Operand -is [Mono.Cecil.FieldReference] -and $_.Operand.Name -eq 'rowPatchesSampled'}|Select-Object -First 1
    $rowRead=$rowCapture.Body.Instructions|Where-Object {$_.Operand -is [Mono.Cecil.MethodReference] -and $_.Operand.Name -eq 'GetPatchInfo'}|Select-Object -First 1
    if(!$rowGate -or !$rowBudget -or !$rowRead -or $rowGate.Offset -gt $rowRead.Offset -or $rowBudget.Offset -gt $rowRead.Offset){throw 'Row patch metadata must follow opt-in and once-per-attempt gates.'}
    if($rowCapture.Body.Instructions|Where-Object {$_.Operand -is [Mono.Cecil.MethodReference] -and $_.Operand.Name -in @('Patch','Unpatch','Invoke','StartCoroutine','Delay')}){throw 'Row patch diagnostics must only observe registration metadata.'}
    foreach($owner in @('NoteInitialized','RecordNotePose','SampleNotes','NoteSnapshot','NoteBoxes','NoteMetadata')){
        $method=$diagnostics.Methods|Where-Object Name -eq $owner
        foreach($instruction in $method.Body.Instructions){
            if($instruction.Operand -isnot [Mono.Cecil.MethodReference]){continue}
            $call=$instruction.Operand
            if($call.Name -in @('StartCoroutine','AddComponent','SetActive','Instantiate','Destroy','SetParent','Delay','SetScale') -or
               $call.Name.StartsWith('set_') -and $call.DeclaringType.FullName -like 'UnityEngine.*' -or
               $call.Name -eq '.ctor' -and $call.DeclaringType.FullName -like 'UnityEngine.*'){
                throw 'Note pose diagnostics must not mutate/create scene objects or add scheduling.'
            }
        }
    }
    $diagnosticRead=$diagnostics.Methods|Where-Object Name -eq 'Read'
    if($diagnosticRead.Body.Instructions|Where-Object {$_.Operand -is [Mono.Cecil.MethodReference] -and $_.Operand.DeclaringType.FullName -eq 'HarmonyLib.AccessTools'}){throw 'Optional diagnostic member lookup must not generate Harmony warnings.'}
    $dynamicHooks=[Collections.Generic.List[object]]::new()
    $install=$diagnostics.Methods|Where-Object Name -eq 'Install'
    foreach($instruction in $install.Body.Instructions){
        if($instruction.Operand -isnot [Mono.Cecil.MethodReference] -or $instruction.Operand.Name -ne 'Hook'){continue}
        $methodName=$instruction.Previous.Operand
        $typeName=$instruction.Previous.Previous.Operand
        $assemblyName=$instruction.Previous.Previous.Previous.Operand
        if($methodName -in @('Update','ManualUpdate','LateUpdate','FixedUpdate','ShouldShow')){throw 'Diagnostics must not hook per-frame/playback paths.'}
        if($assemblyName -eq 'Main'){$diagnosticModule=$main}
        elseif($assemblyName -eq 'NoodleExtensions' -and !(Test-Path -LiteralPath (Join-Path $GameRoot 'Plugins/NoodleExtensions.dll'))){continue}
        else{
            $diagnosticPath=Join-Path $GameRoot $(if($assemblyName -eq 'NoodleExtensions'){'Plugins/NoodleExtensions.dll'}else{"Beat Saber_Data/Managed/$assemblyName.dll"})
            $diagnosticModule=[Mono.Cecil.ModuleDefinition]::ReadModule($diagnosticPath);$modules.Add($diagnosticModule)
        }
        $diagnosticType=$diagnosticModule.Types|Where-Object FullName -eq $typeName
        $targets=@($diagnosticType.Methods|Where-Object Name -eq $methodName)
        if($targets.Count -ne 1){throw "Missing/ambiguous diagnostic boundary: $typeName::$methodName"}
        if($targets[0].ReturnType.FullName -ne 'System.Void' -and $targets[0].IsStatic){throw 'Non-void static diagnostic boundary needs a distinct adapter.'}
        $dynamicHooks.Add(@{assembly=$assemblyName;type=$typeName;method=$methodName})
    }
    if($dynamicHooks.Count -lt 12 -or @($dynamicHooks|Where-Object {$_.type -eq 'NoteController' -and $_.method -eq 'Init'}).Count -ne 1){throw 'Expected required scene/room and typed note diagnostic boundaries.'}
    foreach($name in @('Before','After','BeforeStatic','AfterStatic','AfterResult')){
        $adapter=$diagnostics.Methods|Where-Object Name -eq $name
        if($adapter.ReturnType.FullName -ne 'System.Void' -or !$adapter.IsStatic){throw 'Diagnostic adapter must be an observational void method.'}
        foreach($parameter in $adapter.Parameters){
            $injectionType=switch($parameter.Name){'__instance'{'System.Object'} '__originalMethod'{'System.Reflection.MethodBase'} '__args'{'System.Object[]'} '__result'{'System.Object'} default{throw 'Unsupported diagnostic injection argument.'}}
            if($parameter.ParameterType.FullName -ne $injectionType -or $parameter.ParameterType.IsByReference){throw 'Diagnostic adapter must not replace arguments/results.'}
        }
    }
    $energyCounter=$main.Types|Where-Object Name -eq 'GameEnergyCounter'
    $zeroEvent=$energyCounter.Events|Where-Object Name -eq 'gameEnergyDidReach0Event'
    if(!$zeroEvent -or $zeroEvent.EventType.FullName -ne 'System.Action' -or !$zeroEvent.AddMethod.IsPublic -or !$zeroEvent.RemoveMethod.IsPublic){throw 'Zero-energy observation contract changed.'}
    $capture=$plugin.Types|Where-Object FullName -eq 'BeatInsight.Mod.RuntimeCapture'
    foreach($contract in @(@{method='Attach';call='add_gameEnergyDidReach0Event'},@{method='Detach';call='remove_gameEnergyDidReach0Event'},@{method='DetachAll';call='remove_gameEnergyDidReach0Event'})){
        $owner=$capture.Methods|Where-Object Name -eq $contract.method
        if(!($owner.Body.Instructions|Where-Object {$_.Operand -is [Mono.Cecil.MethodReference] -and $_.Operand.DeclaringType.FullName -eq 'GameEnergyCounter' -and $_.Operand.Name -eq $contract.call})){throw 'Zero-energy observer must be attached and removed with the gameplay manager.'}
    }
    $presenter=$plugin.Types|Where-Object FullName -eq 'BeatInsight.Mod.SquatWarningPresenter'
    $visited=[Collections.Generic.HashSet[string]]::new()
    function Test-WarningSignalMethod($method){
        if(!$visited.Add($method.FullName)){return}
        foreach($instruction in $method.Body.Instructions){
            if($instruction.OpCode.Name -eq 'newobj' -or $instruction.OpCode.Name -eq 'newarr'){throw 'Warning signal path must not allocate objects or arrays.'}
            if($instruction.Operand -is [Mono.Cecil.MethodReference]){
                $call=$instruction.Operand
                if($call.Name -in @('CreatePrimitive','AddComponent','Instantiate','Find','ForceMeshUpdate','Prepare','CreateText','CreateCircles')){throw "Resource creation reached from warning signal: $($call.FullName)"}
                if($call.DeclaringType.FullName -eq $presenter.FullName){
                    $next=$presenter.Methods|Where-Object FullName -eq $call.FullName
                    if($next.HasBody){Test-WarningSignalMethod $next}
                }
            }
        }
    }
    Test-WarningSignalMethod ($presenter.Methods|Where-Object Name -eq 'Show')
    $warnings=$plugin.Types|Where-Object FullName -eq 'BeatInsight.Mod.SquatWarnings'
    $lookup=$warnings.Methods|Where-Object Name -eq 'ShouldShow'
    $prepareWarnings=$warnings.Methods|Where-Object Name -eq 'Prepare'
    if(!($prepareWarnings.Body.Instructions|Where-Object {$_.Operand -is [Mono.Cecil.MethodReference] -and $_.Operand.Name -eq 'HasCustomWallGeometry'})){throw 'Custom obstacle geometry must be checked before warning playback.'}
    foreach($instruction in $lookup.Body.Instructions){
        if($instruction.OpCode.Name -in @('newobj','newarr')){throw 'Prepared warning lookup must not allocate.'}
        if($instruction.Operand -is [Mono.Cecil.MethodReference] -and $instruction.Operand.Name -in @('Build','MappingActive','GetValue','OrderBy','ToArray','HasCustomWallGeometry')){throw 'Warning lookup must not prepare geometry or inspect extension state.'}
    }
    $warningLibrary=[Mono.Cecil.ModuleDefinition]::ReadModule((Join-Path $PackageDirectory 'Libs/BeatInsight.WallAlert.dll'),$readerParameters);$modules.Add($warningLibrary)
    $playback=$warningLibrary.Types|Where-Object FullName -eq 'BeatInsight.WallAlert.SquatWarningPlayback'
    foreach($method in $playback.Methods|Where-Object Name -in @('Observe','Reset')){
        if(@($method.Body.Instructions|Where-Object {$_.OpCode.Name -in @('newobj','newarr')}).Count){throw 'Prepared warning playback/reset must not allocate.'}
    }
    $callbackInit=$main.Types.Where({$_.Name -eq 'BeatmapCallbacksController'}).NestedTypes|Where-Object Name -eq 'InitData'
    foreach($required in @(@{name='beatmapData';type='IReadonlyBeatmapData'},@{name='startFilterTime';type='System.Single'})){
        if(!($callbackInit.Fields|Where-Object {$_.Name -eq $required.name -and $_.FieldType.FullName -eq $required.type})){throw 'Loaded callback obstacle snapshot contract changed.'}
    }
    $mappingPlugin=Join-Path $GameRoot 'Plugins/MappingExtensions.dll'
    if(Test-Path -LiteralPath $mappingPlugin){
        $mappingModule=[Mono.Cecil.ModuleDefinition]::ReadModule($mappingPlugin);$modules.Add($mappingModule)
        $mappingType=$mappingModule.Types|Where-Object FullName -eq 'MappingExtensions.Plugin'
        if(!($mappingType.Fields|Where-Object {$_.Name -eq 'active' -and $_.IsStatic -and $_.FieldType.FullName -eq 'System.Boolean'})){throw 'Mapping Extensions activation contract changed.'}
    }
    $customPlugin=Join-Path $GameRoot 'Plugins/CustomJSONData.dll'
    if(Test-Path -LiteralPath $customPlugin){
        $customModule=[Mono.Cecil.ModuleDefinition]::ReadModule($customPlugin);$modules.Add($customModule)
        $obstacleMetadata=$customModule.Types|Where-Object FullName -eq 'CustomJSONData.CustomBeatmap.CustomObstacleData'
        foreach($required in @('customData','version')){
            if(!($obstacleMetadata.Properties|Where-Object {$_.Name -eq $required -and $_.GetMethod.IsPublic})){throw 'Custom obstacle fake metadata contract changed.'}
        }
    }
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
    $result=@{status='pass';validation='metadata, packaged references and Harmony signatures; game not started';gameVersion=$manifest.gameVersion;patches=$patches.ToArray();diagnosticHooks=$dynamicHooks.ToArray();references=$references.ToArray();files=@($expected|ForEach-Object {@{file=$_;sha256=(Get-FileHash -LiteralPath (Join-Path $PackageDirectory $_)).Hash}})}
    if($Report){$result|ConvertTo-Json -Depth 8|Set-Content -LiteralPath $Report}
    Write-Host "Package metadata, references and $($patches.Count) Harmony targets and $($dynamicHooks.Count) diagnostic hooks verified."
} finally {foreach($module in $modules){$module.Dispose()};$resolver.Dispose()}
