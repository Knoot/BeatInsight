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
    internal static void Enable()=>enabled=true;
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
    internal static void Detach(BeatmapCallbacksController controller)
    {
        if(!subscriptions.TryGetValue(controller,out var state))return;
        controller.RemoveBeatmapCallback(state.Callback);subscriptions.Remove(controller);
        if(subscriptions.Count==0)SquatWarningPresenter.Dispose();
    }
    internal static void SettingsChanged()
    {
        // Ahead time belongs to the callback registration, so replace it when changed.
        var controllers=new List<BeatmapCallbacksController>(subscriptions.Keys);
        foreach(var controller in controllers){Detach(controller);Attach(controller);}
        SquatWarningPresenter.Hide();
    }
    internal static void Disable()
    {
        enabled=false;
        try{foreach(var controller in new List<BeatmapCallbacksController>(subscriptions.Keys))Detach(controller);}
        finally{subscriptions.Clear();SquatWarningPresenter.Dispose();}
    }
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
