using System;
using System.Collections.Generic;
using BeatInsight.WallAlert;
using HarmonyLib;

namespace BeatInsight.Mod;

internal static class SquatWarnings
{
    sealed class Subscription
    {
        internal readonly SquatWarningSequence Sequence=new();
        internal BeatmapDataCallbackWrapper Callback=null!;
        internal float PreviousTime=float.NegativeInfinity;
    }
    static readonly Dictionary<BeatmapCallbacksController,Subscription> subscriptions=new();
    static bool enabled;
    static bool configuredEnabled,configuredCircles;
    static float configuredLead;
    internal static void Enable()
    {enabled=true;configuredEnabled=Plugin.Config.SquatWarningsEnabled;configuredCircles=Plugin.Config.SquatWarningCircles;configuredLead=LeadTime;}
    internal static void Prepare(bool reportUnavailable=false)
    {
        if(!enabled||subscriptions.Count==0||!Plugin.Config.SquatWarningsEnabled)return;
        bool circles=Plugin.Config.SquatWarningCircles;
        SquatWarningPresenter.Prepare(circles);
        if(reportUnavailable&&!SquatWarningPresenter.Ready(circles))
            Plugin.Log.Warn("Squat warning resources unavailable before song start; signals will be skipped.");
    }
    internal static void Attach(BeatmapCallbacksController controller)
    {
        if(!enabled||subscriptions.ContainsKey(controller))return;
        var state=new Subscription();
        state.Callback=controller.AddBeatmapCallback<ObstacleData>(LeadTime,obstacle=> {
            if(!enabled||!Plugin.Config.SquatWarningsEnabled)return;
            try
            {
                if(state.Sequence.Observe(obstacle.beat,obstacle.endBeat,obstacle.duration,
                    obstacle.lineIndex,(int)obstacle.lineLayer,obstacle.width,obstacle.height))
                    SquatWarningPresenter.Show(Plugin.Config.SquatWarningCircles);
            }
            catch(Exception e){Plugin.Log.Error("Could not show squat warning: "+e);}
        });
        subscriptions.Add(controller,state);
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
        if(time<state.PreviousTime){state.Sequence.Reset();SquatWarningPresenter.Hide();}
        state.PreviousTime=time;
    }
    internal static void Detach(BeatmapCallbacksController controller,bool releasePresenter=true)
    {
        if(!subscriptions.TryGetValue(controller,out var state))return;
        controller.RemoveBeatmapCallback(state.Callback);subscriptions.Remove(controller);
        if(releasePresenter&&subscriptions.Count==0)SquatWarningPresenter.Dispose();
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
            foreach(var controller in new List<BeatmapCallbacksController>(subscriptions.Keys)){Detach(controller,false);Attach(controller);}
        if(!active)SquatWarningPresenter.Dispose();
        else Prepare();
    }
    internal static void Disable()
    {
        enabled=false;
        try{foreach(var controller in new List<BeatmapCallbacksController>(subscriptions.Keys))Detach(controller);}
        finally{subscriptions.Clear();SquatWarningPresenter.Dispose();}
    }
}

[HarmonyPatch(typeof(GameSongController),"StartSong")]
internal static class SquatSongStartPatch
{
    // Camera setup can finish after gameplay-manager Start. Retry before audio
    // starts, never from an obstacle callback or from the presenter's Show path.
    [HarmonyPrefix] static void Prefix()
    {try{SquatWarnings.Prepare(true);}catch(Exception e){Plugin.Log.Error("Could not prepare squat warnings before song start: "+e);}}
}

[HarmonyPatch(typeof(BeatmapCallbacksController),MethodType.Constructor,new[]{typeof(BeatmapCallbacksController.InitData)})]
internal static class SquatCallbackStartPatch
{
    [HarmonyPostfix] static void Postfix(BeatmapCallbacksController __instance)
    {try{SquatWarnings.Attach(__instance);}catch(Exception e){Plugin.Log.Error("Could not attach squat warnings: "+e);}}
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
