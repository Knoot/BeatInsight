using System;
using System.Reflection;
using HarmonyLib;
using IPA;
using IPA.Logging;

namespace BeatInsight.Mod;

[Plugin(RuntimeOptions.DynamicInit)]
public sealed class Plugin
{
    internal static Logger Log {get;private set;}=null!;
    Harmony? harmony;

    [Init]
    public void Init(Logger logger)=>Log=logger;

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
            harmony=new Harmony("arevu.beatinsight");
            harmony.PatchAll(Assembly.GetExecutingAssembly());
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
        Stop(MenuUI.Disable,"menu UI");
        Stop(MenuAnalysis.Disable,"menu analysis");
        Stop(RuntimeCapture.Disable,"gameplay capture");
    }
    static void Stop(Action cleanup,string name)
    {try{cleanup();}catch(Exception e){Log.Error("Could not clean up "+name+": "+e);}}
}
