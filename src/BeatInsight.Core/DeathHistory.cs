using System;
using System.Collections.Generic;
using System.Linq;

namespace BeatInsight.Core;

public sealed class HistoryModifiers
{
    public int? LegacyParam { get; set; }
    public bool? NoBombs { get; set; }
    public string? Obstacles { get; set; }
    public string? SpeedPreset { get; set; }
    public double? SpeedMultiplier { get; set; }
    public Dictionary<string,string> Additional { get; set; } = new Dictionary<string,string>();

    // Preserve the complete mask, including unknown bits and missing legacy values.
    public static HistoryModifiers FromLegacy(int? mask)
    {
        if (!mask.HasValue) return new HistoryModifiers();
        int speed = mask.Value & (256 | 512 | 4096);
        return new HistoryModifiers {
            LegacyParam=mask, NoBombs=(mask.Value&16)!=0,
            Obstacles=(mask.Value&8)!=0?"none":"not-specified-by-legacy-mask",
            SpeedPreset=speed==0?"normal":speed==256?"faster":speed==512?"slower":speed==4096?"super-fast":"unknown-conflicting-flags"
        };
    }
}

public sealed class DeathRecord
{
    public string Id { get; set; } = "";
    public string MapKey { get; set; } = "";
    public string? BeatmapSha256 { get; set; }
    public long Date { get; set; }
    // Position on the ORIGINAL song timeline, never elapsed wall-clock time.
    public double SongTimeSeconds { get; set; }
    public HistoryModifiers? Modifiers { get; set; }
    public bool IsPractice { get; set; }
    // Absent in older own-history files; do not infer precision for those records.
    public string? TimingSource { get; set; }
}

public sealed class DeathMarker
{
    public string Source { get; set; } = "";
    public string Shape => "cross";
    public string ColorHex => Source=="legacy"?"#FFFF00":"#FF0000";
    public long Date { get; set; }
    public int? LastNote { get; set; }
    public double? SongTimeSeconds { get; set; }
    public double? NormalizedPosition { get; set; }
    public string PositionStatus { get; set; } = "";
    public string? TimingSource { get; set; }
    public HistoryModifiers Modifiers { get; set; } = new HistoryModifiers();
    public string MapVersionStatus {get;set;}="unknown";
}

public static class DeathHistory
{
    public static DeathRecord? Create(string mapKey, double songTimeSeconds, long date,
        HistoryModifiers modifiers, bool isPractice, bool failed, string? beatmapSha256=null)
    {
        if (isPractice || !failed) return null;
        if (string.IsNullOrWhiteSpace(mapKey)) throw new ArgumentException("Exact map key is required",nameof(mapKey));
        if (double.IsNaN(songTimeSeconds)||double.IsInfinity(songTimeSeconds)||songTimeSeconds<0) throw new ArgumentOutOfRangeException(nameof(songTimeSeconds));
        if (modifiers==null) throw new ArgumentNullException(nameof(modifiers));
        var snapshot=new HistoryModifiers {LegacyParam=modifiers.LegacyParam,NoBombs=modifiers.NoBombs,
            Obstacles=modifiers.Obstacles,SpeedPreset=modifiers.SpeedPreset,SpeedMultiplier=modifiers.SpeedMultiplier,
            Additional=new Dictionary<string,string>(modifiers.Additional)};
        return new DeathRecord { Id=Guid.NewGuid().ToString("N"),MapKey=mapKey,SongTimeSeconds=songTimeSeconds,Date=date,Modifiers=snapshot,BeatmapSha256=beatmapSha256 };
    }

    public static DeathMarker[] LegacyMarkers(IEnumerable<PlayRecord> records, MapInput input)
    {
        var times=input.Notes.Where(n=>n.ScoringType<=3).SelectMany(n=>Enumerable.Repeat(n.Time,Math.Max(1,n.SliceCount))).ToArray();
        return records.Where(r=>r.IsPractice!=true&&r.LastNote>0).OrderByDescending(r=>r.Date).Take(100)
            .Select(r=> {
                bool valid=r.LastNote<times.Length;
                double? time=valid?(double?)times[r.LastNote]:null;
                return new DeathMarker {Source="legacy",Date=r.Date,LastNote=r.LastNote,SongTimeSeconds=time,
                    NormalizedPosition=valid&&input.Duration>0?time/input.Duration:null,
                    PositionStatus=valid?"legacy-note-index-estimate":"invalid-index",MapVersionStatus="not-recorded",Modifiers=HistoryModifiers.FromLegacy(r.Param)};
            }).ToArray();
    }

    public static DeathMarker[] OwnMarkers(IEnumerable<DeathRecord> records, string mapKey, double duration, string? beatmapSha256=null)
        => records.Where(r=>!r.IsPractice&&r.MapKey==mapKey).OrderByDescending(r=>r.Date).Select(r=> {
            bool storedHash=!string.IsNullOrWhiteSpace(r.BeatmapSha256),currentHash=!string.IsNullOrWhiteSpace(beatmapSha256);
            bool stale=currentHash&&storedHash&&!string.Equals(beatmapSha256,r.BeatmapSha256,StringComparison.OrdinalIgnoreCase);
            bool valid=!stale&&!double.IsNaN(r.SongTimeSeconds)&&!double.IsInfinity(r.SongTimeSeconds)&&r.SongTimeSeconds>=0&&duration>0&&r.SongTimeSeconds<=duration;
            return new DeathMarker {Source="beatinsight",Date=r.Date,SongTimeSeconds=valid?(double?)r.SongTimeSeconds:null,
                NormalizedPosition=valid?(double?)(r.SongTimeSeconds/duration):null,
                PositionStatus=stale?"beatmap-changed":valid?(r.TimingSource=="completion-song-time-fallback"?"completion-song-time-estimate":"recorded-song-time"):"invalid-song-time",
                MapVersionStatus=!storedHash?"not-recorded":!currentHash?"not-checked":stale?"changed":"verified",
                TimingSource=r.TimingSource,Modifiers=r.Modifiers??new HistoryModifiers()};
        }).ToArray();
}
