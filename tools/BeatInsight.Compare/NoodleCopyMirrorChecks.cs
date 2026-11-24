using System.Collections;
using System.Reflection;
using System.Runtime.Loader;
using Newtonsoft.Json.Linq;

namespace BeatInsight.Compare;

// These adapters forward to the installed patch bodies in an isolated CLI only.
// They avoid Harmony/Mono detour ordering and never load into the game package.
public static class NoodleCopyMirrorBridge
{
    public static int PrefixCalls,Fallbacks,MirrorCalls,InsertCalls;
    public static Action<object>? AfterMirror;
    public static bool FilteredCopy(object source,object filter,out object? result)
    {
        PrefixCalls++;
        var assembly=AssemblyLoadContext.Default.Assemblies.Single(a=>a.GetName().Name=="CustomJSONData");
        var method=assembly.GetType("CustomJSONData.CustomBeatmap.CustomBeatmapData",true)!
            .GetMethod("GetFilteredCopy",BindingFlags.Static|BindingFlags.Public|BindingFlags.NonPublic)!;
        object?[] args={source,filter,null};bool original=(bool)method.Invoke(null,args)!;
        result=args[2];if(original)Fallbacks++;return original;
    }
    public static bool Insert(object source,object item,out object? result)
    {
        result=null;
        if(source.GetType().FullName!="CustomJSONData.CustomBeatmap.CustomBeatmapDataSortedListForTypeAndIds")return true;
        var assembly=AssemblyLoadContext.Default.Assemblies.Single(a=>a.GetName().Name=="CustomJSONData");
        InsertCalls++;
        var method=assembly.GetType("CustomJSONData.CustomBeatmap.CustomBeatmapDataSortedListForTypeAndIds",true)!
            .GetMethod("InsertItem",BindingFlags.Static|BindingFlags.Public|BindingFlags.NonPublic)!;
        object?[] args={source,item,null};bool original=(bool)method.Invoke(null,args)!;result=args[2];return original;
    }
    public static void Mirrored(object note)
    {
        MirrorCalls++;
        var assembly=AssemblyLoadContext.Default.Assemblies.Single(a=>a.GetName().Name=="NoodleExtensions");
        assembly.GetType("NoodleExtensions.HarmonyPatches.ObjectProcessing.LeftHandedMirrorNoteData",true)!
            .GetMethod("FlipLineIndex",BindingFlags.Static|BindingFlags.Public|BindingFlags.NonPublic)!.Invoke(null,new[]{note});
        AfterMirror?.Invoke(note);
    }
    public static void Reset(){PrefixCalls=Fallbacks=MirrorCalls=InsertCalls=0;}
}

static class NoodleCopyMirrorChecks
{
    public static object Run(string gameRoot)
    {
        foreach(var pin in new[]{("CustomJSONData","4F9F8EFD8012159D8995B6D936E3C21B6AA6A35E319686FA3E9B04C2B6331F7C"),
            ("NoodleExtensions","FE50124E33631EED7ECB78EA1A19BB7EA64CC12974B0298DAD991672C2ABDA95")})
            if(Comparison.HashFile(Path.Combine(gameRoot,"Plugins",pin.Item1+".dll"))!=pin.Item2)throw new InvalidDataException("Reinspect changed copy/mirror provider");
        GameLoader.Load(gameRoot,"""{"version":"3.3.0","colorNotes":[],"obstacles":[]}""",120,noodleExtensions:true);
        const BindingFlags flags=BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic;
        var custom=AssemblyLoadContext.Default.Assemblies.Single(a=>a.GetName().Name=="CustomJSONData");
        var metadataType=custom.GetType("CustomJSONData.CustomBeatmap.CustomData",true)!;
        var mapType=custom.GetType("CustomJSONData.CustomBeatmap.CustomBeatmapData",true)!;
        var factory=custom.GetType("CustomJSONData.CustomBeatmap.CustomNoteData",true)!.GetMethod("CreateCustomBasicNoteData")!;
        var noodle=AssemblyLoadContext.Default.Assemblies.Single(a=>a.GetName().Name=="NoodleExtensions");
        var ctor=noodle.GetType("NoodleExtensions.NoodleNoteData",true)!.GetConstructors(flags).Single();var cp=ctor.GetParameters();
        var objectType=noodle.GetType("NoodleExtensions.NoodleObjectData",true)!;
        var transform=AssemblyLoadContext.Default.Assemblies.Select(a=>a.GetType("BeatmapDataMirrorTransform")).First(t=>t!=null)!
            .GetMethod("CreateTransformedData",BindingFlags.Public|BindingFlags.Static)!;
        object Metadata()=>Activator.CreateInstance(metadataType)!;
        IDictionary Data(NoteData note)=>(IDictionary)note.GetType().GetProperty("customData")!.GetValue(note)!;
        object? Property(object value,string name)=>objectType.GetProperty(name,flags)!.GetValue(value);
        var helperType=noodle.GetType("NoodleExtensions.Animation.AnimationHelper",true)!;
        var offset=helperType.GetMethod("GetObjectOffset",flags)!;
        int cases=0,failed=0,helperCalls=0;var results=new List<object>();
        foreach(int major in new[]{2,3})foreach(int direction in new[]{0,3,8})foreach(int color in new[]{0,1})
        foreach(bool coordinates in new[]{false,true})foreach(bool frames in new[]{false,true})foreach(bool mirror in new[]{false,true})
        {
            var map=(BeatmapData)Activator.CreateInstance(mapType,new[]{(object)4,Metadata(),Metadata(),Metadata(),new Version(major,major==2?6:3,0)})!;
            var source=new List<NoteData>();var sourceMetadata=new List<string>();
            for(int n=0;n<2;n++)
            {
                var metadata=(IDictionary)Metadata();
                if(coordinates)metadata[major==2?"_position":"coordinates"]=new List<object>{n-.75f,.25f};
                if(major==2)metadata["_cutDirection"]=37.5f;
                foreach(string key in new[]{"NE_flipYSide","NE_flipLineIndex","NE_startNoteLineLayer","NE_tailStartNoteLineLayer"})metadata[key]=.5f;
                var animation=(IDictionary)Metadata();
                animation[major==2?"_scale":"scale"]=frames?
                    new List<object>{new List<object>{1f,1f,1f,0f},new List<object>{1f,1f,1f,1f}}:new List<object>{1f,1f,1f};
                metadata[major==2?"_animation":"animation"]=animation;
                var p=factory.GetParameters();
                var note=(NoteData)factory.Invoke(null,new object[]{1f+n,2f+2*n,0,n,Enum.ToObject(p[4].ParameterType,0),
                    Enum.ToObject(p[5].ParameterType,n==0?color:1-color),Enum.ToObject(p[6].ParameterType,direction),metadata,new Version(major,major==2?6:3,0)})!;
                if(major==3)note.SetCutDirectionAngleOffset(15);
                source.Add(note);map.AddBeatmapObjectDataInOrder(note);
            }
            foreach(var note in source)sourceMetadata.Add(JToken.FromObject(Data(note)).ToString());
            NoodleCopyMirrorBridge.Reset();
            var copied=mirror?(BeatmapData)transform.Invoke(null,new object[]{map})!:map.GetFilteredCopy(item=>item);
            var notes=OracleBridge.GetItems(copied).OfType<NoteData>().OrderBy(n=>n.time).ToArray();
            var errors=new List<string>();
            if(copied.GetType()!=mapType||ReferenceEquals(copied,map)||notes.Length!=2||NoodleCopyMirrorBridge.PrefixCalls!=1
                ||NoodleCopyMirrorBridge.Fallbacks!=0||NoodleCopyMirrorBridge.InsertCalls!=2||NoodleCopyMirrorBridge.MirrorCalls!=(mirror?2:0))errors.Add("Copy/mirror dispatch or source type changed");
            for(int n=0;n<notes.Length;n++)
            {
                var metadata=Data(notes[n]);
                if(ReferenceEquals(notes[n],source[n])||ReferenceEquals(metadata,Data(source[n])))errors.Add("Copied note or top-level metadata aliases source");
                if(notes[n].lineIndex!=(mirror?3-n:n)||(int)notes[n].colorType!=(mirror?1-(n==0?color:1-color):n==0?color:1-color))errors.Add("Mirrored identity differs");
                string[] cacheKeys={"NE_flipYSide","NE_flipLineIndex","NE_startNoteLineLayer","NE_tailStartNoteLineLayer"};
                if(!cacheKeys.All(k=>metadata.Contains(k)!=mirror))errors.Add("Mirror postfix cache invalidation changed");
                if(coordinates)
                {
                    var point=(IList)metadata[major==2?"_position":"coordinates"]!;
                    if(Convert.ToSingle(point[0])!=(mirror?-n-.25f:n-.75f))errors.Add("Custom position reflection changed");
                }
                var parsed=ctor.Invoke(new object[]{notes[n],metadata,120f,Activator.CreateInstance(cp[3].ParameterType)!,Activator.CreateInstance(cp[4].ParameterType)!,major==2,mirror});
                var helper=Activator.CreateInstance(helperType,flags,null,new object[]{mirror},null)!;
                foreach(float time in new[]{0f,.5f,1f})
                {
                    object?[] args=new object?[offset.GetParameters().Length];args[0]=Property(parsed,"AnimationObject");args[2]=time;
                    offset.Invoke(helper,args);helperCalls++;
                    var vector=args[5];
                    bool unit=vector!=null&&new[]{"x","y","z"}.All(axis=>(float)vector.GetType().GetField(axis)!.GetValue(vector)! ==(mirror&&axis=="x"?-1:1));
                    if(!unit||new[]{3,4,6,7,8,9}.Any(i=>args[i]!=null))errors.Add("Copied unit-scale animation changed helper geometry outputs");
                }
                float expectedAngle=(major==2?37.5f:15f)*(mirror?-1:1);
                if(notes[n].cutDirectionAngleOffset!=expectedAngle||Property(parsed,"AnimationObject")==null)errors.Add("Post-copy constructor angle/animation changed");
                if(sourceMetadata[n]!=JToken.FromObject(Data(source[n])).ToString()||source[n].lineIndex!=n
                    ||source[n].cutDirectionAngleOffset!=(major==3?15:0))errors.Add("Copy/mirror mutated original chart");
            }
            cases++;if(errors.Count>0)failed++;results.Add(new{major,direction,color,coordinates,frames,mirror,errors});
        }
        // Ordinary BeatmapData must still use the original method body.
        foreach(bool mirror in new[]{false,true})
        {
            var map=new BeatmapData(4);var raw=GameLoader.Load(gameRoot,"""{"version":"3.3.0","colorNotes":[{"b":2,"x":1,"y":0,"c":0,"d":3}]}""",120);
            map.AddBeatmapObjectDataInOrder(OracleBridge.GetItems(raw).OfType<NoteData>().Single());
            NoodleCopyMirrorBridge.Reset();
            var copied=mirror?(BeatmapData)transform.Invoke(null,new object[]{map})!:map.GetFilteredCopy(item=>item);
            var note=OracleBridge.GetItems(copied).OfType<NoteData>().Single();
            bool pass=NoodleCopyMirrorBridge.PrefixCalls==1&&NoodleCopyMirrorBridge.Fallbacks==1&&note.lineIndex==(mirror?2:1);
            cases++;if(!pass)failed++;results.Add(new{plain=true,mirror,pass});
        }
        return new{cases,failed,helperCalls,adaptedFieldAccessors=2,results,scope="Whole installed mirror entry and filtered-copy loop, actual custom copy prefix, virtual note copy, mirror and selected Noodle postfix then constructor; in-memory prefix/postfix forwarding, sorted-list insert prefix and two direct field-address adapters replacing BSIPA lookup only; Harmony ordering, all-mod pipeline, head pose and Unity lifecycle not executed"};
    }
}
