using System.Collections;
using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.CompilerServices;

namespace BeatInsight.Compare;

sealed class AffinityKernelChecks
{
    const BindingFlags flags=BindingFlags.Public|BindingFlags.NonPublic|BindingFlags.Instance|BindingFlags.Static;
    static AffinityKernelChecks? current;
    readonly object patcher,generator,receiver;
    readonly IReadOnlyDictionary<MethodInfo,MethodInfo> wrappers;
    readonly List<object> requests=new();
    readonly object plugins;
    int disposeRequests;
    int pluginLookups,generatorFactories;
    bool failGeneration;
    AffinityKernelChecks(object patcher,object generator,object receiver,IReadOnlyDictionary<MethodInfo,MethodInfo> wrappers)
    {
        this.patcher=patcher;this.generator=generator;this.receiver=receiver;this.wrappers=wrappers;
        var metadataType=generator.GetType().GetConstructors(flags).Single(c=>!c.IsStatic).GetParameters()[0].ParameterType;
        var list=(IList)Activator.CreateInstance(typeof(List<>).MakeGenericType(metadataType))!;
        foreach(var assembly in new[]{typeof(AffinityKernelChecks).Assembly,receiver.GetType().Assembly})
        {
            var metadata=RuntimeHelpers.GetUninitializedObject(metadataType);
            metadataType.GetField("<Assembly>k__BackingField",flags)!.SetValue(metadata,assembly);list.Add(metadata);
        }
        plugins=list;
    }
    public static object Patcher()=>current!.patcher;
    public static object Plugins(){var owner=current!;owner.pluginLookups++;return owner.plugins;}
    public static object CreateGenerator(object metadata)
    {
        var owner=current??throw new InvalidOperationException("Missing Affinity discovery context");
        if(!ReferenceEquals(metadata.GetType().GetProperty("Assembly",flags)!.GetValue(metadata),owner.receiver.GetType().Assembly))
            throw new InvalidDataException("Affinity plugin discovery selected wrong assembly");
        owner.generatorFactories++;return owner.generator;
    }
    public static MethodInfo Generate(object generator,object receiver,MethodInfo native,object kind,object attribute,int priority,string[]? before,string[]? after)
    {
        var owner=current??throw new InvalidOperationException("Missing Affinity kernel probe");
        if(!ReferenceEquals(generator,owner.generator)||!ReferenceEquals(receiver,owner.receiver)||kind.ToString()!="Prefix"||!owner.wrappers.TryGetValue(native,out var wrapper))
            throw new InvalidDataException("Kernel selected unexpected Affinity patch");
        if(owner.failGeneration)throw new InvalidDataException("Controlled Affinity generation failure");
        owner.requests.Add(new{method=native.Name,kind=kind.ToString(),priority,before=before??Array.Empty<string>(),after=after??Array.Empty<string>()});
        return wrapper;
    }
    public static void DisposeGenerator(object generator)
    {
        var owner=current??throw new InvalidOperationException("Missing Affinity kernel disposal probe");
        if(!ReferenceEquals(generator,owner.generator))throw new InvalidDataException("Kernel disposed unexpected generator");
        owner.disposeRequests++;
    }
    internal static (object Evidence,WeakReference[] Released) Run(Assembly sira,object generator,object receiver,IReadOnlyDictionary<MethodInfo,MethodInfo> wrappers)
    {
        if(current!=null)throw new InvalidOperationException("Nested Affinity kernel probe");
        var patcherType=sira.GetType("SiraUtil.Affinity.Harmony.HarmonyAffinityPatcher",true)!;
        var patcher=Activator.CreateInstance(patcherType,true)!;
        var generators=(IDictionary)patcherType.GetField("_patchGenerators",flags)!.GetValue(patcher)!;
        var owner=new AffinityKernelChecks(patcher,generator,receiver,wrappers);current=owner;
        try
        {
            MethodInfo identity=typeof(HeckMapChecks).GetMethod(nameof(HeckMapChecks.Identity))!;
            int generationSites=0,registrySites=0,generatorFactorySites=0;
            var patch=InstalledTranspilerCompiler.Compile(patcherType.GetMethod("Patch",flags)!,identity,(code,operand)=>
            {
                if(operand is ConstructorInfo c&&c.DeclaringType==generator.GetType())
                {
                    generatorFactorySites++;var edgeShim=new DynamicMethod("ControlledDiscoveredGenerator",c.DeclaringType,new[]{c.GetParameters()[0].ParameterType},typeof(AffinityKernelChecks).Module,true);
                    var edgeIl=edgeShim.GetILGenerator();edgeIl.Emit(OpCodes.Ldarg_0);edgeIl.Emit(OpCodes.Call,typeof(AffinityKernelChecks).GetMethod(nameof(CreateGenerator))!);edgeIl.Emit(OpCodes.Castclass,c.DeclaringType);edgeIl.Emit(OpCodes.Ret);
                    return(OpCodes.Call,edgeShim);
                }
                if(operand is MethodInfo registry&&registry.DeclaringType?.FullName=="IPA.Loader.PluginManager"&&registry.Name=="get_EnabledPlugins")
                {
                    registrySites++;var edgeShim=new DynamicMethod("ControlledEnabledPlugins",registry.ReturnType,Type.EmptyTypes,typeof(AffinityKernelChecks).Module,true);
                    var edgeIl=edgeShim.GetILGenerator();edgeIl.Emit(OpCodes.Call,typeof(AffinityKernelChecks).GetMethod(nameof(Plugins))!);edgeIl.Emit(OpCodes.Castclass,registry.ReturnType);edgeIl.Emit(OpCodes.Ret);
                    return(OpCodes.Call,edgeShim);
                }
                if(operand is not MethodInfo m||m.DeclaringType!=generator.GetType()||m.Name!="Patch")return(code,operand);
                generationSites++;
                var shim=new DynamicMethod("ControlledKernelGenerate",typeof(MethodInfo),new[]{m.DeclaringType}.Concat(m.GetParameters().Select(p=>p.ParameterType)).ToArray(),typeof(AffinityKernelChecks).Module,true);
                var il=shim.GetILGenerator();
                for(int i=0;i<=m.GetParameters().Length;i++){il.Emit(OpCodes.Ldarg,i);if(i==3)il.Emit(OpCodes.Box,m.GetParameters()[2].ParameterType);}
                il.Emit(OpCodes.Call,typeof(AffinityKernelChecks).GetMethod(nameof(Generate))!);il.Emit(OpCodes.Ret);
                return(OpCodes.Call,shim);
            },exceptionBlocks:true).Method;
            int disposeSites=0;
            var dispose=InstalledTranspilerCompiler.Compile(patcherType.GetMethod("Dispose",flags)!,identity,(code,operand)=>
            {
                if(operand is MethodInfo m&&m.DeclaringType==generator.GetType()&&m.Name=="Dispose")
                {disposeSites++;return(OpCodes.Call,typeof(AffinityKernelChecks).GetMethod(nameof(DisposeGenerator))!);}
                return(code,operand);
            },exceptionBlocks:true).Method;
            var managerType=sira.GetType("SiraUtil.Affinity.AffinityManager",true)!;
            var managerCtor=managerType.GetConstructors(flags).Single();
            var list=(IList)Activator.CreateInstance(managerCtor.GetParameters()[0].ParameterType)!;list.Add(receiver);list.Add(receiver);
            var manager=managerCtor.Invoke(new object[]{list});
            var kernelType=sira.GetType("SiraUtil.Affinity.AffinityKernel",true)!;
            int factorySites=0,patchSites=0;
            var constructor=InstalledTranspilerCompiler.Compile(kernelType.GetConstructors(flags).Single(),identity,(code,operand)=>
            {
                if(operand is ConstructorInfo c&&c.DeclaringType==patcherType)
                {
                    factorySites++;var factory=new DynamicMethod("ControlledKernelPatcher",patcherType,Type.EmptyTypes,typeof(AffinityKernelChecks).Module,true);
                    var il=factory.GetILGenerator();il.Emit(OpCodes.Call,typeof(AffinityKernelChecks).GetMethod(nameof(Patcher))!);il.Emit(OpCodes.Castclass,patcherType);il.Emit(OpCodes.Ret);
                    return(OpCodes.Call,factory);
                }
                if(operand is MethodInfo m&&m.Name=="Patch"&&m.DeclaringType?.FullName=="SiraUtil.Affinity.IAffinityPatcher")
                {
                    patchSites++;var shim=new DynamicMethod("ControlledKernelPatch",typeof(void),new[]{m.DeclaringType,m.GetParameters()[0].ParameterType},typeof(AffinityKernelChecks).Module,true);
                    var il=shim.GetILGenerator();il.Emit(OpCodes.Ldarg_0);il.Emit(OpCodes.Castclass,patcherType);il.Emit(OpCodes.Ldarg_1);il.Emit(OpCodes.Call,patch);il.Emit(OpCodes.Ret);
                    return(OpCodes.Call,shim);
                }
                return(code,operand);
            },exceptionBlocks:true).Method;
            if(generationSites!=1||disposeSites!=1||factorySites!=1||patchSites!=1||registrySites!=1||generatorFactorySites!=1)throw new InvalidDataException("Kernel controlled edges changed");
            var siraLifecycle=SiraKernelLifecycleChecks.Run(sira);
            var scene=AffinitySceneBindingsChecks.Run(sira,manager,constructor,dispose,()=> (owner.requests.Count,owner.disposeRequests));
            var cache=(IDictionary)patcherType.GetField("_patchCache",flags)!.GetValue(patcher)!;
            int unique=((IEnumerable)managerType.GetProperty("Affinities",flags)!.GetValue(manager)!).Cast<object>().Count();
            bool valid=unique==1&&owner.requests.Count==3&&cache.Count==1&&((IList)cache[receiver]!).Count==3
                &&generators.Count==1&&ReferenceEquals(generators[receiver.GetType().Assembly],generator)&&owner.pluginLookups==1&&owner.generatorFactories==1;
            valid&=owner.disposeRequests==1;
            var failedKernel=RuntimeHelpers.GetUninitializedObject(kernelType);
            bool failureWrapped=false;owner.failGeneration=true;
            try{constructor.Invoke(null,new[]{failedKernel,manager});}
            catch(TargetInvocationException e)when(e.InnerException?.GetType().FullName=="SiraUtil.Affinity.AffinityException"
                &&e.InnerException.InnerException is InvalidDataException inner&&inner.Message=="Controlled Affinity generation failure")
            {failureWrapped=true;}
            finally{owner.failGeneration=false;}
            valid&=failureWrapped&&owner.requests.Count==3&&((IList)cache[receiver]!).Count==3&&owner.pluginLookups==1&&owner.generatorFactories==1;
            if(!valid)throw new InvalidDataException("Native kernel discovery or lifecycle changed");
            return(new{failed=0,inputAffinities=2,uniqueAffinities=unique,patchRequests=owner.requests.Count,generatorDisposeRequests=owner.disposeRequests,failureChecks=1,failureWrapped,pluginEntries=2,pluginLookups=owner.pluginLookups,generatorFactories=owner.generatorFactories,sceneBindings=scene.Evidence,siraLifecycle=siraLifecycle.Evidence,requests=owner.requests.ToArray(),
                scope="Actual manager dedup, ResolveRoots kernel lifecycle, DisposableManager, patcher plugin predicate/cache and attribute discovery. Controlled plugin registry has decoy then matching assembly; selected metadata factory returns existing tested generator. Wrappers reused, generator disposal deferred to owner; six compiled edges plus scoped constructor factory and disposal proxy, no live registry/detours/scenes."},
                new[]{new WeakReference(manager),new WeakReference(patcher),new WeakReference(failedKernel)}.Concat(scene.Released).Concat(siraLifecycle.Released).ToArray());
        }
        finally{current=null;}
    }
}
