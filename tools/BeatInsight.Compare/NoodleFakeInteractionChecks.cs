using System.Collections;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.Loader;
using Newtonsoft.Json.Linq;

namespace BeatInsight.Compare;

// Controlled property state, not animation event dispatch or Unity collider execution.
static class NoodleFakeInteractionChecks
{
    public static object Run(string gameRoot,string? inventory=null)
    {
        foreach(var pin in new[]{("NoodleExtensions","FE50124E33631EED7ECB78EA1A19BB7EA64CC12974B0298DAD991672C2ABDA95"),
            ("Heck","9E5163F09E122713543FB69F89799D1121FB712820E6B4350F7A30C5D790793C")})
            if(Comparison.HashFile(Path.Combine(gameRoot,"Plugins",pin.Item1+".dll"))!=pin.Item2)throw new InvalidDataException("Reinspect changed dynamic interaction consumer");
        const BindingFlags flags=BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic;
        GameLoader.Load(gameRoot,"""{"version":"3.3.0","colorNotes":[]}""",120,noodleExtensions:true);
        var custom=AssemblyLoadContext.Default.Assemblies.Single(a=>a.GetName().Name=="CustomJSONData");
        var metadataType=custom.GetType("CustomJSONData.CustomBeatmap.CustomData",true)!;
        var factory=custom.GetType("CustomJSONData.CustomBeatmap.CustomNoteData",true)!.GetMethod("CreateCustomBasicNoteData")!;var fp=factory.GetParameters();
        var noodle=AssemblyLoadContext.Default.Assemblies.Single(a=>a.GetName().Name=="NoodleExtensions");
        var noteCtor=noodle.GetType("NoodleExtensions.NoodleNoteData",true)!.GetConstructors(flags).Single();var cp=noteCtor.GetParameters();
        var objectType=noodle.GetType("NoodleExtensions.NoodleObjectData",true)!;
        var helperType=noodle.GetType("NoodleExtensions.Animation.AnimationHelper",true)!;
        var offset=helperType.GetMethod("GetObjectOffset",flags)!;
        var heck=AssemblyLoadContext.Default.Assemblies.Single(a=>a.GetName().Name=="Heck");
        var trackType=heck.GetType("Heck.Animation.Track",true)!;
        var propertyType=heck.GetType("Heck.Animation.Property`1",true)!.MakeGenericType(typeof(float));
        var deserializedCtor=heck.GetType("Heck.Deserialize.DeserializedData",true)!.GetConstructors(flags).Single();
        var managerType=noodle.GetType("NoodleExtensions.Managers.FakePatchesManager",true)!;
        var cuttable=managerType.GetMethod("GetCuttable",flags)!;
        int cases=0,failed=0,staticFalseDynamicTrue=0;var failures=new List<object>();
        float?[] values={null,0,.5f,1,2};
        foreach(bool mirror in new[]{false,true})foreach(bool uninteractable in new[]{false,true})
        foreach(int count in new[]{0,1,2})foreach(float? local in values)foreach(float? first in values)foreach(float? second in values)
        {
            if(count==0&&(first!=null||second!=null)||count==1&&second!=null)continue;
            var data=(IDictionary)Activator.CreateInstance(metadataType)!;
            data["NE_fake"]=true;data["uninteractable"]=uninteractable;
            if(local.HasValue)
            {
                var animation=(IDictionary)Activator.CreateInstance(metadataType)!;
                animation["interactable"]=new List<object>{new List<object>{local.Value,0f}};data["animation"]=animation;
            }
            var tracks=(IDictionary)Activator.CreateInstance(cp[4].ParameterType)!;
            if(count>0)
            {
                data["track"]=Enumerable.Range(0,count).Select(i=>(object)("probe-"+i)).ToList();
                for(int i=0;i<count;i++)
                {
                    var track=Activator.CreateInstance(trackType,true)!;
                    var property=Activator.CreateInstance(propertyType,true)!;
                    propertyType.GetProperty("Value",flags)!.SetValue(property,i==0?first:second);
                    ((IDictionary)trackType.GetField("_properties",flags)!.GetValue(track)!)["interactable"]=property;
                    tracks["probe-"+i]=track;
                }
            }
            var note=(NoteData)factory.Invoke(null,new object[]{1f,2f,0,0,Enum.ToObject(fp[4].ParameterType,0),Enum.ToObject(fp[5].ParameterType,0),Enum.ToObject(fp[6].ParameterType,1),data,new Version(3,3,0)})!;
            var parsed=noteCtor.Invoke(new object[]{note,data,120f,Activator.CreateInstance(cp[3].ParameterType)!,tracks,false,mirror});
            var dictionaries=deserializedCtor.GetParameters().Select(p=>Activator.CreateInstance(p.ParameterType)!).ToArray();
            ((IDictionary)dictionaries[2])[note]=parsed;
            var manager=RuntimeHelpers.GetUninitializedObject(managerType);
            managerType.GetField("_deserializedData",flags)!.SetValue(manager,deserializedCtor.Invoke(dictionaries));
            bool initial=(bool)cuttable.Invoke(manager,new object[]{note})!;
            var helper=Activator.CreateInstance(helperType,flags,null,new object[]{mirror},null)!;
            object?[] args=new object?[offset.GetParameters().Length];
            args[0]=objectType.GetProperty("AnimationObject",flags)!.GetValue(parsed);
            args[1]=objectType.GetProperty("Track",flags)!.GetValue(parsed);args[2]=.5f;
            offset.Invoke(helper,args);float? actual=(float?)args[9];
            var factors=new[]{local,count>0?first:null,count>1?second:null}.Where(v=>v.HasValue).Select(v=>v!.Value).ToArray();
            float? expected=factors.Length==0?null:factors.Aggregate(1f,(a,b)=>a*b);
            bool pass=initial==!uninteractable&&actual==expected&&(int)note.scoringType==-1;
            if(!initial&&actual>=1)staticFalseDynamicTrue++;
            cases++;if(!pass){failed++;failures.Add(new{mirror,uninteractable,count,local,first,second,initial,actual,expected});}
        }
        var contexts=new List<object>();
        if(inventory!=null)foreach(string id in new[]{"089","090","091","092"})
        {
            var request=JObject.Parse(File.ReadAllText(Path.Combine(inventory,id+".result.json")))["request"]!;
            var root=JObject.Parse(File.ReadAllText(Path.Combine((string)request["Folder"]!,(string)request["File"]!)));
            var graph=NoodleTrackInventory.Inspect(root);
            string[] Tracks(IEnumerable<JToken> objects)=>objects.SelectMany(o=>new[]{"customData","_customData"}.SelectMany(k=>new[]{"track","_track"}.SelectMany(t=>
                o[k]?[t] is JArray array?array.Values<string>().OfType<string>():o[k]?[t]?.Type==JTokenType.String?new[]{(string)o[k]![t]!}:Array.Empty<string>())))
                .Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray();
            var real=Tracks(new[]{"colorNotes","bombNotes","obstacles","sliders","burstSliders"}.SelectMany(k=>root[k]??new JArray()));
            var fake=Tracks(new[]{"fakeColorNotes","fakeBombNotes","fakeObstacles","fakeBurstSliders"}.SelectMany(k=>root["customData"]?[k]??new JArray()));
            var environment=(root["customData"]?["environment"]??new JArray()).SelectMany(o=>
                o["track"] is JArray array?array.Values<string>().OfType<string>():o["track"]?.Type==JTokenType.String?new[]{(string)o["track"]!}:Array.Empty<string>())
                .Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray();
            var interactionKeys=root.Descendants().OfType<JProperty>().Where(p=>p.Name is "interactable" or "_interactable" or "cuttable")
                .Select(p=>p.Path).ToArray();
            contexts.Add(new{id,realTracks=real,fakeTracks=fake,environmentTrackCount=environment.Length,
                directSharedEnvironmentTracks=fake.Intersect(environment,StringComparer.Ordinal).ToArray(),interactionKeys,
                graph.PlayerMotion,graph.ParentEdges,graph.UnknownEvents,graph.Issues,
                eventTypes=graph.Events.GroupBy(e=>e.Type).ToDictionary(g=>g.Key,g=>g.Count()),
                scope="Structural inventory only: direct bindings and exact interaction key occurrences, not environment/event execution or authorization"});
        }
        return new{cases,failed,staticFalseDynamicTrue,failures,contexts,
            scope="Installed fake note constructor, static GetCuttable and actual AnimationHelper with controlled zero/one/two Track property states and per-object interactable animation; injected property state, not event dispatch, path properties, complete processor pipeline or Unity collider writes. Pinned NoteUpdate consumer compares non-null dynamic cuttable >= 1 independently of static uninteractable."};
    }
}
