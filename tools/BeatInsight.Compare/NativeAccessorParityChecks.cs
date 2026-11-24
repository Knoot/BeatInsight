using Newtonsoft.Json.Linq;
namespace BeatInsight.Compare;

static class NativeAccessorParityChecks
{
    static readonly HashSet<string> Diagnostics=new(StringComparer.Ordinal){"scope","nativeFieldAccessors","nativeAccessorOwnership","customFieldAddressAdapters","nativeAccessors","nativePatchConstructor","generatedAssembly"};
    static void Normalize(JToken token)
    {
        if(token is JObject obj)foreach(var property in obj.Properties().ToArray())
        {if(Diagnostics.Contains(property.Name))property.Remove();else Normalize(property.Value);}
        else if(token is JArray array)foreach(var item in array)Normalize(item);
        else if(token is JValue value&&value.Type==JTokenType.String&&value.Value is string trace
            &&trace.StartsWith("System.Security.SecurityException:",StringComparison.Ordinal)
            &&trace.Contains("UnityEngine.Quaternion.Internal_FromEulerRad",StringComparison.Ordinal)
            &&trace.Contains("Heck.Animation.QuaternionPointDefinition.CreatePointData",StringComparison.Ordinal))
        {
            // JIT tiering can inline any of the managed wrappers differently.
            // Preserve exception type/message and the two exact boundary anchors;
            // the preceding event/time diagnostic and all error counts stay intact.
            value.Value=trace.Replace("\r\n","\n").Split('\n')[0]+"\nUnityEngine.Quaternion.Internal_FromEulerRad\nHeck.Animation.QuaternionPointDefinition.CreatePointData";
        }
    }
    public static object Run(string baselinePath,string nativePath)
    {
        var baseline=JObject.Parse(File.ReadAllText(baselinePath));var native=JObject.Parse(File.ReadAllText(nativePath));
        var errors=new List<string>();
        if((bool?)baseline["nativeFieldAccessors"]!=false||(int?)baseline["customFieldAddressAdapters"]!=2)errors.Add("Baseline must exercise field adapters");
        if((bool?)native["nativeFieldAccessors"]!=true||(int?)native["customFieldAddressAdapters"]!=0
            ||(int?)native["nativeAccessorOwnership"]?["nativeDelegates"]!=9||(int?)native["nativeAccessorOwnership"]?["capturedReceivers"]!=0)errors.Add("Candidate must exercise nine open native accessors");
        if((int?)baseline["failed"]!=0||(int?)native["failed"]!=0||(int?)baseline["cases"]<=0)errors.Add("Both input runs must pass");
        foreach(var input in new[]{baseline,native})
        {
            int count=(int?)input["cases"]??0;
            if(count<=0||input["results"] is not JArray rows||rows.Count!=count||(int?)input["transformedCases"]!=count*4)
                errors.Add("Input must include complete four-transform result matrix");
            if(input["registrarLifetime"] is not JObject lifetime||(bool?)lifetime["disposed"]!=true
                ||(int?)lifetime["retainedBindings"]!=0||(int?)lifetime["cacheDelta"]!=0||(int?)lifetime["indexDelta"]!=0
                ||(int?)lifetime["generatedDelegates"]!=1||(int?)lifetime["constructors"]!=1)
                errors.Add("Input registrar lifetime must be complete and released");
            if(input["callbackPatchLifetime"] is not JObject callbackLifetime||(bool?)callbackLifetime["disposed"]!=true
                ||(int?)callbackLifetime["retainedManagers"]!=0||(int?)callbackLifetime["retainedPatchReceivers"]!=0
                ||(int?)callbackLifetime["cacheDelta"]!=0||(int?)callbackLifetime["indexDelta"]!=0
                ||(int?)callbackLifetime["generatedDelegates"]!=3||(int?)callbackLifetime["constructors"]!=1)
                errors.Add("Input callback patch lifetime must be complete and released");
            if(input["affinitySpawnLifetime"] is not JObject affinity||(bool?)affinity["disposed"]!=true
                ||(int?)affinity["generatorConstructors"]!=2||(int?)affinity["constructorAssemblyAdapters"]!=1
                ||(int?)affinity["constructorHarmonyAdapters"]!=1||(int?)affinity["constructorHarmonyCalls"]!=2
                ||(int?)affinity["constructorModuleAdapters"]!=1
                ||(bool?)affinity["constructorModuleReused"]!=true||(bool?)affinity["staticAssemblyRestored"]!=true
                ||(int?)affinity["generatedWrappers"]!=3||(int?)affinity["harmonyComposedMethods"]!=3
                ||(int?)affinity["capturedPatchCalls"]!=3||(int?)affinity["capturedUnpatchCalls"]!=3
                ||(int?)affinity["retainedDelegates"]!=0||(int?)affinity["retainedReceivers"]!=0||(int?)affinity["cacheCount"]!=0)
                errors.Add("Input Affinity spawn wrappers must be composed and released");
            if(input["affinitySpawnLifetime"]?["kernel"] is not JObject kernel||(int?)kernel["failed"]!=0
                ||(int?)kernel["inputAffinities"]!=2||(int?)kernel["uniqueAffinities"]!=1||(int?)kernel["patchRequests"]!=3
                ||(int?)kernel["generatorDisposeRequests"]!=1||(int?)kernel["failureChecks"]!=1||(bool?)kernel["failureWrapped"]!=true)
                errors.Add("Input Affinity kernel discovery and failure checks must pass");
            if(input["affinitySpawnLifetime"]?["kernel"] is not JObject discovery
                ||(int?)discovery["pluginEntries"]!=2||(int?)discovery["pluginLookups"]!=1||(int?)discovery["generatorFactories"]!=1
                ||discovery["sceneBindings"] is not JObject scene||(int?)scene["cases"]!=2||(int?)scene["failed"]!=0
                ||(int?)scene["projectContextAdapters"]!=2||(int?)scene["checks"]!=11)
                errors.Add("Input Affinity plugin discovery and scene bindings must pass");
            if(input["affinitySpawnLifetime"]?["kernel"]?["sceneBindings"]?["hierarchy"] is not JObject hierarchy
                ||(int?)hierarchy["containers"]!=5||(int?)hierarchy["uniqueManagers"]!=5||(int?)hierarchy["injectorCalls"]!=3
                ||(int?)hierarchy["checks"]!=17||(string?)hierarchy["managerSource"]!="Local"||!JToken.DeepEquals(hierarchy["affinityCounts"],new JArray(0,1,0,1,0)))
                errors.Add("Input Affinity container hierarchy checks must pass");
            if(input["affinitySpawnLifetime"]?["kernel"]?["sceneBindings"]?["lookupQuiescence"] is not JObject quiescence
                ||(int?)quiescence["ownedProvidersBefore"]!=1||(int?)quiescence["ownedProvidersAfter"]!=0||(int?)quiescence["neutralConstructions"]!=1)
                errors.Add("Input native lookup pool transition must be explicit");
            if(input["affinitySpawnLifetime"]?["kernel"]?["sceneBindings"]?["roots"] is not JObject roots
                ||(int?)roots["passes"]!=1||(bool?)roots["repeatedRootsRejected"]!=true||(int?)roots["constructions"]!=1||(int?)roots["lateDisposals"]!=1||(bool?)roots["factoryRestored"]!=true
                ||(int?)roots["inputAffinities"]!=2||(int?)roots["uniqueAffinities"]!=1||(int?)roots["patchRequests"]!=3||(int?)roots["generatorDisposeRequests"]!=1)
                errors.Add("Input native root resolution and scoped factory restoration must pass");
            if(input["affinitySpawnLifetime"]?["kernel"]?["sceneBindings"]?["roots"]?["disposableManager"] is not JObject disposal
                ||new[]{"ordinaryDisposeSkippedKernel","repeatedDisposeRejected","repeatedLateDisposeRejected","patcherFieldRestored","proxyCallbackCleared"}.Any(k=>(bool?)disposal[k]!=true))
                errors.Add("Input native DisposableManager lifecycle must pass");
            if(input["affinitySpawnLifetime"]?["kernel"] is not JObject kernelLifetime
                ||(int?)kernelLifetime["weakReferences"]!=71||(int?)kernelLifetime["retainedObjects"]!=0)
                errors.Add("Input Affinity kernel receivers must be released");
            if(input["affinitySpawnLifetime"]?["kernel"]?["siraLifecycle"] is not JObject sira
                ||(int?)sira["cases"]!=7||(int?)sira["failed"]!=0||(int?)sira["factoryAdapters"]!=1||(int?)sira["calls"]!=7||(bool?)sira["factoryCleared"]!=true
                ||sira["results"] is not JArray sr||sr.Count!=7
                ||sr.Any(r=>(bool?)r["canceled"]!=true)
                ||(int?)sr[0]["invoked"]!=0||(int?)sr[0]["scheduled"]!=0
                ||(int?)sr[1]["invoked"]!=3||(int?)sr[1]["scheduled"]!=4||(int?)sr[1]["canceledTasks"]!=0
                ||(int?)sr[2]["invoked"]!=0||(int?)sr[2]["canceledTasks"]!=2
                ||sr.Skip(3).Any(r=>(int?)r["invoked"]!=1||(int?)r["canceledTasks"]!=0)
                ||(int?)sr[5]["scheduled"]!=2||(int?)sr[6]["scheduled"]!=1
                ||(int?)sr[6]["managerDispatchAdapters"]!=1||new[]{"diSingleton","repeatedManagerInitRejected","managerCanceled"}.Any(k=>(bool?)sr[6][k]!=true))
                errors.Add("Input Sira kernel async lifecycle must pass");
            if(input["affinitySpawnLifetime"]?["kernel"]?["sceneBindings"]?["roots"]?["disposalBindings"] is not JObject order
                ||new[]{"singleton","repeatedDisposeRejected","repeatedLateDisposeRejected","callbacksCleared"}.Any(k=>(bool?)order[k]!=true)
                ||!JToken.DeepEquals(order["trace"],new JArray("ordinary-high","ordinary-low","late-high","kernel","late-low"))
                ||!JToken.DeepEquals(order["priorities"],new JArray(10,0,-10)))
                errors.Add("Input native DI disposal priorities must pass");
            var dispatches=input.SelectTokens("$..spawnDispatch").OfType<JObject>().ToArray();
            if(dispatches.Length!=count*5||dispatches.Any(d=>d["lifetime"] is not JObject state
                ||(int?)state["boundCallbacks"]!=3||(int?)state["restoredCallbacks"]!=3
                ||(int?)state["retainedCallbacks"]!=0||(bool?)state["disposed"]!=true))
                errors.Add("Input direct spawn callbacks must be complete and restored");
        }
        Normalize(baseline);Normalize(native);
        bool identical=JToken.DeepEquals(baseline,native);
        if(!identical)errors.Add("Semantic result snapshots differ after explicit diagnostic-only normalization");
        return new{cases=1,failed=errors.Count,comparedCases=(int?)baseline["cases"],comparedTransformations=(int?)baseline["transformedCases"],identical,errors,
            ignoredFields=Diagnostics.Order(StringComparer.Ordinal).ToArray(),normalizedKnownQuaternionStackTrace=true,scope="Whole result snapshots including data roles, cache fields, callback registration, movement/pool/reload and lifetime counters. Backend flags, scope prose, generated assembly identity and variable stack frames of the already classified native quaternion boundary normalized to its type/message and exact anchors; exception types/messages/counts retained; no metric/geometry fields removed."};
    }
}
