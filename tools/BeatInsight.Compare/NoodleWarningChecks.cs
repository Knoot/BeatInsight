using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.Loader;
using BeatInsight.Formats;
using BeatInsight.WallAlert;
using Newtonsoft.Json.Linq;
namespace BeatInsight.Compare;

static class NoodleWarningChecks
{
    sealed class EventFixture {public object[] customEventDatas {get;set;}=Array.Empty<object>();public System.Collections.IDictionary customData {get;set;}=new Dictionary<string,object>();}
    public static object Run(string gameRoot,string reportPath)
    {
        string folder=Path.Combine(Path.GetDirectoryName(Path.GetFullPath(reportPath))!,"noodle-warning-fixture");Directory.CreateDirectory(folder);
        File.WriteAllText(Path.Combine(folder,"Info.dat"),"""{"_version":"2.1.0","_beatsPerMinute":120,"audio":{"songDuration":30},"_difficultyBeatmapSets":[{"_beatmapCharacteristicName":"Standard","_difficultyBeatmaps":[{"_difficulty":"Expert","_beatmapFilename":"map.dat","_noteJumpMovementSpeed":12,"_customData":{"_requirements":["Noodle Extensions"]}}]}]}""");
        JObject Wall(float beat,float x,float width,float? length=null,bool fake=false)=>new() {["b"]=beat,["x"]=6,["y"]=2,["w"]=4,["h"]=3,["d"]=2,["customData"]=new JObject {["coordinates"]=new JArray(x,1.5f),["size"]=new JArray(width,1,length.HasValue?new JValue(length.Value):JValue.CreateNull()),["NE_fake"]=fake,["_isAnchor"]=true,["_paul"]="29",["track"]="fixture"}};
        var rolled=Wall(5,0,4);rolled["customData"]!["coordinates"]![1]=3;rolled["customData"]!["size"]![1]=4;rolled["customData"]!["localRotation"]=new JArray(0,0,90);
        var bareWide=new JObject {["b"]=20,["d"]=2,["x"]=-1,["y"]=2,["w"]=4,["h"]=3,["customData"]=new JObject {["NE_fake"]=false,["_isAnchor"]=true,["_paul"]="29",["track"]="fixture"}};
        rolled["customData"]!["animation"]=new JObject {["dissolve"]=new JArray(0)};
        bareWide["customData"]!["animation"]=new JObject {["dissolve"]=new JArray(0)};
        rolled["customData"]!["animation"]!["scale"]=new JArray(1,1,1);
        bareWide["customData"]!["animation"]!["scale"]=new JArray(1,1,1);
        rolled["customData"]!["customData"]=new JObject {["NE_fake"]=true,["coordinates"]=new JArray(50,30),["requirements"]=new JArray("UnknownExtension")};
        bareWide["customData"]!["customData"]=rolled["customData"]!["customData"]!.DeepClone();
        File.WriteAllText(Path.Combine(folder,"map.dat"),new JObject {["version"]="3.3.0",["colorNotes"]=new JArray(),["obstacles"]=new JArray(Wall(2,-1,1),Wall(2,0,1),rolled,Wall(8,-1,2,fake:true),Wall(14,-1,1,2),Wall(14.5f,0,1),bareWide)}.ToString());
        var map=LocalMap.Load(new MapCase {Folder=folder,Characteristic="Standard",Difficulty="Expert",File="map.dat"});
        var raw=GameLoader.Load(gameRoot,map.Json,map.Bpm,noodleExtensions:true);
        var customObstacles=new List<ObstacleData>();
        var customAssembly=AssemblyLoadContext.Default.Assemblies.Single(a=>a.GetName().Name=="CustomJSONData");
        var customType=customAssembly.GetType("CustomJSONData.CustomBeatmap.CustomData",true)!;
        var obstacleType=customAssembly.GetType("CustomJSONData.CustomBeatmap.CustomObstacleData",true)!;
        var ctor=obstacleType.GetConstructors().Single();
        foreach(var wall in JObject.Parse(map.Json)["obstacles"]!)
        {
            var metadata=(System.Collections.IDictionary)Activator.CreateInstance(customType)!;
            if(wall["customData"]!["coordinates"] is JArray coordinates)metadata["coordinates"]=coordinates.Select(t=>(object)(float)t).ToList();
            if(wall["customData"]!["size"] is JArray size)metadata["size"]=size.Select(t=>t.Type==JTokenType.Null?null:(object)(float)t).ToList();
            if(wall["customData"]!["localRotation"] is JArray rotation)metadata["localRotation"]=rotation.Select(t=>(object)(float)t).ToList();
            metadata["NE_fake"]=(bool)wall["customData"]!["NE_fake"]!;
            metadata["_isAnchor"]=(bool)wall["customData"]!["_isAnchor"]!;metadata["_paul"]=(string)wall["customData"]!["_paul"]!;
            metadata["track"]="fixture";
            if(wall["customData"]!["animation"]!=null)metadata["animation"]=new Dictionary<string,object>{{"dissolve",new List<object>{0f}},{"scale",new List<object>{1f,1f,1f}}};
            if(wall["customData"]!["customData"]!=null)metadata["customData"]=new Dictionary<string,object>{{"NE_fake",true},{"coordinates",new List<object>{50f,30f}},{"requirements",new List<object>{"UnknownExtension"}}};
            float beat=(float)wall["b"]!;
            var obstacle=ctor.Invoke(new object[]{beat*.5f,beat,beat+2,0,(int)wall["x"]!,Enum.ToObject(ctor.GetParameters()[5].ParameterType,2),1f,4,3,metadata,new Version(3,3,0)});
            customObstacles.Add((ObstacleData)obstacle);
        }

        var core=AssemblyLoadContext.Default.LoadFromAssemblyPath(Path.Combine(gameRoot,"Beat Saber_Data/Managed/GameplayCore.dll"));
        var models=AssemblyLoadContext.Default.Assemblies.Single(a=>a.GetName().Name=="DataModels");
        var modifiersType=core.GetType("GameplayModifiers",true)!;
        var speedType=modifiersType.GetNestedType("SongSpeed")!;var obstaclesType=modifiersType.GetNestedType("EnabledObstacleType")!;
        var transform=models.GetType("BeatmapDataTransformHelper",true)!.GetMethod("CreateTransformedBeatmapData")!;
        var level=RuntimeHelpers.GetUninitializedObject(models.GetType("BeatmapLevel",true)!);level.GetType().GetField("levelID")!.SetValue(level,"custom_level_static_warning");
        var results=new List<object>();int failed=0,cases=0;long maxSignalBytes=0;
        var eventType=customAssembly.GetType("CustomJSONData.CustomBeatmap.CustomEventData",true)!;
        var playerEvent=Activator.CreateInstance(eventType,new object[]{0f,"AssignPlayerToTrack",Activator.CreateInstance(customType)!,new Version(3,3,0)})!;
        var visualData=(System.Collections.IDictionary)Activator.CreateInstance(customType)!;
        visualData["track"]="fixture";visualData["dissolve"]=new List<object>{new List<object>{0f,0f},new List<object>{1f,1f}};
        var visualEvent=Activator.CreateInstance(eventType,new object[]{0f,"AnimateTrack",visualData,new Version(3,3,0)})!;
        if(!CustomObjectFlags.HasStaticTrackContext(new EventFixture {customEventDatas=new[]{visualEvent}}))throw new InvalidDataException("Visual-only runtime context was rejected");
        visualData["interactable"]=new List<object>{0f};
        if(CustomObjectFlags.HasStaticTrackContext(new EventFixture {customEventDatas=new[]{visualEvent}}))throw new InvalidDataException("Interactable event accepted as visual-only");
        visualData.Remove("interactable");
        int visualContextCases=0;
        foreach(int major in new[]{2,3})foreach(string context in new[]{"loaded","loaded-and-raw","wrong-root-alias","mixed-player"})
        {
            visualContextCases++;
            var metadata=(System.Collections.IDictionary)Activator.CreateInstance(customType)!;
            metadata[major==2?"_track":"track"]="fixture";metadata[major==2?"_dissolve":"dissolve"]=new List<object>{0f};
            var ev=Activator.CreateInstance(eventType,new object[]{0f,"AnimateTrack",metadata,new Version(major,0)})!;
            var root=new EventFixture {customEventDatas=context=="mixed-player"?new[]{ev,playerEvent}:new[]{ev}};
            if(context=="loaded-and-raw"||context=="wrong-root-alias")
            {
                string key=context=="wrong-root-alias"?(major==2?"customEvents":"_customEvents"):(major==2?"_customEvents":"customEvents");
                root.customData[key]=new List<object>{new Dictionary<string,object>{{major==2?"_time":"b",0f},{major==2?"_type":"t","AnimateTrack"},{major==2?"_data":"d",metadata}}};
            }
            bool expected=context=="loaded"||context=="loaded-and-raw";
            bool accepted=CustomObjectFlags.HasStaticTrackContext(root);
            if(accepted!=expected)failed++;
            results.Add(new {kind="runtime-visual-context",major,context,expected,accepted});
        }
        if(!CustomObjectFlags.HasPlayerMotion(new EventFixture {customEventDatas=new[]{playerEvent}}))throw new InvalidDataException("Deserialized player-motion event was missed");
        var unsupportedVersion=ctor.Invoke(new object[]{1f,2f,4f,0,0,Enum.ToObject(ctor.GetParameters()[5].ParameterType,2),1f,4,3,Activator.CreateInstance(customType)!,new Version(4,0)});
        bool rejectedVersion=false;try{CustomObjectFlags.StaticWall(unsupportedVersion,0,2,4,3);}catch(NotSupportedException){rejectedVersion=true;}
        if(!rejectedVersion)throw new InvalidDataException("Unvalidated runtime version was accepted");
        foreach(var obstacle in customObstacles)
        {
            var data=(System.Collections.IDictionary)obstacleType.GetProperty("customData")!.GetValue(obstacle)!;
            object? originalRotation=data.Contains("localRotation")?data["localRotation"]:null;
            data["localRotation"]=new List<object>{30f,0f,0f};bool rejected=false;
            try{CustomObjectFlags.StaticWall(obstacle,0,2,4,3,allowStaticTracks:true);}catch(NotSupportedException){rejected=true;}
            if(originalRotation==null)data.Remove("localRotation");else data["localRotation"]=originalRotation;
            if(!rejected)throw new InvalidDataException("Rotated runtime metadata was accepted");
        }
        foreach(bool mirror in new[]{false,true})foreach(string speed in new[]{"Normal","Faster","Slower","SuperFast"})foreach(string obstacleMode in Enum.GetNames(obstaclesType))
        {
            cases++;var errors=new List<string>();var modifiers=Activator.CreateInstance(modifiersType)!;
            modifiersType.GetField("_songSpeed",BindingFlags.Instance|BindingFlags.NonPublic)!.SetValue(modifiers,Enum.Parse(speedType,speed));
            modifiersType.GetField("_enabledObstacleType",BindingFlags.Instance|BindingFlags.NonPublic)!.SetValue(modifiers,Enum.Parse(obstaclesType,obstacleMode));
            var parameters=transform.GetParameters();var args=new object?[parameters.Length];args[0]=raw;args[1]=level;args[2]=modifiers;args[3]=mirror;
            for(int i=4;i<args.Length;i++){var type=parameters[i].ParameterType;if(type.IsByRef)type=type.GetElementType()!;args[i]=type.IsValueType?Activator.CreateInstance(type):null;}
            var transformed=OracleBridge.GetItems(transform.Invoke(null,args)!).OfType<ObstacleData>().ToArray();
            if(transformed.Length!=0&&transformed.Length!=customObstacles.Count)errors.Add("Uniform top-wall fixture was unexpectedly partially filtered");
            // Exercise native filtering separately; no timestamp matching or claim
            // that the raw loader performed the custom-data runtime copy patches.
            var native=transformed.Length==0?Array.Empty<ObstacleData>():customObstacles.Where(w=>!CustomObjectFlags.IsFake(w)).Select(w=>(ObstacleData)w.GetCopy()).ToArray();
            if(mirror)foreach(var obstacle in native)obstacle.Mirror(4);
            var snapshot=native.Select(w=>{
                var shape=CustomObjectFlags.StaticWall(w,w.lineIndex,(int)w.lineLayer,w.width,w.height,mirror,CustomObjectFlags.HasStaticTrackContext(new EventFixture {customEventDatas=new[]{visualEvent}}));
                float seconds=NoodleWallAnalysis.Duration(w.duration,shape.Length,12);
                return new NoodleWallSnapshot(w.time,w.beat,shape.Length.HasValue?w.beat+seconds*2:w.endBeat,shape);
            }).ToArray();
            var plan=NoodleWallAnalysis.Build(snapshot);
            int mode=Convert.ToInt32(Enum.Parse(obstaclesType,obstacleMode));
            if(mode==0&&(plan.Length!=3||!plan.Select(i=>native[i].beat).SequenceEqual(new[]{2f,5f,20f})))errors.Add("Independent expected groups at beats 2, 5 and 20 differ");
            if(mode==0&&plan.Length!=WallAlertAdapter.Count(map.SquatJson!,map.Bpm,false,true))errors.Add("Menu and loaded schedule disagree");
            if(mode==2&&plan.Length!=0)errors.Add("Removed obstacles produced warnings");
            foreach(float filter in new[]{0f,1.5f,3f,30f})if(NoodleWallAnalysis.Build(snapshot,filter).Any(i=>native[i].time<filter))errors.Add("Initial filter ignored");
            var triggers=new Dictionary<ObstacleData,int>();for(int i=0;i<plan.Length;i++)triggers.Add(native[plan[i]],i);
            var playback=new SquatWarningPlayback(plan.Length);
            int Play(){int count=0;foreach(var obstacle in native)if(triggers.TryGetValue(obstacle,out int index)&&playback.Observe(index))count++;return count;}
            for(int i=0;i<100;i++){playback.Reset();Play();}
            playback.Reset();long before=GC.GetAllocatedBytesForCurrentThread();int first=Play(),duplicate=Play();playback.Reset();int replay=Play();long bytes=GC.GetAllocatedBytesForCurrentThread()-before;
            maxSignalBytes=Math.Max(maxSignalBytes,bytes);
            if(bytes!=0||first!=plan.Length||duplicate!=0||replay!=plan.Length)errors.Add("Prepared playback allocated or failed duplicate/reset semantics");
            triggers.Clear();if(errors.Count>0)failed++;
            results.Add(new {mirror,speed,obstacleMode,signals=plan.Length,errors});
        }
        int neutralMetadataCases=0,rejectedMetadataCases=0,emptyTrackCases=0,staticTrackCases=0,visualWallCases=0,nestedMetadataCases=0,identityScaleCases=0;
        object RuntimeWall(int major,System.Collections.IDictionary data)=>ctor.Invoke(new object[]{1f,2f,4f,0,0,Enum.ToObject(ctor.GetParameters()[5].ParameterType,2),1f,4,3,data,new Version(major,major==2?6:3,0)});
        System.Collections.IDictionary RuntimeMetadata(int major)
        {
            var data=(System.Collections.IDictionary)Activator.CreateInstance(customType)!;
            data[major==2?"_position":"coordinates"]=new List<object>{-1f,1.5f};
            data[major==2?"_scale":"size"]=new List<object>{2f,1f};
            data[major==2?"_fake":"NE_fake"]=false;data["_isAnchor"]=true;data["_paul"]="29";return data;
        }
        foreach(int major in new[]{2,3})
        {
            foreach(string variant in new[]{"constant","frames","unused","active","unused-active","duplicate"})
            {
                identityScaleCases++;var metadata=RuntimeMetadata(major);bool old=major==2;
                bool unused=variant=="unused"||variant=="unused-active";
                float factor=variant=="active"||variant=="unused-active"?2f:1f;
                var animation=new Dictionary<string,object>();
                animation[unused?(old?"scale":"_scale"):(old?"_scale":"scale")]=variant=="frames"||variant=="duplicate"?
                    new List<object>{new List<object>{1f,1f,1f,0f},new List<object>{1f,1f,1f,variant=="duplicate"?0f:1f}}:new List<object>{factor,factor,factor};
                metadata[unused?(old?"animation":"_animation"):(old?"_animation":"animation")]=animation;
                bool accepted=true;NoodleWallShape shape=default;
                try{shape=CustomObjectFlags.StaticWall(RuntimeWall(major,metadata),0,2,4,3);}catch(NotSupportedException){accepted=false;}
                bool expected=variant=="unused"||variant=="constant"||variant=="frames";
                bool pass=accepted==expected&&(!accepted||shape.X==1&&shape.Y==1.5f&&shape.Width==2&&shape.Height==1);
                if(!pass)failed++;results.Add(new{kind="runtime-identity-scale",major,variant,accepted,expected,pass});
            }
            foreach(string variant in new[]{"object","empty","array","nan","alias"})
            {
                nestedMetadataCases++;var metadata=RuntimeMetadata(major);string key=major==2?"_customData":"customData";
                var nested=new Dictionary<string,object>{{major==2?"_fake":"NE_fake",true},{major==2?"_position":"coordinates",new List<object>{50f,30f}}};
                metadata[variant=="alias"?(major==2?"customData":"_customData"):key]=variant=="empty"?new Dictionary<string,object>():variant=="array"?new List<object>():nested;
                if(variant=="nan")nested["marker"]=double.NaN;
                var obstacle=RuntimeWall(major,metadata);bool accepted=true;NoodleWallShape shape=default;
                try{shape=CustomObjectFlags.StaticWall(obstacle,0,2,4,3);}catch(NotSupportedException){accepted=false;}
                bool expected=variant=="object"||variant=="empty";
                bool pass=accepted==expected&&(!accepted||shape.X==1&&shape.Y==1.5f&&shape.Width==2&&shape.Height==1&&!CustomObjectFlags.IsFake(obstacle));
                if(!pass)failed++;results.Add(new{kind="runtime-nested-metadata",major,variant,accepted,expected,pass});
            }
            foreach(string variant in new[]{"direct","animated","empty","physical","alias"})
            {
                visualWallCases++;var visualMetadata=RuntimeMetadata(major);
                if(variant=="direct")visualMetadata[major==2?"_dissolve":"dissolve"]=new List<object>{0f};
                else
                {
                    var animation=(System.Collections.IDictionary)Activator.CreateInstance(customType)!;
                    if(variant!="empty")animation[variant=="alias"?(major==2?"dissolve":"_dissolve"):(major==2?"_dissolve":"dissolve")]=new List<object>{0f};
                    if(variant=="physical")animation[major==2?"_position":"offsetPosition"]=new List<object>{1f,0f,0f};
                    visualMetadata[major==2?"_animation":"animation"]=animation;
                }
                bool accepted=true;NoodleWallShape shape=default;
                try{shape=CustomObjectFlags.StaticWall(RuntimeWall(major,visualMetadata),0,2,4,3);}catch(NotSupportedException){accepted=false;}
                bool expected=variant!="physical"&&variant!="alias";
                bool pass=accepted==expected&&(!accepted||shape.X==1&&shape.Y==1.5f&&shape.Width==2&&shape.Height==1);
                if(!pass)failed++;results.Add(new {kind="runtime-visual-wall",major,variant,accepted,expected,pass});
            }
            foreach(object assignment in new object[]{"fixture",new List<object>{"fixture","other"}})
            {
                staticTrackCases++;var data=RuntimeMetadata(major);data[major==2?"_track":"track"]=assignment;
                var obstacle=RuntimeWall(major,data);var shape=CustomObjectFlags.StaticWall(obstacle,0,2,4,3,allowStaticTracks:CustomObjectFlags.HasStaticTrackContext(new EventFixture()));
                bool pass=CustomObjectFlags.HasCustomWallGeometry(obstacle)&&shape.X==1&&shape.Y==1.5f&&shape.Width==2&&shape.Height==1;
                if(!pass)failed++;results.Add(new {kind="runtime-static-track",major,status=pass?"pass":"mismatch"});
            }
            foreach(object? assignment in new object?[]{null,new List<object>()})
            {
                emptyTrackCases++;var data=(System.Collections.IDictionary)Activator.CreateInstance(customType)!;
                data[major==2?"_track":"track"]=assignment;var obstacle=RuntimeWall(major,data);
                var shape=CustomObjectFlags.StaticWall(obstacle,0,2,4,3);
                bool pass=!CustomObjectFlags.HasCustomWallGeometry(obstacle)&&shape.X==0&&shape.Y==2&&shape.Width==4&&shape.Height==3;
                if(!pass)failed++;results.Add(new {kind="runtime-empty-track",major,status=pass?"pass":"mismatch"});
            }
            foreach(object? anchor in new object?[]{false,true,null})foreach(object? scalar in new object?[]{9,105.75,"29",true,new DateTime(2020,1,1,0,0,0,DateTimeKind.Utc),null})
            {
                neutralMetadataCases++;var data=RuntimeMetadata(major);data["_isAnchor"]=anchor;data["_paul"]=scalar;
                var obstacle=RuntimeWall(major,data);bool pass=false;string? error=null;
                try{var shape=CustomObjectFlags.StaticWall(obstacle,0,2,4,3);pass=shape.X==1&&shape.Y==1.5f&&shape.Width==2&&shape.Height==1&&!CustomObjectFlags.IsFake(obstacle);}catch(Exception e){error=e.Message;}
                if(!pass)failed++;results.Add(new {kind="runtime-neutral-metadata",major,status=pass?"pass":"mismatch",error});
            }
            foreach(string invalid in new[]{"anchor-string","anchor-number","paul-object","paul-array","paul-nan","paul-infinity","unknown","animation","track-string","track-empty-string","track-null-item","track-object","track-alias"})
            {
                rejectedMetadataCases++;var data=RuntimeMetadata(major);
                if(invalid=="anchor-string")data["_isAnchor"]="true";
                if(invalid=="anchor-number")data["_isAnchor"]=1;
                if(invalid=="paul-object")data["_paul"]=Activator.CreateInstance(customType)!;
                if(invalid=="paul-array")data["_paul"]=new List<object>{1,2};
                if(invalid=="paul-nan")data["_paul"]=double.NaN;
                if(invalid=="paul-infinity")data["_paul"]=double.PositiveInfinity;
                if(invalid=="unknown")data["futureRuntimeFlag"]=false;
                if(invalid=="animation")data[major==2?"_animation":"animation"]=new Dictionary<string,object>{{major==2?"_position":"offsetPosition",new List<object>{1f,0f,0f}}};
                if(invalid=="track-string")data[major==2?"_track":"track"]="fixture";
                if(invalid=="track-empty-string")data[major==2?"_track":"track"]="";
                if(invalid=="track-null-item")data[major==2?"_track":"track"]=new List<object?>{null};
                if(invalid=="track-object")data[major==2?"_track":"track"]=Activator.CreateInstance(customType)!;
                if(invalid=="track-alias")data[major==2?"track":"_track"]=new List<object>();
                bool rejected=false;try{CustomObjectFlags.StaticWall(RuntimeWall(major,data),0,2,4,3);}catch(NotSupportedException){rejected=true;}
                if(!rejected)failed++;results.Add(new {kind="runtime-invalid-metadata",major,invalid,status=rejected?"pass":"mismatch"});
            }
        }
        foreach(string context in new[]{"loaded-event","raw-event","environment","unknown"})
        {
            staticTrackCases++;var root=new EventFixture();
            if(context=="loaded-event")root.customEventDatas=new[]{playerEvent};
            if(context=="raw-event")root.customData["customEvents"]=new List<object>{new object()};
            if(context=="environment")root.customData["environment"]=new List<object>{new object()};
            bool allowed=CustomObjectFlags.HasStaticTrackContext(context=="unknown"?new object():root);
            if(allowed)failed++;results.Add(new {kind="runtime-static-context-rejection",context,status=!allowed?"pass":"mismatch"});
        }
        int entryWallCases=0,entryAccepted=0;
        foreach(bool mirror in new[]{false,true})foreach(float half in new[]{.1f,.5f,1.5f})foreach(float duration in new[]{.125f,2f})
        {
            var metadata=(System.Collections.IDictionary)Activator.CreateInstance(customType)!;
            metadata["animation"]=new Dictionary<string,object>{{"offsetPosition",new List<object>{new List<object>{-4f,0f,0f,.125f},new List<object>{0f,0f,0f,.375f,"easeOutSine"}}}};
            var obstacle=(ObstacleData)ctor.Invoke(new object[]{2f,4f,4f+duration*2,0,1,Enum.ToObject(ctor.GetParameters()[5].ParameterType,2),duration,2,3,metadata,new Version(3,3,0)});
            if(mirror)obstacle.Mirror(4);
            string before=JToken.FromObject(metadata).ToString();
            var movement=WallJumpMovement.FromMeasured(20,half,2*half);
            bool accepted=false,pass=true;int signals=0;
            try
            {
                var shape=CustomObjectFlags.StaticWall(obstacle,obstacle.lineIndex,(int)obstacle.lineLayer,obstacle.width,obstacle.height,mirror,false,movement);
                var plan=NoodleWallAnalysis.Build(new[]{new NoodleWallSnapshot(obstacle.time,obstacle.beat,obstacle.endBeat,shape)});
                signals=plan.Length;accepted=true;
                var playback=new SquatWarningPlayback(signals);
                for(int i=0;i<100;i++){playback.Reset();foreach(int index in Enumerable.Range(0,signals))playback.Observe(index);}
                playback.Reset();long baseline=GC.GetAllocatedBytesForCurrentThread();
                bool first=playback.Observe(0),duplicate=playback.Observe(0);playback.Reset();bool replay=playback.Observe(0);
                long bytes=GC.GetAllocatedBytesForCurrentThread()-baseline;maxSignalBytes=Math.Max(maxSignalBytes,bytes);
                pass&=signals==1&&first&&!duplicate&&replay&&bytes==0;
            }
            catch(NotSupportedException){}
            bool expected=half/(2*half+duration)>.375f+.00001f;
            pass&=accepted==expected&&before==JToken.FromObject(metadata).ToString();
            entryWallCases++;if(accepted)entryAccepted++;if(!pass)failed++;
            results.Add(new{kind="runtime-wall-entry",mirror,half,duration,accepted,expected,signals,pass});
        }
        foreach(string variant in new[]{"unknown-movement","z","rotation","scale","track","object-njs","object-offset","time"})
        {
            var metadata=(System.Collections.IDictionary)Activator.CreateInstance(customType)!;
            var animation=new Dictionary<string,object>{{"offsetPosition",new List<object>{new List<object>{-4f,0f,0f,0f},new List<object>{0f,0f,0f,.375f}}}};
            metadata["animation"]=animation;
            if(variant=="z")animation["offsetPosition"]=new List<object>{new List<object>{0f,0f,1f,0f},new List<object>{0f,0f,0f,.375f}};
            if(variant=="rotation")metadata["localRotation"]=new List<object>{0f,0f,30f};
            if(variant=="scale")animation["scale"]=new List<object>{1f,1f,1f};
            if(variant=="track")metadata["track"]="fixture";
            if(variant=="object-njs")metadata["noteJumpMovementSpeed"]=20f;
            if(variant=="object-offset")metadata["noteJumpStartBeatOffset"]=0f;
            if(variant=="time")animation["time"]=new List<object>{0f};
            var obstacle=(ObstacleData)ctor.Invoke(new object[]{2f,4f,4.25f,0,1,Enum.ToObject(ctor.GetParameters()[5].ParameterType,2),.125f,2,3,metadata,new Version(3,3,0)});
            bool rejected=false;try{CustomObjectFlags.StaticWall(obstacle,1,2,2,3,entryMovement:variant=="unknown-movement"?null:WallJumpMovement.FromMeasured(20,.5f,1));}catch(NotSupportedException){rejected=true;}
            entryWallCases++;if(!rejected)failed++;results.Add(new{kind="runtime-wall-entry-rejection",variant,rejected});
        }
        bool environmentGate=CustomObjectFlags.HasNoWallEntryEnvironment(new EventFixture())&&!CustomObjectFlags.HasNoWallEntryEnvironment(new object());
        var withEnvironment=new EventFixture();withEnvironment.customData["environment"]=new List<object>{new object()};
        environmentGate&=!CustomObjectFlags.HasNoWallEntryEnvironment(withEnvironment);
        if(!environmentGate)failed++;
        return new {schemaVersion=1,profileVersion=BeatInsight.Formats.LocalMap.AnalysisProfileVersion,cases,entryWallCases,entryAccepted,environmentGate,neutralMetadataCases,rejectedMetadataCases,emptyTrackCases,staticTrackCases,visualContextCases,visualWallCases,nestedMetadataCases,identityScaleCases,failed,maxSignalBytes,gameStarted=false,userHistoryRead=false,referenceUsed=false,
            scope="independent fixed-speed fixtures including local roll and both handedness modes, actual custom obstacle metadata and separately tested installed filtering of uniform top-wall fixtures; custom native objects constructed explicitly (runtime copy patches not executed); live extension activation, movement binding and callback lifecycle not executed",results};
    }
}
