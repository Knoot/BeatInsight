using System;
using System.Linq;
using System.Collections;
using HarmonyLib;
using System.Threading.Tasks;
using BeatSaberMarkupLanguage.GameplaySetup;
using IPA.Utilities.Async;
using UnityEngine;

namespace BeatInsight.Mod;

internal static class MenuSettings
{
    static bool enabled;
    const string TabName="BeatInsight";
    static GameplaySetup? owner;
    internal static void Enable()
    {
        enabled=true;
    }
    internal static void EnsureRegistered(GameplaySetup settings)
    {
        if(!enabled)return;
        try
        {
            if(ReferenceEquals(owner,settings))return;
            Remove();settings.AddTab(TabName,"BeatInsight.Mod.Settings.bsml",Plugin.Config);owner=settings;
            Plugin.Log.Debug("Gameplay setup settings tab registered.");
        }
        catch(Exception e){Plugin.Log.Warn("Settings menu registration deferred: "+e.Message);}
    }
    internal static void Disable(){enabled=false;Remove();}
    static void Remove()
    {
        var previous=owner;owner=null;GameObject? view=null;
        try
        {
            if(previous==null)return;
            var menus=(IEnumerable)AccessTools.Field(typeof(GameplaySetup),"menus").GetValue(previous);
            var menu=menus.Cast<object>().FirstOrDefault(m=>ReferenceEquals(AccessTools.Property(m.GetType(),"Host").GetValue(m),Plugin.Config));
            view=menu==null?null:AccessTools.Field(menu.GetType(),"tabObject").GetValue(menu) as GameObject;
            previous.RemoveTab(TabName);
        }
        finally
        {
            // This config's notification subscribers belong to our settings view.
            // BSML refreshes its tab tree asynchronously after removal.
            Plugin.Config.ReleaseUIBindings();
            if(view!=null){view.SetActive(false);UnityEngine.Object.Destroy(view);}
        }
    }
    internal static async void ConfigChanged()
    {
        if(!enabled)return;
        bool recording=Plugin.Config.RecordDeaths;
        try
        {
            await UnityMainThreadTaskScheduler.Factory.StartNew(()=> {
                if(!enabled)return;
                if(!recording)RuntimeCapture.SuppressRecording();
                SquatWarnings.SettingsChanged();
                MenuUI.SettingsChanged();
                MenuAnalysis.SettingsChanged();
            });
        }
        catch(Exception e){Plugin.Log.Error("Could not apply settings: "+e);}
    }
}
