using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using System.Threading.Tasks;
using BeatInsight.Core;
using BeatInsight.Formats;

namespace BeatInsight.Mod;

internal static class RuntimeCapture
{
    sealed class Session
    {
        public StandardLevelScenesTransitionSetupDataSO Setup=null!;
        public DeathAttempt Attempt=null!;
    }
    static Session? current;
    static Func<Session>? createSession;
    static AttemptFingerprint? fingerprint;
    static bool restartPending;
    static bool enabled;
    static string historyPath="";
    static readonly Dictionary<StandardLevelGameplayManager,(Action Failure,AudioTimeSyncController Clock)> subscriptions=new();
    static readonly object writerLock=new object();
    static Task pendingWrite=Task.CompletedTask;
    internal static event Action? HistoryWritten;
    static ConditionalWeakTable<LevelCompletionResults,object> finishedResults=new ConditionalWeakTable<LevelCompletionResults,object>();

    internal static void VerifyGameBuild()
    {
        foreach(var pair in new[] {
            (typeof(StandardLevelGameplayManager).Assembly.Location,"A276C8A50ACFAD69A3DD3A82CDE08571C3262344702B9B014C89756771B4495E"),
            (typeof(BeatmapKey).Assembly.Location,"B95112A1A581F52070195B419600591C98ADFB5E225DCB49C819F69B61C703B8"),
            (typeof(GameplayModifiers).Assembly.Location,"C45267116BB2AE61104E994AC24E9A86378055A3FEDB62B6872EEE51C2FA8C7C")})
        {
            using var hash=SHA256.Create();
            string actual=BitConverter.ToString(hash.ComputeHash(File.ReadAllBytes(pair.Item1))).Replace("-","");
            if(actual!=pair.Item2)throw new InvalidOperationException("Unsupported game assembly: "+Path.GetFileName(pair.Item1));
        }
    }

    internal static void Enable()
    {
        historyPath=Path.Combine(IPA.Utilities.UnityGame.UserDataPath,DeathHistoryStore.FileName);
        enabled=true;
        Plugin.Log.Info("Own history: "+historyPath);
    }

    internal static void Begin(StandardLevelScenesTransitionSetupDataSO setup,PlayerSpecificSettings playerSettings)
    {
        if(!enabled)return;
        // Never reuse an attempt or a manager's failure handler across scene setups.
        DetachAll();current=null;createSession=null;fingerprint=null;restartPending=false;
        if(!Plugin.Config.RecordDeaths)Plugin.Log.Debug("Death recording disabled by settings.");
        var key=setup.beatmapKey;
        if(string.IsNullOrEmpty(key.levelId)||key.beatmapCharacteristic==null)return;
        var m=setup.gameplayModifiers;
        if(m==null)return;
        var modifiers=new HistoryModifiers {NoBombs=m.noBombs,
            Obstacles=m.enabledObstacleType switch {GameplayModifiers.EnabledObstacleType.All=>"all",GameplayModifiers.EnabledObstacleType.FullHeightOnly=>"full-height-only",GameplayModifiers.EnabledObstacleType.NoObstacles=>"none",_=>"unknown"},
            SpeedPreset=m.songSpeed switch {GameplayModifiers.SongSpeed.Normal=>"normal",GameplayModifiers.SongSpeed.Faster=>"faster",GameplayModifiers.SongSpeed.Slower=>"slower",GameplayModifiers.SongSpeed.SuperFast=>"super-fast",_=>"unknown"},SpeedMultiplier=m.songSpeedMul,
            Additional=new Dictionary<string,string> {
                ["energyType"]=m.energyType.ToString(),["noFailOn0Energy"]=m.noFailOn0Energy.ToString(),
                ["instaFail"]=m.instaFail.ToString(),["failOnSaberClash"]=m.failOnSaberClash.ToString(),
                ["fastNotes"]=m.fastNotes.ToString(),["strictAngles"]=m.strictAngles.ToString(),
                ["disappearingArrows"]=m.disappearingArrows.ToString(),["ghostNotes"]=m.ghostNotes.ToString(),
                ["noArrows"]=m.noArrows.ToString(),["proMode"]=m.proMode.ToString(),
                ["zenMode"]=m.zenMode.ToString(),["smallCubes"]=m.smallCubes.ToString(),
                ["leftHanded"]=playerSettings.leftHanded.ToString(),["autoRestart"]=playerSettings.autoRestart.ToString()
            }};
        string mapKey=PlayHistory.Key(key.levelId,(int)key.difficulty,key.beatmapCharacteristic.serializedName);
        fingerprint=new AttemptFingerprint(mapKey);
        bool practice=setup.practiceSettings!=null;
        createSession=()=> {
            var attempt=new DeathAttempt(mapKey,modifiers,practice,fingerprint?.Hash);
            if(!Plugin.Config.RecordDeaths)attempt.SuppressRecording();
            return new Session {Setup=setup,Attempt=attempt};
        };
        current=createSession();
        Plugin.Log.Debug("Death capture started: "+mapKey+"; practice="+practice);
    }

    internal static void PrepareFingerprint(GameplayCoreSceneSetupData data)
    {
        if(!enabled||fingerprint==null||createSession==null||!Plugin.Config.RecordDeaths||data.practiceSettings!=null||subscriptions.Count>0)return;
        var key=data.beatmapKey;
        if(key.beatmapCharacteristic==null)return;
        string mapKey=PlayHistory.Key(key.levelId,(int)key.difficulty,key.beatmapCharacteristic.serializedName);
        if(mapKey!=fingerprint.MapKey)return;
        var loaded=data.beatmapLevelData;
        try
        {
            if(loaded==null){fingerprint.Invalidate();Plugin.Log.Warn("Loaded beatmap data unavailable; death fingerprint is unknown.");return;}
            fingerprint.Prepare(mapKey,loaded,()=>loaded.GetBeatmapString(in key)??throw new InvalidDataException("Loaded beatmap JSON is unavailable"));
            Plugin.Log.Debug("Death fingerprint prepared: "+mapKey);
        }
        finally{current?.Attempt.PrepareBeatmapHash(fingerprint.Hash);}
    }

    internal static void Attach(StandardLevelGameplayManager manager,AudioTimeSyncController clock)
    {
        if(!enabled||subscriptions.ContainsKey(manager))return;
        // The game's Restart path reuses the transition setup and replaces scenes
        // without calling InitAndSetupScenes again. Rearm at the new manager's Start.
        if(current==null&&restartPending&&createSession!=null){current=createSession();restartPending=false;}
        if(current==null)return;
        current.Attempt.FreezeBeatmapHash();
        if(!Plugin.Config.RecordDeaths)SuppressRecording();
        var session=current;
        Action failure=()=> {
            if(!ReferenceEquals(current,session))return;
            try
            {
                if(session.Attempt.CaptureFailure(clock.songTime,DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()))
                    Plugin.Log.Info("Observed native failure at song time "+clock.songTime.ToString(System.Globalization.CultureInfo.InvariantCulture));
            }
            catch(Exception e){Plugin.Log.Error("Could not capture failure time: "+e);}
        };
        manager.levelFailedEvent+=failure;subscriptions.Add(manager,(failure,clock));
        Plugin.Log.Debug("Native failure observer attached.");
    }

    internal static void Seek(AudioTimeSyncController clock,float target)
    {
        if(!enabled||current==null)return;
        // StartSong invokes SeekTo(0) at the initial song time. This is normal
        // initialization; a later seek to zero still has a noninitial old time.
        if(Math.Abs(clock.songTime-clock.startSongTime)<.001f&&Math.Abs(target)<.001f)return;
        foreach(var entry in subscriptions.Values)
            if(ReferenceEquals(entry.Clock,clock))
            {
                if(current.Attempt.ExcludeAfterSeek())Plugin.Log.Debug("Own history disabled for this attempt: song-seek.");
                return;
            }
    }

    internal static void Detach(StandardLevelGameplayManager manager)
    {
        if(!subscriptions.TryGetValue(manager,out var action))return;
        manager.levelFailedEvent-=action.Failure;subscriptions.Remove(manager);
    }

    static void DetachAll()
    {
        foreach(var pair in subscriptions)pair.Key.levelFailedEvent-=pair.Value.Failure;
        subscriptions.Clear();
    }

    internal static void Finish(StandardLevelScenesTransitionSetupDataSO setup,LevelCompletionResults results)
    {
        if(!enabled||current==null||!ReferenceEquals(current.Setup,setup))return;
        if(finishedResults.TryGetValue(results,out _))return;
        if(!Plugin.Config.RecordDeaths)SuppressRecording();
        finishedResults.Add(results,new object());
        var session=current;current=null;DetachAll();
        restartPending=results.levelEndAction==LevelCompletionResults.LevelEndAction.Restart;
        if(!restartPending){createSession=null;fingerprint=null;}
        var outcome=results.levelEndStateType==LevelCompletionResults.LevelEndStateType.Failed?AttemptOutcome.Failed:
            results.levelEndStateType==LevelCompletionResults.LevelEndStateType.Cleared?AttemptOutcome.Cleared:AttemptOutcome.Incomplete;
        var record=session.Attempt.Complete(outcome,results.endSongTime,DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());
        if(record==null)
        {Plugin.Log.Debug("Own death skipped: "+session.Attempt.ExclusionReason+"; outcome="+outcome);return;}
        string path=historyPath;
        // IO has no Unity dependency and is serialized off the game thread.
        lock(writerLock)pendingWrite=pendingWrite.ContinueWith(_=> {
            try {DeathHistoryStore.Append(path,record);Plugin.Log.Info("Recorded death ("+record.TimingSource+").");}
            catch(Exception e){Plugin.Log.Error("Could not save BeatInsight death history: "+e);return;}
            try {HistoryWritten?.Invoke();}catch(Exception e){Plugin.Log.Error("History refresh notification failed: "+e);}
        },TaskScheduler.Default);
    }

    internal static void Disable()
    {
        enabled=false;current=null;createSession=null;fingerprint=null;restartPending=false;DetachAll();
        finishedResults=new ConditionalWeakTable<LevelCompletionResults,object>();
        Task writes;lock(writerLock)writes=pendingWrite;
        if(!writes.Wait(TimeSpan.FromSeconds(5)))Plugin.Log.Warn("History write is still pending at shutdown.");
    }

    internal static Task PendingHistoryWrites()
    {lock(writerLock)return pendingWrite;}
    internal static void SuppressRecording()=>current?.Attempt.SuppressRecording();
}
