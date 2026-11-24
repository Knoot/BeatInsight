using System.Collections;
using System.Reflection;
using System.Runtime.Loader;
using Newtonsoft.Json.Linq;

namespace BeatInsight.Compare;

static class NoodleV3AliasChecks
{
    public static object Run(string gameRoot)
    {
        foreach(var pin in new[]{("NoodleExtensions","FE50124E33631EED7ECB78EA1A19BB7EA64CC12974B0298DAD991672C2ABDA95"),
            ("CustomJSONData","4F9F8EFD8012159D8995B6D936E3C21B6AA6A35E319686FA3E9B04C2B6331F7C")})
            if(Comparison.HashFile(Path.Combine(gameRoot,"Plugins",pin.Item1+".dll"))!=pin.Item2)throw new InvalidDataException("Reinspect changed v3 alias consumer");
        const BindingFlags flags=BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic;
        var raw=GameLoader.Load(gameRoot,"""{"version":"3.3.0","colorNotes":[],"obstacles":[]}""",120,noodleExtensions:true);
        var custom=AssemblyLoadContext.Default.Assemblies.Single(a=>a.GetName().Name=="CustomJSONData");
        var metadataType=custom.GetType("CustomJSONData.CustomBeatmap.CustomData",true)!;
        var factory=custom.GetType("CustomJSONData.CustomBeatmap.CustomNoteData",true)!.GetConstructors().Single();
        var noodle=AssemblyLoadContext.Default.Assemblies.Single(a=>a.GetName().Name=="NoodleExtensions");
        var noteType=noodle.GetType("NoodleExtensions.NoodleNoteData",true)!;
        var ctor=noteType.GetConstructors(flags).Single();var cp=ctor.GetParameters();
        var processorType=noodle.GetType("NoodleExtensions.HarmonyPatches.ObjectProcessing.NoodleBeatmapObjectsInTimeRowProcessor",true)!;
        var baseProcessor=typeof(NoteData).Assembly.GetType("BeatmapObjectsInTimeRowProcessor",true)!;
        var all=noodle.GetType("NoodleExtensions.HarmonyPatches.ObjectProcessing.ProcessNotesNoodleDataInTimeRow",true)!.GetMethod("ProcessAllNotesInTimeRowPatch",BindingFlags.Static|BindingFlags.NonPublic)!;
        var pair=baseProcessor.GetMethod("HandlePerColorTypeTimeSliceContainerDidFinishTimeSlice",flags)!;
        var mirrorPostfix=noodle.GetType("NoodleExtensions.HarmonyPatches.ObjectProcessing.LeftHandedMirrorNoteData",true)!.GetMethod("FlipLineIndex",BindingFlags.Static|BindingFlags.Public|BindingFlags.NonPublic)!;
        object Slice(Type itemType,NoteData[] notes)
        {
            var type=baseProcessor.GetNestedType("TimeSliceContainer`1",flags)!.MakeGenericType(itemType);
            var slice=Activator.CreateInstance(type,flags,null,new object[]{4},null)!;
            foreach(var note in notes)type.GetMethod("AddWithoutNotifications",flags)!.Invoke(slice,new object[]{note});
            return slice;
        }
        PropertyInfo Property(string name)
        {
            for(Type? t=noteType;t!=null;t=t.BaseType)
                if(t.GetProperty(name,flags|BindingFlags.DeclaredOnly) is PropertyInfo property)return property;
            throw new InvalidDataException("Changed parsed metadata property: "+name);
        }
        string[] fields={"StartX","StartY","ScaleX","ScaleY","ScaleZ","Njs","SpawnOffset","Fake","Uninteractable",
            "InternalFlipLineIndex","InternalFlipYSide","InternalStartNoteLineLayer","DisableGravity","DisableLook","Link"};
        int cases=0,failed=0,linkedPairs=0;var failures=new List<object>();var roles=new HashSet<string>();
        foreach(bool arcs in new[]{false,true})foreach(int color in new[]{0,1})foreach(int direction in new[]{0,3,8})
        foreach(bool mirror in new[]{false,true})foreach(bool coordinates in new[]{false,true})foreach(bool scale in new[]{false,true})
        foreach(string variant in new[]{"_position","_scale","_cutDirection","size","all"})
        {
            JArray Build(bool extra)
            {
                var root=new JObject {["version"]="3.3.0",["colorNotes"]=new JArray(
                    new JObject {["b"]=2,["x"]=0,["y"]=0,["c"]=color,["d"]=direction,["a"]=15},
                    new JObject {["b"]=arcs?4:2,["x"]=1,["y"]=1,["c"]=color,["d"]=direction,["a"]=15})};
                if(arcs)root["sliders"]=new JArray(new JObject {["b"]=2,["c"]=color,["x"]=0,["y"]=0,["d"]=direction,["tb"]=4,["tx"]=1,["ty"]=1,["tc"]=direction});
                var loaded=GameLoader.Load(gameRoot,root.ToString(),120,noodleExtensions:true);
                var source=OracleBridge.GetItems(loaded).OfType<NoteData>().ToArray();
                if(source.Length!=2||arcs&&(!source.Any(n=>n.scoringType.ToString()=="ArcHead")||!source.Any(n=>n.scoringType.ToString()=="ArcTail")))throw new InvalidDataException("Arc fixture roles: "+string.Join(",",source.Select(n=>n.scoringType.ToString())));
                var notes=new List<NoteData>();
                for(int n=0;n<source.Length;n++)
                {
                    var metadata=(IDictionary)Activator.CreateInstance(metadataType)!;
                    if(coordinates)metadata["coordinates"]=new List<object>{n-.75f,n+.25f};
                    if(scale)metadata["scale"]=new List<object>{2f,1f,1f}; // Negative control: active v3 scale must win.
                    if(extra)
                    {
                        if(variant is "_position" or "all")metadata["_position"]=new List<object>{3f-n,-3f+n};
                        if(variant is "_scale" or "all")metadata["_scale"]=new List<object>{1f,1f,1f};
                        if(variant is "_cutDirection" or "all")metadata["_cutDirection"]=n==0?-45f:225f;
                        if(variant is "size" or "all")metadata["size"]=new List<object>{1f,1f,1f};
                    }
                    var args=factory.GetParameters().Select(p=>p.Name=="customData"?(object)metadata:p.Name=="version"?new Version(3,3,0):typeof(NoteData).GetProperty(p.Name!,flags)!.GetValue(source[n])).ToArray();
                    notes.Add((NoteData)factory.Invoke(args));
                }
                var processor=Activator.CreateInstance(processorType,flags,null,new[]{(object)4,raw},null)!;
                foreach(var row in notes.GroupBy(n=>n.time))
                {
                    var items=row.ToArray();all.Invoke(null,new[]{processor,(object)4,Slice(typeof(BeatmapDataItem),items)});
                    pair.Invoke(processor,new[]{Slice(typeof(NoteData),items),(object)100f});
                }
                var snapshot=new JArray();
                foreach(var note in notes)
                {
                    if(mirror){note.Mirror(4);mirrorPostfix.Invoke(null,new object[]{note});}
                    var metadata=note.GetType().GetProperty("customData")!.GetValue(note)!;
                    var parsed=ctor.Invoke(new object[]{note,metadata,120f,Activator.CreateInstance(cp[3].ParameterType)!,Activator.CreateInstance(cp[4].ParameterType)!,false,mirror});
                    var data=new JObject(fields.Select(name=>new JProperty(name,Property(name).GetValue(parsed))));
                    data["direction"]=(int)note.cutDirection;data["angle"]=note.cutDirectionAngleOffset;
                    data["scoring"]=(int)note.scoringType;data["color"]=(int)note.colorType;data["time"]=note.time;data["lane"]=note.lineIndex;
                    roles.Add(note.scoringType.ToString());snapshot.Add(data);
                }
                return snapshot;
            }
            var baseline=Build(false);var actual=Build(true);
            bool pass=JToken.DeepEquals(baseline,actual)&&actual.All(n=>(float?)n["ScaleX"]==(scale?2f:(float?)null));
            cases++;if(arcs)linkedPairs++;
            if(!pass){failed++;if(failures.Count<12)failures.Add(new{arcs,color,direction,mirror,coordinates,scale,variant,baseline,actual});}
        }
        return new{cases,failed,linkedPairs,roles,failures,
            scope="Installed v3 loader roles, full custom-note copy constructor, actual row handlers, selected mirror/postfix and NoodleNoteData constructor; separate/conflicting legacy aliases and canonical size, ordinary and arc-linked notes, both hands; active-scale negative control; no Unity lifecycle or all-mod patch ordering"};
    }
}
