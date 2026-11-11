using System;
using System.Collections.Generic;

namespace BeatInsight.Core;

public enum AttemptOutcome { Incomplete, Cleared, Failed }

// One instance per attempt. The game layer owns correlation with its scene/setup.
// Capture early, confirm the outcome later, and emit at most one record.
public sealed class DeathAttempt
{
    readonly string mapKey;
    readonly HistoryModifiers modifiers;
    readonly bool practice;
    readonly string? beatmapHash;
    double? failureTime;
    long failureDate;
    bool completed;

    public DeathAttempt(string mapKey, HistoryModifiers modifiers, bool practice, string? beatmapHash=null)
    {
        if(string.IsNullOrWhiteSpace(mapKey))throw new ArgumentException("Exact map key is required",nameof(mapKey));
        if(modifiers==null)throw new ArgumentNullException(nameof(modifiers));
        this.mapKey=mapKey;this.practice=practice;this.beatmapHash=beatmapHash;
        this.modifiers=new HistoryModifiers {LegacyParam=modifiers.LegacyParam,NoBombs=modifiers.NoBombs,
            Obstacles=modifiers.Obstacles,SpeedPreset=modifiers.SpeedPreset,SpeedMultiplier=modifiers.SpeedMultiplier,
            Additional=new Dictionary<string,string>(modifiers.Additional)};
    }

    static bool ValidTime(double time)=>!double.IsNaN(time)&&!double.IsInfinity(time)&&time>=0;

    public bool CaptureFailure(double songTimeSeconds,long unixMilliseconds)
    {
        if(practice||completed||failureTime.HasValue||!ValidTime(songTimeSeconds))return false;
        failureTime=songTimeSeconds;failureDate=unixMilliseconds;return true;
    }

    public DeathRecord? Complete(AttemptOutcome outcome,double completionSongTimeSeconds,long unixMilliseconds)
    {
        if(completed)return null;
        completed=true;
        // A restart/quit after an observed failure can carry Incomplete. A plain
        // restart/quit has no failure event and must never create a death record.
        if(practice||outcome==AttemptOutcome.Cleared||outcome!=AttemptOutcome.Failed&&!failureTime.HasValue)return null;
        double time=failureTime??completionSongTimeSeconds;
        if(!ValidTime(time))return null;
        var record=DeathHistory.Create(mapKey,time,failureTime.HasValue?failureDate:unixMilliseconds,
            modifiers,false,true,beatmapHash)!;
        record.TimingSource=failureTime.HasValue?"failure-song-time":"completion-song-time-fallback";
        return record;
    }
}
