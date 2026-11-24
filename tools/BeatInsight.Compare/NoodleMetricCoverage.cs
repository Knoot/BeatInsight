using BeatInsight.Core;
using BeatInsight.Formats;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace BeatInsight.Compare;

static class NoodleMetricCoverage
{
    internal static object Run(string directory,string output,JumpSettingsSnapshot? jump)
    {
        const AnalysisFeatures notes=AnalysisFeatures.Average|AnalysisFeatures.Peak|AnalysisFeatures.Median|AnalysisFeatures.FirstNotes|AnalysisFeatures.Density;
        int errors=0;var results=new List<object>();
        foreach(var file in Directory.GetFiles(directory,"*.result.json").Order(StringComparer.Ordinal))
        {
            var saved=JObject.Parse(File.ReadAllText(file));var request=saved["request"]!.ToObject<MapCase>()!;
            int rank=request.Difficulty switch {"Easy"=>0,"Normal"=>1,"Hard"=>2,"Expert"=>3,"ExpertPlus"=>4,_=>throw new InvalidDataException("Unknown difficulty")};
            object Evaluate(AnalysisFeatures features)
            {
                try
                {
                    var analysis=SelectedMapAnalyzer.Load(new SelectedMapRequest("diagnostic",request.Characteristic,request.Difficulty,rank,
                        LocalMap.SafePath(request.Folder,request.File),features:features,jumpSettings:jump));
                    bool walls=features==AnalysisFeatures.Squats;
                    bool noteAvailable=analysis.NoteError==null;
                    return new{available=walls?analysis.SquatCount.HasValue:noteAvailable,error=walls?analysis.SquatError:analysis.NoteError,
                        densityAvailable=(features&AnalysisFeatures.Density)!=0&&(noteAvailable||analysis.IndependentDensity),
                        metrics=walls||!noteAvailable?null:Comparison.Metrics(analysis.Corrected),squats=analysis.SquatCount,
                        medianLeft=walls||!noteAvailable?(double?)null:analysis.MedianLeft,medianRight=walls||!noteAvailable?(double?)null:analysis.MedianRight};
                }
                catch(NotSupportedException e){return new{available=false,error=e.Message};}
                catch(FileNotFoundException e){return new{available=false,error=e.Message,unavailable=true};}
                catch(DirectoryNotFoundException e){return new{available=false,error=e.Message,unavailable=true};}
                catch(Exception e){errors++;return new{available=false,error=e.Message,unexpected=true};}
            }
            var noteResult=JObject.FromObject(Evaluate(notes));var squatResult=JObject.FromObject(Evaluate(AnalysisFeatures.Squats));
            bool n=(bool)noteResult["available"]!,s=(bool)squatResult["available"]!;
            bool d=(bool?)noteResult["densityAvailable"]==true;
            bool unavailable=(bool?)noteResult["unavailable"]==true||(bool?)squatResult["unavailable"]==true;
            results.Add(new{source=Path.GetFileName(file),status=unavailable?"unavailable":n?(s?"both":"notes-only"):(s?(d?"squats-and-density":"squats-only"):(d?"density-only":"unsupported")),notes=noteResult,squats=squatResult});
        }
        if(results.Count==0)throw new InvalidDataException("No bounded source cases");
        var rows=JArray.FromObject(results);
        File.WriteAllText(output,JsonConvert.SerializeObject(rows,Formatting.Indented));
        return new{schemaVersion=1,cases=results.Count,errors,statuses=rows.GroupBy(r=>(string)r["status"]!).ToDictionary(g=>g.Key,g=>g.Count()),jumpSettings=jump,
            scope="Read-only bounded availability by requested metric groups through current SelectedMapAnalyzer. Density availability is separate from validated note metrics; no history or game execution. Squats-only/density-only do not validate note geometry; both does not prove combined runtime behavior."};
    }
}
