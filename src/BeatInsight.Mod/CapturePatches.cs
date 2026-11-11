using System;
using HarmonyLib;

namespace BeatInsight.Mod;

[HarmonyPatch(typeof(StandardLevelScenesTransitionSetupDataSO),"InitAndSetupScenes")]
internal static class AttemptStartPatch
{
    [HarmonyPrefix]
    static void Prefix(StandardLevelScenesTransitionSetupDataSO __instance,PlayerSpecificSettings __0)
    {
        try {RuntimeCapture.Begin(__instance,__0);}
        catch(Exception e){Plugin.Log.Error("Could not start BeatInsight attempt: "+e);}
    }
}

[HarmonyPatch(typeof(StandardLevelGameplayManager),"Start")]
internal static class GameplayStartPatch
{
    [HarmonyPostfix]
    static void Postfix(StandardLevelGameplayManager __instance,AudioTimeSyncController ____audioTimeSyncController)
    {
        try {RuntimeCapture.Attach(__instance,____audioTimeSyncController);}
        catch(Exception e){Plugin.Log.Error("Could not attach BeatInsight failure capture: "+e);}
    }
}

[HarmonyPatch(typeof(StandardLevelGameplayManager),"OnDestroy")]
internal static class GameplayDestroyPatch
{
    [HarmonyPrefix]
    static void Prefix(StandardLevelGameplayManager __instance)
    {
        try {RuntimeCapture.Detach(__instance);}
        catch(Exception e){Plugin.Log.Error("Could not detach BeatInsight capture: "+e);}
    }
}

[HarmonyPatch(typeof(StandardLevelScenesTransitionSetupDataSO),"Finish")]
internal static class AttemptFinishPatch
{
    [HarmonyPrefix]
    static void Prefix(StandardLevelScenesTransitionSetupDataSO __instance,LevelCompletionResults __0)
    {
        try {RuntimeCapture.Finish(__instance,__0);}
        catch(Exception e){Plugin.Log.Error("Could not finish BeatInsight attempt: "+e);}
    }
}
