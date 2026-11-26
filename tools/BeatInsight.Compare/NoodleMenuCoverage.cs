using BeatInsight.Core;
using BeatInsight.Formats;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace BeatInsight.Compare;

static class NoodleMenuCoverage
{
    public static object Run(string directory,string output,JumpSettingsSnapshot? jumpSettings=null)
    {
        var results=new List<object>();int errors=0,regressions=0,baselineCases=0,newlyAvailable=0,existingPartial=0;
        foreach(string path in Directory.GetFiles(directory,"*.result.json").Order(StringComparer.Ordinal))
        {
            var previous=JObject.Parse(File.ReadAllText(path));
            var request=previous["request"]!.ToObject<MapCase>()!;
            string source=Path.GetFileName(path);string? mapSha256=null,infoSha256=null;float? duration=null;string? status=null,error=null,squatError=null,noteError=null,previousMenuStatus=null,densityError=null;object? metrics=null;double? medianLeft=null,medianRight=null;int? squats=null;
            bool independentDensity=false;int densitySegments=0;
            float[]? densityValues=null;string? densitySha256=null;
            var differences=new List<string>();bool baseline=(string?)previous["status"]=="analyzed";
            if(baseline)baselineCases++;
            try
            {
                string file=LocalMap.SafePath(request.Folder,request.File);
                string hash=mapSha256=Comparison.Hash(File.ReadAllText(file));
                var analysis=SelectedMapAnalyzer.Load(new SelectedMapRequest("diagnostic",request.Characteristic,request.Difficulty,
                    request.Difficulty switch {"Easy"=>0,"Normal"=>1,"Hard"=>2,"Expert"=>3,"ExpertPlus"=>4,_=>throw new InvalidDataException("Unknown difficulty")},
                    file,features:new MenuVisibility().Features,jumpSettings:jumpSettings));
                duration=analysis.Duration;squatError=analysis.SquatError;noteError=analysis.NoteError;squats=analysis.SquatCount;
                independentDensity=analysis.IndependentDensity;densityError=analysis.DensityError;densitySegments=analysis.Corrected.Segments.Length;
                if(noteError==null||independentDensity)
                {densityValues=analysis.Corrected.Segments;densitySha256=Comparison.Hash(JsonConvert.SerializeObject(densityValues));}
                if(noteError==null){metrics=Comparison.Metrics(analysis.Corrected);medianLeft=analysis.MedianLeft;medianRight=analysis.MedianRight;}
                status=squatError==null&&noteError==null?"ready":"partial";
                if(status=="partial")
                {
                    try{LocalMap.Load(request,new MenuVisibility().Features);previousMenuStatus="partial";existingPartial++;}
                    catch(NotSupportedException){previousMenuStatus="unsupported";newlyAvailable++;}
                }
                if(baseline)
                {
                    if((string?)previous["mapSha256"]!=hash)differences.Add("map hash");
                    // Compare serialized snapshots at the same float precision as
                    // the prior JSON report, rather than boxed float vs double.
                    if(!JToken.DeepEquals(previous["corrected"],JToken.Parse(JsonConvert.SerializeObject(metrics))))differences.Add("corrected metrics");
                    if((double?)previous["median"]?["left"]!=medianLeft||(double?)previous["median"]?["right"]!=medianRight)differences.Add("median");
                    if((int?)previous["squats"]?["SquatCount"]!=squats||squatError!=null)differences.Add("squats");
                }
            }
            catch(NotSupportedException e){status="unsupported";error=e.Message;if(baseline)differences.Add("previously analyzed map rejected");}
            catch(FileNotFoundException e){status="unavailable";error=e.Message;}
            catch(DirectoryNotFoundException e){status="unavailable";error=e.Message;}
            catch(Exception e){status="error";error=e.Message;errors++;}
            if(differences.Count>0)regressions++;
            // Fingerprint metadata independently of status; an unreadable context
            // must not reclassify the actual menu result or validate later parity.
            if(mapSha256!=null)try{infoSha256=Comparison.Hash(LocalMap.Info(request.Folder).ToString(Formatting.None));}
            catch(Exception e) when(e is IOException or UnauthorizedAccessException or JsonException or InvalidOperationException){}
            results.Add(new{source,status,mapSha256,analysisContext=new{features=(int)new MenuVisibility().Features,jump=jumpSettings?.CacheIdentity??"unknown",
                request.Characteristic,request.Difficulty,request.File,infoSha256,duration},previousStatus=(string?)previous["status"],previousMenuStatus,metrics,medianLeft,medianRight,squats,squatError,noteError,independentDensity,densityError,densitySegments,densitySha256,densityValues,error,differences});
        }
        if(results.Count==0)throw new InvalidDataException("No bounded source cases");
        File.WriteAllText(output,JsonConvert.SerializeObject(results,Formatting.Indented));
        var rows=JArray.FromObject(results);
        var statuses=rows.GroupBy(r=>(string)r["status"]!).ToDictionary(g=>g.Key,g=>g.Count());
        var available=rows.Where(r=>(string?)r["status"] is "ready" or "partial").ToArray();
        var availability=new{noteMetrics=available.Count(r=>r["metrics"]?.Type==JTokenType.Object),
            squats=available.Count(r=>r["squats"]?.Type==JTokenType.Integer),
            density=available.Count(r=>r["densitySha256"]?.Type==JTokenType.String),
            independentDensity=available.Count(r=>(bool?)r["independentDensity"]==true)};
        var limitations=new Dictionary<string,object>();
        foreach(string field in new[]{"noteError","squatError","densityError"})
        {
            string Reason(JToken row)
            {
                string reason=(string)row[field]!;int path=reason.IndexOf('(');
                return(path<0?reason:reason.Substring(0,path)).Trim();
            }
            limitations[field]=available.Where(r=>r[field]?.Type==JTokenType.String)
                .GroupBy(Reason).Select(g=>new{reason=g.Key,cases=g.Count(),sources=g.Select(r=>(string)r["source"]!).ToArray()})
                .OrderByDescending(g=>g.cases).ThenBy(g=>g.reason,StringComparer.Ordinal).ToArray();
        }
        return new{schemaVersion=1,profileVersion=BeatInsight.Formats.LocalMap.AnalysisProfileVersion,cases=results.Count,errors,regressions,baselineCases,newlyAvailable,existingPartial,statuses,availability,limitations,
            unavailableSources=rows.Where(r=>(string?)r["status"]=="unavailable").Select(r=>(string)r["source"]!).ToArray(),jumpSettings,
            scope="bounded prior case inventory; actual menu feature path with null history files; partial preserves independently validated notes, squats or nominal density and masks unsupported metrics; missing files unavailable, not analysis failures; no game or reference execution, no full-library run"};
    }
}
