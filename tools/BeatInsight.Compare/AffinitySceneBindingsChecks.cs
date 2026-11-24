using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.Loader;

namespace BeatInsight.Compare;

static class AffinitySceneBindingsChecks
{
    const BindingFlags flags=BindingFlags.Public|BindingFlags.NonPublic|BindingFlags.Instance|BindingFlags.Static;
    static object? project;
    static Func<object[],object>? construct;
    public static object Construct(object[] arguments)=>construct!(arguments);
    public static object Project()=>project??throw new InvalidOperationException("Missing controlled project container");
    internal static (object Evidence,WeakReference[] Released) Run(Assembly sira,object existingManager,MethodInfo kernelConstructor,MethodInfo patcherDispose,Func<(int Patches,int Disposals)> snapshot)
    {
        var zenject=AssemblyLoadContext.Default.Assemblies.Single(a=>a.GetName().Name=="Zenject");
        var containerType=zenject.GetType("Zenject.DiContainer",true)!;
        var managerType=existingManager.GetType();
        var kernelType=sira.GetType("SiraUtil.Affinity.AffinityKernel",true)!;
        var siraKernel=sira.GetType("SiraUtil.Zenject.Internal.SiraKernel",true)!;
        bool Has(object container,Type contract)=>(bool)containerType.GetMethods(flags).Single(m=>m.Name=="HasBinding"&&!m.IsGenericMethod&&m.GetParameters().Length==1&&m.GetParameters()[0].ParameterType==typeof(Type)).Invoke(container,new object[]{contract})!;
        object Resolve(object container,Type contract)=>containerType.GetMethods(flags).Single(m=>m.Name=="Resolve"&&!m.IsGenericMethod&&m.GetParameters().Length==1&&m.GetParameters()[0].ParameterType==typeof(Type)).Invoke(container,new object[]{contract})!;
        int contextSites=0,containerSites=0;
        var method=sira.GetType("SiraUtil.Affinity.Harmony.Patches.SceneAffinityKernelInjector",true)!.GetMethod("AffinityKernelInjector",flags)!;
        var compiled=InstalledTranspilerCompiler.Compile(method,typeof(HeckMapChecks).GetMethod(nameof(HeckMapChecks.Identity))!,(code,operand)=>
        {
            if(operand is not MethodInfo m)return(code,operand);
            if(m.DeclaringType?.FullName=="Zenject.ProjectContext"&&m.Name=="get_Instance")
            {
                contextSites++;var shim=new DynamicMethod("ControlledProjectContext",m.ReturnType,Type.EmptyTypes,typeof(AffinitySceneBindingsChecks).Module,true);
                var il=shim.GetILGenerator();il.Emit(OpCodes.Ldnull);il.Emit(OpCodes.Ret);return(OpCodes.Call,shim);
            }
            if(m.DeclaringType?.FullName=="Zenject.Context"&&m.Name=="get_Container")
            {
                containerSites++;var shim=new DynamicMethod("ControlledProjectContainer",m.ReturnType,new[]{m.DeclaringType},typeof(AffinitySceneBindingsChecks).Module,true);
                var il=shim.GetILGenerator();il.Emit(OpCodes.Call,typeof(AffinitySceneBindingsChecks).GetMethod(nameof(Project))!);il.Emit(OpCodes.Castclass,m.ReturnType);il.Emit(OpCodes.Ret);
                return(OpCodes.Call,shim);
            }
            return(code,operand);
        }).Method;
        if(contextSites!=1||containerSites!=1)throw new InvalidDataException("Scene injector project context changed");
        var previous=project;var released=new List<WeakReference>();int failed=0,checks=0,hierarchyChecks=0;var results=new List<object>();
        object? hierarchy=null,lookupQuiescence=null;
        int rootPasses=0,rootConstructions=0,rootDisposals=0;bool repeatedRootsRejected=false;
        bool repeatedDisposeRejected=false,repeatedLateDisposeRejected=false,ordinaryDisposeSkippedKernel=false;
        bool patcherFieldRestored=false,proxyCallbackCleared=false;object? disposalBindings=null;
        int rootPatchRequests=0,rootGeneratorDisposals=0,rootUniqueAffinities=0;
        var analyzer=zenject.GetType("Zenject.TypeAnalyzer",true)!;
        var info=analyzer.GetMethods(flags).Single(m=>m.Name=="GetInfo"&&!m.IsGenericMethod&&m.GetParameters().Length==1).Invoke(null,new object[]{kernelType})!;
        var injection=info.GetType().GetField("InjectConstructor",flags)!.GetValue(info)!;
        var factoryField=injection.GetType().GetField("Factory",flags)!;
        var originalFactory=factoryField.GetValue(injection);
        try
        {
            foreach(bool prebound in new[]{false,true})
            {
                project=Activator.CreateInstance(containerType)!;var container=Activator.CreateInstance(containerType)!;
                if(prebound)containerType.GetMethods(flags).Single(m=>m.Name=="BindInstance"&&m.IsGenericMethodDefinition).MakeGenericMethod(managerType).Invoke(container,new[]{existingManager});
                else
                {
                    var affinities=managerType.GetProperty("Affinities",flags)!;
                    var receiver=((System.Collections.IEnumerable)affinities.GetValue(existingManager)!).Cast<object>().Single();
                    var bind=containerType.GetMethods(flags).Single(m=>m.Name=="BindInstance"&&m.IsGenericMethodDefinition).MakeGenericMethod(affinities.PropertyType.GetGenericArguments().Single());
                    bind.Invoke(container,new[]{receiver});bind.Invoke(container,new[]{receiver});
                }
                var binder=compiled.Invoke(null,new[]{container});
                if(!prebound)binder!.GetType().GetMethod("NonLazy",flags)!.Invoke(binder,null);
                bool local=Has(container,managerType),root=Has(project,managerType);
                if(!local||root==prebound||binder==null||!Has(container,kernelType)||!Has(container,siraKernel))failed++;checks++;
                foreach(var contract in kernelType.GetInterfaces().Concat(siraKernel.GetInterfaces()).Distinct())
                {if(!Has(container,contract))failed++;checks++;}
                var manager=Resolve(container,managerType);
                if(!ReferenceEquals(manager,Resolve(container,managerType))||(prebound&&!ReferenceEquals(manager,existingManager)))failed++;checks++;
                if(!prebound)
                {
                    var rootManager=Resolve(project,managerType);
                    if(!ReferenceEquals(rootManager,Resolve(project,managerType))||ReferenceEquals(rootManager,manager))failed++;checks++;
                    released.Add(new WeakReference(manager));released.Add(new WeakReference(rootManager));
                }
                results.Add(new{prebound,localManager=local,projectManager=root,kernelBinding=true,siraKernelBinding=true});
                // The same native receiver is injected twice; the actual manager
                // deduplicates it before kernel attribute discovery.
                if(!prebound)
                {
                    construct=arguments=>
                    {
                        if(arguments.Length!=1||!ReferenceEquals(arguments[0],manager))throw new InvalidDataException("Root kernel received unexpected manager");
                        var instance=System.Runtime.CompilerServices.RuntimeHelpers.GetUninitializedObject(kernelType);
                        kernelConstructor.Invoke(null,new[]{instance,manager});rootConstructions++;return instance;
                    };
                    factoryField.SetValue(injection,Delegate.CreateDelegate(factoryField.FieldType,typeof(AffinitySceneBindingsChecks).GetMethod(nameof(Construct))!));
                    try
                    {
                        var roots=containerType.GetMethod("ResolveRoots",Type.EmptyTypes)!;
                        roots.Invoke(container,null);rootPasses++;
                        if(rootConstructions!=1)throw new InvalidDataException("NonLazy kernel was not constructed by ResolveRoots");
                        rootUniqueAffinities=((System.Collections.IEnumerable)managerType.GetProperty("Affinities",flags)!.GetValue(manager)!).Cast<object>().Count();
                        rootPatchRequests=snapshot().Patches;
                        if(rootUniqueAffinities!=1||rootPatchRequests!=3||snapshot().Disposals!=0)
                            throw new InvalidDataException("Nonempty root kernel discovery changed");
                        var instance=Resolve(container,kernelType);
                        try{roots.Invoke(container,null);}
                        catch(TargetInvocationException e)when(e.InnerException?.GetType().FullName=="Zenject.ZenjectException"){repeatedRootsRejected=true;}
                        if(!repeatedRootsRejected)throw new InvalidDataException("Repeated ResolveRoots was accepted");
                        if(rootConstructions!=1||!ReferenceEquals(instance,Resolve(container,kernelType)))throw new InvalidDataException("Root kernel singleton changed");
                        foreach(var contract in kernelType.GetInterfaces())
                            if(!((System.Collections.IEnumerable)containerType.GetMethods(flags).Single(m=>m.Name=="ResolveAll"&&!m.IsGenericMethod&&m.GetParameters().Length==1&&m.GetParameters()[0].ParameterType==typeof(Type)).Invoke(container,new object[]{contract})!).Cast<object>().Contains(instance))
                                throw new InvalidDataException("Root kernel interface binding changed");
                        var patcherField=kernelType.GetField("_affinityPatcher",flags)!;
                        var nativePatcher=patcherField.GetValue(instance)!;
                        var proxy=(ControlledAffinityDisposalProxy)DispatchProxy.Create(patcherField.FieldType,typeof(ControlledAffinityDisposalProxy));
                        var disposalTrace=new List<string>();
                        proxy.Callback=()=>{patcherDispose.Invoke(null,new[]{nativePatcher});rootDisposals++;disposalTrace.Add("kernel");};
                        patcherField.SetValue(instance,proxy);
                        try
                        {
                            var lifecycle=NativeDisposalBindingsChecks.Run(zenject,instance,disposalTrace);
                            disposalBindings=lifecycle.Evidence;released.AddRange(lifecycle.Released);
                            ordinaryDisposeSkippedKernel=true;
                            repeatedDisposeRejected=true;repeatedLateDisposeRejected=true;
                            rootGeneratorDisposals=snapshot().Disposals;
                            if(rootDisposals!=1||rootGeneratorDisposals!=1||snapshot()!=(3,1))
                                throw new InvalidDataException("DisposableManager kernel lifecycle changed");
                        }
                        finally{proxy.Callback=null;patcherField.SetValue(instance,nativePatcher);}
                        patcherFieldRestored=ReferenceEquals(patcherField.GetValue(instance),nativePatcher);
                        proxyCallbackCleared=proxy.Callback==null;
                        if(!patcherFieldRestored||!proxyCallbackCleared)throw new InvalidDataException("Disposal adapter retained state");
                        released.Add(new WeakReference(proxy));
                        released.Add(new WeakReference(instance));
                    }
                    finally{factoryField.SetValue(injection,originalFactory);construct=null;}
                }
                released.Add(new WeakReference(container));released.Add(new WeakReference(project));
            }
            project=Activator.CreateInstance(containerType)!;
            var createChild=containerType.GetMethod("CreateSubContainer",Type.EmptyTypes)!;
            object Child(object parent)=>createChild.Invoke(parent,null)!;
            var earlyChild=Child(project);
            var affinitiesProperty=managerType.GetProperty("Affinities",flags)!;
            var marker=((System.Collections.IEnumerable)affinitiesProperty.GetValue(existingManager)!).Cast<object>().Single();
            var affinityType=affinitiesProperty.PropertyType.GetGenericArguments().Single();
            containerType.GetMethods(flags).Single(m=>m.Name=="BindInstance"&&m.IsGenericMethodDefinition).MakeGenericMethod(affinityType).Invoke(earlyChild,new[]{marker});
            if(Has(earlyChild,managerType))failed++;hierarchyChecks++;
            compiled.Invoke(null,new[]{earlyChild});
            var lateChild=Child(project);var nestedChild=Child(earlyChild);
            var nestedMarker=marker.GetType().GetConstructors(flags).Single(c=>!c.IsStatic).Invoke(new[]{marker.GetType().GetField("_spawnDataManager",flags)!.GetValue(marker)!});
            containerType.GetMethods(flags).Single(m=>m.Name=="BindInstance"&&m.IsGenericMethodDefinition).MakeGenericMethod(affinityType).Invoke(nestedChild,new[]{nestedMarker});
            released.Add(new WeakReference(nestedMarker));
            foreach(var child in new[]{lateChild,nestedChild})
            {
                if(!Has(child,managerType))failed++;hierarchyChecks++;
                compiled.Invoke(null,new[]{child});
                if(!Has(child,kernelType)||!Has(child,siraKernel))failed++;hierarchyChecks++;
            }
            var deepestChild=Child(nestedChild);
            var containers=new[]{project,earlyChild,lateChild,nestedChild,deepestChild};
            var managers=containers.Select(c=>Resolve(c,managerType)).ToArray();
            for(int i=0;i<containers.Length;i++)
            {
                if(!ReferenceEquals(managers[i],Resolve(containers[i],managerType)))failed++;hierarchyChecks++;
                released.Add(new WeakReference(containers[i]));released.Add(new WeakReference(managers[i]));
            }
            int unique=managers.Distinct(ReferenceEqualityComparer.Instance).Count();
            if(unique!=containers.Length)failed++;hierarchyChecks++;
            var affinityCounts=new List<int>();
            var injectionAttribute=managerType.GetConstructors(flags).Single().GetParameters()[0].GetCustomAttributes(true).Single(a=>a.GetType().Name=="InjectAttribute");
            string? managerSource=injectionAttribute.GetType().GetProperty("Source",flags)!.GetValue(injectionAttribute)?.ToString();
            if(managerSource!="Local")failed++;hierarchyChecks++;
            foreach(int i in Enumerable.Range(0,managers.Length))
            {
                var affinities=((System.Collections.IEnumerable)affinitiesProperty.GetValue(managers[i])!).Cast<object>().ToArray();
                int expected=i is 1 or 3?1:0;
                var expectedMarker=i==3?nestedMarker:marker;
                if(affinities.Length!=expected||affinities.Any(a=>!ReferenceEquals(a,expectedMarker)))failed++;hierarchyChecks++;
                affinityCounts.Add(affinities.Length);
            }
            hierarchy=new{containers=containers.Length,uniqueManagers=unique,injectorCalls=3,checks=hierarchyChecks,managerSource,affinityCounts,
                scope="Real CreateSubContainer hierarchy: child before root binding, sibling and descendant after CopyIntoAllSubContainers, third-level child without injector. Manager is singleton inside each container and distinct across all five. Kernel bindings registered; kernel instances not resolved."};
            var owned=new HashSet<object>(released.Select(w=>w.Target).Where(o=>o?.GetType()==containerType)!,ReferenceEqualityComparer.Instance);
            int RetainedLookups()
            {
                var pool=zenject.GetType("Zenject.Internal.ZenPools",true)!.GetField("_lookupIdPool",flags)!.GetValue(null)!;
                FieldInfo? Find(Type? t,string name)=>t==null?null:t.GetField(name,BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic|BindingFlags.DeclaredOnly)??Find(t.BaseType,name);
                var idle=(System.Collections.IEnumerable)Find(pool.GetType(),"_stack")!.GetValue(pool)!;int count=0;
                foreach(var lookup in idle)
                {
                    var provider=lookup.GetType().GetField("Provider",flags)!.GetValue(lookup);
                    var source=provider==null?null:Find(provider.GetType(),"_container")?.GetValue(provider);
                    if(source!=null&&owned.Contains(source))count++;
                }
                return count;
            }
            int before=RetainedLookups();
            // Native LookupId pooling retains its last provider. A benign ordinary
            // constructor injection replaces that reference; never edit native pools.
            var neutralContainer=Activator.CreateInstance(containerType)!;
            var neutralMarker=DispatchProxy.Create(affinityType,typeof(ControlledObjectSpawnerProxy));
            containerType.GetMethods(flags).Single(m=>m.Name=="BindInstance"&&m.IsGenericMethodDefinition).MakeGenericMethod(affinityType).Invoke(neutralContainer,new[]{neutralMarker});
            var neutralBinding=containerType.GetMethods(flags).Single(m=>m.Name=="Bind"&&m.IsGenericMethodDefinition&&m.GetGenericArguments().Length==1&&m.GetParameters().Length==0).MakeGenericMethod(managerType).Invoke(neutralContainer,null)!;
            var neutralSelf=neutralBinding.GetType().GetMethod("ToSelf",flags)!.Invoke(neutralBinding,null)!;
            neutralSelf.GetType().GetMethod("AsSingle",flags)!.Invoke(neutralSelf,null);
            var neutralManager=Resolve(neutralContainer,managerType);
            if(!((System.Collections.IEnumerable)affinitiesProperty.GetValue(neutralManager)!).Cast<object>().SequenceEqual(new[]{neutralMarker},ReferenceEqualityComparer.Instance))failed++;
            int after=RetainedLookups();if(after!=0)failed++;
            lookupQuiescence=new{ownedProvidersBefore=before,ownedProvidersAfter=after,neutralConstructions=1,
                scope="Native idle LookupId pool retains a last provider. One ordinary manager injection with a benign marker moves fixture references out without editing pool fields; weak collection is checked after this transition, not before it."};
        }
        finally{project=previous;factoryField.SetValue(injection,originalFactory);construct=null;}
        if(failed!=0)throw new InvalidDataException("Native scene injector bindings changed: "+Newtonsoft.Json.JsonConvert.SerializeObject(new{hierarchy,lookupQuiescence,
            injection=managerType.GetConstructors(flags).Single().GetParameters()[0].GetCustomAttributes(true).Select(a=>new{type=a.GetType().FullName,
                source=a.GetType().GetProperty("Source",flags)?.GetValue(a)?.ToString(),field=a.GetType().GetField("Source",flags)?.GetValue(a)?.ToString()})}));
        return(new{cases=2,failed,checks,projectContextAdapters=2,results,hierarchy,lookupQuiescence,
            roots=new{passes=rootPasses,repeatedRootsRejected,constructions=rootConstructions,lateDisposals=rootDisposals,factoryRestored=ReferenceEquals(factoryField.GetValue(injection),originalFactory),
                inputAffinities=2,uniqueAffinities=rootUniqueAffinities,patchRequests=rootPatchRequests,generatorDisposeRequests=rootGeneratorDisposals,
                disposalBindings,
                disposableManager=new{ordinaryDisposeSkippedKernel,repeatedDisposeRejected,repeatedLateDisposeRejected,patcherFieldRestored,proxyCallbackCleared},
                scope="Real NonLazy/ResolveRoots, singleton/interface resolution and actual DisposableManager/native kernel LateDispose with one native receiver injected twice. Attribute discovery produces three patches, late disposal reaches generator once. Scoped constructor factory invokes adapted native constructor; temporary patcher proxy forwards Dispose to adapted native patcher body. SiraKernel stays lazy."},
            scope="Actual scene injector binding body and real Zenject containers/manager construction; prebound and absent manager branches, singleton identity and interface/self bindings. ProjectContext access redirected to controlled root. One nonempty kernel constructed through ResolveRoots with scoped factory adapter and released through DisposableManager; no Unity scenes, all affinity instantiation or native detours."},released.ToArray());
    }
}
