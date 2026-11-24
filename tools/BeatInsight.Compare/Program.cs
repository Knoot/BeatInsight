using BeatInsight.Compare;
using BeatInsight.Core;
using BeatInsight.Formats;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System.Security.Cryptography;

try
{
    if(args.Length==1&&args[0]=="--profile-version"){Console.WriteLine(LocalMap.AnalysisProfileVersion);return 0;}
    if((args.Length==4||args.Length==5)&&args[0]=="--check-noodle-metric-coverage")
    {
        var jump=args.Length==5?JsonConvert.DeserializeObject<JumpSettingsSnapshot>(File.ReadAllText(args[4])):null;
        var report=NoodleMetricCoverage.Run(args[1],args[2],jump);
        File.WriteAllText(args[3],JsonConvert.SerializeObject(report,Formatting.Indented));
        return (int)JObject.FromObject(report)["errors"]! ==0?0:2;
    }
    if(args.Length==4&&args[0]=="--check-native-accessor-parity")
    {
        var report=NativeAccessorParityChecks.Run(args[1],args[2]);
        File.WriteAllText(args[3],JsonConvert.SerializeObject(report,Formatting.Indented));
        return JObject.FromObject(report)["failed"]!.Value<int>()==0?0:2;
    }
    if((args.Length==4||args.Length==5)&&args[0]=="--check-noodle-menu-coverage")
    {
        var jump=args.Length==5?JsonConvert.DeserializeObject<JumpSettingsSnapshot>(File.ReadAllText(args[4])):null;
        var report=NoodleMenuCoverage.Run(args[1],args[2],jump);
        File.WriteAllText(args[3],JsonConvert.SerializeObject(report,Formatting.Indented));
        var result=JObject.FromObject(report);
        return (int)result["errors"]! ==0&&(int)result["regressions"]! ==0?0:2;
    }
    if(args.Length==3&&args[0]=="--inventory-noodle-tracks")
    {
        var report=NoodleTrackInventory.Run(args[1]);
        File.WriteAllText(args[2],JsonConvert.SerializeObject(report,Formatting.Indented));
        return JObject.FromObject(report)["errors"]!.Value<int>()==0?0:2;
    }
    if(args.Length==2&&args[0]=="--check-noodle-track-inventory")
    {
        var report=NoodleTrackInventoryChecks.Run();
        File.WriteAllText(args[1],JsonConvert.SerializeObject(report,Formatting.Indented));
        return JObject.FromObject(report)["failed"]!.Value<int>()==0?0:2;
    }
    if(args.Length == 0) throw new ArgumentException("Supply gameRoot as the first argument.");
    string gameRoot = Path.GetFullPath(args[0]);
    if(args.Length>1&&args[1] is "--check-noodle-installer" or "--check-native-field-accessors" or "--check-heck-native-accessors" or "--check-heck-native-chains")NativeFieldAccessorChecks.Initialize(gameRoot);
    MappingNativeRuntime.Initialize(gameRoot,args.Length>1&&args[1] is "--check-noodle-copy-mirror" or "--check-noodle-custom-loader" or "--check-noodle-row-loader" or "--check-noodle-arc-density" or "--check-noodle-admitted-arcs" or "--check-noodle-admitted-chains" or "--check-noodle-row-copy" or "--check-noodle-copy-chains" or "--check-heck-map" or "--check-heck-copy" or "--check-heck-native-accessors" or "--check-heck-chains" or "--check-heck-native-chains",
        args.Length>1&&args[1] is "--check-noodle-row-loader" or "--check-noodle-arc-density" or "--check-noodle-admitted-arcs" or "--check-noodle-admitted-chains" or "--check-noodle-row-copy" or "--check-noodle-copy-chains" or "--check-heck-map" or "--check-heck-copy" or "--check-heck-native-accessors" or "--check-heck-chains" or "--check-heck-native-chains",
        args.Length>1&&args[1] is "--check-noodle-row-copy" or "--check-noodle-copy-chains" or "--check-heck-copy" or "--check-heck-native-accessors" or "--check-heck-chains" or "--check-heck-native-chains",
        args.Length>1&&args[1] is "--check-heck-map" or "--check-heck-copy" or "--check-heck-native-accessors" or "--check-heck-chains" or "--check-heck-native-chains");
    var oracle = new OriginalOracle(gameRoot);
    if(args.Length is 3 or 4&&args[1]=="--check-noodle-arc-density")
    {
        var report=NoodleCustomLoaderChecks.Run(gameRoot,inventory:args.Length==4?args[3]:null,noodleRows:true,independentArcDensity:true);
        File.WriteAllText(args[2],JsonConvert.SerializeObject(report,Formatting.Indented));
        return JObject.FromObject(report)["failed"]!.Value<int>()==0?0:2;
    }
    if(args.Length==3&&args[1]=="--check-noodle-installer")
    {
        var report=NoodleInstallerChecks.Run(gameRoot);
        File.WriteAllText(args[2],JsonConvert.SerializeObject(report,Formatting.Indented));
        return JObject.FromObject(report)["failed"]!.Value<int>()==0?0:2;
    }
    if(args.Length==3&&args[1]=="--check-native-field-accessors")
    {
        var report=NativeFieldAccessorChecks.Run(gameRoot);
        File.WriteAllText(args[2],JsonConvert.SerializeObject(report,Formatting.Indented));
        return JObject.FromObject(report)["failed"]!.Value<int>()==0?0:2;
    }
    if(args.Length==3&&args[1]=="--check-heck-registrar")
    {
        var report=HeckRegistrarChecks.Run(gameRoot);
        File.WriteAllText(args[2],JsonConvert.SerializeObject(report,Formatting.Indented));
        return JObject.FromObject(report)["failed"]!.Value<int>()==0?0:2;
    }
    if((args.Length==3||args.Length==4)&&args[1]=="--check-vanilla-walls")
    {
        var report=VanillaWallChecks.Run(gameRoot,args.Length==4?args[3]:null);
        File.WriteAllText(args[2],JsonConvert.SerializeObject(report,Formatting.Indented));
        return JObject.FromObject(report)["failed"]!.Value<int>()==0?0:2;
    }
    if((args.Length==5||args.Length==6)&&args[1] is "--check-noodle-admitted-arcs" or "--check-noodle-admitted-chains" or "--check-noodle-copy-chains" or "--check-heck-chains" or "--check-heck-native-chains")
    {
        var jump=args.Length==6?JsonConvert.DeserializeObject<JumpSettingsSnapshot>(File.ReadAllText(args[5])):null;
        var report=NoodleCustomLoaderChecks.Run(gameRoot,args[3],true,args[4],jump,args[1]!="--check-noodle-admitted-arcs",args[1]=="--check-noodle-copy-chains",heckMap:args[1] is "--check-heck-chains" or "--check-heck-native-chains",heckCopies:args[1] is "--check-heck-chains" or "--check-heck-native-chains");
        File.WriteAllText(args[2],JsonConvert.SerializeObject(report,Formatting.Indented));
        return JObject.FromObject(report)["failed"]!.Value<int>()==0?0:2;
    }
    if((args.Length==3||args.Length==4)&&args[1] is "--check-noodle-custom-loader" or "--check-noodle-row-loader" or "--check-noodle-row-copy" or "--check-heck-map" or "--check-heck-copy" or "--check-heck-native-accessors" or "--check-heck-chains" or "--check-heck-native-chains")
    {
        var report=NoodleCustomLoaderChecks.Run(gameRoot,args.Length==4?args[3]:null,args[1]!="--check-noodle-custom-loader",copyRows:args[1]=="--check-noodle-row-copy",heckMap:args[1] is "--check-heck-map" or "--check-heck-copy" or "--check-heck-native-accessors" or "--check-heck-chains" or "--check-heck-native-chains",heckCopies:args[1] is "--check-heck-copy" or "--check-heck-native-accessors");
        File.WriteAllText(args[2],JsonConvert.SerializeObject(report,Formatting.Indented));
        return JObject.FromObject(report)["failed"]!.Value<int>()==0?0:2;
    }
    if(args.Length==3&&args[1]=="--check-noodle-converters")
    {
        var report=NoodleConverterChecks.Run(gameRoot);
        File.WriteAllText(args[2],JsonConvert.SerializeObject(report,Formatting.Indented));
        return JObject.FromObject(report)["failed"]!.Value<int>()==0?0:2;
    }
    if((args.Length==3||args.Length==4)&&args[1]=="--check-noodle-fake-events")
    {
        var report=NoodleFakeEventChecks.Run(gameRoot,args.Length==4?args[3]:null);
        File.WriteAllText(args[2],JsonConvert.SerializeObject(report,Formatting.Indented));
        return JObject.FromObject(report)["failed"]!.Value<int>()==0?0:2;
    }
    if((args.Length==3||args.Length==4)&&args[1]=="--check-noodle-fake-interaction")
    {
        var report=NoodleFakeInteractionChecks.Run(gameRoot,args.Length==4?args[3]:null);
        File.WriteAllText(args[2],JsonConvert.SerializeObject(report,Formatting.Indented));
        return JObject.FromObject(report)["failed"]!.Value<int>()==0?0:2;
    }
    if((args.Length==3||args.Length==4)&&args[1]=="--check-noodle-fake-load")
    {
        var report=NoodleFakeLoadChecks.Run(gameRoot,args.Length==4?args[3]:null);
        File.WriteAllText(args[2],JsonConvert.SerializeObject(report,Formatting.Indented));
        return JObject.FromObject(report)["failed"]!.Value<int>()==0?0:2;
    }
    if(args.Length==3&&args[1]=="--check-noodle-fake-tail")
    {
        var report=NoodleFakeTailChecks.Run(gameRoot);
        File.WriteAllText(args[2],JsonConvert.SerializeObject(report,Formatting.Indented));
        return JObject.FromObject(report)["failed"]!.Value<int>()==0?0:2;
    }
    if(args.Length==3&&args[1]=="--check-noodle-v3-aliases")
    {
        var report=NoodleV3AliasChecks.Run(gameRoot);
        File.WriteAllText(args[2],JsonConvert.SerializeObject(report,Formatting.Indented));
        return JObject.FromObject(report)["failed"]!.Value<int>()==0?0:2;
    }
    if(args.Length==3&&args[1]=="--check-noodle-colliders")
    {
        var report=NoodleColliderChecks.Run(gameRoot);
        File.WriteAllText(args[2],JsonConvert.SerializeObject(report,Formatting.Indented));
        return JObject.FromObject(report)["failed"]!.Value<int>()==0?0:2;
    }
    if(args.Length==3&&args[1]=="--check-noodle-look")
    {
        var report=NoodleLookChecks.Run(gameRoot);
        File.WriteAllText(args[2],JsonConvert.SerializeObject(report,Formatting.Indented));
        return JObject.FromObject(report)["failed"]!.Value<int>()==0?0:2;
    }
    if(args.Length==3&&args[1]=="--check-noodle-copy-mirror")
    {
        var report=NoodleCopyMirrorChecks.Run(gameRoot);
        File.WriteAllText(args[2],JsonConvert.SerializeObject(report,Formatting.Indented));
        return JObject.FromObject(report)["failed"]!.Value<int>()==0?0:2;
    }
    if(args.Length==3&&args[1]=="--check-noodle-bomb-angle")
    {
        var report=NoodleBombAngleChecks.Run(gameRoot);
        File.WriteAllText(args[2],JsonConvert.SerializeObject(report,Formatting.Indented));
        return JObject.FromObject(report)["failed"]!.Value<int>()==0?0:2;
    }
    if(args.Length==3&&args[1]=="--check-noodle-note-size")
    {
        var report=NoodleNoteSizeChecks.Run(gameRoot);
        File.WriteAllText(args[2],JsonConvert.SerializeObject(report,Formatting.Indented));
        return JObject.FromObject(report)["failed"]!.Value<int>()==0?0:2;
    }
    if(args.Length==3&&args[1]=="--check-noodle-player-tracks")
    {
        var report=NoodlePlayerTrackChecks.Run(gameRoot);
        File.WriteAllText(args[2],JsonConvert.SerializeObject(report,Formatting.Indented));
        return JObject.FromObject(report)["failed"]!.Value<int>()==0?0:2;
    }
    if(args.Length==3&&args[1]=="--check-noodle-zero-world-rotation")
    {
        var report=NoodleZeroWorldRotationChecks.Run(gameRoot);
        File.WriteAllText(args[2],JsonConvert.SerializeObject(report,Formatting.Indented));
        return JObject.FromObject(report)["failed"]!.Value<int>()==0?0:2;
    }
    if((args.Length==3||args.Length==4)&&args[1]=="--check-noodle-wall-interaction")
    {
        var report=NoodleWallInteractionChecks.Run(gameRoot,args.Length==4?args[3]:null);
        File.WriteAllText(args[2],JsonConvert.SerializeObject(report,Formatting.Indented));
        return JObject.FromObject(report)["failed"]!.Value<int>()==0?0:2;
    }
    if(args.Length==3&&args[1]=="--check-noodle-cut-flags")
    {
        var report=NoodleCutFlagChecks.Run(gameRoot);
        File.WriteAllText(args[2],JsonConvert.SerializeObject(report,Formatting.Indented));
        return JObject.FromObject(report)["failed"]!.Value<int>()==0?0:2;
    }
    if(args.Length==3&&args[1]=="--check-noodle-wall-contact")
    {
        var report=NoodleWallContactChecks.Run(gameRoot);
        File.WriteAllText(args[2],JsonConvert.SerializeObject(report,Formatting.Indented));
        return JObject.FromObject(report)["failed"]!.Value<int>()==0?0:2;
    }
    if(args.Length==3&&args[1]=="--check-noodle-jump-policy")
    {
        var report=NoodleJumpPolicyChecks.Run(gameRoot);
        File.WriteAllText(args[2],JsonConvert.SerializeObject(report,Formatting.Indented));
        return JObject.FromObject(report)["failed"]!.Value<int>()==0?0:2;
    }
    if(args.Length==3&&args[1]=="--check-noodle-wall-clock")
    {
        var report=NoodleWallClockChecks.Run(gameRoot);
        File.WriteAllText(args[2],JsonConvert.SerializeObject(report,Formatting.Indented));
        return JObject.FromObject(report)["failed"]!.Value<int>()==0?0:2;
    }
    if(args.Length==3&&args[1]=="--check-noodle-event-parsing")
    {
        var report=NoodleEventParsingChecks.Run(gameRoot);
        File.WriteAllText(args[2],JsonConvert.SerializeObject(report,Formatting.Indented));
        return JObject.FromObject(report)["failed"]!.Value<int>()==0?0:2;
    }
    if(args.Length==3&&args[1]=="--check-noodle-path-animation")
    {
        var report=NoodlePathAnimationChecks.Run(gameRoot);
        File.WriteAllText(args[2],JsonConvert.SerializeObject(report,Formatting.Indented));
        return JObject.FromObject(report)["failed"]!.Value<int>()==0?0:2;
    }
    if(args.Length==3&&args[1]=="--check-noodle-track-dispatch")
    {
        var report=NoodleTrackDispatchChecks.Run(gameRoot);
        File.WriteAllText(args[2],JsonConvert.SerializeObject(report,Formatting.Indented));
        return JObject.FromObject(report)["failed"]!.Value<int>()==0?0:2;
    }
    if(args.Length==3&&args[1]=="--check-noodle-track-clock")
    {
        var report=NoodleTrackClockChecks.Run(gameRoot);
        File.WriteAllText(args[2],JsonConvert.SerializeObject(report,Formatting.Indented));
        return JObject.FromObject(report)["failed"]!.Value<int>()==0?0:2;
    }
    if(args.Length==3&&args[1]=="--check-noodle-bounce")
    {
        var report=NoodleBounceChecks.Run(gameRoot);
        File.WriteAllText(args[2],JsonConvert.SerializeObject(report,Formatting.Indented));
        return JObject.FromObject(report)["failed"]!.Value<int>()==0?0:2;
    }
    if(args.Length==3&&args[1]=="--check-noodle-local-rotation")
    {
        var report=NoodleLocalRotationChecks.Run(gameRoot);
        File.WriteAllText(args[2],JsonConvert.SerializeObject(report,Formatting.Indented));
        return JObject.FromObject(report)["failed"]!.Value<int>()==0?0:2;
    }
    if(args.Length==3&&args[1]=="--check-noodle-motion-flags")
    {
        var report=NoodleMotionFlagChecks.Run(gameRoot);
        File.WriteAllText(args[2],JsonConvert.SerializeObject(report,Formatting.Indented));
        return JObject.FromObject(report)["failed"]!.Value<int>()==0?0:2;
    }
    if(args.Length==3&&args[1]=="--check-noodle-position")
    {
        var report=NoodlePositionChecks.Run(gameRoot);
        File.WriteAllText(args[2],JsonConvert.SerializeObject(report,Formatting.Indented));
        return JObject.FromObject(report)["failed"]!.Value<int>()==0?0:2;
    }
    if(args.Length==4&&args[1]=="--check-noodle-note-scale")
    {
        var report=NoodleNoteScaleChecks.Run(gameRoot,args[2]);
        File.WriteAllText(args[3],JsonConvert.SerializeObject(report,Formatting.Indented));
        return JObject.FromObject(report)["failed"]!.Value<int>()==0?0:2;
    }
    if(args.Length==3&&args[1]=="--check-noodle-scale-composition")
    {
        var report=NoodleScaleCompositionChecks.Run(gameRoot);
        File.WriteAllText(args[2],JsonConvert.SerializeObject(report,Formatting.Indented));
        return JObject.FromObject(report)["failed"]!.Value<int>()==0?0:2;
    }
    if(args.Length==3&&args[1]=="--check-noodle-identity-scale")
    {
        var report=NoodleArcOpacityChecks.Run(gameRoot,new[]{"Note","Bomb","Obstacle","Slider"},new[]{"identity-scale","identity-scale-frames","unused-identity-scale","active-scale"});
        File.WriteAllText(args[2],JsonConvert.SerializeObject(report,Formatting.Indented));
        return JObject.FromObject(report)["failed"]!.Value<int>()==0?0:2;
    }
    if(args.Length==3&&args[1]=="--check-noodle-arc-interaction")
    {
        var report=NoodleArcInteractionChecks.Run(gameRoot);
        File.WriteAllText(args[2],JsonConvert.SerializeObject(report,Formatting.Indented));
        return JObject.FromObject(report)["failed"]!.Value<int>()==0?0:2;
    }
    if(args.Length==3&&args[1]=="--check-noodle-object-opacity")
    {
        var report=NoodleArcOpacityChecks.Run(gameRoot,new[]{"Note","Bomb","Obstacle"});
        File.WriteAllText(args[2],JsonConvert.SerializeObject(report,Formatting.Indented));
        return JObject.FromObject(report)["failed"]!.Value<int>()==0?0:2;
    }
    if(args.Length==3&&args[1]=="--check-noodle-arc-opacity")
    {
        var report=NoodleArcOpacityChecks.Run(gameRoot);
        File.WriteAllText(args[2],JsonConvert.SerializeObject(report,Formatting.Indented));
        return JObject.FromObject(report)["failed"]!.Value<int>()==0?0:2;
    }
    if(args.Length==3&&args[1]=="--check-noodle-dissolve")
    {
        var report=NoodleDissolveChecks.Run(gameRoot);
        File.WriteAllText(args[2],JsonConvert.SerializeObject(report,Formatting.Indented));
        return JObject.FromObject(report)["failed"]!.Value<int>()==0?0:2;
    }
    if(args.Length==3&&args[1]=="--check-noodle-cut-direction")
    {
        var report=NoodleCutDirectionChecks.Run(gameRoot,args[2]);
        File.WriteAllText(args[2],JsonConvert.SerializeObject(report,Formatting.Indented));
        return JObject.FromObject(report)["failed"]!.Value<int>()==0?0:2;
    }
    if(args.Length==3&&args[1]=="--check-noodle-metadata")
    {
        var report=NoodleMetadataChecks.Run(gameRoot);
        File.WriteAllText(args[2],JsonConvert.SerializeObject(report,Formatting.Indented));
        return JObject.FromObject(report)["failed"]!.Value<int>()==0?0:2;
    }
    if(args.Length==3&&args[1]=="--check-noodle-spawn")
    {
        var report=NoodleSpawnChecks.Run(gameRoot,args[2]);
        File.WriteAllText(args[2],JsonConvert.SerializeObject(report,Formatting.Indented));
        return JObject.FromObject(report)["failed"]!.Value<int>()==0?0:2;
    }
    if(args.Length==3&&args[1]=="--check-noodle-overlaps")
    {
        var report=NoodleOverlapChecks.Run(gameRoot,args[2]);
        File.WriteAllText(args[2],JsonConvert.SerializeObject(report,Formatting.Indented));
        return JObject.FromObject(report)["failed"]!.Value<int>()==0?0:2;
    }
    if(args.Length==3&&args[1]=="--check-noodle-bursts")
    {
        var report=NoodleBurstChecks.Run(gameRoot);
        File.WriteAllText(args[2],JsonConvert.SerializeObject(report,Formatting.Indented));
        return JObject.FromObject(report)["failed"]!.Value<int>()==0?0:2;
    }
    if(args.Length==3&&args[1]=="--check-noodle-arcs")
    {
        var report=NoodleArcChecks.Run(gameRoot);
        File.WriteAllText(args[2],JsonConvert.SerializeObject(report,Formatting.Indented));
        return JObject.FromObject(report)["failed"]!.Value<int>()==0?0:2;
    }
    if(args.Length==3&&args[1]=="--check-noodle-mirror")
    {
        var report=NoodleMirrorChecks.Run(gameRoot);
        File.WriteAllText(args[2],JsonConvert.SerializeObject(report,Formatting.Indented));
        return JObject.FromObject(report)["failed"]!.Value<int>()==0?0:2;
    }
    if(args.Length==3&&args[1]=="--check-noodle-warnings")
    {
        var report=NoodleWarningChecks.Run(gameRoot,args[2]);
        File.WriteAllText(args[2],JsonConvert.SerializeObject(report,Formatting.Indented));
        return JObject.FromObject(report)["failed"]!.Value<int>()==0?0:2;
    }
    if(args.Length==3&&args[1]=="--check-noodle-fake")
    {
        var report=NoodleFakeChecks.Run(oracle,gameRoot,args[2]);
        File.WriteAllText(args[2],JsonConvert.SerializeObject(report,Formatting.Indented));
        return JObject.FromObject(report)["failed"]!.Value<int>()==0?0:2;
    }
    if(args.Length==3&&args[1]=="--check-noodle-static")
    {
        var report=NoodleStaticChecks.Run(oracle,gameRoot,args[2]);
        File.WriteAllText(args[2],JsonConvert.SerializeObject(report,Formatting.Indented));
        return JObject.FromObject(report)["failed"]!.Value<int>()==0?0:2;
    }
    if((args.Length==3||args.Length==4)&&args[1]=="--check-mapping-warnings")
    {
        var report=MappingWarningChecks.Run(gameRoot,args[2],args.Length==4?args[3]:null);
        File.WriteAllText(args[2],JsonConvert.SerializeObject(report,Formatting.Indented));
        return JObject.FromObject(report)["failed"]!.Value<int>()==0?0:2;
    }
    if(args.Length==3&&args[1]=="--check-mapping-notes")
    {
        var report=MappingNoteChecks.Run(oracle,gameRoot,args[2]);
        File.WriteAllText(args[2],JsonConvert.SerializeObject(report,Formatting.Indented));
        return JObject.FromObject(report)["failed"]!.Value<int>()==0?0:2;
    }
    if((args.Length==3||args.Length==4)&&args[1]=="--check-mapping-walls")
    {
        var report=MappingWallChecks.Run(gameRoot,args.Length==4?args[3]:null);
        File.WriteAllText(args[2],JsonConvert.SerializeObject(report,Formatting.Indented));
        return JObject.FromObject(report)["failed"]!.Value<int>()==0?0:2;
    }
    if((args.Length==3||args.Length==4)&&args[1]=="--check-chroma")
    {
        var report=ChromaChecks.Run(oracle,gameRoot,args[2],args.Length==4?args[3]:null);
        File.WriteAllText(args[2],JsonConvert.SerializeObject(report,Formatting.Indented));
        return JObject.FromObject(report)["failed"]!.Value<int>()==0?0:2;
    }
    if(args.Length==3&&args[1]=="--check-audio-data")
    {
        var report=AudioDataChecks.Run(oracle,gameRoot,args[2]);
        File.WriteAllText(args[2],JsonConvert.SerializeObject(report,Formatting.Indented));
        return JObject.FromObject(report)["failed"]!.Value<int>()==0?0:2;
    }
    if((args.Length==3||args.Length==4)&&args[1]=="--check-rare-formats")
    {
        var report=RareFormatChecks.Run(oracle,gameRoot,args[2],args.Length==4?args[3]:null);
        File.WriteAllText(args[2],JsonConvert.SerializeObject(report,Formatting.Indented));
        return JObject.FromObject(report)["failed"]!.Value<int>()==0?0:2;
    }
    if(args.Length==5&&args[1]=="--check-menu-data")
    {
        var report=MenuDataChecks.Run(oracle,gameRoot,args[2],args[3]);
        File.WriteAllText(args[4],JsonConvert.SerializeObject(report,Formatting.Indented));
        return JObject.FromObject(report)["failed"]!.Value<int>()==0?0:2;
    }
    if(args.Length==5&&args[1]=="--check-modifiers")
    {
        var report=ModifierChecks.Run(oracle,gameRoot,args[2],args[3],args[4]);
        File.WriteAllText(args[4],JsonConvert.SerializeObject(report,Formatting.Indented));
        return JObject.FromObject(report)["failed"]!.Value<int>()==0?0:2;
    }
    if(args.Length>=4&&args[1]=="--diagnose")
    {
        var diagnosticRequest=JsonConvert.DeserializeObject<MapCase>(File.ReadAllText(args[2]))!;
        var map=LocalMap.Load(diagnosticRequest);
        var game=GameLoader.Load(gameRoot,map.Json,map.Bpm,map.AudioJson,mappingExtensions:map.MappingExtensions,noodleExtensions:map.NoodleExtensions);
        File.WriteAllText(args[3],JsonConvert.SerializeObject(new {input=map.Input,game=GameLoader.Describe(game)},Formatting.Indented));
        return 0;
    }
    if (args.Length > 1 && args[1] == "--self-test")
    {
        var left=new MapNote {Time=1,Color=0,X=0};var right=new MapNote {Time=1,Color=1,X=3};
        if(Comparison.InputDiff([left,right],[right,left])!=0)throw new Exception("Cross-hand tie equivalence failed");
        var otherLeft=new MapNote {Time=1,Color=0,X=2};
        if(Comparison.InputDiff([left,otherLeft],[otherLeft,left])==0)throw new Exception("Same-hand tie order must be preserved");
        if(!Comparison.SameSimultaneousNotes([left,otherLeft],[otherLeft,left]))throw new Exception("Same-time content equivalence failed");
        if(Comparison.SameSimultaneousNotes([left],[otherLeft]))throw new Exception("Different simultaneous objects must fail");
        var firstOrder=new MapInput {Duration=10,Notes=[new(){Time=1,Direction=0},new(){Time=1,Direction=1}]};
        var secondOrder=new MapInput {Duration=10,Notes=firstOrder.Notes.Reverse().ToArray()};
        if(Comparison.Diff(CompatibleAnalysis.Analyze(firstOrder),CompatibleAnalysis.Analyze(secondOrder)).Count==0)throw new Exception("Order-sensitive outputs must detect simultaneous permutation effects");
        var random = new Random(1948);
        foreach(var input in new[] {
            new MapInput {Duration=10},
            new MapInput {Duration=0},
            new MapInput {Duration=10,Notes=[new(){Time=1,Direction=8}]},
            new MapInput {Duration=10,Notes=[new(){Time=.99f},new(){Time=1.01f},new(){Time=1.03f,Color=1}]}
        })
        {
            var difference=Comparison.Diff(oracle.Analyze(input),CompatibleAnalysis.Analyze(input,history:OriginalOracle.HistoryFixture(input)));
            if(difference.Count>0)throw new Exception("Edge fixture: "+string.Join(",",difference.Keys));
        }
        foreach(int x in new[]{-2,0,1,2,3,5})foreach(int width in new[]{1,2,4})
        {
            var input=new MapInput {Duration=10,Notes=[new(){Time=1}],Walls=[new(){Time=2,Duration=5,X=x,Width=width,Height=5}]};
            var difference=Comparison.Diff(oracle.Analyze(input),CompatibleAnalysis.Analyze(input,history:OriginalOracle.HistoryFixture(input)));
            if(difference.Count>0)throw new Exception($"Wall end fixture x={x}, width={width}: "+string.Join(",",difference.Keys));
        }
        for (int i = 0; i < 1500; i++)
        {
            var input = new MapInput { Duration = 10, Notes = Enumerable.Range(0, 50).Select(_ => new MapNote { Time = random.Next(0, i%2==0?1000:100)/100f, X=random.Next(4), Y=random.Next(3), Color=random.Next(2), Direction=random.Next(9) }).OrderBy(n=>n.Time).ToArray() };
            var differences = Comparison.Diff(oracle.Analyze(input), CompatibleAnalysis.Analyze(input,history:OriginalOracle.HistoryFixture(input)));
            if (differences.Count>0) throw new Exception($"Synthetic case {i}: {string.Join(",", differences.Keys)}");
        }
        Console.WriteLine("1500 deterministic synthetic comparisons, 4 edge fixtures and 18 wall end fixtures passed");
        return 0;
    }
    if(args.Length>=3&&args[1]=="--batch")
    {
        int failed=0;
        var jobs=JsonConvert.DeserializeObject<Dictionary<string,string>[]>(File.ReadAllText(args[2]))!;
        foreach(var job in jobs){int code=RunCase(oracle,gameRoot,job["request"],job["result"]);if(code!=0&&code!=3)failed++;}
        return failed==0?0:2;
    }
    if (args.Length != 4 || args[1] is not ("--case" or "--case-with-menu")) throw new ArgumentException("Usage: <gameRoot> --case|--case-with-menu <request.json> <result.json> OR --batch <jobs.json> OR --self-test");
    return RunCase(oracle,gameRoot,args[2],args[3],args[1]=="--case-with-menu");
}
catch (Exception e) { Console.Error.WriteLine(e); return 5; }

static object[] KnownDefects(LoadedMap map,int excluded)
{
    var defects=new List<object>();
    if(excluded>0)defects.Add(new {id="REF-001",excludedCombinedArcNotes=excluded});
    int wrapped=map.Input.Notes.Count(n=>n.MappingExtensions&&MappingNotes.IsExtendedDirection(n.Direction)&&MappingNotes.Angle(n.Direction)<-180);
    if(wrapped>0)defects.Add(new {id="REF-002",encodedAnglesOutsideCanonicalRange=wrapped,issue="Equivalent Mapping Extensions directions can produce an extra swing; corrected angles use the standard direction range"});
    return defects.ToArray();
}

static int RunCase(OriginalOracle oracle,string gameRoot,string requestPath,string resultPath,bool includeMenu=false)
{
    var request = JsonConvert.DeserializeObject<MapCase>(File.ReadAllText(requestPath))!;
    int Save(object value,int code)
    {
        var saved=JObject.FromObject(value);
        if(includeMenu)saved["menu"]=JObject.FromObject(MenuReportSnapshot.Create(request));
        File.WriteAllText(resultPath,JsonConvert.SerializeObject(saved,Formatting.Indented));
        Console.WriteLine(JsonConvert.SerializeObject(new {result=Path.GetFileName(resultPath),exitCode=code}));
        return code;
    }
    object record; int exitCode;
    try
    {
        var map = LocalMap.Load(request);
        if(map.NoodleExtensions)
        {
            var analysis=CompatibleAnalysis.Analyze(map.Input,reproduceKnownDefects:false);
            record=new {
                schemaVersion=3,profileVersion=BeatInsight.Formats.LocalMap.AnalysisProfileVersion,status="analyzed",profile="noodle-static-neutral",request,
                mapVersion=map.Version,features=map.Features,mapSha256=Comparison.Hash(map.Json),
                duration=map.Input.Duration,corrected=Comparison.Metrics(analysis),
                median=new {left=WindowStatistics.ActiveWindowMedian(analysis.LeftBins),right=WindowStatistics.ActiveWindowMedian(analysis.RightBins),unit="swings/s",policy=WindowStatistics.ActiveHandMedianPolicy},
                squats=BeatInsight.WallAlert.WallAlertAdapter.Analyze(map.SquatJson??map.Json,map.Bpm,map.MappingExtensions,map.NoodleExtensions),
                validation=new {basis="controlled-format-fixtures",referenceComparison="not-performed",fullExtensionRuntime="not-tested",mapCorrectness="not-independently-verified"}
            };
            return Save(record,0);
        }
        var game = GameLoader.Load(gameRoot, map.Json, map.Bpm, map.AudioJson,mappingExtensions:map.MappingExtensions,noodleExtensions:map.NoodleExtensions);
        var original = oracle.Analyze(map.Input, game);
        var historyFixture=OriginalOracle.HistoryFixture(map.Input);
        var own = CompatibleAnalysis.Analyze(map.Input,history:historyFixture);
        var corrected = CompatibleAnalysis.Analyze(map.Input, reproduceKnownDefects:false,history:historyFixture);
        var differences = Comparison.Diff(original, own);
        var ownPrepared=map.Input.Notes.Where(n=>n.ScoringType<=3).ToArray();
        int inputDifferences = Comparison.InputDiff(oracle.LastPrepared.Notes, ownPrepared);
        bool equivalentSimultaneousOrder=inputDifferences>0&&differences.Count==0&&Comparison.SameSimultaneousNotes(oracle.LastPrepared.Notes,ownPrepared);
        if (inputDifferences>0&&!equivalentSimultaneousOrder) differences["preparedNotes"] = new { mismatches=inputDifferences };
        var correctedDifferences = Comparison.Diff(original, corrected);
        int excluded = map.Input.Notes.Count(n=>n.ScoringType==6);
        string? artifact = null;
        if (differences.Count>0)
        {
            artifact = Path.GetFileNameWithoutExtension(resultPath)+".details.json";
            File.WriteAllText(Path.Combine(Path.GetDirectoryName(Path.GetFullPath(resultPath))!,artifact), JsonConvert.SerializeObject(new { request, input=map.Input, oracleInput=oracle.LastPrepared, original, compatible=own, corrected, differences }, Formatting.Indented));
        }
        record = new {
            schemaVersion=3,profileVersion=BeatInsight.Formats.LocalMap.AnalysisProfileVersion, status=differences.Count==0?"pass":"mismatch", profile=map.NoodleExtensions?"noodle-static-neutral":map.MappingExtensions?"mapping-neutral":"vanilla-neutral", request,
            loaderAdapter=map.NoodleExtensions?"noodle-custom-json-fake-v1":map.MappingExtensions?"mapping-extensions-data-v1":"vanilla-pass-through",
            mapVersion=map.Version, features=map.Features,
            mapSha256=Comparison.Hash(map.Json), audioDataSha256=map.AudioJson==null?null:Comparison.Hash(map.AudioJson),
            duration=map.Input.Duration, originalHash=oracle.LevelHash, previewHash=oracle.PreviewHash,
            original=Comparison.Metrics(original), compatible=Comparison.Metrics(own), corrected=Comparison.Metrics(corrected),
            resultDigests=new { original=Comparison.Hash(JsonConvert.SerializeObject(original)), compatible=Comparison.Hash(JsonConvert.SerializeObject(own)) },
            differences, preparedOrder=new {equivalentSimultaneousOrder,orderedDifferences=inputDifferences}, knownDefects=KnownDefects(map,excluded),
            correctedDifferences,
            median=new {left=WindowStatistics.ActiveWindowMedian(corrected.LeftBins),right=WindowStatistics.ActiveWindowMedian(corrected.RightBins),unit="swings/s",policy=WindowStatistics.ActiveHandMedianPolicy},
            squats=BeatInsight.WallAlert.WallAlertAdapter.Analyze(map.SquatJson??map.Json,map.Bpm,map.MappingExtensions,map.NoodleExtensions),
            history=new { status="synthetic-compared",records=historyFixture.Length,selected=own.HistoryIndices.Length,source="deterministic-fixture-not-user-history",eventCapture="not-compared" },
            artifact
        };
        exitCode=differences.Count==0?0:2;
    }
    catch (NotSupportedException e) {record=new {schemaVersion=3,profileVersion=BeatInsight.Formats.LocalMap.AnalysisProfileVersion,status="unsupported",request,error=e.Message};exitCode=3;}
    catch (Exception e) {record=new {schemaVersion=3,profileVersion=BeatInsight.Formats.LocalMap.AnalysisProfileVersion,status="error",request,error=e.ToString()};exitCode=4;}
    return Save(record,exitCode);
}
