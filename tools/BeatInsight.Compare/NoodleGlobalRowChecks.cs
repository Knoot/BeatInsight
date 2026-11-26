using System.Collections;
using BeatInsight.Core;
using BeatInsight.Formats;
using Newtonsoft.Json.Linq;

namespace BeatInsight.Compare;

// This compares explicit row patch compositions, not the live Harmony registry.
static class NoodleGlobalRowChecks
{
    internal static object Run(Func<JObject,float,BeatmapData> load,string inventory)
    {
        int cases=0,failed=0;var results=new List<object>();
        try
        {
            foreach(var item in JArray.Parse(File.ReadAllText(inventory)))
            {
                var selected=item.ToObject<MapCase>()!;
                string file=Path.Combine(selected.Folder,selected.File),json=File.ReadAllText(file),hash=Comparison.HashFile(file);
                var root=JObject.Parse(json);
                if(!Version.TryParse((string?)root["version"],out var version)||version.Major!=3||root["bombNotes"] is JArray {Count:>0}||root["burstSliders"] is JArray {Count:>0})throw new InvalidDataException("Global row audit is limited to v3 colored notes/arcs without real bombs/chains");
                var own=LocalMap.Load(selected,AnalysisFeatures.All&~(AnalysisFeatures.Squats|AnalysisFeatures.Reference),includeSwingWindowWalls:false);
                var ownNotes=own.Input.Notes.Where(n=>!n.IsFake).ToArray();
                var expected=CompatibleAnalysis.Analyze(own.Input,reproduceKnownDefects:false);
                foreach(string composition in new[]{"noodle","mapping-first/postfix-first","mapping-first/postfix-last"})
                {
                    NoodleRowBridge.CombinedMapping=composition!="noodle";NoodleRowBridge.MappingFirst=true;NoodleRowBridge.MappingPostfixFirst=composition.EndsWith("postfix-first",StringComparison.Ordinal);
                    var native=OracleBridge.GetItems(load(root,own.Bpm)).OfType<NoteData>()
                        .Where(n=>((IDictionary)n.GetType().GetProperty("customData")!.GetValue(n)!)["NE_fake"] is not true&&(int)n.scoringType>0&&(int)n.scoringType!=4&&(int)n.scoringType!=7).ToArray();
                    var input=new MapInput{Duration=own.Input.Duration,Notes=native.Select(n=>new MapNote{Time=n.time,X=n.lineIndex,Y=(int)n.noteLineLayer,Color=(int)n.colorType,Direction=(int)n.cutDirection,ScoringType=(int)n.scoringType}).ToArray()};
                    var actual=CompatibleAnalysis.Analyze(input,reproduceKnownDefects:false);
                    int roleDifferences=0,angleDifferences=0,knownAngles=0,inputDifferences=Math.Abs(native.Length-ownNotes.Length);
                    for(int i=0;i<Math.Min(native.Length,ownNotes.Length);i++)
                    {
                        var a=native[i];var b=ownNotes[i];
                        if(Math.Abs(a.time-b.Time)>.0001f||a.lineIndex!=b.X||(int)a.noteLineLayer!=b.Y||(int)a.colorType!=b.Color||(int)a.cutDirection!=b.Direction)inputDifferences++;
                        if((int)a.scoringType!=b.ScoringType)roleDifferences++;
                        if(b.CutDirectionAngleOffset.HasValue){knownAngles++;if(Math.Abs(a.cutDirectionAngleOffset-b.CutDirectionAngleOffset.Value)>.0001f)angleDifferences++;}
                    }
                    bool bins=actual.LeftBins.SequenceEqual(expected.LeftBins)&&actual.RightBins.SequenceEqual(expected.RightBins),graph=actual.Segments.SequenceEqual(expected.Segments);
                    bool sourceUnchanged=hash==Comparison.HashFile(file)&&own.Json==json;
                    bool pass=inputDifferences==0&&angleDifferences==0&&bins&&graph&&sourceUnchanged;
                    cases++;if(!pass)failed++;
                    results.Add(new{name=Path.GetFileName(selected.Folder),selected.Characteristic,selected.Difficulty,composition,nativeNotes=native.Length,ownNotes=ownNotes.Length,
                        inputDifferences,roleDifferences,knownAngles,angleDifferences,bins,graph,sourceUnchanged,pass});
                }
            }
        }
        finally{NoodleRowBridge.CombinedMapping=false;}
        return new{cases,failed,scope="Explicit actual row compositions; corrected bins/graph/known base angles only. Role differences are reported, not asserted as full input parity. No live patch order or physical pose proof",results};
    }
}
