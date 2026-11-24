using System.Collections;
using System.Linq.Expressions;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.Loader;
using UnityEngine;

namespace BeatInsight.Compare;

public static class HeckBurstRegistrationChecks
{
    public static object Run(string gameRoot,BeatmapData map,object bindings)
    {
        const BindingFlags flags=BindingFlags.Public|BindingFlags.NonPublic|BindingFlags.Instance|BindingFlags.Static;
        var chains=OracleBridge.GetItems(map).OfType<SliderData>().Where(s=>(int)s.sliderType==1).ToArray();
        if(chains.Length==0)return new{chains=0,slices=0,registered=0,failed=0};
        string path=Path.Combine(gameRoot,"Beat Saber_Data/Managed/Main.dll");
        if(Comparison.HashFile(path)!="A276C8A50ACFAD69A3DD3A82CDE08571C3262344702B9B014C89756771B4495E")throw new InvalidDataException("Reinspect changed native chain spawner");
        if(Comparison.HashFile(Path.Combine(gameRoot,"Beat Saber_Data/Managed/Zenject.dll"))!="2031FF13D801ACCF409E3F4D02ADB9C61AA7A07DA8E23A8A7F996716D0E82E49")throw new InvalidDataException("Reinspect changed movement pool contract");
        var main=AssemblyLoadContext.Default.LoadFromAssemblyPath(path);
        var heck=AssemblyLoadContext.Default.Assemblies.Single(a=>a.GetName().Name=="Heck");
        var instanceType=heck.GetType("Heck.InstanceTranspilers",true)!;
        var cache=(IDictionary)instanceType.GetField("_delegateCache",flags)!.GetValue(null)!;
        var indices=(IDictionary)instanceType.GetField("_indices",flags)!.GetValue(null)!;
        int cacheBefore=cache.Count,indicesBefore=indices.Count;
        var session=HeckNativeRegistrarSession.Current??throw new InvalidOperationException("Missing native registrar session");
        var process=session.Bind(bindings);object result;
        try
        {
        var movementType=main.GetType("VariableMovementDataProvider",true)!;
        var movement=RuntimeHelpers.GetUninitializedObject(movementType);movementType.GetField("_halfJumpDuration",flags)!.SetValue(movement,1f);
        var emitted=new List<NoteData>();var observer=new HeckSpawnCallbackChecks(bindings,emitted);var callbackType=process.GetParameters()[4].ParameterType;
        var ps=callbackType.GetMethod("Invoke")!.GetParameters().Select(p=>Expression.Parameter(p.ParameterType,p.Name)).ToArray();
        var callback=Expression.Lambda(callbackType,Expression.Call(Expression.Constant(observer),typeof(HeckSpawnCallbackChecks).GetMethod(nameof(HeckSpawnCallbackChecks.Capture))!,ps[0]),ps).Compile();
        int failed=0,slices=0,registered=0,aheadResetChecks=0;var rows=new List<object>();
        foreach(var chain in chains)
        {
            emitted.Clear();observer.Chain=chain;
            var spawnResult=NoodleSliderSpawnChecks.Run(main,chain,bindings);
            failed+=(int)Newtonsoft.Json.Linq.JObject.FromObject(spawnResult.Evidence)["failed"]!;
            var spawn=spawnResult.Spawn;
            process.Invoke(null,new[]{chain,spawn,(object)false,movement,callback});
            if(emitted.Count!=chain.sliceCount-1)failed++;
            foreach(var slice in emitted)
            {
                slices++;
                foreach(var binding in (IEnumerable)bindings)
                {
                    var data=binding.GetType().GetField("Item2")!.GetValue(binding)!;
                    var objects=(IDictionary)data.GetType().GetProperty("ObjectCustomDatas",flags)!.GetValue(data)!;
                    var parent=objects[chain];
                    bool copyable=parent?.GetType().GetInterfaces().Any(i=>i.IsGenericType&&i.GetGenericTypeDefinition().FullName=="Heck.Deserialize.ICopyable`1")==true;
                    if(objects.Contains(slice)!=copyable){failed++;continue;}
                    if(!copyable)continue;
                    var actual=objects[slice]!;registered++;
                    if(ReferenceEquals(parent,actual))failed++;
                    if(actual.GetType().GetProperty("InternalAheadTime",flags) is PropertyInfo ahead)
                    {
                        aheadResetChecks++;
                        if(ahead.GetValue(actual)!=null)failed++;
                    }
                    var resolve=data.GetType().GetMethods(flags).Single(m=>m.Name=="Resolve"&&m.IsGenericMethodDefinition&&m.GetParameters().Length==2&&m.GetParameters()[0].ParameterType==typeof(BeatmapObjectData));
                    object?[] args={slice,null};
                    if(!(bool)resolve.MakeGenericMethod(actual.GetType()).Invoke(data,args)!||!ReferenceEquals(actual,args[1]))failed++;
                    if(actual.GetType().GetProperty("Track",flags)?.GetValue(actual) is IEnumerable tracks
                        &&parent!.GetType().GetProperty("Track",flags)?.GetValue(parent) is IEnumerable sourceTracks
                        &&!tracks.Cast<object>().SequenceEqual(sourceTracks.Cast<object>(),ReferenceEqualityComparer.Instance))failed++;
                }
            }
            var noodle=AssemblyLoadContext.Default.Assemblies.Single(a=>a.GetName().Name=="NoodleExtensions");
            var movementEvidence=NoodleBurstMovementChecks.Run(main,noodle,chain,emitted,bindings);
            failed+=(int)Newtonsoft.Json.Linq.JObject.FromObject(movementEvidence)["failed"]!;
            rows.Add(new{chain.sliceCount,chain.hasHeadNote,emitted=emitted.Count,spawnEvidence=spawnResult.Evidence,movementEvidence});
        }
        failed+=observer.Failed;
        result=new{chains=chains.Length,slices,registered,failed,aheadResetChecks,callbackChecks=observer.Checks,callbackFailures=observer.Failed,rows,scope="Actual BurstSliderSpawner with installed Heck instance transpiler and delegate registration; real ICopyable copies, Resolve identity and shared Track instances checked. Controlled half jump; real Noodle slider spawn data under controlled geometry settings; actual MonoMod Cecil-generated delegate reused within bounded session; Noodle movement InitObject and managed pool attachment checked; Unity spawn lifecycle and all-mod ordering excluded."};
        }
        finally{session.Unbind();}
        if(cache.Count!=cacheBefore||indices.Count!=indicesBefore)throw new InvalidDataException("Controlled burst registration changed native delegate cache");
        var report=Newtonsoft.Json.Linq.JObject.FromObject(result);report["delegateCacheUnchanged"]=true;
        return report;
    }
}
