using System;
namespace BeatInsight.Core;

public sealed class MapNote
{
    public float Time { get; set; }
    public float Beat { get; set; }
    public int X { get; set; }
    public int Y { get; set; }
    public int Color { get; set; }
    public int Direction { get; set; }
    public float? CutDirectionAngleOffset { get; set; }
    public int ScoringType { get; set; } = 1;
    public int SliceCount { get; set; } = 1;
}
public sealed class MapWall
{
    public float Time { get; set; }
    public float Beat { get; set; }
    public float EndBeat { get; set; }
    public float Duration { get; set; }
    public int X { get; set; }
    public int Y { get; set; }
    public int Width { get; set; }
    public int Height { get; set; }
}
public sealed class MapInput
{
    public MapNote[] Notes { get; set; } = Array.Empty<MapNote>();
    public MapWall[] Walls { get; set; } = Array.Empty<MapWall>();
    public float Duration { get; set; }
}
public sealed class AnalysisResult
{
    public int[] LeftBins { get; set; } = Array.Empty<int>();
    public int[] RightBins { get; set; } = Array.Empty<int>();
    public float LeftAverage { get; set; }
    public float RightAverage { get; set; }
    public float TotalAverage { get; set; }
    public int LeftPeak { get; set; }
    public int RightPeak { get; set; }
    public int TotalPeak { get; set; }
    public float FirstLeft { get; set; }
    public float FirstRight { get; set; }
    public float[] Segments { get; set; } = Array.Empty<float>();
    public float[][] Colors { get; set; } = Array.Empty<float[]>();
    public int[] HistoryIndices { get; set; } = Array.Empty<int>();
    public float[] HistorySeconds { get; set; } = Array.Empty<float>();
    public float[] HistoryNormalized { get; set; } = Array.Empty<float>();
}
