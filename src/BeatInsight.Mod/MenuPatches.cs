using System;
using HarmonyLib;
using BeatSaberMarkupLanguage.GameplaySetup;

namespace BeatInsight.Mod;

[HarmonyPatch(typeof(GameplaySetup),"Initialize")]
internal static class SettingsMenuPatch
{
    [HarmonyPrefix]
    static void Prefix(GameplaySetup __instance)=>MenuSettings.EnsureRegistered(__instance);
}

[HarmonyPatch(typeof(StandardLevelDetailViewController),"DidActivate")]
internal static class MenuActivatePatch
{
    [HarmonyPostfix]
    static void Postfix(StandardLevelDetailViewController __instance,BeatmapLevelsModel ____beatmapLevelsModel)
    {
        try {MenuAnalysis.Activate(__instance,____beatmapLevelsModel);MenuUI.Activate(__instance);}
        catch(Exception e)
        {
            Plugin.Log.Error("Could not attach menu analysis: "+e);
            try {MenuAnalysis.Deactivate(__instance);}catch(Exception cleanup){Plugin.Log.Error("Menu analysis cleanup failed: "+cleanup);}
        }
    }
}

[HarmonyPatch(typeof(StandardLevelDetailViewController),"DidDeactivate")]
internal static class MenuDeactivatePatch
{
    [HarmonyPrefix]
    static void Prefix(StandardLevelDetailViewController __instance)
    {
        try {MenuUI.Deactivate(__instance);}
        catch(Exception e){Plugin.Log.Error("Could not deactivate menu UI: "+e);}
        finally {try {MenuAnalysis.Deactivate(__instance);}catch(Exception e){Plugin.Log.Error("Could not deactivate menu analysis: "+e);}}
    }
}

[HarmonyPatch(typeof(StandardLevelDetailViewController),"OnDestroy")]
internal static class MenuDestroyPatch
{
    [HarmonyPrefix]
    static void Prefix(StandardLevelDetailViewController __instance)
    {
        try {MenuUI.Deactivate(__instance);}
        catch(Exception e){Plugin.Log.Error("Could not detach menu UI: "+e);}
        finally {try {MenuAnalysis.Deactivate(__instance);}catch(Exception e){Plugin.Log.Error("Could not detach menu analysis: "+e);}}
    }
}

[HarmonyPatch(typeof(StandardLevelDetailViewController),"ClearSelected")]
internal static class MenuClearPatch
{
    [HarmonyPostfix]
    static void Postfix(StandardLevelDetailViewController __instance)
    {
        try {MenuAnalysis.Clear(__instance);}
        catch(Exception e){Plugin.Log.Error("Could not clear menu analysis: "+e);}
    }
}

[HarmonyPatch(typeof(PlayerData),"set_playerSpecificSettings")]
internal static class PlayerJumpSettingsPatch
{
    [HarmonyPostfix]
    static void Postfix(PlayerData __instance)
    {
        try {MenuAnalysis.PlayerSettingsChanged(__instance);}
        catch(Exception e){Plugin.Log.Error("Could not refresh jump settings: "+e);}
    }
}

[HarmonyPatch(typeof(PlayerData),"set_gameplayModifiers")]
internal static class PlayerMovementModifiersPatch
{
    [HarmonyPostfix]
    static void Postfix(PlayerData __instance)
    {
        try {MenuAnalysis.PlayerSettingsChanged(__instance);}
        catch(Exception e){Plugin.Log.Error("Could not refresh movement modifiers: "+e);}
    }
}
