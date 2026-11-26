using System.Collections;
using System.Reflection;
using BeatInsight.Core;
using BeatInsight.Formats;
using Newtonsoft.Json.Linq;

namespace BeatInsight.Compare;

static class IndependentDensityChecks
{
    const BindingFlags flags=BindingFlags.Public|BindingFlags.NonPublic|BindingFlags.Instance|BindingFlags.Static;
    static object Get(object value,string name)=>value.GetType().GetProperty(name,flags)?.GetValue(value)??value.GetType().GetField(name,flags)?.GetValue(value)
        ??throw new InvalidDataException("Density source member changed: "+name);
    internal static object Run(string gameRoot)
    {
        var game=AppDomain.CurrentDomain.GetAssemblies().Single(a=>a.GetName().Name=="DataModels");
        var custom=AppDomain.CurrentDomain.GetAssemblies().Single(a=>a.GetName().Name=="CustomJSONData");
        var patches=custom.GetType("CustomJSONData.HarmonyPatches.ConvertersCustomify",true)!;
        string folder=BeatInsight.Tooling.LocalArtifacts.PathFor("density-native-fixtures",Guid.NewGuid().ToString("N"));Directory.CreateDirectory(folder);
        File.WriteAllText(Path.Combine(folder,"Info.dat"),"""{"version":"4.0.1","audio":{"bpm":120,"songDuration":12},"difficultyBeatmaps":[{"characteristic":"Standard","difficulty":"Expert","beatmapDataFilename":"map.dat","customData":{"requirements":["Noodle Extensions"]}}]}""");
        int cases=0,failed=0;var results=new List<object>();
        foreach(int major in new[]{2,3})
        {
            string ns=major==2?"BeatmapDataLoaderVersion2_6_0AndEarlier":"BeatmapDataLoaderVersion3";
            var converter=game.GetType(ns+".BeatmapDataLoader+ColorNoteConverter",true)!;
            var method=InstalledTranspilerCompiler.Compile(converter.GetMethod("Convert",flags)!,patches.GetMethod(major==2?"ColorNoteConvertV2_6_0AndEarlier":"ColorNoteConvertV3",flags)!).Method;
            var bpm=game.GetType("BpmTimeProcessor",true)!.GetConstructors(flags).Single(c=>c.GetParameters().Length==2&&c.GetParameters()[1].ParameterType.GenericTypeArguments[0].Name==(major==2?"EventData":"BpmChangeEventData"));
            var rotation=game.GetType("RotationTimeProcessor",true)!.GetConstructors(flags).Single(c=>c.GetParameters().Length==1&&c.GetParameters()[0].ParameterType.GenericTypeArguments[0].Name==(major==2?"EventData":"RotationEventData"));
            foreach(bool change in new[]{false,true})foreach(bool fake in new[]{false,true})foreach(bool player in new[]{false,true})
            {
                var root=major==2?JObject.Parse("""{"_version":"2.6.0","_notes":[{"_time":2,"_type":0,"_cutDirection":1},{"_time":4,"_type":1,"_cutDirection":1}],"_events":[]}"""):
                    JObject.Parse("""{"version":"3.3.0","colorNotes":[{"b":2,"c":0,"d":1},{"b":4,"c":1,"d":1}],"bpmEvents":[],"rotationEvents":[]}""");
                string dataKey=major==2?"_customData":"customData",sourceKey=major==2?"_notes":"colorNotes",flag=major==2?"_fake":"NE_fake";
                foreach(var note in root[sourceKey]!)note[dataKey]=new JObject{[major==2?"_rotation":"worldRotation"]=new JArray(30,90,0)};
                root[sourceKey]![0]![dataKey]![flag]=fake;
                if(change)((JArray)root[major==2?"_events":"bpmEvents"]!).Add(major==2?new JObject{["_time"]=2,["_type"]=100,["_floatValue"]=180}:new JObject{["b"]=2,["m"]=180});
                if(player)root[dataKey]=new JObject{[major==2?"_customEvents":"customEvents"]=new JArray(new JObject{[major==2?"_time":"b"]=0,[major==2?"_type":"t"]="AssignPlayerToTrack",[major==2?"_data":"d"]=new JObject{[major==2?"_track":"track"]="player"}})};
                var saved=NoodleNativeData.Deserialize(gameRoot,root,major.ToString());
                var events=Get(saved,major==2?"events":"bpmEvents");
                var instance=Activator.CreateInstance(converter,flags,null,new[]{bpm.Invoke(new[]{(object)120f,events}),rotation.Invoke(new[]{Get(saved,major==2?"events":"rotationEvents")})},null)!;
                var native=new List<MapNote>();
                foreach(var source in (IEnumerable)Get(saved,major==2?"notes":"colorNotes"))
                {
                    var actual=(NoteData)method.Invoke(null,new[]{instance,source})!;
                    var metadata=(IDictionary)Get(actual,"customData");
                    if(!metadata.Contains(flag)||!Equals(metadata[flag],true))native.Add(new MapNote{Time=actual.time,Color=(int)actual.colorType});
                }
                // Native fake-array handling is covered separately; appended fakes never count.
                if(major==3)
                {
                    root[dataKey]??=new JObject();root[dataKey]!["fakeColorNotes"]=new JArray(new JObject{["b"]=3,["c"]=0,["d"]=1,["customData"]=new JObject{["animation"]=new JObject{["offsetPosition"]=new JArray(10,0,0)}}});
                }
                string json=root.ToString();File.WriteAllText(Path.Combine(folder,"map.dat"),json);
                var loaded=LocalMap.Load(new MapCase{Folder=folder,Characteristic="Standard",Difficulty="Expert",File="map.dat"},AnalysisFeatures.Density);
                var result=CompatibleAnalysis.Analyze(loaded.Input,reproduceKnownDefects:false,features:AnalysisFeatures.Density);
                var expected=CompatibleAnalysis.SegmentValues(native.OrderBy(n=>n.Time).ToArray(),12);
                bool pass=result.Segments.SequenceEqual(expected)&&result.LeftBins.Length==0&&result.RightBins.Length==0&&loaded.Json==json
                    &&loaded.Input.Notes.Count(n=>!n.IsFake)==native.Count;
                cases++;if(!pass)failed++;results.Add(new{major,change,fake,player,nativeNotes=native.Count,pass});
            }
        }
        return new{cases,failed,results,
            scope="Real custom v2/v3 color-note converters/native BPM times compared with independent-density projection and identical segment algorithm. No arcs/chains; fake markers excluded, appended fake notes omitted. Unsupported world/player metadata is not executed as geometry. Source JSON preserved; no swing/pose/ordinal history admission."};
    }
}
