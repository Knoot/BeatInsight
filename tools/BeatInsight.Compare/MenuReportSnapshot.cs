using BeatInsight.Core;
using BeatInsight.Formats;
using Newtonsoft.Json;

namespace BeatInsight.Compare;

static class MenuReportSnapshot
{
    internal static object Create(MapCase request)
    {
        try
        {
            int rank=request.Difficulty switch {"Easy"=>0,"Normal"=>1,"Hard"=>2,"Expert"=>3,"ExpertPlus"=>4,_=>throw new InvalidDataException("Unknown difficulty")};
            var result=SelectedMapAnalyzer.Load(new SelectedMapRequest("diagnostic",request.Characteristic,request.Difficulty,rank,
                LocalMap.SafePath(request.Folder,request.File),features:new MenuVisibility().Features));
            bool notes=result.NoteError==null,density=notes||result.IndependentDensity;
            return new{status=notes&&result.SquatError==null?"ready":"partial",mapSha256=result.BeatmapSha256,
                noteMetricsAvailable=notes,squatsAvailable=result.SquatCount.HasValue,densityAvailable=density,
                noteError=result.NoteError,squatError=result.SquatError,densityError=result.DensityError,independentDensity=result.IndependentDensity,
                metrics=notes?Comparison.Metrics(result.Corrected):null,
                median=notes?new{left=result.MedianLeft,right=result.MedianRight}:null,squats=result.SquatCount,
                density=density?new{unit=result.GraphUnit,values=result.Corrected.Segments,sha256=Comparison.Hash(JsonConvert.SerializeObject(result.Corrected.Segments))}:null,
                history="not-read",jumpSettings="not-supplied; jump-dependent squats require known settings",
                validation="selective-menu-analysis; partial data does not validate full map geometry or reference parity"};
        }
        catch(NotSupportedException e){return new{status="unsupported",error=e.Message};}
        catch(FileNotFoundException e){return new{status="unavailable",error=e.Message};}
        catch(DirectoryNotFoundException e){return new{status="unavailable",error=e.Message};}
        catch(Exception e){return new{status="error",error=e.ToString()};}
    }
}
