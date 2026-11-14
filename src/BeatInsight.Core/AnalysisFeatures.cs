using System;

namespace BeatInsight.Core;

[Flags]
public enum AnalysisFeatures
{
    None=0,Average=1,Peak=2,Median=4,FirstNotes=8,Density=16,Squats=32,
    ImportedDeaths=64,OwnDeaths=128,Reference=256,
    All=Average|Peak|Median|FirstNotes|Density|Squats|ImportedDeaths|OwnDeaths|Reference
}
