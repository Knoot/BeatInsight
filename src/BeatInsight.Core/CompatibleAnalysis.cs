using System;
using System.Linq;
using System.Collections.Generic;

namespace BeatInsight.Core;

// Independent implementation; the comparison tool executes original DLL IL separately.
public static class CompatibleAnalysis
{
    public static float NoteAngle(MapNote note,bool corrected=false)
    {
        if(!note.MappingExtensions||!MappingNotes.IsExtendedDirection(note.Direction))return Angle(note.Direction);
        float angle=MappingNotes.Angle(note.Direction);
        return corrected&&angle< -180?angle+360:angle;
    }
    public static float Angle(int direction) => direction switch
    {
        0 => -180f, 1 => 0f, 2 => -90f, 3 => 90f, 4 => -135f,
        5 => 135f, 6 => -45f, 7 => 45f, _ => 0f
    };
    public static AnalysisResult Analyze(MapInput input, bool reproduceKnownDefects = true, IEnumerable<PlayRecord>? history = null,AnalysisFeatures features=AnalysisFeatures.All)
    {
        bool bins=(features&(AnalysisFeatures.Average|AnalysisFeatures.Peak|AnalysisFeatures.Median))!=0;
        var notes = input.Notes.Where(n => reproduceKnownDefects?n.ScoringType<=3:!n.IsFake).ToArray();
        float end = notes.LastOrDefault()?.Time ?? 0;
        if(bins)foreach (var w in input.Walls)
        {
            bool central=reproduceKnownDefects||!w.InfluencesWindowEnd.HasValue
                ?w.X==1||w.X==2||w.Width>=2:w.InfluencesWindowEnd.Value;
            if((reproduceKnownDefects||!w.IsFake)&&central)end=Math.Max(end,w.Time+(!reproduceKnownDefects?w.CorrectedDuration??w.Duration:w.Duration));
        }
        int size = !bins||notes.Length == 0 ? 0 : checked((int)Math.Floor(end) + 1);
        if (size > 86400) throw new ArgumentOutOfRangeException(nameof(input), "Analysis exceeds 24 hours");
        var left = new int[size]; var right = new int[size];
        MapNote? prevLeft = null, prevRight = null;
        if(bins)foreach (var n in notes)
        {
            if (n.Time < 0 || n.Color < 0 || n.Color > 1) continue;
            if (n.Color == 0) Add(n, ref prevLeft, left,!reproduceKnownDefects); else Add(n, ref prevRight, right,!reproduceKnownDefects);
        }
        int[] total = (features&(AnalysisFeatures.Average|AnalysisFeatures.Peak))!=0?left.Zip(right, (a, b) => a + b).ToArray():Array.Empty<int>();
        var previewNotes=(features&(AnalysisFeatures.Density|AnalysisFeatures.ImportedDeaths))!=0?notes.Where(n=>!n.IsFake).ToArray():Array.Empty<MapNote>();
        var values = (features&AnalysisFeatures.Density)!=0?SegmentValues(previewNotes, input.Duration):Array.Empty<float>();
        var indices=(features&AnalysisFeatures.ImportedDeaths)!=0?(history??Enumerable.Empty<PlayRecord>()).Where(r=>r.LastNote>0).OrderByDescending(r=>r.Date).Take(100).Select(r=>r.LastNote).ToArray():Array.Empty<int>();
        var historyTimes=indices.Length>0?input.Notes.Where(n=>n.ScoringType<=3&&!n.IsFake).SelectMany(n=>Enumerable.Repeat(n.Time,Math.Max(1,n.SliceCount))).ToArray():Array.Empty<float>();
        var seconds=indices.Select(i=>i>=0&&i<historyTimes.Length?historyTimes[i]:0f).ToArray();
        return new AnalysisResult
        {
            LeftBins = left, RightBins = right,
            LeftAverage = (features&AnalysisFeatures.Average)!=0?Average(left):0, RightAverage = (features&AnalysisFeatures.Average)!=0?Average(right):0, TotalAverage = (features&AnalysisFeatures.Average)!=0?Average(total):0,
            LeftPeak = (features&AnalysisFeatures.Peak)!=0?left.DefaultIfEmpty().Max():0, RightPeak = (features&AnalysisFeatures.Peak)!=0?right.DefaultIfEmpty().Max():0, TotalPeak = (features&AnalysisFeatures.Peak)!=0?total.DefaultIfEmpty().Max():0,
            FirstLeft = (features&AnalysisFeatures.FirstNotes)!=0&&notes.FirstOrDefault(n => n.Color == 0) is MapNote firstLeft?NoteAngle(firstLeft,!reproduceKnownDefects):0,
            FirstRight = (features&AnalysisFeatures.FirstNotes)!=0&&notes.FirstOrDefault(n => n.Color == 1) is MapNote firstRight?NoteAngle(firstRight,!reproduceKnownDefects):0,
            Segments = values, Colors = values.Select(Color).ToArray(),
            HistoryIndices=indices,HistorySeconds=seconds,HistoryNormalized=seconds.Select(t=>input.Duration>0?t/input.Duration:0).ToArray()
        };
    }
    static float Average(int[] bins)
    {
        int count = 0, sum = 0;
        foreach (int n in bins) if (n > 0) { sum += n; count++; }
        return count == 0 ? 0 : (float)sum / count;
    }
    static void Add(MapNote n, ref MapNote? previous, int[] bins,bool corrected)
    {
        bool accepted = previous == null;
        if (previous != null)
        {
            float dt = n.Time - previous.Time;
            float angle = Math.Abs(NoteAngle(n,corrected) - NoteAngle(previous,corrected));
            if (angle >= 180) angle -= 180;
            accepted = dt > .06f || angle > 90f;
        }
        if (accepted) bins[(int)Math.Floor(n.Time)]++;
        previous = n;
    }
    public static float[] SegmentValues(MapNote[] notes, float duration)
    {
        if (duration <= 0 || notes.Length==0) return Array.Empty<float>();
        var values = new float[90];
        int index = 0;
        for (int segment = 0; segment < 90; segment++)
        {
            int count = 0;
            float end = (segment + 1) * duration / 90;
            while (index < notes.Length && notes[index].Time <= end) { count++; index++; }
            values[segment] = count / (duration / 90);
        }
        return values;
    }
    public static float[] Color(float value)
    {
        float[][] stops = {
            new[] {.5f,.5f,.5f,1f}, new[] {0f,1f,0f,1f}, new[] {1f,235f/255f,4f/255f,1f},
            new[] {1f,128f/255f,0f,1f}, new[] {1f,0f,0f,1f}, new[] {1f,0f,1f,1f}, new[] {128f/255f,0f,128f/255f,1f}
        };
        if (value >= 15) return stops[6];
        int slot = Math.Max(0, (int)(value / 2.5f));
        float t = Math.Max(0, Math.Min(1, (value - slot * 2.5f) / 2.5f));
        return stops[slot].Zip(stops[slot + 1], (a, b) => a + (b - a) * t).ToArray();
    }
}
