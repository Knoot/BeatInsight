using System.Collections;
using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.Loader;
using System.Runtime.CompilerServices;
using Newtonsoft.Json.Linq;

namespace BeatInsight.Compare;

// Keep the three noncollectible native delegate assemblies bounded to one CLI run.
sealed class NoodleCallbackPatchSession:IDisposable
{
    const BindingFlags flags=BindingFlags.Public|BindingFlags.NonPublic|BindingFlags.Instance|BindingFlags.Static;
    internal static NoodleCallbackPatchSession? Current;
    readonly Type patchType;
    readonly IDictionary cache,indices;
    readonly int cacheBefore,indexBefore;
    object? patch;
    DynamicMethod? start;
    int passes;
    readonly List<WeakReference> managers=new();
    internal JObject Lifetime {get;}=new();
    internal NoodleCallbackPatchSession(string gameRoot)
    {
        if(Current!=null)throw new InvalidOperationException("Nested callback patch session");
        foreach(var pin in new[]{("Plugins/NoodleExtensions.dll","FE50124E33631EED7ECB78EA1A19BB7EA64CC12974B0298DAD991672C2ABDA95"),("Plugins/Heck.dll","9E5163F09E122713543FB69F89799D1121FB712820E6B4350F7A30C5D790793C"),("Beat Saber_Data/Managed/Main.dll","A276C8A50ACFAD69A3DD3A82CDE08571C3262344702B9B014C89756771B4495E"),("Libs/MonoMod.Utils.dll","B712EC7781C5BFC9F8A1B65527A43478BCBD0F022A7ED2D2B1823923DC2C1DE8")})
            if(Comparison.HashFile(Path.Combine(gameRoot,pin.Item1))!=pin.Item2)throw new InvalidDataException("Reinspect changed callback patch");
        var noodle=AssemblyLoadContext.Default.LoadFromAssemblyPath(Path.Combine(gameRoot,"Plugins/NoodleExtensions.dll"));
        patchType=noodle.GetType("NoodleExtensions.HarmonyPatches.ObjectProcessing.ObjectCallbackAheadTimeReorder",true)!;
        var heck=AssemblyLoadContext.Default.Assemblies.Single(a=>a.GetName().Name=="Heck");
        var utilities=heck.GetType("Heck.InstanceTranspilers",true)!;
        cache=(IDictionary)utilities.GetField("_delegateCache",flags)!.GetValue(null)!;
        indices=(IDictionary)utilities.GetField("_indices",flags)!.GetValue(null)!;
        cacheBefore=cache.Count;indexBefore=indices.Count;Current=this;
    }
    internal void Bind(object manager)
    {
        if(Current!=this)throw new ObjectDisposedException(nameof(NoodleCallbackPatchSession));
        if(patch!=null)
        {
            if(patchType.GetField("_noodleObjectsCallbacksManager",flags)!.GetValue(patch)!=null)throw new InvalidOperationException("Callback manager retained between maps");
            patchType.GetField("_noodleObjectsCallbacksManager",flags)!.SetValue(patch,manager);
            managers.Add(new WeakReference(manager));passes++;return;
        }
        var keys=cache.Keys.Cast<object>().ToHashSet();var indexKeys=indices.Keys.Cast<object>().ToHashSet();
        var previous=Environment.GetEnvironmentVariable("MONOMOD_DMD_TYPE");
        try
        {
            Environment.SetEnvironmentVariable("MONOMOD_DMD_TYPE","cecil");
            patch=patchType.GetConstructors(flags).Single(c=>!c.IsStatic).Invoke(new[]{manager});
            var main=AssemblyLoadContext.Default.Assemblies.Single(a=>a.GetName().Name=="Main");
            var method=main.GetType("BeatmapObjectSpawnController",true)!.GetMethod("Start",flags)!;
            var compiled=InstalledTranspilerCompiler.Compile(method,patchType.GetMethod("UseNoodleCallback",flags)!,exceptionBlocks:true,patchInstance:patch);
            start=compiled.Method;
            if(compiled.Patched!=compiled.Original-6)throw new InvalidDataException("Callback redirect instruction count changed");
            if(cache.Count!=cacheBefore+3||indices.Count!=indexBefore+3)throw new InvalidDataException("Callback delegate registration changed");
            Lifetime["constructors"]=1;Lifetime["generatedDelegates"]=3;
            Lifetime["originalInstructions"]=compiled.Original;Lifetime["patchedInstructions"]=compiled.Patched;
            managers.Add(new WeakReference(manager));passes++;
        }
        catch
        {
            try{(patch as IDisposable)?.Dispose();}
            finally
            {
                foreach(var key in cache.Keys.Cast<object>().Where(k=>!keys.Contains(k)).ToArray())cache.Remove(key);
                foreach(var key in indices.Keys.Cast<object>().Where(k=>!indexKeys.Contains(k)).ToArray())indices.Remove(key);
                patch=null;start=null;
            }
            throw;
        }
        finally{Environment.SetEnvironmentVariable("MONOMOD_DMD_TYPE",previous);}
    }
    internal void Start(object controller)=> (start??throw new InvalidOperationException("Callback patch not bound")).Invoke(null,new[]{controller});
    internal void Update(float time)=>patchType.GetMethod("UpdateNoodleCallback",flags)!.Invoke(patch??throw new InvalidOperationException("Callback patch not bound"),new object[]{time});
    internal void Unbind(){if(patch!=null)patchType.GetField("_noodleObjectsCallbacksManager",flags)!.SetValue(patch,null);}
    [MethodImpl(MethodImplOptions.NoInlining)]
    WeakReference ReleasePatch()
    {
        Unbind();var weak=new WeakReference(patch);
        (patch as IDisposable)?.Dispose();patch=null;start=null;
        return weak;
    }
    public void Dispose()
    {
        if(Current!=this)return;
        var weak=ReleasePatch();Current=null;
        // Diagnostic host only; force collection once per bounded run, never in the game.
        GC.Collect();GC.WaitForPendingFinalizers();GC.Collect();
        int retainedManagers=managers.Count(w=>w.IsAlive);managers.Clear();
        Lifetime["bindingPasses"]=passes;Lifetime["retainedManagers"]=retainedManagers;
        Lifetime["retainedPatchReceivers"]=weak.IsAlive?1:0;
        Lifetime["cacheDelta"]=cache.Count-cacheBefore;Lifetime["indexDelta"]=indices.Count-indexBefore;Lifetime["disposed"]=true;
        if(cache.Count!=cacheBefore||indices.Count!=indexBefore)throw new InvalidDataException("Callback patch retained native delegates");
        if(retainedManagers!=0||weak.IsAlive)throw new InvalidDataException("Callback patch lifetime retained managed receivers");
    }
}
