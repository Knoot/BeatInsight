namespace BeatInsight.Core;

public enum DrawGroup { None, Average, Peak, Median, Squats, FirstNotes, Density, ImportedDeaths, OwnDeaths }

public sealed class MenuVisibility
{
    public bool Average=true,Peak=true,Median=true,Squats=true,FirstNotes=true,Density=true,ImportedDeaths=true,OwnDeaths=true;
    public bool HasStatistics=>Average||Peak||Median||Squats||FirstNotes;
    public bool HasTimeline=>Density||ImportedDeaths||OwnDeaths;
    public AnalysisFeatures Features=>
        (Average?AnalysisFeatures.Average:0)|(Peak?AnalysisFeatures.Peak:0)|(Median?AnalysisFeatures.Median:0)|
        (FirstNotes?AnalysisFeatures.FirstNotes:0)|(Density?AnalysisFeatures.Density:0)|(Squats?AnalysisFeatures.Squats:0)|
        (ImportedDeaths?AnalysisFeatures.ImportedDeaths:0)|(OwnDeaths?AnalysisFeatures.OwnDeaths:0);
    public bool Includes(DrawGroup group)=>group switch {
        DrawGroup.Average=>Average,DrawGroup.Peak=>Peak,DrawGroup.Median=>Median,DrawGroup.Squats=>Squats,
        DrawGroup.FirstNotes=>FirstNotes,DrawGroup.Density=>Density,DrawGroup.ImportedDeaths=>ImportedDeaths,DrawGroup.OwnDeaths=>OwnDeaths,_=>true};
    public void Apply(Drawing drawing)
    {
        drawing.Triangles.RemoveAll(t=>!Includes(t.Group));
        drawing.Labels.RemoveAll(l=>!Includes(l.Group));
    }
}
