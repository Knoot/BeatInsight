using System.Threading;
using BeatInsight.Core;
using BeatInsight.Formats;
using Newtonsoft.Json.Linq;

namespace BeatInsight.Compare;

static class MenuDataChecks
{
    public static object Run(OriginalOracle oracle,string gameRoot,string mapsRoot,string manifest)
    {
        var results=new List<object>();int failed=0,selectiveCases=0;
        foreach(var row in JObject.Parse(File.ReadAllText(manifest))["maps"]!)
        {
            string folder=Path.Combine(mapsRoot,(string)row["Folder"]!);
            var test=LocalMap.Cases(folder).Single(c=>c.Characteristic==(string?)row["Characteristic"]&&c.Difficulty==(string?)row["Difficulty"]&&
                (row["File"]==null||c.File==(string?)row["File"]));
            int rank=Array.IndexOf(new[]{"Easy","Normal","Hard","Expert","ExpertPlus"},test.Difficulty);
            var request=new SelectedMapRequest("synthetic-menu-identity-"+results.Count,test.Characteristic,test.Difficulty,rank,LocalMap.SafePath(folder,test.File));
            var snapshot=SelectedMapAnalyzer.Load(request,CancellationToken.None);
            var map=LocalMap.Load(test);
            var game=GameLoader.Load(gameRoot,map.Json,map.Bpm,map.AudioJson);
            var original=oracle.Analyze(map.Input,game);
            var differences=Comparison.Diff(original,snapshot.Compatible);
            // Oracle fixtures always inject synthetic attempts. This pass covers
            // map outputs; history import/selection is exercised by MenuChecks.
            foreach(string field in differences.Keys.Where(k=>k.StartsWith("History",StringComparison.Ordinal)).ToArray())differences.Remove(field);
            var corrected=CompatibleAnalysis.Analyze(map.Input,reproduceKnownDefects:false);
            foreach(var difference in Comparison.Diff(corrected,snapshot.Corrected))differences["corrected."+difference.Key]=difference.Value;
            if(snapshot.MedianLeft!=WindowStatistics.ActiveWindowMedian(corrected.LeftBins)||snapshot.MedianRight!=WindowStatistics.ActiveWindowMedian(corrected.RightBins))
                differences["median"]="UI median differs from active per-hand policy";
            if(snapshot.SquatCount!=BeatInsight.WallAlert.WallAlertAdapter.Count(map.Json,map.Bpm))differences["squats"]="UI squat count differs from reference analyzer";
            foreach(var first in new[]{snapshot.FirstLeftNote,snapshot.FirstRightNote})
            {
                if(first?.CutDirectionAngleOffset==null)continue;
                var native=OracleBridge.GetItems(game).OfType<NoteData>().FirstOrDefault(n=>(int)n.colorType==first.Color&&n.lineIndex==first.X&&(int)n.noteLineLayer==first.Y&&
                    (int)n.cutDirection==first.Direction&&Math.Abs(n.time-first.Time)<=1e-5);
                if(native==null||Math.Abs(native.cutDirectionAngleOffset-first.CutDirectionAngleOffset.Value)>1e-5)
                    differences["firstNoteOffset."+first.Color]="Declared first cube rotation differs from installed game data";
            }
            if(snapshot.LegacyHistoryStatus!="not-requested"||snapshot.OwnHistoryStatus!="not-requested"||snapshot.DeathMarkers.Length!=0)
                differences["history"]="Unexpected history access in map-only fixture";
            foreach(var features in new[]{AnalysisFeatures.Average,AnalysisFeatures.Peak,AnalysisFeatures.Median,AnalysisFeatures.FirstNotes,
                AnalysisFeatures.Density,AnalysisFeatures.Squats,AnalysisFeatures.ImportedDeaths,AnalysisFeatures.OwnDeaths,
                AnalysisFeatures.Average|AnalysisFeatures.Peak|AnalysisFeatures.Median,AnalysisFeatures.All&~AnalysisFeatures.Reference})
            {
                var partial=SelectedMapAnalyzer.Load(new SelectedMapRequest(request.LevelId,request.Characteristic,request.Difficulty,rank,request.BeatmapFile,features:features));
                bool Has(AnalysisFeatures feature)=>(features&feature)!=0;
                bool equal=(!Has(AnalysisFeatures.Average)||partial.Corrected.LeftAverage==snapshot.Corrected.LeftAverage&&partial.Corrected.RightAverage==snapshot.Corrected.RightAverage)
                    &&(!Has(AnalysisFeatures.Peak)||partial.Corrected.LeftPeak==snapshot.Corrected.LeftPeak&&partial.Corrected.RightPeak==snapshot.Corrected.RightPeak)
                    &&(!Has(AnalysisFeatures.Median)||partial.MedianLeft==snapshot.MedianLeft&&partial.MedianRight==snapshot.MedianRight)
                    &&(!Has(AnalysisFeatures.Density)||JToken.DeepEquals(JToken.FromObject(partial.Corrected.Colors),JToken.FromObject(snapshot.Corrected.Colors)))
                    &&(!Has(AnalysisFeatures.FirstNotes)||JToken.DeepEquals(JToken.FromObject(new{partial.FirstLeftNote,partial.FirstRightNote}),JToken.FromObject(new{snapshot.FirstLeftNote,snapshot.FirstRightNote})))
                    &&(!Has(AnalysisFeatures.Squats)||partial.SquatCount==snapshot.SquatCount)
                    &&(!Has(AnalysisFeatures.OwnDeaths)||partial.BeatmapSha256==snapshot.BeatmapSha256);
                if(!equal)differences["selective."+features]="Selective visible outputs differ from full analysis";
                selectiveCases++;
            }
            if(differences.Count>0)failed++;
            results.Add(new {map=Path.GetFileName(folder),test.Characteristic,test.Difficulty,snapshot.SquatCount,status=differences.Count==0?"pass":"mismatch",differences});
        }
        var orientation=OrientationChecks.Run(gameRoot,out int orientationFailures);
        if(orientationFailures>0)failed++;
        return new {schemaVersion=1,profileVersion=3,scope="menu data pipeline; map outputs compared with original IL; history tested separately on synthetic fixtures",
            cases=results.Count,selectiveCases,failed,gameStarted=false,userHistoryRead=false,results,orientation};
    }
}
