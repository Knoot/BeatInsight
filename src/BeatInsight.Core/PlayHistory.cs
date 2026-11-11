using System;
using System.Collections.Generic;
using System.Linq;

namespace BeatInsight.Core;

public sealed class PlayRecord
{
    public long Date { get; set; }
    public int ModifiedScore { get; set; }
    public int RawScore { get; set; }
    public int LastNote { get; set; }
    public int? Param { get; set; }
    public bool? IsPractice { get; set; }
}

public sealed class HistoryMark
{
    public int LastNote { get; set; }
    public int Attempts { get; set; }
    public int?[] ModifierMasks { get; set; } = Array.Empty<int?>();
}

public static class PlayHistory
{
    public static string Key(string levelId, int difficulty, string characteristic)
        => $"{levelId}___{difficulty}___{characteristic}";

    // Independent aggregation. Retains modifier masks and never invents seconds from an index.
    public static HistoryMark[] Aggregate(IEnumerable<PlayRecord> records)
        => records.Where(r => r.IsPractice != true && r.LastNote >= 0).GroupBy(r => r.LastNote)
            .OrderBy(g => g.Key).Select(g => new HistoryMark
            {
                LastNote = g.Key, Attempts = g.Count(),
                ModifierMasks = g.Select(r => r.Param).Distinct().OrderBy(x => x).ToArray()
            }).ToArray();
}
