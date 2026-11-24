using System.Collections;
using System.Reflection;
using System.Runtime.Loader;

namespace BeatInsight.Compare;

static class HeckRegistrarChecks
{
    [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
    static WeakReference CreateInvokeDispose(Type type,BindingFlags flags)
    {
        var constructor=type.GetConstructors(flags).Single(c=>!c.IsStatic);
        var bindings=Activator.CreateInstance(constructor.GetParameters()[0].ParameterType)!;
        var receiver=constructor.Invoke(new[]{bindings});
        try
        {
            var instruction=type.GetField("_registerBurstSliderNoteData",flags)!.GetValue(receiver)!;
            var method=(MethodInfo)instruction.GetType().GetField("operand")!.GetValue(instruction)!;
            method.Invoke(null,new object?[]{null,null});
            return new WeakReference(receiver);
        }
        finally{((IDisposable)receiver).Dispose();}
    }
    static IEnumerable ThrowAfter(object entry)
    {
        yield return entry;
        throw new InvalidDataException("Controlled binding enumeration failure");
    }
    [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
    static WeakReference BindRelease(HeckNativeRegistrarSession session,Type dataType,Type entryType,BindingFlags flags,bool interrupted)
    {
        var constructor=dataType.GetConstructors(flags).Single();
        var data=constructor.Invoke(constructor.GetParameters().Select(p=>Activator.CreateInstance(p.ParameterType)).ToArray());
        var entry=Activator.CreateInstance(entryType,new[]{(object)"release-control",data})!;
        if(interrupted)
        {
            try{session.Bind(ThrowAfter(entry));throw new InvalidOperationException("Missing enumeration failure");}
            catch(InvalidDataException e)when(e.Message=="Controlled binding enumeration failure"){}
        }
        else{session.Bind(new[]{entry});session.Unbind();}
        return new WeakReference(data);
    }
    public static object Run(string gameRoot)
    {
        foreach(var pin in new[]{("Plugins/Heck.dll","9E5163F09E122713543FB69F89799D1121FB712820E6B4350F7A30C5D790793C"),("Libs/MonoMod.Utils.dll","B712EC7781C5BFC9F8A1B65527A43478BCBD0F022A7ED2D2B1823923DC2C1DE8")})
            if(Comparison.HashFile(Path.Combine(gameRoot,pin.Item1))!=pin.Item2)throw new InvalidDataException("Reinspect changed registrar backend");
        const BindingFlags flags=BindingFlags.Public|BindingFlags.NonPublic|BindingFlags.Instance|BindingFlags.Static;
        var heck=AssemblyLoadContext.Default.LoadFromAssemblyPath(Path.Combine(gameRoot,"Plugins/Heck.dll"));
        var type=heck.GetType("Heck.HarmonyPatches.BurstSliderDataRegisterer",true)!;
        var utilities=heck.GetType("Heck.InstanceTranspilers",true)!;
        var cache=(IDictionary)utilities.GetField("_delegateCache",flags)!.GetValue(null)!;
        var indices=(IDictionary)utilities.GetField("_indices",flags)!.GetValue(null)!;
        int cacheBefore=cache.Count,indicesBefore=indices.Count,failed=0,invocations=0;var weak=new List<WeakReference>();
        var previous=Environment.GetEnvironmentVariable("MONOMOD_DMD_TYPE");
        try
        {
            Environment.SetEnvironmentVariable("MONOMOD_DMD_TYPE","cecil");
            for(int i=0;i<4;i++)
            {
                weak.Add(CreateInvokeDispose(type,flags));invocations++;
                if(cache.Count!=cacheBefore||indices.Count!=indicesBefore)failed++;
            }
        }
        finally{Environment.SetEnvironmentVariable("MONOMOD_DMD_TYPE",previous);}
        GC.Collect();GC.WaitForPendingFinalizers();GC.Collect();
        int retainedReceivers=weak.Count(w=>w.IsAlive);if(retainedReceivers!=0)failed++;
        if(Environment.GetEnvironmentVariable("MONOMOD_DMD_TYPE")!=previous)failed++;
        object sessionLifetime;int retainedBoundData=0;
        using(var session=new HeckNativeRegistrarSession(gameRoot))
        {
            var dataType=heck.GetType("Heck.Deserialize.DeserializedData",true)!;
            var constructor=dataType.GetConstructors(flags).Single();
            var data=constructor.Invoke(constructor.GetParameters().Select(p=>Activator.CreateInstance(p.ParameterType)).ToArray());
            var entryType=type.GetConstructors(flags).Single(c=>!c.IsStatic).GetParameters()[0].ParameterType.GenericTypeArguments[0];
            var entry=Activator.CreateInstance(entryType,new[]{(object)"failure-control",data})!;
            try{session.Bind(ThrowAfter(entry));failed++;}
            catch(InvalidDataException e)when(e.Message=="Controlled binding enumeration failure"){}
            if(session.BindingCount!=0)failed++;
            session.Bind(new[]{entry});
            try{session.Bind(Array.Empty<object>());failed++;}
            catch(InvalidOperationException e)when(e.Message=="Previous map bindings retained"){}
            if(session.BindingCount!=1)failed++;
            session.Unbind();session.Bind(Array.Empty<object>());session.Unbind();
            if(session.BindingCount!=0)failed++;
            var released=new[]{BindRelease(session,dataType,entryType,flags,false),BindRelease(session,dataType,entryType,flags,true)};
            GC.Collect();GC.WaitForPendingFinalizers();GC.Collect();
            retainedBoundData=released.Count(w=>w.IsAlive);if(retainedBoundData!=0||session.BindingCount!=0)failed++;
            sessionLifetime=session.Lifetime;
        }
        if(cache.Count!=cacheBefore||indices.Count!=indicesBefore)failed++;
        return new{cases=9,failed,invocations,sessionFailureChecks=3,payloadReleaseChecks=2,retainedBoundData,sessionLifetime,retainedReceivers,cacheDelta=cache.Count-cacheBefore,indexDelta=indices.Count-indicesBefore,backend="cecil",
            scope="Installed registrar constructor, MonoMod Cecil delegate generation/invocation and Dispose; empty module set, no fake forwarding. Backend environment restored. Actual map registrations use a separate bounded native registrar session; no Unity lifecycle/all-mod ordering."};
    }
}
