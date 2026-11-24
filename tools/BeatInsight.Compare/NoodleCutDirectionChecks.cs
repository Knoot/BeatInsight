using System.Reflection;
using System.Runtime.Loader;
using BeatInsight.Core;
using BeatInsight.Formats;
using Newtonsoft.Json.Linq;

namespace BeatInsight.Compare;

static class NoodleCutDirectionChecks
{
    public static object Run(string gameRoot,string reportPath)
    {
        GameLoader.Load(gameRoot,"""{"_version":"2.6.0","_notes":[],"_obstacles":[]}""",120,noodleExtensions:true);
        const BindingFlags flags=BindingFlags.Public|BindingFlags.NonPublic|BindingFlags.Instance;
        var noodle=AssemblyLoadContext.Default.Assemblies.Single(a=>a.GetName().Name=="NoodleExtensions");
        var ctor=noodle.GetType("NoodleExtensions.NoodleNoteData",true)!.GetConstructors(flags).Single();var cp=ctor.GetParameters();
        var customAssembly=AssemblyLoadContext.Default.Assemblies.Single(a=>a.GetName().Name=="CustomJSONData");
        var metadataType=customAssembly.GetType("CustomJSONData.CustomBeatmap.CustomData",true)!;
        var factory=customAssembly.GetType("CustomJSONData.CustomBeatmap.CustomNoteData",true)!.GetMethod("CreateCustomBasicNoteData")!;
        string folder=Path.Combine(Path.GetDirectoryName(Path.GetFullPath(reportPath))!,"legacy-cut-direction-fixtures");Directory.CreateDirectory(folder);
        File.WriteAllText(Path.Combine(folder,"Info.dat"),"""{"_version":"2.1.0","_beatsPerMinute":120,"audio":{"songDuration":10},"_difficultyBeatmapSets":[{"_beatmapCharacteristicName":"Standard","_difficultyBeatmaps":[{"_difficulty":"Expert","_beatmapFilename":"map.dat","_customData":{"_requirements":["Noodle Extensions"]}}]}]}""");
        var results=new List<object>();int cases=0,failed=0;
        for(int direction=0;direction<=8;direction++)foreach(float? angle in new float?[]{null,-315,-45,0,45,450})foreach(bool pair in new[]{false,true})foreach(bool fake in new[]{false,true})
        {
            cases++;var custom=new JObject {["_cutDirection"]=angle.HasValue?new JValue(angle.Value):JValue.CreateNull(),["_fake"]=fake};
            var notes=new JArray(new JObject {["_time"]=2,["_lineIndex"]=0,["_lineLayer"]=0,["_type"]=0,["_cutDirection"]=direction,["_customData"]=custom},
                new JObject {["_time"]=6,["_lineIndex"]=2,["_lineLayer"]=0,["_type"]=0,["_cutDirection"]=1});
            if(pair)notes.Add(new JObject {["_time"]=2,["_lineIndex"]=1,["_lineLayer"]=2,["_type"]=0,["_cutDirection"]=direction==8?1:direction});
            var root=new JObject {["_version"]="2.6.0",["_notes"]=notes};
            File.WriteAllText(Path.Combine(folder,"map.dat"),root.ToString());
            var native=GameLoader.Load(gameRoot,root.ToString(),120,noodleExtensions:true);
            var nativeNotes=OracleBridge.GetItems(native).OfType<NoteData>().Select(note=>
            {
                // The raw v2 loader stand does not execute runtime custom-data copy
                // patches. Carry the controlled fixture metadata explicitly.
                var source=notes.Single(n=>(float)n["_time"]!*.5f==note.time&&(int)n["_lineIndex"]! ==note.lineIndex);
                var data=Activator.CreateInstance(metadataType)!;var dictionary=(System.Collections.IDictionary)data;
                if(source["_customData"] is JObject supplied)
                {
                    dictionary["_cutDirection"]=supplied["_cutDirection"]!.Type==JTokenType.Null?null:(float)supplied["_cutDirection"]!;
                    dictionary["_fake"]=(bool)supplied["_fake"]!;
                }
                var copied=(NoteData)factory.Invoke(null,new object[]{note.time,note.beat,0,note.lineIndex,note.noteLineLayer,note.colorType,note.cutDirection,data,new Version(2,6,0)})!;
                copied.SetCutDirectionAngleOffset(note.cutDirectionAngleOffset);
                if(copied.scoringType!=note.scoringType)throw new InvalidDataException("Unexpected non-basic raw fixture note");
                ctor.Invoke(new object[]{copied,data,120f,Activator.CreateInstance(cp[3].ParameterType)!,Activator.CreateInstance(cp[4].ParameterType)!,true,false});
                return copied;
            }).ToArray();
            var own=LocalMap.Load(new MapCase {Folder=folder,File="map.dat",Characteristic="Standard",Difficulty="Expert"},AnalysisFeatures.FirstNotes|AnalysisFeatures.Average);
            var target=own.Input.Notes.Single(n=>n.Time==1&&n.X==0);var nativeTarget=nativeNotes.Single(n=>n.time==1&&n.lineIndex==0);
            bool pass=own.Input.Notes.Length==nativeNotes.Length&&target.IsFake==fake;
            foreach(var note in own.Input.Notes)
            {
                var actual=nativeNotes.Single(n=>n.time==note.Time&&n.lineIndex==note.X);
                pass=pass&&note.Direction==(int)actual.cutDirection&&note.CutDirectionAngleOffset.HasValue&&Math.Abs(note.CutDirectionAngleOffset.Value-actual.cutDirectionAngleOffset)<1e-4;
            }
            if(angle.HasValue)pass=pass&&nativeTarget.cutDirectionAngleOffset==angle.Value&&(int)nativeTarget.cutDirection==(direction==8?8:1);
            if(fake)pass=pass&&(int)nativeTarget.scoringType==-1;
            if(!pass)failed++;
            results.Add(new {direction,angle,pair,fake,nativeDirection=(int)nativeTarget.cutDirection,nativeAngle=nativeTarget.cutDirectionAngleOffset,ownDirection=target.Direction,ownAngle=target.CutDirectionAngleOffset,status=pass?"pass":"mismatch"});
        }
        return new {schemaVersion=1,profileVersion=BeatInsight.Formats.LocalMap.AnalysisProfileVersion,cases,failed,referenceUsed=false,gameStarted=false,
            scope="installed loader row processing copied into real custom note objects with explicit fixture metadata, followed by installed NoodleNoteData constructor; canonical unmirrored v2 notes, runtime copy patches and full lifecycle not executed",results};
    }
}
