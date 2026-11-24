using System;
using HarmonyLib;

namespace BeatInsight.Mod;

[HarmonyPatch(typeof(GameplayCoreInstaller),"InstallBindings")]
internal static class AttemptFingerprintPatch
{
    [HarmonyPrefix]
    static void Prefix(GameplayCoreSceneSetupData ____sceneSetupData)
    {
        SquatWarnings.SetPlayerSettings(____sceneSetupData.playerSpecificSettings.leftHanded,
            ____sceneSetupData.gameplayModifiers!=null&&(int)____sceneSetupData.gameplayModifiers.songSpeed==0&&!____sceneSetupData.gameplayModifiers.fastNotes);
        try {RuntimeCapture.PrepareFingerprint(____sceneSetupData);}
        catch(Exception e){Plugin.Log.Warn("Could not prepare death fingerprint; map version remains unknown: "+e.Message);}
    }
}

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
    static void Postfix(StandardLevelGameplayManager __instance,AudioTimeSyncController ____audioTimeSyncController,GameEnergyCounter ____gameEnergyCounter)
    {
        try {SquatWarnings.Prepare();}
        catch(Exception e){Plugin.Log.Error("Could not prepare squat warning resources: "+e);}
        try {RuntimeCapture.Attach(__instance,____audioTimeSyncController,____gameEnergyCounter);}
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

[HarmonyPatch(typeof(AudioTimeSyncController),"SeekTo")]
internal static class SongSeekPatch
{
    [HarmonyPrefix]
    static void Prefix(AudioTimeSyncController __instance,float __0)
    {
        try {RuntimeCapture.Seek(__instance,__0);}
        catch(Exception e){Plugin.Log.Error("Could not track song seek: "+e);}
    }
}
