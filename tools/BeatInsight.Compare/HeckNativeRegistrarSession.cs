using System.Collections;
using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.Loader;
using Newtonsoft.Json.Linq;

namespace BeatInsight.Compare;

// One generated delegate per bounded CLI run; no map graph survives Unbind.
sealed class HeckNativeRegistrarSession:IDisposable
{
    internal static HeckNativeRegistrarSession? Current;
    const BindingFlags flags=BindingFlags.Public|BindingFlags.NonPublic|BindingFlags.Instance|BindingFlags.Static;
    object? receiver,bindings;
    DynamicMethod? process;
    readonly IDictionary cache,indices;
    readonly int cacheBefore,indicesBefore;
    readonly MethodInfo clear,add;
    int passes,maxBindings,failedBinds;
    internal int BindingCount=>bindings==null?0:(int)bindings.GetType().GetProperty("Count")!.GetValue(bindings)!;
    internal JObject Lifetime {get;}=new();
    internal HeckNativeRegistrarSession(string gameRoot)
    {
        if(Current!=null)throw new InvalidOperationException("Nested registrar session");
        foreach(var pin in new[]{("Plugins/Heck.dll","9E5163F09E122713543FB69F89799D1121FB712820E6B4350F7A30C5D790793C"),("Libs/MonoMod.Utils.dll","B712EC7781C5BFC9F8A1B65527A43478BCBD0F022A7ED2D2B1823923DC2C1DE8"),("Beat Saber_Data/Managed/Main.dll","A276C8A50ACFAD69A3DD3A82CDE08571C3262344702B9B014C89756771B4495E")})
            if(Comparison.HashFile(Path.Combine(gameRoot,pin.Item1))!=pin.Item2)throw new InvalidDataException("Reinspect changed native registrar session");
        var heck=AssemblyLoadContext.Default.LoadFromAssemblyPath(Path.Combine(gameRoot,"Plugins/Heck.dll"));
        var type=heck.GetType("Heck.HarmonyPatches.BurstSliderDataRegisterer",true)!;
        var utilities=heck.GetType("Heck.InstanceTranspilers",true)!;
        cache=(IDictionary)utilities.GetField("_delegateCache",flags)!.GetValue(null)!;
        indices=(IDictionary)utilities.GetField("_indices",flags)!.GetValue(null)!;
        cacheBefore=cache.Count;indicesBefore=indices.Count;
        var constructor=type.GetConstructors(flags).Single(c=>!c.IsStatic);
        bindings=Activator.CreateInstance(constructor.GetParameters()[0].ParameterType)!;
        clear=bindings.GetType().GetMethod("Clear")!;add=bindings.GetType().GetMethod("Add")!;
        var priorKeys=cache.Keys.Cast<object>().ToHashSet();var priorIndices=indices.Keys.Cast<object>().ToHashSet();
        var previous=Environment.GetEnvironmentVariable("MONOMOD_DMD_TYPE");
        try
        {
            Environment.SetEnvironmentVariable("MONOMOD_DMD_TYPE","cecil");
            receiver=constructor.Invoke(new[]{bindings});
            var main=AssemblyLoadContext.Default.LoadFromAssemblyPath(Path.Combine(gameRoot,"Beat Saber_Data/Managed/Main.dll"));
            var original=main.GetType("BurstSliderSpawner",true)!.GetMethod("ProcessSliderData",flags)!;
            var compiled=InstalledTranspilerCompiler.Compile(original,type.GetMethod("ReplaceConditionTranspiler",flags)!,patchInstance:receiver);
            if(compiled.Patched!=compiled.Original+3)throw new InvalidDataException("Native registrar insertion changed");
            process=compiled.Method;
            var instruction=type.GetField("_registerBurstSliderNoteData",flags)!.GetValue(receiver)!;
            var generated=(MethodInfo)instruction.GetType().GetField("operand")!.GetValue(instruction)!;
            Lifetime["backend"]="cecil";Lifetime["constructors"]=1;Lifetime["generatedDelegates"]=1;
            Lifetime["generatedAssembly"]=generated.DeclaringType!.Assembly.GetName().Name;
            Lifetime["generatedAssemblyCollectible"]=generated.DeclaringType.Assembly.IsCollectible;
            if(cache.Count!=cacheBefore+1||indices.Count!=indicesBefore+1)throw new InvalidDataException("Native registrar ownership changed");
            Current=this;
        }
        catch
        {
            try{(receiver as IDisposable)?.Dispose();}
            finally
            {
                foreach(var key in cache.Keys.Cast<object>().Where(k=>!priorKeys.Contains(k)).ToArray())cache.Remove(key);
                foreach(var key in indices.Keys.Cast<object>().Where(k=>!priorIndices.Contains(k)).ToArray())indices.Remove(key);
                receiver=null;bindings=null;process=null;
            }
            throw;
        }
        finally{Environment.SetEnvironmentVariable("MONOMOD_DMD_TYPE",previous);}
    }
    internal DynamicMethod Bind(object source)
    {
        if(receiver==null||bindings==null||process==null)throw new ObjectDisposedException(nameof(HeckNativeRegistrarSession));
        if((int)bindings.GetType().GetProperty("Count")!.GetValue(bindings)! !=0)throw new InvalidOperationException("Previous map bindings retained");
        try{foreach(var binding in (IEnumerable)source)add.Invoke(bindings,new[]{binding});}
        catch{Unbind();failedBinds++;throw;}
        int count=(int)bindings.GetType().GetProperty("Count")!.GetValue(bindings)!;
        maxBindings=Math.Max(maxBindings,count);passes++;return process;
    }
    internal void Unbind(){if(bindings!=null)clear.Invoke(bindings,null);}
    public void Dispose()
    {
        if(receiver==null)return;
        Unbind();(receiver as IDisposable)!.Dispose();receiver=null;bindings=null;process=null;Current=null;
        Lifetime["bindingPasses"]=passes;Lifetime["failedBinds"]=failedBinds;Lifetime["maxBindings"]=maxBindings;Lifetime["retainedBindings"]=0;
        Lifetime["cacheDelta"]=cache.Count-cacheBefore;Lifetime["indexDelta"]=indices.Count-indicesBefore;Lifetime["disposed"]=true;
        if(cache.Count!=cacheBefore||indices.Count!=indicesBefore)throw new InvalidDataException("Native registrar cache retained receiver");
    }
}
