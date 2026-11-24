using System.Collections;
using System.Reflection;
using System.Runtime.Loader;
using Newtonsoft.Json.Linq;

namespace BeatInsight.Compare;

static class NoodleFakeLoadChecks
{
    public static object Run(string gameRoot,string? inventory=null)
    {
        const BindingFlags flags=BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic;
        GameLoader.Load(gameRoot,"""{"version":"3.3.0","colorNotes":[]}""",120,noodleExtensions:true);
        var custom=AssemblyLoadContext.Default.Assemblies.Single(a=>a.GetName().Name=="CustomJSONData");
        var factory=custom.GetType("CustomJSONData.CustomBeatmap.CustomNoteData",true)!.GetConstructors().Single();
        var noodle=AssemblyLoadContext.Default.Assemblies.Single(a=>a.GetName().Name=="NoodleExtensions");
        var parsedCtor=noodle.GetType("NoodleExtensions.NoodleNoteData",true)!.GetConstructors(flags).Single();var cp=parsedCtor.GetParameters();
        var trackType=AssemblyLoadContext.Default.Assemblies.Single(a=>a.GetName().Name=="Heck").GetType("Heck.Animation.Track",true)!;
        var tests=new List<(string Name,JObject Root,float Bpm)>();
        foreach(bool arcs in new[]{false,true})foreach(bool customFirst in new[]{false,true})foreach(int color in new[]{0,1})
        foreach(int direction in new[]{0,1,8})foreach(float fakeBeat in new[]{2f,4f,12f})
        {
            var root=new JObject {["version"]="3.3.0",["colorNotes"]=new JArray(
                Note(2,0,0,color,direction),Note(2,1,2,color,direction),Note(6,2,1,color,1))};
            if(arcs)root["sliders"]=new JArray(new JObject {["b"]=2,["c"]=color,["x"]=0,["y"]=0,["d"]=direction,["tb"]=6,["tx"]=2,["ty"]=1,["tc"]=1});
            var data=new JObject {["fakeColorNotes"]=new JArray(Note(fakeBeat,0,0,color,direction),Note(fakeBeat,1,2,color,direction))};
            foreach(var fake in (JArray)data["fakeColorNotes"]!)fake["customData"]=new JObject {["uninteractable"]=true};
            data["fakeBombNotes"]=new JArray(new JObject {["b"]=fakeBeat,["x"]=2,["y"]=1,["customData"]=new JObject {["uninteractable"]=true}});
            if(customFirst)root.AddFirst(new JProperty("customData",data));else root["customData"]=data;
            tests.Add(($"arc={arcs};first={customFirst};color={color};direction={direction};fake={fakeBeat}",root,120));
        }
        if(inventory!=null)foreach(string source in new[]{"089","090","091","092"})
        {
            var request=JObject.Parse(File.ReadAllText(Path.Combine(inventory,source+".result.json")))["request"]!;
            string folder=(string)request["Folder"]!,file=(string)request["File"]!;
            var info=JObject.Parse(File.ReadAllText(Path.Combine(folder,"Info.dat")));
            tests.Add((source+" "+(string?)info["_songName"]+" "+(string?)request["Characteristic"]+"/"+(string?)request["Difficulty"],
                JObject.Parse(File.ReadAllText(Path.Combine(folder,file))),(float?)info["_beatsPerMinute"]??(float)info["audio"]!["bpm"]!));
        }
        int failed=0,stableSpatial=0,temporalChanges=0;var results=new List<object>();
        NativeFakeRegistry.CaptureSources=true;
        try
        {
            foreach(var test in tests)
            {
                var input=(JObject)test.Root.DeepClone();int realCount=0,fakeCount=0;
                foreach(var note in input["colorNotes"]??new JArray())Tag(note,"real-"+realCount++);
                foreach(var note in input["customData"]?["fakeColorNotes"]??new JArray())Tag(note,"fake-"+fakeCount++);
                foreach(var note in input["customData"]?["fakeBombNotes"]??new JArray())Tag(note,"fake-"+fakeCount++);
                var plain=(JObject)input.DeepClone();((JObject)plain["customData"]!).Remove("fakeColorNotes");((JObject)plain["customData"]!).Remove("fakeBombNotes");
                (Dictionary<string,JObject> Real,JArray Arcs,int Fake) Load(JObject root)
                {
                    var map=GameLoader.Load(gameRoot,root.ToString(),test.Bpm,noodleExtensions:true);
                    var snapshots=new Dictionary<string,JObject>();int fakes=0;
                    foreach(var note in OracleBridge.GetItems(map).OfType<NoteData>())
                    {
                        if(!NativeFakeRegistry.TryGetSource(note,out var origin))throw new InvalidDataException("Native note conversion source was not captured");
                        var metadata=origin!.GetType().GetProperty("customData")!.GetValue(origin)!;
                        var dictionary=(IDictionary)metadata;
                        string id=dictionary["probeIdentity"] as string??throw new InvalidDataException("Missing probe identity: "+test.Name+" "+origin.GetType()+" "+Newtonsoft.Json.JsonConvert.SerializeObject(dictionary));bool fake=id.StartsWith("fake-",StringComparison.Ordinal);
                        if(NativeFakeRegistry.IsFake(note)!=fake)throw new InvalidDataException("Native fake-array marker disagrees with source identity");
                        var state=new JObject {["time"]=note.time,["beat"]=note.beat,["lane"]=note.lineIndex,["layer"]=(int)note.noteLineLayer,
                            ["beforeJumpLayer"]=(int)note.beforeJumpNoteLineLayer,["direction"]=(int)note.cutDirection,["angle"]=note.cutDirectionAngleOffset,
                            ["scoring"]=(int)note.scoringType,["color"]=(int)note.colorType,["flipLane"]=note.flipLineIndex,["flipY"]=note.flipYSide,
                            ["next"]=note.timeToNextColorNote,["previous"]=note.timeToPrevColorNote};
                        var args=factory.GetParameters().Select(p=>p.Name=="customData"?metadata:p.Name=="version"?new Version(3,3,0):typeof(NoteData).GetProperty(p.Name!,flags)!.GetValue(note)).ToArray();
                        var copied=(NoteData)factory.Invoke(args);
                        var tracks=(IDictionary)Activator.CreateInstance(cp[4].ParameterType)!;
                        if(dictionary.Contains("track"))
                        {
                            var names=dictionary["track"] is string one?new[]{one}:((IEnumerable)dictionary["track"]!).Cast<string>();
                            foreach(string name in names)tracks[name]=Activator.CreateInstance(trackType,true)!;
                        }
                        parsedCtor.Invoke(new object[]{copied,metadata,test.Bpm,Activator.CreateInstance(cp[3].ParameterType)!,tracks,false,false});
                        if(fake){fakes++;if((int)copied.scoringType!=-1||dictionary["NE_fake"] is not true)throw new InvalidDataException("Genuine fake-array data did not become NoScore");}
                        else snapshots.Add(id,state);
                    }
                    var arcs=new JArray(OracleBridge.GetItems(map).OfType<SliderData>().Select(a=>new JObject {["time"]=a.time,["tailTime"]=a.tailTime,["headLane"]=a.headLineIndex,["headLayer"]=(int)a.headLineLayer,["hasHead"]=a.hasHeadNote,["color"]=(int)a.colorType}));
                    return(snapshots,arcs,fakes);
                }
                var baseline=Load(plain);var actual=Load(input);var spatial=new List<string>();var timing=new List<object>();
                foreach(var entry in baseline.Real)
                {
                    if(!actual.Real.TryGetValue(entry.Key,out var current)){spatial.Add(entry.Key+":missing");continue;}
                    foreach(var property in entry.Value.Properties())if(!JToken.DeepEquals(property.Value,current[property.Name]))
                    {if(property.Name is "next" or "previous")timing.Add(new{note=entry.Key,field=property.Name,before=property.Value,after=current[property.Name]});else spatial.Add(entry.Key+":"+property.Name);}
                }
                float lastReal=baseline.Real.Count==0?-1:baseline.Real.Values.Max(n=>(float)n["beat"]!);
                bool tail=new[]{"fakeColorNotes","fakeBombNotes"}.SelectMany(key=>input["customData"]?[key]??new JArray()).All(f=>(float)f["b"]!>lastReal);
                bool arcsEqual=JToken.DeepEquals(baseline.Arcs,actual.Arcs);
                bool pass=actual.Fake==fakeCount&&actual.Real.Count==realCount&&baseline.Real.Count==realCount&&(!tail||spatial.Count==0&&arcsEqual);
                if(spatial.Count==0&&arcsEqual)stableSpatial++;if(timing.Count>0)temporalChanges++;if(!pass)failed++;
                results.Add(new{name=test.Name,realCount,fakeCount,tail,pass,spatialChanges=spatial.Take(12).ToArray(),timingChanges=timing.Take(12).ToArray(),arcsEqual});
            }
        }
        finally{NativeFakeRegistry.CaptureSources=false;}
        return new{cases=tests.Count,failed,stableSpatial,temporalChanges,results,
            scope="Registered installed FakeNotesJson deserializer and complete installed v3 raw loader/temporal preparation and arc roles; conversion provenance captured by weak keys, genuine fake metadata passed through full custom note copy and Noodle constructor; no complete custom converter/processor patch pipeline, event/environment execution, Unity lifecycle or support admission"};
    }
    static JObject Note(float beat,int x,int y,int color,int direction)=>new(){["b"]=beat,["x"]=x,["y"]=y,["c"]=color,["d"]=direction};
    static void Tag(JToken note,string id)
    {
        if(note["customData"] is not JObject)note["customData"]=new JObject();
        note["customData"]!["probeIdentity"]=id;
    }
}
