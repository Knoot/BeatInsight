using System.Collections;
using System.Reflection;
using System.Runtime.Loader;
using Newtonsoft.Json.Linq;

namespace BeatInsight.Compare;

static class NoodleBombAngleChecks
{
    public static object Run(string gameRoot)
    {
        foreach(var pin in new[]{("NoodleExtensions","FE50124E33631EED7ECB78EA1A19BB7EA64CC12974B0298DAD991672C2ABDA95"),
            ("CustomJSONData","4F9F8EFD8012159D8995B6D936E3C21B6AA6A35E319686FA3E9B04C2B6331F7C")})
        {
            if(Comparison.HashFile(Path.Combine(gameRoot,"Plugins",pin.Item1+".dll"))!=pin.Item2)
                throw new InvalidDataException("Reinspect changed legacy bomb angle consumer");
        }
        GameLoader.Load(gameRoot,"""{"_version":"2.6.0","_notes":[],"_obstacles":[]}""",120,noodleExtensions:true);
        const BindingFlags flags=BindingFlags.Public|BindingFlags.NonPublic|BindingFlags.Instance;
        var custom=AssemblyLoadContext.Default.Assemblies.Single(a=>a.GetName().Name=="CustomJSONData");
        var metadataType=custom.GetType("CustomJSONData.CustomBeatmap.CustomData",true)!;
        var factory=custom.GetType("CustomJSONData.CustomBeatmap.CustomNoteData",true)!.GetMethod("CreateCustomBombNoteData")!;
        var noodle=AssemblyLoadContext.Default.Assemblies.Single(a=>a.GetName().Name=="NoodleExtensions");
        var noteType=noodle.GetType("NoodleExtensions.NoodleNoteData",true)!;
        var objectType=noodle.GetType("NoodleExtensions.NoodleObjectData",true)!;
        var ctor=noteType.GetConstructors(flags).Single();var cp=ctor.GetParameters();
        int cases=0,failed=0;var results=new List<object>();
        foreach(float? angle in new float?[]{null,-450,-63.806f,-45,0,26.567f,180,450})
        foreach(bool mirror in new[]{false,true})foreach(bool positioned in new[]{false,true})
        {
            (NoteData Note,JObject Geometry) Build(float? value)
            {
                var metadata=(IDictionary)Activator.CreateInstance(metadataType)!;
                metadata["_cutDirection"]=value;
                if(positioned)metadata["_position"]=new List<object>{1f,.5f};
                var p=factory.GetParameters();
                var note=(NoteData)factory.Invoke(null,new object[]{1f,2f,0,1,Enum.ToObject(p[4].ParameterType,2),metadata,new Version(2,6,0)})!;
                if(mirror)note.Mirror(4);
                var parsed=ctor.Invoke(new object[]{note,metadata,120f,Activator.CreateInstance(cp[3].ParameterType)!,Activator.CreateInstance(cp[4].ParameterType)!,true,mirror});
                var geometry=new JObject(new[]{"StartX","StartY","ScaleX","ScaleY","ScaleZ","Njs","SpawnOffset","Fake","Uninteractable"}
                    .Select(name=>new JProperty(name,objectType.GetProperty(name,flags)!.GetValue(parsed))));
                return(note,geometry);
            }
            var baseline=Build(null);var actual=Build(angle);
            bool pass=JToken.DeepEquals(baseline.Geometry,actual.Geometry)&&actual.Note.colorType==baseline.Note.colorType
                &&actual.Note.scoringType==baseline.Note.scoringType&&actual.Note.time==baseline.Note.time&&actual.Note.beat==baseline.Note.beat
                &&actual.Note.lineIndex==baseline.Note.lineIndex&&actual.Note.noteLineLayer==baseline.Note.noteLineLayer
                &&(!angle.HasValue||Math.Abs(actual.Note.cutDirectionAngleOffset)==Math.Abs(angle.Value));
            cases++;if(!pass)failed++;
            results.Add(new{angle,mirror,positioned,pass,scoringType=(int)actual.Note.scoringType,color=(int)actual.Note.colorType,
                direction=(int)actual.Note.cutDirection,actual.Note.cutDirectionAngleOffset,actual.Geometry});
        }
        return new{cases,failed,results,scope="Installed custom bomb factory and NoodleNoteData constructor; legacy angle changes orientation, not bomb identity/time/position/scale; no collision or Unity lifecycle claim"};
    }
}
