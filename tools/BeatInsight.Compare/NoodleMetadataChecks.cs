using System.Reflection;
using System.Runtime.Loader;
using Mono.Cecil;
using Mono.Cecil.Cil;
using Newtonsoft.Json.Linq;

namespace BeatInsight.Compare;

static class NoodleMetadataChecks
{
    public static object Run(string gameRoot)
    {
        var audits=new List<object>();int failed=0,cases=0;
        foreach(var entry in new[]{("NoodleExtensions","FE50124E33631EED7ECB78EA1A19BB7EA64CC12974B0298DAD991672C2ABDA95"),("Heck","9E5163F09E122713543FB69F89799D1121FB712820E6B4350F7A30C5D790793C"),("CustomJSONData","4F9F8EFD8012159D8995B6D936E3C21B6AA6A35E319686FA3E9B04C2B6331F7C")})
        {
            string path=Path.Combine(gameRoot,"Plugins",entry.Item1+".dll");
            if(Comparison.HashFile(path)!=entry.Item2)throw new InvalidDataException("Reinspect changed metadata consumer: "+entry.Item1);
            using var module=ModuleDefinition.ReadModule(path);
            IEnumerable<TypeDefinition> All(TypeDefinition t)=>new[]{t}.Concat(t.NestedTypes.SelectMany(All));
            var types=module.Types.SelectMany(All).ToArray();
            foreach(string field in new[]{"_isAnchor","_paul"})
            {
                int uses=types.SelectMany(t=>t.Fields).Count(f=>f.HasConstant&&f.Constant is string s&&s.Contains(field,StringComparison.Ordinal))
                    +types.SelectMany(t=>t.Methods).Where(m=>m.HasBody).SelectMany(m=>m.Body.Instructions).Count(i=>i.OpCode==OpCodes.Ldstr&&i.Operand is string s&&s.Contains(field,StringComparison.Ordinal));
                if(uses!=0)failed++;audits.Add(new {assembly=entry.Item1,field,sha256=entry.Item2,literalUses=uses});
            }
        }
        var loaded=GameLoader.Load(gameRoot,"""{"version":"3.3.0","colorNotes":[],"obstacles":[],"sliders":[{"b":1,"x":0,"y":0,"c":0,"d":1,"tb":2,"tx":1,"ty":0,"tc":1}]}""",120,noodleExtensions:true);
        var slider=OracleBridge.GetItems(loaded).OfType<SliderData>().Single();
        const BindingFlags flags=BindingFlags.Public|BindingFlags.NonPublic|BindingFlags.Instance;
        var custom=AssemblyLoadContext.Default.Assemblies.Single(a=>a.GetName().Name=="CustomJSONData");
        var metadataType=custom.GetType("CustomJSONData.CustomBeatmap.CustomData",true)!;
        var factory=custom.GetType("CustomJSONData.CustomBeatmap.CustomNoteData",true)!.GetMethod("CreateCustomBasicNoteData")!;
        var noodle=AssemblyLoadContext.Default.Assemblies.Single(a=>a.GetName().Name=="NoodleExtensions");
        var shared=AssemblyLoadContext.Default.LoadFromAssemblyPath(Path.Combine(gameRoot,"Plugins/Heck.dll")).GetType("Heck.HeckController",true)!;
        foreach(var key in new[]{("SCALE","scale"),("V2_SCALE","_scale"),("TRACK","track"),("V2_TRACK","_track")})
            if((string?)shared.GetField(key.Item1,BindingFlags.Static|BindingFlags.Public|BindingFlags.NonPublic)!.GetRawConstantValue()!=key.Item2)throw new InvalidDataException("Reinspect changed scale metadata key");
        var results=new List<object>();
        var toggles=new Dictionary<string,string> { ["_disableNoteGravity"]="DisableGravity",["_disableNoteLook"]="DisableLook",["disableNoteGravity"]="DisableGravity",["disableNoteLook"]="DisableLook",["disableBadCutDirection"]="DisableBadCutDirection",["disableBadCutSpeed"]="DisableBadCutSpeed",["disableBadCutSaberType"]="DisableBadCutSaberType" };
        foreach(string field in new[]{"_isAnchor","_paul","tailCoordinates","_scale","scale","_track","track","_customData","customData"}.Concat(toggles.Keys))foreach(string kind in new[]{"Note","Obstacle","Slider"})foreach(int major in new[]{2,3})
        {
            bool toggle=toggles.ContainsKey(field);
            bool scale=field=="_scale"||field=="scale";
            bool track=field=="_track"||field=="track";
            bool nested=field=="_customData"||field=="customData";
            if(nested&&field!=(major==2?"_customData":"customData"))continue;
            if(track&&(field=="_track"?major!=2:major!=3))continue;
            if(scale&&(kind=="Obstacle"||(field=="_scale"?major!=2:major!=3)))continue;
            if(toggle&&(kind=="Obstacle"||(field.StartsWith("_",StringComparison.Ordinal)?major!=2:major!=3)))continue;
            var ctor=noodle.GetType("NoodleExtensions.Noodle"+kind+"Data",true)!.GetConstructors(flags).Single();var cp=ctor.GetParameters();
            JObject? baseline=null;
            foreach(string variant in nested?new[]{"absent","null","object-empty","nested-signals","nested-requirements"}:track?new[]{"absent","null","empty","assigned","assigned-array"}:scale?new[]{"absent","null","empty","identity","identity-partial","active-scale"}:field=="tailCoordinates"?new[]{"absent","null","empty","short","partial","full"}:field=="_paul"?new[]{"absent","zero","nine","negative","maximum","fractional","negative-fraction","large-finite","false","true","string-id","string-text","date","null"}:new[]{"absent","false","null","true"})
            {
                cases++;var data=Activator.CreateInstance(metadataType)!;
                if(variant!="absent")((System.Collections.IDictionary)data)[field]=variant switch {"null"=>null,"true"=>true,"false"=>false,"zero"=>0,"nine"=>9,"negative"=>-1,"maximum"=>int.MaxValue,"fractional"=>105.75,"negative-fraction"=>-.25,"large-finite"=>1e100,"string-id"=>"29","string-text"=>"marker","assigned"=>"fixture","assigned-array"=>new List<object>{"fixture","other"},"date"=>new DateTime(2020,1,1,0,0,0,DateTimeKind.Utc),"empty"=>new List<object?>(),"short"=>new List<object?>{-2f},"partial"=>new List<object?>{null,1.5f},"full"=>new List<object?>{-2f,1.5f},"identity"=>new List<object?>{1f,1f,1f},"identity-partial"=>new List<object?>{null,1f,null},"active-scale"=>new List<object?>{2f,.5f,3f},"object-empty"=>Activator.CreateInstance(metadataType)!,"nested-signals"=>new Dictionary<string,object?>{{major==2?"_fake":"NE_fake",true},{major==2?"_interactable":"uninteractable",major!=2},{major==2?"_position":"coordinates",new List<object>{50f,30f}},{major==2?"_localRotation":"localRotation",new List<object>{45f,0f,0f}},{major==2?"_track":"track","unbound"}},"nested-requirements"=>new Dictionary<string,object?>{{"requirements",new List<object>{"UnknownExtension"}},{"customData",new Dictionary<string,object>{{"NE_fake",true}}}},_=>throw new InvalidOperationException()};
                var p=factory.GetParameters();
                var note=(NoteData)factory.Invoke(null,new object[]{1f,2f,0,0,Enum.ToObject(p[4].ParameterType,0),Enum.ToObject(p[5].ParameterType,0),Enum.ToObject(p[6].ParameterType,1),data,new Version(major,0)})!;
                object item=note;
                if(kind=="Slider")item=slider;
                if(kind=="Obstacle")
                {
                    var oc=custom.GetType("CustomJSONData.CustomBeatmap.CustomObstacleData",true)!.GetConstructors().Single();
                    item=oc.Invoke(new object[]{1f,2f,4f,0,0,Enum.ToObject(oc.GetParameters()[5].ParameterType,2),1f,4,3,data,new Version(major,0)});
                }
                var tracks=Activator.CreateInstance(cp[4].ParameterType)!;
                if(track)foreach(string name in new[]{"fixture","other"})((System.Collections.IDictionary)tracks)[name]=Activator.CreateInstance(cp[4].ParameterType.GetGenericArguments()[1],true)!;
                var parsed=ctor.Invoke(new object[]{item,data,120f,Activator.CreateInstance(cp[3].ParameterType)!,tracks,major==2,false});
                var snapshot=new JObject();
                if(kind=="Note"){snapshot["scoringType"]=(int)note.scoringType;snapshot["direction"]=(int)note.cutDirection;snapshot["angle"]=note.cutDirectionAngleOffset;}
                var names=new[]{"Fake","Uninteractable","StartX","StartY","ScaleX","ScaleY","ScaleZ","Njs","SpawnOffset"}.AsEnumerable();
                if(kind!="Obstacle")names=names.Concat(new[]{"DisableGravity","DisableLook","DisableBadCutDirection","DisableBadCutSpeed","DisableBadCutSaberType","InternalStartNoteLineLayer"});
                if(kind=="Note")names=names.Append("Link");
                if(kind=="Slider")names=names.Concat(new[]{"TailStartX","TailStartY"});
                foreach(string name in names)
                {
                    PropertyInfo? property=null;for(Type? type=parsed.GetType();type!=null&&property==null;type=type.BaseType)property=type.GetProperty(name,flags|BindingFlags.DeclaredOnly);
                    if(property==null)throw new InvalidDataException("Reinspect missing metadata property: "+name);
                    var value=property.GetValue(parsed);snapshot[name]=value==null?JValue.CreateNull():JToken.FromObject(value);
                }
                var assignment=parsed.GetType().GetProperty("Track",flags)!.GetValue(parsed) as System.Collections.ICollection;
                snapshot["TrackCount"]=assignment?.Count??0;
                baseline??=snapshot;
                var expected=(JObject)baseline.DeepClone();
                if(toggle&&variant=="true")expected[toggles[field]]=true;
                if(track&&variant=="assigned")expected["TrackCount"]=1;
                if(track&&variant=="assigned-array")expected["TrackCount"]=2;
                if(field=="tailCoordinates"&&kind=="Slider")
                {
                    if(variant=="short"||variant=="full")expected["TailStartX"]=-2f;
                    if(variant=="partial"||variant=="full")expected["TailStartY"]=1.5f;
                }
                if(scale&&major==3)
                {
                    if(variant=="identity")foreach(string axis in new[]{"ScaleX","ScaleY","ScaleZ"})expected[axis]=1f;
                    if(variant=="identity-partial")expected["ScaleY"]=1f;
                    if(variant=="active-scale"){expected["ScaleX"]=2f;expected["ScaleY"]=.5f;expected["ScaleZ"]=3f;}
                }
                bool pass=JToken.DeepEquals(expected,snapshot);
                if(track)foreach(var instance in ((System.Collections.IDictionary)tracks).Values)
                    foreach(string name in new[]{"_properties","_pathProperties"})
                        pass=pass&&((System.Collections.IDictionary)instance!.GetType().GetField(name,flags)!.GetValue(instance)!).Count==0;
                if(!pass)failed++;
                results.Add(new {field,kind,major,variant,snapshot,status=pass?"pass":"mismatch"});
            }
        }
        int nestedLoaderCases=0;
        foreach(int major in new[]{2,3})
        {
            nestedLoaderCases++;
            var source=major==2?JObject.Parse("""{"_version":"2.6.0","_notes":[{"_time":1,"_lineIndex":0,"_lineLayer":0,"_type":0,"_cutDirection":1},{"_time":1,"_lineIndex":1,"_lineLayer":1,"_type":0,"_cutDirection":1},{"_time":2,"_lineIndex":2,"_lineLayer":0,"_type":3}],"_obstacles":[{"_time":3,"_duration":2,"_lineIndex":1,"_width":2,"_type":1}]}"""):
                JObject.Parse("""{"version":"3.3.0","colorNotes":[{"b":1,"x":0,"y":0,"c":0,"d":1},{"b":1,"x":1,"y":1,"c":0,"d":1}],"bombNotes":[{"b":2,"x":2,"y":0}],"obstacles":[{"b":3,"d":2,"x":1,"y":2,"w":2,"h":3}]}""");
            var baseline=GameLoader.Describe(GameLoader.Load(gameRoot,source.ToString(),120,noodleExtensions:true));
            string key=major==2?"_customData":"customData";
            foreach(var objects in source.Properties().Select(p=>p.Value).OfType<JArray>())foreach(var item in objects)
                item[key]=new JObject {[key]=new JObject {[major==2?"_fake":"NE_fake"]=true,[major==2?"_position":"coordinates"]=new JArray(50,30),["requirements"]=new JArray("UnknownExtension"),
                    ["fakeColorNotes"]=new JArray {new JObject {["b"]=4,["x"]=2,["y"]=0,["c"]=1,["d"]=1}},
                    [major==2?"_customEvents":"customEvents"]=new JArray {new JObject {[major==2?"_type":"t"]="AssignPlayerToTrack",[major==2?"_time":"b"]=0,[major==2?"_data":"d"]=new JObject {[major==2?"_track":"track"]="marker"}}}}};
            var parsed=GameLoader.Load(gameRoot,source.ToString(),120,noodleExtensions:true);
            bool pass=JToken.DeepEquals(JToken.FromObject(baseline),JToken.FromObject(GameLoader.Describe(parsed)))
                &&OracleBridge.GetItems(parsed).OfType<NoteData>().All(n=>!NativeFakeRegistry.IsFake(n));
            if(!pass)failed++;results.Add(new{kind="nested-native-deserialization",major,pass});
        }
        return new {schemaVersion=1,profileVersion=BeatInsight.Formats.LocalMap.AnalysisProfileVersion,cases,nestedLoaderCases,failed,referenceUsed=false,gameStarted=false,audits,
            scope="installed Noodle note/obstacle/slider constructors and literal/constant audit of pinned metadata consumers; nominal object state only, other mods and full game lifecycle not tested",results};
    }
}
