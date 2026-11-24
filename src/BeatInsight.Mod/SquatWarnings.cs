using System;
using System.Collections.Generic;
using System.Reflection;
using System.Linq;
using BeatInsight.WallAlert;
using BeatInsight.Formats;
using HarmonyLib;

namespace BeatInsight.Mod;

internal static class SquatWarnings
{
    sealed class Subscription
    {
        internal readonly SquatWarningSequence Sequence=new();
        internal BeatmapDataCallbackWrapper Callback=null!;
        internal IReadonlyBeatmapData? Data;
        internal float StartFilterTime;
        internal int DiagnosticSignals;
        internal int Mode; // 0: not prepared, 1: ordinary, 2: mapping, 3: unavailable, 4: static custom schedule.
        internal IVariableMovementDataProvider? Movement;
        internal float InitialBpm;
        internal Dictionary<ObstacleData,int>? MappingTriggers;
        internal SquatWarningPlayback? Playback;
        internal HashSet<ObstacleData>? ExcludedObstacles;
        internal float PreviousTime=float.NegativeInfinity;
    }
    static readonly Dictionary<BeatmapCallbacksController,Subscription> subscriptions=new();
    static bool enabled;
    static bool? leftHanded;
    static bool standardMovement;
    internal static void SetPlayerSettings(bool value,bool standard=false){leftHanded=value;standardMovement=standard;}
    internal static void BindMovement(BeatmapCallbacksController controller,IVariableMovementDataProvider movement,float bpm)
    {if(subscriptions.TryGetValue(controller,out var state)){state.Movement=movement;state.InitialBpm=bpm;}}
    static bool configuredEnabled,configuredCircles;
    static float configuredLead;
    static FieldInfo? mappingActive;
    static bool mappingLookedUp;
    static bool MappingActive()
    {
        if(!mappingLookedUp)
        {
            var type=AccessTools.TypeByName("MappingExtensions.Plugin");
            if(type!=null)mappingActive=AccessTools.Field(type,"active")??throw new InvalidOperationException("Mapping Extensions activation field is unavailable");
            mappingLookedUp=true;
        }
        return mappingActive!=null&&(bool)mappingActive.GetValue(null)!;
    }
    internal static void Enable()
    {enabled=true;configuredEnabled=Plugin.Config.SquatWarningsEnabled;configuredCircles=Plugin.Config.SquatWarningCircles;configuredLead=LeadTime;}
    internal static void Prepare(bool reportUnavailable=false)
    {
        RuntimeDiagnostics.Record("warnings.prepare",new{enabled=Plugin.Config.SquatWarningsEnabled,bound=subscriptions.Count,lead=LeadTime,circles=Plugin.Config.SquatWarningCircles,standardMovement,leftHanded});
        if(!enabled||subscriptions.Count==0||!Plugin.Config.SquatWarningsEnabled)return;
        bool mapping;
        try{mapping=MappingActive();}
        catch{foreach(var state in subscriptions.Values){state.Mode=3;state.MappingTriggers?.Clear();state.Playback=null;state.ExcludedObstacles?.Clear();state.ExcludedObstacles=null;}throw;}
        foreach(var state in subscriptions.Values)
        {
            int mode=mapping?2:1;
            if(state.Mode==mode||state.Mode==4)continue;
            state.Mode=3;state.MappingTriggers?.Clear();state.MappingTriggers=null;state.Playback=null;state.Sequence.Reset();
            state.ExcludedObstacles?.Clear();state.ExcludedObstacles=null;
            bool customGeometry=false;bool? noodleProcessor=null;
            if(state.Data!=null)foreach(var item in state.Data.allBeatmapDataItems)
                if(item is ObstacleData obstacle)
                {
                    if(CustomObjectFlags.IsFake(obstacle)||CustomObjectFlags.IsNonInteractiveWall(obstacle)
                        &&((noodleProcessor??=(AccessTools.Field(state.Data.GetType(),"_beatmapObjectsInTimeRowProcessor")?.GetValue(state.Data)?.GetType().FullName
                            =="NoodleExtensions.HarmonyPatches.ObjectProcessing.NoodleBeatmapObjectsInTimeRowProcessor"))==true))
                    {state.ExcludedObstacles??=new HashSet<ObstacleData>();state.ExcludedObstacles.Add(obstacle);}
                    else if(CustomObjectFlags.HasCustomWallGeometry(obstacle))customGeometry=true;
                }
            if(state.Data!=null&&CustomObjectFlags.HasPlayerMotion(state.Data))
            {
                RuntimeDiagnostics.Record("warnings.unavailable",new{reason="player-motion"});
                Plugin.Log.Debug("Squat warnings skipped: player motion needs a separate schedule.");
                continue;
            }
            if(customGeometry)
            {
                try
                {
                    if(mapping||!leftHanded.HasValue||state.Data==null)throw new NotSupportedException("Custom schedule needs known handedness and static geometry without player motion");
                    var processorField=AccessTools.Field(state.Data.GetType(),"_beatmapObjectsInTimeRowProcessor");
                    var processor=processorField?.GetValue(state.Data);
                    if(processor?.GetType().FullName!="NoodleExtensions.HarmonyPatches.ObjectProcessing.NoodleBeatmapObjectsInTimeRowProcessor")
                    {
                        RuntimeDiagnostics.Record("warnings.geometry-context",new{beatmapType=state.Data.GetType().FullName,
                            processorFieldFound=processorField!=null,processorType=processor?.GetType().FullName,mapping,leftHanded});
                        throw new NotSupportedException("Active custom row processor was not confirmed");
                    }
                    PrepareCustom(state);
                    state.Mode=4;
                }
                catch(NotSupportedException e){RuntimeDiagnostics.Record("warnings.unavailable",new{reason=e.Message});Plugin.Log.Debug("Squat warnings skipped: "+e.Message);}
                continue;
            }
            if(mapping)
            {
                if(state.Data==null)throw new InvalidOperationException("Loaded obstacle data is unavailable for warning preparation");
                var obstacles=new List<ObstacleData>();var snapshot=new List<SquatWallData>();
                foreach(var item in state.Data.allBeatmapDataItems)if(item is ObstacleData obstacle)
                {
                    if(state.ExcludedObstacles?.Contains(obstacle)==true)continue;
                    obstacles.Add(obstacle);
                    snapshot.Add(new SquatWallData(obstacle.time,obstacle.beat,obstacle.endBeat,obstacle.lineIndex,(int)obstacle.lineLayer,obstacle.width,obstacle.height));
                }
                var triggers=MappingSquatSchedule.Build(snapshot,state.StartFilterTime);
                state.MappingTriggers=new Dictionary<ObstacleData,int>(triggers.Length);
                for(int i=0;i<triggers.Length;i++)state.MappingTriggers.Add(obstacles[triggers[i]],i);
                state.Playback=new SquatWarningPlayback(triggers.Length);
                Plugin.Log.Debug("Mapping squat warning schedule prepared from loaded obstacles: "+obstacles.Count+" obstacles, "+triggers.Length+" signals.");
            }
            state.Mode=mode;
        }
        foreach(var diagnostic in subscriptions.Values)RuntimeDiagnostics.Record("warnings.schedule",new{mode=diagnostic.Mode,signals=diagnostic.MappingTriggers?.Count,excluded=diagnostic.ExcludedObstacles?.Count,diagnostic.InitialBpm,provider=diagnostic.Movement?.GetType().FullName});
        bool circles=Plugin.Config.SquatWarningCircles;
        SquatWarningPresenter.Prepare(circles);
        RuntimeDiagnostics.Record("warnings.presenter",new{circles,ready=SquatWarningPresenter.Ready(circles)});
        if(reportUnavailable&&!SquatWarningPresenter.Ready(circles))
            Plugin.Log.Warn("Squat warning resources unavailable before song start; signals will be skipped.");
    }
    static void PrepareCustom(Subscription state)
    {
        var obstacles=new List<ObstacleData>();var snapshot=new List<NoodleWallSnapshot>();
        var bpmChanges=state.Data!.allBeatmapDataItems.OfType<BPMChangeBeatmapEventData>().OrderBy(e=>e.time).ToArray();
        bool variable=state.Data.allBeatmapDataItems.OfType<NoteJumpSpeedEventData>().Any();
        float BeatAt(float seconds)
        {
            float bpm=state.InitialBpm,previous=0,beat=0;
            if(!float.IsFinite(bpm)||bpm<=0)throw new NotSupportedException("Initial BPM unavailable for wall length");
            foreach(var change in bpmChanges)
            {
                if(change.time<0)throw new NotSupportedException("Negative BPM change time has not been validated");
                if(change.time>seconds)break;
                beat+=(change.time-previous)*bpm/60f;previous=change.time;bpm=change.bpm;
                if(!float.IsFinite(bpm)||bpm<=0)throw new NotSupportedException("Invalid BPM in wall timeline");
            }
            return beat+(seconds-previous)*bpm/60f;
        }
        bool staticTracks=CustomObjectFlags.HasStaticTrackContext(state.Data);
        WallJumpMovement? entryMovement=null;
        if(standardMovement&&!variable&&state.Movement?.GetType()==typeof(VariableMovementDataProvider)
            &&bpmChanges.All(e=>e.time==0&&e.bpm==state.InitialBpm)&&CustomObjectFlags.HasNoWallEntryEnvironment(state.Data))
        {
            try{entryMovement=WallJumpMovement.FromMeasured(state.Movement.noteJumpSpeed,state.Movement.halfJumpDuration,state.Movement.jumpDuration);}
            catch(NotSupportedException){entryMovement=null;}
        }
        foreach(var item in state.Data.allBeatmapDataItems)if(item is ObstacleData obstacle)
        {
            if(state.ExcludedObstacles?.Contains(obstacle)==true)continue;
            if(obstacle.rotation!=0)throw new NotSupportedException("Rotated obstacles need a separate schedule");
            var shape=CustomObjectFlags.StaticWall(obstacle,obstacle.lineIndex,(int)obstacle.lineLayer,obstacle.width,obstacle.height,leftHanded==true,staticTracks,entryMovement);
            float end=obstacle.endBeat;
            if(shape.Length.HasValue)
            {
                if(variable)throw new NotSupportedException("Variable movement speed with wall length is not supported");
                float seconds=NoodleWallAnalysis.Duration(obstacle.duration,shape.Length,state.Movement?.noteJumpSpeed??0);
                if(seconds==0)continue;
                end=BeatAt(obstacle.time+seconds);
            }
            obstacles.Add(obstacle);snapshot.Add(new NoodleWallSnapshot(obstacle.time,obstacle.beat,end,shape));
        }
        var triggers=NoodleWallAnalysis.Build(snapshot,state.StartFilterTime);
        state.MappingTriggers=new Dictionary<ObstacleData,int>(triggers.Length);
        for(int i=0;i<triggers.Length;i++)state.MappingTriggers.Add(obstacles[triggers[i]],i);
        state.Playback=new SquatWarningPlayback(triggers.Length);
        Plugin.Log.Debug("Static custom squat schedule prepared: "+obstacles.Count+" obstacles, "+triggers.Length+" signals.");
    }
    internal static void Attach(BeatmapCallbacksController controller,BeatmapCallbacksController.InitData init)
        =>AttachSource(controller,init.beatmapData,init.startFilterTime);
    static void AttachSource(BeatmapCallbacksController controller,IReadonlyBeatmapData data,float startFilterTime)
    {
        if(!enabled||subscriptions.ContainsKey(controller))return;
        var state=new Subscription {Data=data,StartFilterTime=startFilterTime};
        state.Callback=controller.AddBeatmapCallback<ObstacleData>(LeadTime,obstacle=> {
            if(!enabled||!Plugin.Config.SquatWarningsEnabled)return;
            try
            {
                if(ShouldShow(state,obstacle))
                {state.DiagnosticSignals++;SquatWarningPresenter.Show(Plugin.Config.SquatWarningCircles);}
            }
            catch(Exception e){Plugin.Log.Error("Could not show squat warning: "+e);}
        });
        subscriptions.Add(controller,state);
    }
    static bool ShouldShow(Subscription state,ObstacleData obstacle)
    {
        if(state.ExcludedObstacles?.Contains(obstacle)==true)return false;
        if(state.Mode==2||state.Mode==4)return state.MappingTriggers!=null&&state.MappingTriggers.TryGetValue(obstacle,out int trigger)&&state.Playback!=null&&state.Playback.Observe(trigger);
        return state.Mode==1&&state.Sequence.Observe(obstacle.beat,obstacle.endBeat,obstacle.duration,
            obstacle.lineIndex,(int)obstacle.lineLayer,obstacle.width,obstacle.height);
    }
    internal static float LeadTime
    {
        get
        {
            float value=Plugin.Config.SquatWarningLeadTime;
            return float.IsNaN(value)||float.IsInfinity(value)?2f:Math.Max(.5f,Math.Min(5f,value));
        }
    }
    internal static void Update(BeatmapCallbacksController controller,float time)
    {
        if(!subscriptions.TryGetValue(controller,out var state))return;
        if(time<state.PreviousTime){state.Sequence.Reset();state.Playback?.Reset();SquatWarningPresenter.Hide();}
        state.PreviousTime=time;
    }
    internal static void Detach(BeatmapCallbacksController controller,bool releasePresenter=true)
    {
        if(!subscriptions.TryGetValue(controller,out var state))return;
        RuntimeDiagnostics.Record("warnings.detached",new{mode=state.Mode,signalRequests=state.DiagnosticSignals});
        try{controller.RemoveBeatmapCallback(state.Callback);}
        finally
        {
            subscriptions.Remove(controller);state.Mode=3;state.Data=null;state.Movement=null;state.MappingTriggers?.Clear();state.MappingTriggers=null;state.Playback=null;state.ExcludedObstacles?.Clear();state.ExcludedObstacles=null;state.Callback=null!;
            if(releasePresenter&&subscriptions.Count==0)SquatWarningPresenter.Dispose();
        }
    }
    internal static void SettingsChanged()
    {
        bool active=Plugin.Config.SquatWarningsEnabled,circles=Plugin.Config.SquatWarningCircles;
        float lead=LeadTime;
        if(active==configuredEnabled&&circles==configuredCircles&&lead==configuredLead)return;
        bool leadChanged=lead!=configuredLead;
        configuredEnabled=active;configuredCircles=circles;configuredLead=lead;
        // Visibility/recording changes must not recreate warning resources.
        if(leadChanged)
            foreach(var controller in new List<BeatmapCallbacksController>(subscriptions.Keys))
            {
                var state=subscriptions[controller];var data=state.Data;float filter=state.StartFilterTime;
                var movement=state.Movement;float bpm=state.InitialBpm;
                Detach(controller,false);
                if(data!=null){AttachSource(controller,data,filter);if(movement!=null)BindMovement(controller,movement,bpm);}
            }
        if(!active)SquatWarningPresenter.Dispose();
        else Prepare();
    }
    internal static void Disable()
    {
        enabled=false;
        try
        {
            foreach(var controller in new List<BeatmapCallbacksController>(subscriptions.Keys))
                try{Detach(controller);}catch(Exception e){Plugin.Log.Error("Could not detach squat warning callback: "+e);}
        }
        finally{standardMovement=false;leftHanded=null;subscriptions.Clear();mappingActive=null;mappingLookedUp=false;SquatWarningPresenter.Dispose();}
    }
}

[HarmonyPatch(typeof(GameSongController),"StartSong")]
internal static class SquatSongStartPatch
{
    // Camera setup can finish after gameplay-manager Start. Retry before audio
    // starts, never from an obstacle callback or from the presenter's Show path.
    [HarmonyPrefix] static void Prefix()
    {try{RuntimeDiagnostics.SongStart();SquatWarnings.Prepare(true);}catch(Exception e){Plugin.Log.Error("Could not prepare squat warnings before song start: "+e);}}
}

[HarmonyPatch(typeof(BeatmapCallbacksController),MethodType.Constructor,new[]{typeof(BeatmapCallbacksController.InitData)})]
internal static class SquatCallbackStartPatch
{
    [HarmonyPostfix] static void Postfix(BeatmapCallbacksController __instance,BeatmapCallbacksController.InitData __0)
    {try{SquatWarnings.Attach(__instance,__0);}catch(Exception e){Plugin.Log.Error("Could not attach squat warnings: "+e);}}
}
[HarmonyPatch(typeof(BeatmapCallbacksController),"Dispose")]
internal static class SquatCallbackDisposePatch
{
    [HarmonyPrefix] static void Prefix(BeatmapCallbacksController __instance)
    {try{SquatWarnings.Detach(__instance);}catch(Exception e){Plugin.Log.Error("Could not detach squat warnings: "+e);}}
}
[HarmonyPatch(typeof(BeatmapCallbacksController),"ManualUpdate")]
internal static class SquatCallbackUpdatePatch
{
    [HarmonyPrefix] static void Prefix(BeatmapCallbacksController __instance,float __0)
    {try{SquatWarnings.Update(__instance,__0);}catch(Exception e){Plugin.Log.Error("Could not update squat warnings: "+e);}}
}

[HarmonyPatch(typeof(BeatmapObjectSpawnController),"Start")]
internal static class SquatMovementReadyPatch
{
    [HarmonyPostfix] static void Postfix(BeatmapCallbacksController ____beatmapCallbacksController,IVariableMovementDataProvider ____variableMovementDataProvider,BeatmapObjectSpawnController.InitData ____initData)
    {SquatWarnings.BindMovement(____beatmapCallbacksController,____variableMovementDataProvider,____initData.beatsPerMinute);}
}
