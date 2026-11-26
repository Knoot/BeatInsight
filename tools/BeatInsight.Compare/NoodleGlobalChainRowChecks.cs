using BeatInsight.Core;
using BeatInsight.Formats;
using Newtonsoft.Json.Linq;

namespace BeatInsight.Compare;

static class NoodleGlobalChainRowChecks
{
    internal static object Run(string gameRoot,Func<JObject,float,BeatmapData> load,string inventory)
    {
        int cases=0,failed=0;var results=new List<object>();
        try
        {
            foreach(var item in JArray.Parse(File.ReadAllText(inventory)))
            {
                var selected=item.ToObject<MapCase>()!;string file=Path.Combine(selected.Folder,selected.File),json=File.ReadAllText(file),hash=Comparison.HashFile(file);
                var root=JObject.Parse(json);
                if(!Version.TryParse((string?)root["version"],out var version)||version.Major!=3||root["burstSliders"] is not JArray {Count:>0})throw new InvalidDataException("Global chain audit requires v3 chains");
                var own=LocalMap.Load(selected,AnalysisFeatures.All&~(AnalysisFeatures.Squats|AnalysisFeatures.Reference),includeSwingWindowWalls:false);
                var ownNotes=own.Input.Notes.Where(n=>!n.IsFake).ToArray();var expected=CompatibleAnalysis.Analyze(own.Input,reproduceKnownDefects:false);
                foreach(string composition in new[]{"noodle","mapping-first/postfix-first","mapping-first/postfix-last"})
                {
                    NoodleRowBridge.CombinedMapping=composition!="noodle";NoodleRowBridge.MappingFirst=true;NoodleRowBridge.MappingPostfixFirst=composition.EndsWith("postfix-first",StringComparison.Ordinal);
                    string? error=null;int? count=null;int differences=0;bool bins=false,graph=false,pass=false;object? evidence=null;
                    try
                    {
                        var projection=NoodleBurstProjection.Prepare(gameRoot,load(root,own.Bpm),own.Bpm,densityOnly:true);var native=projection.Notes;count=native.Length;evidence=projection.Evidence;
                        differences=Math.Abs(native.Length-ownNotes.Length);
                        for(int i=0;i<Math.Min(native.Length,ownNotes.Length);i++)
                        {
                            var a=native[i];var b=ownNotes[i];
                            if(Math.Abs(a.Time-b.Time)>.0001f||a.Color!=b.Color||a.Direction!=b.Direction||a.SliceCount!=b.SliceCount)differences++;
                        }
                        var actual=CompatibleAnalysis.Analyze(new MapInput{Duration=own.Input.Duration,Notes=native},reproduceKnownDefects:false);
                        bins=actual.LeftBins.SequenceEqual(expected.LeftBins)&&actual.RightBins.SequenceEqual(expected.RightBins);graph=actual.Segments.SequenceEqual(expected.Segments);
                        pass=differences==0&&bins&&graph&&hash==Comparison.HashFile(file)&&own.Json==json;
                    }
                    catch(Exception ex){error=ex.GetBaseException().GetType().Name+": "+ex.GetBaseException().Message;}
                    cases++;if(!pass)failed++;results.Add(new{name=Path.GetFileName(selected.Folder),selected.Characteristic,selected.Difficulty,composition,nativeGroups=count,ownGroups=ownNotes.Length,differences,bins,graph,evidence,error,pass});
                }
            }
        }
        finally{NoodleRowBridge.CombinedMapping=false;}
        return new{cases,failed,scope="Explicit row compositions plus controlled unpatched native slice/group projection; nominal groups, bins and graph only. No physical positions, angles, modded spawn patch chain or Unity lifecycle proof",results};
    }
}
