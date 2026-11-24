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
    readonly bool noFail;
    double? zeroEnergyTime;
    long zeroEnergyDate;
    string? beatmapHash;
    bool hashFrozen;
    double? failureTime;
    long failureDate;
    bool completed;
    bool sought;
    bool recordingSuppressed;
    public void SuppressRecording(){if(!completed){recordingSuppressed=true;failureTime=null;zeroEnergyTime=null;}}
    public string? ExclusionReason {get;private set;}
    public void FreezeBeatmapHash()=>hashFrozen=true;
    public bool PrepareBeatmapHash(string? hash)
    {
        if(hashFrozen||completed||failureTime.HasValue||zeroEnergyTime.HasValue)return false;
        beatmapHash=hash;return true;
    }
    public bool ExcludeAfterSeek()
    {
        if(completed||sought)return false;
        sought=true;failureTime=null;zeroEnergyTime=null;return true;
    }

    public DeathAttempt(string mapKey, HistoryModifiers modifiers, bool practice, string? beatmapHash=null)
    {
        if(string.IsNullOrWhiteSpace(mapKey))throw new ArgumentException("Exact map key is required",nameof(mapKey));
        if(modifiers==null)throw new ArgumentNullException(nameof(modifiers));
        this.mapKey=mapKey;this.practice=practice;this.beatmapHash=beatmapHash;
        noFail=modifiers.Additional.TryGetValue("noFailOn0Energy",out var flag)&&bool.TryParse(flag,out var value)&&value;
        this.modifiers=new HistoryModifiers {LegacyParam=modifiers.LegacyParam,NoBombs=modifiers.NoBombs,
            Obstacles=modifiers.Obstacles,SpeedPreset=modifiers.SpeedPreset,SpeedMultiplier=modifiers.SpeedMultiplier,
            Additional=new Dictionary<string,string>(modifiers.Additional)};
    }

    static bool ValidTime(double time)=>!double.IsNaN(time)&&!double.IsInfinity(time)&&time>=0;

    public bool CaptureFailure(double songTimeSeconds,long unixMilliseconds)
    {
        if(practice||sought||recordingSuppressed||completed||failureTime.HasValue||!ValidTime(songTimeSeconds))return false;
        hashFrozen=true;
        failureTime=songTimeSeconds;failureDate=unixMilliseconds;return true;
    }

    public bool CaptureZeroEnergy(double songTimeSeconds,long unixMilliseconds)
    {
        if(!noFail||practice||sought||recordingSuppressed||completed||zeroEnergyTime.HasValue||!ValidTime(songTimeSeconds))return false;
        hashFrozen=true;zeroEnergyTime=songTimeSeconds;zeroEnergyDate=unixMilliseconds;return true;
    }

    public DeathRecord? Complete(AttemptOutcome outcome,double completionSongTimeSeconds,long unixMilliseconds)
    {
        if(completed)return null;
        completed=true;
        // A restart/quit after an observed failure can carry Incomplete. A plain
        // restart/quit has no failure event and must never create a death record.
        if(practice){ExclusionReason="practice";return null;}
        if(recordingSuppressed){ExclusionReason="recording-disabled";return null;}
        if(sought){ExclusionReason="song-seek";return null;}
        if(noFail)
        {
            if(outcome!=AttemptOutcome.Cleared){ExclusionReason="no-fail-not-cleared";return null;}
            if(!zeroEnergyTime.HasValue){ExclusionReason="no-observed-zero-energy";return null;}
            var noFailRecord=DeathHistory.Create(mapKey,zeroEnergyTime.Value,zeroEnergyDate,
                modifiers,false,true,beatmapHash)!;
            noFailRecord.TimingSource="zero-energy-song-time";
            return noFailRecord;
        }
        if(outcome==AttemptOutcome.Cleared){ExclusionReason="cleared";return null;}
        // Modified completion results are not evidence of a real failure.
        if(!failureTime.HasValue){ExclusionReason="no-observed-failure";return null;}
        double time=failureTime.Value;
        var record=DeathHistory.Create(mapKey,time,failureDate,
            modifiers,false,true,beatmapHash)!;
        record.TimingSource="failure-song-time";
        return record;
    }
}
