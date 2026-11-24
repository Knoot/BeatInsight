using System;
using System.Reflection;
using HarmonyLib;
using IPA;
using IPA.Logging;
using IPA.Config.Stores;

namespace BeatInsight.Mod;

[Plugin(RuntimeOptions.DynamicInit)]
public sealed class Plugin
{
    internal static Logger Log {get;private set;}=null!;
    public static PluginConfig Config {get;private set;}=new PluginConfig();
    Harmony? harmony;

    [Init]
    public void Init(Logger logger,IPA.Config.Config store){Log=logger;Config=store.Generated<PluginConfig>();}

    [OnEnable]
    public void Enable()
    {
        if(harmony!=null)return;
        try
        {
            RuntimeCapture.VerifyGameBuild();
            RuntimeCapture.Enable();
            MenuAnalysis.Enable();
            MenuUI.Enable();
            SquatWarnings.Enable();
            harmony=new Harmony("arevu.beatinsight");
            harmony.PatchAll(Assembly.GetExecutingAssembly());
            try {RuntimeDiagnostics.Enable();}catch(Exception e){Log.Warn("Runtime diagnostics unavailable: "+e.Message);}
            MenuSettings.Enable();
            Log.Info("BeatInsight menu analysis and death capture enabled; game integration still requires live validation.");
        }
        catch(Exception e)
        {
            Cleanup();
            Log.Error("BeatInsight integration could not be enabled: "+e);
        }
    }

    [OnDisable]
    public void Disable()
        =>Cleanup();

    void Cleanup()
    {
        try {harmony?.UnpatchSelf();}catch(Exception e){Log.Error("Could not unpatch BeatInsight: "+e);}
        finally {harmony=null;}
        Stop(MenuSettings.Disable,"settings menu");
        Stop(SquatWarnings.Disable,"squat warnings");
        Stop(MenuUI.Disable,"menu UI");
        Stop(MenuAnalysis.Disable,"menu analysis");
        Stop(RuntimeCapture.Disable,"gameplay capture");
        Stop(RuntimeDiagnostics.Disable,"runtime diagnostics");
    }
    static void Stop(Action cleanup,string name)
    {try{cleanup();}catch(Exception e){Log.Error("Could not clean up "+name+": "+e);}}
}
