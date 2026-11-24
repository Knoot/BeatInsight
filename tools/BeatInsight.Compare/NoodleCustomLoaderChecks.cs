using System.Collections;
using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.Loader;
using Newtonsoft.Json.Linq;
using BeatInsight.Core;
using BeatInsight.Formats;

namespace BeatInsight.Compare;

static class NoodleCustomLoaderChecks
{
    public static void CustomDefaultEvents(BeatmapData map)
    {
        var custom=AssemblyLoadContext.Default.Assemblies.Single(a=>a.GetName().Name=="CustomJSONData");
        var prefix=custom.GetType("CustomJSONData.HarmonyPatches.DefaultEnvironmentEventsCustomify",true)!.GetMethod("Prefix",BindingFlags.Static|BindingFlags.Public|BindingFlags.NonPublic)!;
        if((bool)prefix.Invoke(null,new object[]{map})!)throw new InvalidDataException("Custom default environment prefix did not handle map");
    }
    public static void CheckContext(object? bpm,object? map,object? saved)
    {if(bpm==null||map==null||saved==null)throw new InvalidDataException($"Loader custom event context missing: bpm={bpm!=null}, map={map!=null}, saved={saved!=null}");}
    public static object Run(string gameRoot,string? inventory=null,bool noodleRows=false,string? admittedCoverage=null,JumpSettingsSnapshot? jumpSettings=null,bool admittedChains=false,bool copyRows=false,bool heckMap=false,bool heckCopies=false,bool independentArcDensity=false)
    {
        const BindingFlags flags=BindingFlags.Public|BindingFlags.NonPublic|BindingFlags.Static|BindingFlags.Instance;
        GameLoader.Load(gameRoot,"""{"version":"3.3.0","colorNotes":[]}""",120,noodleExtensions:true);
        using var registrar=heckMap?new HeckNativeRegistrarSession(gameRoot):null;
        using var callbackPatch=heckMap?new NoodleCallbackPatchSession(gameRoot):null;
        using var affinitySpawn=heckMap?new AffinitySpawnPatchSession(gameRoot):null;
        var custom=AssemblyLoadContext.Default.Assemblies.Single(a=>a.GetName().Name=="CustomJSONData");
        if(noodleRows)
        {
            if(Comparison.HashFile(Path.Combine(gameRoot,"Plugins/Heck.dll"))!="9E5163F09E122713543FB69F89799D1121FB712820E6B4350F7A30C5D790793C")throw new InvalidDataException("Reinspect changed Noodle data consumer");
            NoodleRowBridge.Initialize();
        }
        var noodle=AssemblyLoadContext.Default.Assemblies.Single(a=>a.GetName().Name=="NoodleExtensions");
        bool unityEulerUnavailable=false;
        if(admittedCoverage!=null)
        {
            try{_ = UnityEngine.Quaternion.Euler(0f,0f,0f);}
            catch(System.Security.SecurityException e)when(e.StackTrace?.Contains("Internal_FromEulerRad",StringComparison.Ordinal)==true){unityEulerUnavailable=true;}
        }
        var game=AssemblyLoadContext.Default.Assemblies.Single(a=>a.GetName().Name=="DataModels");
        var converterPatches=custom.GetType("CustomJSONData.HarmonyPatches.ConvertersCustomify",true)!;
        var loader=game.GetType("BeatmapDataLoaderVersion3.BeatmapDataLoader",true)!;
        var replacement=new Dictionary<MethodInfo,DynamicMethod>();
        foreach(var binding in new[]{("ColorNoteConverter","ColorNoteConvertV3"),("BombNoteConverter","BombNoteConvertV3"),
            ("ObstacleConverter","ObstacleConvertV3"),("SliderConverter","SliderConvertV3"),("BurstSliderConverter","BurstSliderConverterV3"),
            ("WaypointConverter","WaypointConvertV3"),("BpmEventConverter","BpmEventConvertV3")})
        {
            var original=loader.GetNestedType(binding.Item1,flags)!.GetMethod("Convert",flags)!;
            replacement[original]=InstalledTranspilerCompiler.Compile(original,converterPatches.GetMethod(binding.Item2,flags)!).Method;
        }
        if(heckMap)
        {
            var basic=loader.GetNestedType("BasicEventConverter",flags)!.GetMethod("Convert",flags)!;
            var converted=InstalledTranspilerCompiler.Compile(basic,converterPatches.GetMethod("BasicEventConvertV3",flags)!).Method;
            var helper=loader.GetMethod("ConvertBasicEvents",flags)!;int sites=0;
            replacement[helper]=InstalledTranspilerCompiler.Compile(helper,typeof(HeckMapChecks).GetMethod(nameof(HeckMapChecks.Identity))!,(code,operand)=>
            {
                if(Equals(operand,basic)){sites++;return(OpCodes.Call,converted);}
                return(code,operand);
            },exceptionBlocks:true).Method;
            if(sites!=1)throw new InvalidDataException("Basic event conversion site changed");
        }
        int forwarded=0;
        var load=loader.GetMethod("GetBeatmapDataFromSaveData",flags)!;
        var patch=custom.GetType("CustomJSONData.HarmonyPatches.BeatmapDataLoaderV3Customify",true)!.GetMethod("Customify",flags)!;
        var addEvents=patch.DeclaringType!.GetMethod("AddCustomEvents",flags)!;
        var factory=patch.DeclaringType!.GetMethod("CreateCustomBeatmapData",flags)!;
        var checkedFactory=new DynamicMethod("CustomMapWithRowProcessor",typeof(BeatmapData),factory.GetParameters().Select(p=>p.ParameterType).ToArray(),typeof(NoodleCustomLoaderChecks).Module,true);
        var factoryIL=checkedFactory.GetILGenerator();var mapLocal=factoryIL.DeclareLocal(typeof(BeatmapData));
        factoryIL.Emit(OpCodes.Ldarg_0);factoryIL.Emit(OpCodes.Ldarg_1);factoryIL.Emit(OpCodes.Call,factory);factoryIL.Emit(OpCodes.Stloc,mapLocal);
        factoryIL.Emit(OpCodes.Ldloc,mapLocal);factoryIL.Emit(OpCodes.Ldarg_0);factoryIL.Emit(OpCodes.Call,typeof(NoodleRowBridge).GetMethod(nameof(NoodleRowBridge.Install))!);
        factoryIL.Emit(OpCodes.Ldloc,mapLocal);factoryIL.Emit(OpCodes.Ret);
        var checkedEvents=new DynamicMethod("CheckedCustomEvents",typeof(void),addEvents.GetParameters().Select(p=>p.ParameterType).ToArray(),typeof(NoodleCustomLoaderChecks).Module,true);
        var eventIL=checkedEvents.GetILGenerator();
        for(int i=0;i<3;i++)eventIL.Emit(OpCodes.Ldarg,i);eventIL.Emit(OpCodes.Call,typeof(NoodleCustomLoaderChecks).GetMethod(nameof(CheckContext))!);
        for(int i=0;i<3;i++)eventIL.Emit(OpCodes.Ldarg,i);eventIL.Emit(OpCodes.Call,addEvents);eventIL.Emit(OpCodes.Ret);
        var compiled=InstalledTranspilerCompiler.Compile(load,patch,(code,operand)=>
        {
            if(operand is MethodInfo method&&replacement.TryGetValue(method,out var target)){forwarded++;return(OpCodes.Call,target);}
            if(heckMap&&operand is MethodInfo defaults&&defaults.Name=="InsertDefaultEvents"&&defaults.DeclaringType?.Name=="DefaultEnvironmentEventsFactory")return(OpCodes.Call,typeof(NoodleCustomLoaderChecks).GetMethod(nameof(CustomDefaultEvents))!);
            if(Equals(operand,addEvents))return(OpCodes.Call,checkedEvents);
            if(noodleRows&&Equals(operand,factory))return(OpCodes.Call,checkedFactory);
            return(code,operand);
        },exceptionBlocks:true);
        if(forwarded!=(heckMap?8:7))throw new InvalidDataException("Loader converter call sites changed");
        BeatmapData Load(JObject root,float bpm)
        {
            var copy=(JObject)root.DeepClone();
            foreach(string key in new[]{"lightColorEventBoxGroups","lightRotationEventBoxGroups","lightTranslationEventBoxGroups","vfxEventBoxGroups"})if(copy[key]!=null)copy[key]=new JArray();
            var saved=NoodleNativeData.Deserialize(gameRoot,copy,"3");
            var args=load.GetParameters().Select(p=>p.ParameterType.IsValueType?Activator.CreateInstance(p.ParameterType):null).ToArray();
            var parameters=load.GetParameters();
            for(int i=0;i<args.Length;i++)
            {
                if(parameters[i].ParameterType.Name=="BeatmapSaveData")args[i]=saved;
                if(parameters[i].ParameterType==typeof(float))args[i]=bpm;
                if(parameters[i].ParameterType.Name=="EnvironmentKeywords")args[i]=Activator.CreateInstance(parameters[i].ParameterType,new object?[]{null});
            }
            NoodleRowBridge.Enabled=noodleRows;
            try{return(BeatmapData)compiled.Method.Invoke(null,args)!;}
            finally{NoodleRowBridge.Enabled=false;}
        }
        if(independentArcDensity)return IndependentArcDensityChecks.Run(gameRoot,Load,inventory);
        var fixtures=new List<(string Name,JObject Root,float Bpm)>();
        MapNote[] PreparedNotes(BeatmapData map)=>OracleBridge.GetItems(map).OfType<NoteData>().Where(n=>
            ((IDictionary)n.GetType().GetProperty("customData")!.GetValue(n)!)["NE_fake"] is not true&&(int)n.scoringType>0&&(int)n.scoringType!=4&&(int)n.scoringType!=7)
            .Select(n=>new MapNote {Time=n.time,Beat=n.beat,X=n.lineIndex,Y=(int)n.noteLineLayer,Color=(int)n.colorType,
                Direction=(int)n.cutDirection,ScoringType=(int)n.scoringType}).ToArray();
        foreach(bool bpmChange in new[]{false,true})foreach(bool arc in new[]{false,true})foreach(bool first in new[]{false,true})foreach(float beat in new[]{2f,4f,12f})
        {
            JObject Note(float b,int x)=>new(){["b"]=b,["x"]=x,["y"]=0,["c"]=0,["d"]=0,["customData"]=new JObject {["probeIdentity"]="real-"+b+"-"+x}};
            var root=new JObject {["version"]="3.3.0",["colorNotes"]=new JArray(Note(2,0),Note(2,1),Note(6,2)),
                ["bombNotes"]=new JArray(new JObject {["b"]=4,["x"]=3,["y"]=1}),
                ["burstSliders"]=new JArray(new JObject {["b"]=4,["c"]=1,["x"]=3,["y"]=1,["d"]=1,["tb"]=5,["tx"]=2,["ty"]=0,["sc"]=3,["s"]=1}),
                ["waypoints"]=new JArray(new JObject {["b"]=1,["x"]=0,["y"]=0,["d"]=0}),
                ["obstacles"]=new JArray(new JObject {["b"]=3,["x"]=0,["y"]=2,["w"]=4,["h"]=3,["d"]=1}),
                ["bpmEvents"]=bpmChange?new JArray(new JObject {["b"]=2,["m"]=180}):new JArray()};
            if(arc)root["sliders"]=new JArray(new JObject {["b"]=2,["c"]=0,["x"]=0,["y"]=0,["d"]=0,["tb"]=6,["tx"]=2,["ty"]=0,["tc"]=0});
            var fake=Note(beat,0);fake["customData"]!["probeIdentity"]="fake";fake["customData"]!["uninteractable"]=true;
            if(heckMap)fake["customData"]!["track"]="decorative";
            var metadata=new JObject {["fakeColorNotes"]=new JArray(fake),["customEvents"]=new JArray(new JObject {["b"]=1,["t"]="AnimateTrack",["d"]=new JObject {["track"]="decorative",["duration"]=1,["dissolve"]=new JArray(0)}})};
            if(first)root.AddFirst(new JProperty("customData",metadata));else root["customData"]=metadata;
            fixtures.Add(($"bpm={bpmChange};arc={arc};first={first};fake={beat}",root,120));
        }
        fixtures.Add(("bomb-arc-tail-control",JObject.Parse("""{"version":"3.3.0","colorNotes":[{"b":2,"x":0,"y":0,"c":0,"d":1,"customData":{"tailCoordinates":[-0.25,2]}}],"bombNotes":[{"b":4,"x":1,"y":0},{"b":3,"x":3,"y":2}],"sliders":[{"b":2,"x":0,"y":0,"c":0,"d":1,"tb":4,"tx":1,"ty":0,"tc":1}]}"""),120));
        fixtures.Add(("chain-source-identity-control",JObject.Parse("""{"version":"3.3.0","colorNotes":[{"b":2,"x":0,"y":0,"c":0,"d":1}],"sliders":[{"b":2.0005,"x":0,"y":0,"c":0,"d":1,"tb":4,"tx":1,"ty":1,"tc":1}],"burstSliders":[{"b":2,"x":0,"y":0,"c":0,"d":1,"tb":3,"tx":1,"ty":0,"sc":4,"s":1}]}"""),120));
        fixtures.Add(("chain-orphan-control",JObject.Parse("""{"version":"3.3.0","colorNotes":[],"burstSliders":[{"b":2,"x":0,"y":0,"c":0,"d":1,"tb":4,"tx":1,"ty":1,"sc":4,"s":1}]}"""),120));
        if(heckMap)
        {
            fixtures.Add(("heck-manager-control",JObject.Parse("""{"version":"3.3.0","colorNotes":[{"b":2,"x":0,"y":0,"c":0,"d":1,"customData":{"track":"shared","animation":{"offsetPosition":"motion"}}},{"b":4,"x":1,"y":1,"c":1,"d":0,"customData":{"track":["shared","other"]}}],"customData":{"pointDefinitions":{"motion":[[0,0,0,0],[1,2,3,1]],"opacity":[[0,0],[1,1]]},"customEvents":[{"b":0,"t":"AnimateTrack","d":{"track":"shared","duration":1,"dissolve":"opacity"}},{"b":0,"t":"AssignPathAnimation","d":{"track":"other","duration":1,"offsetPosition":"motion"}},{"b":0,"t":"AssignTrackParent","d":{"parentTrack":"parent","childrenTracks":["shared","other"]}},{"b":0,"t":"AssignPlayerToTrack","d":{"track":"player"}},{"b":1,"t":"AnimateTrack","d":{"track":"player","duration":1,"dissolve":"opacity"}}]}}"""),120));
            fixtures.Add(("scale-track-six-notes-control",JObject.Parse("""{"version":"3.3.0","colorNotes":[{"b":475,"x":1,"y":0,"a":0,"c":0,"d":1,"customData":{"coordinates":[-0.5,0],"track":"size"}},{"b":476.5,"x":1,"y":0,"a":0,"c":1,"d":1,"customData":{"coordinates":[-0.25,-0.5],"track":"size"}},{"b":477,"x":1,"y":0,"a":0,"c":0,"d":0,"customData":{"coordinates":[-1,-0.75],"track":"size"}},{"b":478,"x":2,"y":0,"a":0,"c":1,"d":0,"customData":{"track":"size"}},{"b":479,"x":0,"y":1,"a":0,"c":0,"d":6,"customData":{"track":"size"}},{"b":479,"x":1,"y":0,"a":0,"c":1,"d":6,"customData":{"track":"size"}}],"customData":{"customEvents":[{"b":474,"t":"AnimateTrack","d":{"track":"size","scale":[[1,1,1,0],[0.5,0.5,0.5,1]],"duration":1}},{"b":477,"t":"AnimateTrack","d":{"track":"size","scale":[[0.5,0.5,0.5,0],[1,1,1,1]],"duration":0.5}}]}}"""),174));
            foreach(string variant in new[]{"none","speed","offset","both"})
            foreach(bool coordinates in new[]{false,true})foreach(bool gravity in new[]{false,true})
            {
                var root=JObject.Parse("""{"version":"3.3.0","colorNotes":[{"b":2,"x":0,"y":0,"c":0,"d":1}],"burstSliders":[{"b":2,"x":0,"y":0,"c":0,"d":1,"tb":4,"tx":1,"ty":1,"sc":4,"s":1,"customData":{"track":"chain"}}]}""");
                if(variant is "speed" or "both")root["burstSliders"]![0]!["customData"]!["noteJumpMovementSpeed"]=12;
                if(variant is "offset" or "both")root["burstSliders"]![0]!["customData"]!["noteJumpStartBeatOffset"]=.75;
                if(coordinates)
                {
                    root["colorNotes"]![0]!["customData"]=new JObject {["coordinates"]=new JArray(-2.25,.75)};
                    root["burstSliders"]![0]!["customData"]!["coordinates"]=new JArray(-2.25,.75);
                    root["burstSliders"]![0]!["customData"]!["tailCoordinates"]=new JArray(2.5,3.25);
                }
                root["burstSliders"]![0]!["customData"]!["disableNoteGravity"]=gravity;
                fixtures.Add(($"chain-movement-{variant}-coordinates={coordinates}-gravity={gravity}",root,120));
            }
            foreach(string origin in new[]{"environment","inline-material","named-material"})
            {
                var root=JObject.Parse("""{"version":"3.3.0","colorNotes":[],"customData":{"customEvents":[{"b":0,"t":"AnimateTrack","d":{"track":"visual","duration":1,"dissolve":[0]}}]}}""");
                var material=new JObject {["track"]="visual",["shader"]="Standard"};
                root["customData"]!["environment"]=new JArray(new JObject {["id"]="fixture",["lookupMethod"]="Exact",["track"]=new JArray("duplicate","duplicate")});
                if(origin=="environment")root["customData"]!["environment"]![0]!["track"]=new JArray("visual","duplicate","visual");
                else if(origin=="inline-material")root["customData"]!["environment"]![0]!["geometry"]=new JObject {["type"]="Cube",["material"]=material};
                else root["customData"]!["materials"]=new JObject {["named"]=material};
                fixtures.Add(("chroma-early-"+origin,root,120));
            }
            fixtures.Add(("heck-unknown-track-control",JObject.Parse("""{"version":"3.3.0","colorNotes":[],"customData":{"customEvents":[{"b":0,"t":"AnimateTrack","d":{"track":"missing","duration":1,"dissolve":[0]}}]}}"""),120));
        }
        var ownRequests=new Dictionary<string,MapCase>();var excluded=new List<object>();
        string[] ids=admittedCoverage==null?new[]{"089","090","091","092"}:JArray.Parse(File.ReadAllText(admittedCoverage))
            .Where(c=>(string?)c["status"] is "ready" or "partial").Select(c=>Path.GetFileNameWithoutExtension(Path.GetFileNameWithoutExtension((string)c["source"]!))).ToArray();
        if(inventory!=null)foreach(string id in ids)
        {
            var request=JObject.Parse(File.ReadAllText(Path.Combine(inventory,id+".result.json")))["request"]!;
            string folder=(string)request["Folder"]!;
            string path=Path.Combine(folder,(string)request["File"]!);
            if(admittedCoverage!=null&&!File.Exists(path)){excluded.Add(new{id,reason="source-file-missing"});continue;}
            var chart=JObject.Parse(File.ReadAllText(path));
            if(admittedCoverage!=null)
            {
                if(!((string?)chart["version"]??"").StartsWith("3.",StringComparison.Ordinal)||chart["sliders"]?.Any()!=true)continue;
                if((chart["burstSliders"]?.Any()==true)!=admittedChains){if(!admittedChains)excluded.Add(new{id,reason="arc-and-chain-composition-pending"});continue;}
                ownRequests[id]=request.ToObject<MapCase>()!;
            }
            var info=JObject.Parse(File.ReadAllText(Path.Combine(folder,"Info.dat")));
            fixtures.Add((id,admittedCoverage==null?chart:new JObject {["probeSourceFile"]=path},
                (float)(info["_beatsPerMinute"]??info["audio"]?["bpm"]??throw new InvalidDataException("Initial BPM missing"))));
        }
        string Identity(BeatmapObjectData item)
        {
            object source=NativeFakeRegistry.TryGetSource(item,out var original)?original!:item;
            var metadata=(IDictionary)source.GetType().GetProperty("customData")!.GetValue(source)!;
            return metadata["probeObjectIdentity"] as string??throw new InvalidDataException("Conversion source identity missing");
        }
        string Key(BeatmapObjectData item)=>Identity(item)+":"+(item switch {
            NoteData note=>$"note:{note.time:R}:{note.beat:R}:{note.lineIndex}:{note.noteLineLayer}:{note.colorType}:{note.cutDirection}",
            SliderData slider=>$"slider:{slider.time:R}:{slider.beat:R}:{slider.headLineIndex}:{slider.headLineLayer}:{slider.sliderType}",
            ObstacleData wall=>$"wall:{wall.time:R}:{wall.beat:R}:{wall.lineIndex}:{wall.lineLayer}:{wall.width}:{wall.height}",
            _=>$"other:{item.time:R}:{item.beat:R}"});
        int failed=0;var results=new List<object>();
        bool previousCapture=NativeFakeRegistry.CaptureSources;NativeFakeRegistry.CaptureSources=true;
        try
        {
        foreach(var pending in fixtures)
        {
            var fixture=(pending.Name,Root:pending.Root["probeSourceFile"] is JToken sourceFile?
                JObject.Parse(File.ReadAllText(sourceFile.Value<string>()??throw new InvalidDataException("Probe source path missing"))):pending.Root,pending.Bpm);
            try
            {
            if(admittedCoverage!=null)
                foreach(string lighting in new[]{"basicBeatmapEvents","colorBoostBeatmapEvents","lightColorEventBoxGroups","lightRotationEventBoxGroups","lightTranslationEventBoxGroups","vfxEventBoxGroups"})
                    if(fixture.Root[lighting]!=null)fixture.Root[lighting]=new JArray();
            int identity=0;
            foreach(string collection in new[]{"colorNotes","bombNotes","obstacles","sliders","burstSliders","waypoints","fakeColorNotes","fakeBombNotes","fakeObstacles","fakeBurstSliders"})
            foreach(var item in fixture.Root[collection]??fixture.Root["customData"]?[collection]??new JArray())
            {
                if(item["customData"] is not JObject)item["customData"]=new JObject();
                item["customData"]!["probeObjectIdentity"]="source-"+(identity++).ToString("D6");
            }
            var raw=(BeatmapData)GameLoader.Load(gameRoot,fixture.Root.ToString(),fixture.Bpm,noodleExtensions:true);
            var actual=Load(fixture.Root,fixture.Bpm);var expected=OracleBridge.GetItems(raw).OfType<BeatmapObjectData>().ToArray();
            var items=OracleBridge.GetItems(actual).OfType<BeatmapObjectData>().ToArray();
            bool orderChanged=!expected.Select(Key).SequenceEqual(items.Select(Key));
            expected=expected.OrderBy(Key,StringComparer.Ordinal).ToArray();items=items.OrderBy(Key,StringComparer.Ordinal).ToArray();
            var errors=new List<string>();var nativeDifferences=new List<object>();
            if(actual.GetType().FullName!="CustomJSONData.CustomBeatmap.CustomBeatmapData"||items.Length!=expected.Length)errors.Add("Custom map or object count changed");
            for(int i=0;i<Math.Min(items.Length,expected.Length);i++)
            {
                if(!items[i].GetType().Name.StartsWith("Custom",StringComparison.Ordinal)){errors.Add("Object was not custom-converted");continue;}
                if(Key(items[i])!=Key(expected[i])){errors.Add("Object identity fields differ");continue;}
                foreach(var property in expected[i].GetType().GetProperties(BindingFlags.Instance|BindingFlags.Public).Where(p=>p.GetIndexParameters().Length==0))
                    if(!Equals(property.GetValue(expected[i]),property.GetValue(items[i])))
                    {
                        if(noodleRows&&(items[i] is NoteData&&property.Name is "scoringType" or "isArcHead" or "isArcTail"
                            ||items[i] is SliderData&&property.Name is "headBeforeJumpLineLayer" or "tailBeforeJumpLineLayer" or "hasHeadNote" or "hasTailNote"))
                            nativeDifferences.Add(new{source=Identity(items[i]),field=property.Name,before=property.GetValue(expected[i]),after=property.GetValue(items[i])});
                        else errors.Add(i+":"+property.Name);
                    }
            }
            int eventCount=((IEnumerable)actual.GetType().GetProperty("customEventDatas")!.GetValue(actual)!).Cast<object>().Count();
            if(eventCount!=(fixture.Root["customData"]?["customEvents"]?.Count()??0))errors.Add("Custom events were not inserted");
            object? copyEvidence=null;
            if(copyRows)
            {
                copyEvidence=NoodleRowCopyChecks.Run(gameRoot,actual,fixture.Bpm);
                if(JObject.FromObject(copyEvidence)["failed"]!.Value<int>()!=0)errors.Add("Copy/mirror row composition differs");
            }
            object? transformEvidence=null;
            if(heckCopies){transformEvidence=HeckMapTransformChecks.Run(gameRoot,actual,fixture.Bpm);if((int)JObject.FromObject(transformEvidence)["failed"]! !=0)errors.Add("Transformed Heck manager composition differs");}
            object? scaleNoteEvidence=null;
            object? heckEvidence=null;
            if(heckMap)
            {
                Dictionary<string,object>? captured=fixture.Name=="scale-track-six-notes-control"?new():null;
                heckEvidence=HeckMapChecks.Run(gameRoot,actual,fixture.Bpm,capture:captured==null?null:(id,data)=>captured.Add(id,data));
                if(captured!=null)
                {
                    scaleNoteEvidence=NoodleScaleNoteChecks.Run(gameRoot,captured["Heck"],captured["NoodleExtensions"]);
                    if((int)JObject.FromObject(scaleNoteEvidence)["failed"]! !=0)errors.Add("Scale track note binding/consumer differs");
                }
                var heckResult=JObject.FromObject(heckEvidence);
                if(fixture.Name=="heck-unknown-track-control")
                {
                    if(heckResult["failed"]!.Value<int>()!=2||heckResult["errors"]!.Count()!=2||heckResult["errors"]!.All(e=>!e.ToString().Contains("Could not find track [missing]",StringComparison.Ordinal)))
                        errors.Add("Unknown event-only track was not rejected by native deserialization");
                }
                else if(heckResult["failed"]!.Value<int>()!=0
                    &&!(fixture.Name is "089" or "090" or "091" or "092" &&heckResult["structuralFailed"]!.Value<int>()==0
                        &&heckResult["onlyQuaternionFailures"]!.Value<bool>()&&heckResult["unityQuaternionFailures"]!.Value<int>()==72)
                        &&!(ownRequests.ContainsKey(fixture.Name)&&heckResult["structuralFailed"]!.Value<int>()==0&&heckResult["onlyQuaternionFailures"]!.Value<bool>()))
                    errors.Add("Full Heck map deserialization differs");
                if(ownRequests.ContainsKey(fixture.Name)&&heckResult["onlyQuaternionFailures"]!.Value<bool>())
                {
                    heckResult["completion"]="partial-native-unity-quaternion-boundary";heckEvidence=heckResult;
                }
                if(fixture.Name is "089" or "090" or "091" or "092")
                {
                    var environmentTracks=new HashSet<string>(StringComparer.Ordinal);
                    void Tracks(JToken? value)
                    {
                        if(value is JValue single&&single.Type==JTokenType.String)environmentTracks.Add(single.Value<string>()!);
                        else if(value is JArray array)foreach(var name in array)Tracks(name);
                    }
                    foreach(var environment in fixture.Root["customData"]?["environment"]??new JArray())
                    {Tracks(environment["track"]);Tracks((environment["geometry"]?["material"] as JObject)?["track"]);}
                    foreach(var material in (fixture.Root["customData"]?["materials"] as JObject)?.Properties()??Enumerable.Empty<JProperty>())Tracks(material.Value["track"]);
                    var observed=heckResult["trackNames"]!.Values<string>().Select(n=>n!).ToHashSet(StringComparer.Ordinal);
                    if(environmentTracks.Count!=330||!environmentTracks.IsSubsetOf(observed))errors.Add("Library environment/material tracks missing or fixture changed");
                    heckResult["environmentTrackCount"]=environmentTracks.Count;
                    heckResult["environmentTracksPresent"]=environmentTracks.IsSubsetOf(observed);
                    heckResult["completion"]=heckResult["failed"]!.Value<int>()==0?"complete":"partial-native-unity-quaternion-boundary";
                    heckEvidence=heckResult;
                }
                if(fixture.Name.StartsWith("chroma-early-",StringComparison.Ordinal))
                {
                    if(!heckResult["trackNames"]!.Values<string>().SequenceEqual(new[]{"duplicate","visual"}))errors.Add("Chroma early tracks missing or duplicated");
                    var binding=heckResult["bindings"]!.Single(b=>(string?)b["id"]=="Heck");
                    if((int)binding["events"]! !=1||(int)binding["sharedEventTrackReferences"]! !=1)errors.Add("Heck event did not resolve Chroma early track");
                }
                if(fixture.Name=="heck-manager-control")
                {
                    if(!heckResult["trackNames"]!.Values<string>().SequenceEqual(new[]{"other","parent","player","shared"}))errors.Add("Early deserialization did not register expected tracks");
                    foreach(var binding in heckResult["bindings"]!)
                    {
                        string id=binding["id"]!.Value<string>()!;
                        if(id=="Chroma")continue;
                        if(binding["objects"]!.Value<int>()!=2||binding["events"]!.Value<int>()!=(id=="Heck"?3:2)||binding["sharedTrackReferences"]!.Value<int>()!=3)
                            errors.Add("Manager module binding counts differ");
                    }
                }
            }
            if(fixture.Name=="bomb-arc-tail-control")
            {
                var stream=PreparedNotes(actual);
                if(stream.Length!=2||stream.Count(n=>n.Color==-1&&n.ScoringType==3)!=1)errors.Add("Prepared projection lost promoted bomb or retained ordinary bomb");
            }
            if(noodleRows&&fixture.Name is "chain-source-identity-control" or "chain-orphan-control")
            {
                var projection=NoodleBurstProjection.Prepare(gameRoot,actual,fixture.Bpm);
                var group=projection.Notes.Single();
                if(fixture.Name=="chain-source-identity-control")
                {
                    var source=items.OfType<NoteData>().Single();
                    if((int)source.gameplayType!=2||(int)source.scoringType!=2||group.SliceCount!=4||group.ScoringType!=1)
                        errors.Add("Shared arc/chain head was not replaced exactly once");
                }
                else if(group.SliceCount!=3||group.Direction!=8||Math.Abs(group.Time-4f/3)>1e-5)
                    errors.Add("Orphan chain did not start at the first emitted native slice");
            }
            object? ownComparison=null;
            if(ownRequests.TryGetValue(fixture.Name,out var ownRequest))
            {
                const AnalysisFeatures features=AnalysisFeatures.Average|AnalysisFeatures.Peak|AnalysisFeatures.Median|AnalysisFeatures.FirstNotes|AnalysisFeatures.Density;
                var own=LocalMap.Load(ownRequest,features,includeSwingWindowWalls:false,jumpSettings:jumpSettings);
                var nativeNotes=PreparedNotes(actual);object? burstEvidence=null;
                if(admittedChains){var projection=NoodleBurstProjection.Prepare(gameRoot,actual,fixture.Bpm);nativeNotes=projection.Notes;burstEvidence=projection.Evidence;}
                var nativeInput=new MapInput {Notes=nativeNotes,Duration=own.Input.Duration};
                var nativeAnalysis=CompatibleAnalysis.Analyze(nativeInput,reproduceKnownDefects:false,features:features);
                var ownAnalysis=CompatibleAnalysis.Analyze(own.Input,reproduceKnownDefects:false,features:features);
                var metricDifferences=Comparison.Diff(nativeAnalysis,ownAnalysis);
                var ownNotes=own.Input.Notes.Where(n=>!n.IsFake).ToArray();
                MapNote[] Sort(IEnumerable<MapNote> notes)=>notes.OrderBy(n=>n.Time).ThenBy(n=>n.Color).ThenBy(n=>n.X).ThenBy(n=>n.Y).ThenBy(n=>n.Direction).ToArray();
                var a=Sort(nativeNotes);var b=Sort(ownNotes);
                int inputMismatches=Math.Abs(a.Length-b.Length)+a.Zip(b,(x,y)=>Math.Abs(x.Time-y.Time)>1e-5||x.Color!=y.Color||x.X!=y.X||x.Y!=y.Y||x.Direction!=y.Direction).Count(v=>v);
                int roleMismatches=a.Zip(b,(x,y)=>x.ScoringType!=y.ScoringType||x.SliceCount!=y.SliceCount).Count(v=>v);
                string ShapeKey(MapNote n)=>$"{n.Beat:R}:{n.Color}:{n.X}:{n.Y}:{n.Direction}";
                var remaining=nativeNotes.GroupBy(ShapeKey).ToDictionary(g=>g.Key,g=>g.Count());
                var extraOwnNotes=new List<MapNote>();
                foreach(var note in ownNotes)
                {string key=ShapeKey(note);if(remaining.GetValueOrDefault(key)>0)remaining[key]--;else extraOwnNotes.Add(note);}
                bool pass=metricDifferences.Count==0&&roleMismatches==0&&inputMismatches==0;
                ownComparison=new{pass,nativeNotes=nativeNotes.Length,ownNotes=ownNotes.Length,inputMismatches,roleMismatches,metricDifferences,
                    burstEvidence,
                    extraOwnNotes=extraOwnNotes.Take(12).ToArray(),
                    nativeBombs=inputMismatches==0?null:items.OfType<NoteData>().Where(n=>(int)n.colorType<0).Select(n=>new{n.beat,n.lineIndex,n.scoringType}).ToArray(),
                    nativeMetrics=Comparison.Metrics(nativeAnalysis),ownMetrics=Comparison.Metrics(ownAnalysis)};
                if(!pass)errors.Add("Admitted arc chart differs from composed native pipeline");
            }
            int parsedObjects=0,fakeNoScore=0,cacheReads=0,constructorUnavailable=0;var constructorFailures=new List<object>();
            if(noodleRows)
            {
                var noteCtor=noodle.GetType("NoodleExtensions.NoodleNoteData",true)!.GetConstructors(flags).Single();var cp=noteCtor.GetParameters();
                var tracks=(IDictionary)Activator.CreateInstance(cp[4].ParameterType)!;
                var heck=AssemblyLoadContext.Default.Assemblies.Single(a=>a.GetName().Name=="Heck");
                var trackType=heck.GetType("Heck.Animation.Track",true)!;
                var graph=NoodleTrackInventory.Inspect(fixture.Root);
                foreach(string name in graph.ReferencedTracks.Concat(graph.GameplayTracks).Distinct())tracks[name]=Activator.CreateInstance(trackType,true)!;
                var definitions=(IDictionary)Activator.CreateInstance(cp[3].ParameterType)!;
                var rootData=(IDictionary)actual.GetType().GetProperty("customData")!.GetValue(actual)!;
                if(rootData["pointDefinitions"] is IDictionary points)foreach(DictionaryEntry point in points)definitions[point.Key]=point.Value;
                foreach(var item in items)
                {
                    string? parsedName=item is NoteData?"NoodleNoteData":item is SliderData?"NoodleSliderData":item is ObstacleData&&admittedCoverage==null?"NoodleObstacleData":null;
                    if(parsedName==null)continue;
                    var metadata=(IDictionary)item.GetType().GetProperty("customData")!.GetValue(item)!;
                    var ctor=noodle.GetType("NoodleExtensions."+parsedName,true)!.GetConstructors(flags).Single();
                    object parsed;
                    try{parsed=ctor.Invoke(new[]{(object)item,metadata,fixture.Bpm,definitions,tracks,false,false});}
                    catch(TargetInvocationException e)when(admittedCoverage!=null)
                    {
                        bool zeroRotation=metadata["worldRotation"] is object rotation&&JToken.FromObject(rotation) is JArray vector&&vector.Count==3
                            &&vector.All(v=>v.Type is JTokenType.Integer or JTokenType.Float&&(double)v==0);
                        bool boundary=unityEulerUnavailable&&zeroRotation&&e.InnerException is NullReferenceException
                            &&e.InnerException.StackTrace?.Contains("DeserializerExtensions.DeserializeFailure",StringComparison.Ordinal)==true;
                        if(boundary)constructorUnavailable++;else errors.Add("Native note/slider constructor failed");
                        if(constructorFailures.Count<12)constructorFailures.Add(new{source=Identity(item),type=parsedName,
                            boundary=boundary?"native-unity-euler-unavailable":null,error=e.InnerException?.GetType().Name,message=e.InnerException?.Message});continue;
                    }
                    parsedObjects++;
                    if(item is NoteData note&&metadata["NE_fake"] is true)
                    {if((int)note.scoringType!=-1)errors.Add("Parsed fake did not become NoScore");else fakeNoScore++;}
                    if(metadata.Contains("NE_startNoteLineLayer"))
                    {
                        var value=parsed.GetType().GetProperty("InternalStartNoteLineLayer",flags)!.GetValue(parsed);
                        if(value==null||Convert.ToSingle(value)!=Convert.ToSingle(metadata["NE_startNoteLineLayer"]))errors.Add("Native constructor did not consume cached row layer");else cacheReads++;
                    }
                }
            }
            if(noodleRows&&items.OfType<NoteData>().Any()&&cacheReads==0)errors.Add("No composed Noodle row cache was consumed");
            if(errors.Count>0)failed++;
            results.Add(new{fixture.Name,objects=items.Length,eventCount,orderChanged,parsedObjects,fakeNoScore,cacheReads,constructorUnavailable,constructorFailures,ownComparison,copyEvidence,heckEvidence,transformEvidence,scaleNoteEvidence,nativeDifferences,errors=errors.Take(12).ToArray()});
            }
            catch(Exception e)when(admittedCoverage!=null)
            {failed++;results.Add(new{fixture.Name,errors=new[]{"Admitted arc probe failed"},exception=e.GetBaseException().GetType().Name,message=e.GetBaseException().ToString()});}
            if(admittedCoverage!=null)
            {
                // Long native-oracle runs must not retain a library of parsed charts.
                // This stand-only collection never runs in the game package.
                GC.Collect(GC.MaxGeneration,GCCollectionMode.Forced,blocking:true,compacting:false);
                Console.Error.WriteLine("Arc probe completed: "+fixture.Name);
            }
        }
        }
        finally{NativeFakeRegistry.CaptureSources=previousCapture;}
        bool rowDispatchValid=!noodleRows||(copyRows||heckCopies?NoodleRowBridge.InstalledProcessors>=fixtures.Count:NoodleRowBridge.InstalledProcessors==fixtures.Count)&&NoodleRowBridge.AllCalls>0&&NoodleRowBridge.ColorCalls>0;
        if(!rowDispatchValid)failed++;
        int transformedCases=results.Sum(r=>JObject.FromObject(r)["transformEvidence"] is JObject evidence?(int)evidence["cases"]!:0);
        if(heckCopies&&transformedCases!=fixtures.Count*4)failed++;
        return new{cases=fixtures.Count,failed,nativeFieldAccessors=NativeFieldAccessorChecks.Enabled,nativeAccessorOwnership=NativeFieldAccessorChecks.Enabled?NativeFieldAccessorChecks.LoadedOwnership():null,customFieldAddressAdapters=MappingNativeRuntime.CustomFieldAddressAdapters,registrarLifetime=registrar?.Lifetime,callbackPatchLifetime=callbackPatch?.Lifetime,affinitySpawnLifetime=affinitySpawn?.Lifetime,transformedCases,partialNativeCases=results.Count(r=>JObject.FromObject(r)["heckEvidence"] is JObject evidence&&(string?)evidence["completion"]=="partial-native-unity-quaternion-boundary"),noodleRows,NoodleRowBridge.AllCalls,NoodleRowBridge.ColorCalls,NoodleRowBridge.InstalledProcessors,
            rowDispatchValid,admittedArcCases=ownRequests.Count,admittedChains,unityEulerUnavailable,excluded,forwardedConverterSites=forwarded,exceptionFinallyRegions=load.GetMethodBody()!.ExceptionHandlingClauses.Count,results,
            scope=heckMap?
                "Actual custom loader, eight converters and controlled Noodle rows followed by installed Heck manager and Heck/Noodle/Chroma module deserialization. Shared tracks, early registration, point definition parsing and Resolve identity checked. Environment/material early registration included; no environment construction, event execution, all-mod Harmony ordering, Unity lifecycle or support admission.":noodleRows?
                "Actual custom loader and seven converters; controlled installation of real Noodle row processor, actual all-row transpiler/body/postfix and color-row prefix/body in installed order, followed by genuine Noodle note/slider/obstacle constructors. Seven type-specific arc/layer role fields may differ from raw and are reported rather than independently validated. No Harmony all-mod ordering, full Heck deserializer, copy/mirror composition, event/environment execution, Unity lifecycle or support admission.":
                "Actual installed custom loader/map factory, seven converter transpilers, native base row/temporal processing, custom event insertion and sort; two constructor field-address adapters and custom sorted-list forwarding. Lighting excluded, no Noodle row transpiler/prefixes, Noodle deserialized data, event execution, Unity lifecycle or support admission."};
    }
}
