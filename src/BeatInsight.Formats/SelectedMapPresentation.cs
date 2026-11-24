using System.Globalization;
using BeatInsight.Core;

namespace BeatInsight.Formats;

public static class SelectedMapPresentation
{
    public static MenuRenderData Create(SelectedMapAnalysis? result,DrawColor left,DrawColor right)
    {
        if(result==null)return new MenuRenderData();
        string Pair(double a,double b,string format="0.##")=>MenuRenderData.Number(a,format)+" / "+MenuRenderData.Number(b,format);
        var r=result.Corrected;
        bool Has(AnalysisFeatures feature)=>(result.Features&feature)!=0;
        bool Notes(AnalysisFeatures feature)=>result.NoteError==null&&Has(feature);
        return new MenuRenderData {Average=Notes(AnalysisFeatures.Average)?Pair(r.LeftAverage,r.RightAverage,"0.0"):"—",Peak=Notes(AnalysisFeatures.Peak)?Pair(r.LeftPeak,r.RightPeak):"—",
            ImportedHistory=Has(AnalysisFeatures.ImportedDeaths)?HistoryOverview.Create("legacy",result.LegacyHistoryStatus,result.DeathMarkers):null,
            OwnHistory=Has(AnalysisFeatures.OwnDeaths)?HistoryOverview.Create("beatinsight",result.OwnHistoryStatus,result.DeathMarkers):null,
            Median=Notes(AnalysisFeatures.Median)?Pair(result.MedianLeft,result.MedianRight):"—",Squats=result.SquatCount?.ToString(CultureInfo.InvariantCulture)??"—",
            Left=result.NoteError==null?FirstNoteVisual.From(result.FirstLeftNote,left):null,Right=result.NoteError==null?FirstNoteVisual.From(result.FirstRightNote,right):null,
            Duration=result.Duration,Colors=result.NoteError==null||result.IndependentDensity?r.Colors:System.Array.Empty<float[]>(),Markers=result.DeathMarkers};
    }
}
