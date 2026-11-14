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
        return new MenuRenderData {Average=Has(AnalysisFeatures.Average)?Pair(r.LeftAverage,r.RightAverage,"0.0"):"—",Peak=Has(AnalysisFeatures.Peak)?Pair(r.LeftPeak,r.RightPeak):"—",
            Median=Has(AnalysisFeatures.Median)?Pair(result.MedianLeft,result.MedianRight):"—",Squats=result.SquatCount?.ToString(CultureInfo.InvariantCulture)??"—",
            Left=FirstNoteVisual.From(result.FirstLeftNote,left),Right=FirstNoteVisual.From(result.FirstRightNote,right),
            Duration=result.Duration,Colors=r.Colors,Markers=result.DeathMarkers};
    }
}
