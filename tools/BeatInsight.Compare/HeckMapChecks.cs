using System.Collections;
using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.CompilerServices;
using System.Runtime.Loader;
using Mono.Cecil;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace BeatInsight.Compare;

public static class HeckMapChecks
{
    static readonly List<string> logErrors=new();
    static Type? loggerType;
    static bool propertiesRegistered;
    public static object Identity(object instructions)=>instructions;
    public static void Log(object? logger,object message,bool error){if(error)logErrors.Add(message.ToString()??"");}
    public static void PluginLog(object level,string message){if(level.ToString() is string name&&(name.Contains("Error",StringComparison.Ordinal)||name.Contains("Critical",StringComparison.Ordinal)))logErrors.Add(message);}
    public static object Run(string gameRoot,BeatmapData map,float bpm,BeatmapData? originalMap=null,bool leftHanded=false,Action<string,object>? capture=null)
    {
        const BindingFlags flags=BindingFlags.Public|BindingFlags.NonPublic|BindingFlags.Instance|BindingFlags.Static;
        if(Comparison.HashFile(Path.Combine(gameRoot,"Plugins/Heck.dll"))!="9E5163F09E122713543FB69F89799D1121FB712820E6B4350F7A30C5D790793C")throw new InvalidDataException("Reinspect changed map deserializer");
        if(Comparison.HashFile(Path.Combine(gameRoot,"Plugins/Chroma.dll"))!="7B0B532AED6C841C631686F44C739AACFFCAEE05F2E9A9C3AF516C2C4920BC37")throw new InvalidDataException("Reinspect changed track property registrations");
        var heck=AssemblyLoadContext.Default.LoadFromAssemblyPath(Path.Combine(gameRoot,"Plugins/Heck.dll"));
        var noodle=AssemblyLoadContext.Default.Assemblies.Single(a=>a.GetName().Name=="NoodleExtensions");
        var chroma=AssemblyLoadContext.Default.LoadFromAssemblyPath(Path.Combine(gameRoot,"Plugins/Chroma.dll"));
        var pluginLogs=new[]{chroma.GetType("Chroma.Plugin",true)!.GetField("<Log>k__BackingField",flags)!,heck.GetType("Heck.Plugin",true)!.GetField("<Log>k__BackingField",flags)!,noodle.GetType("NoodleExtensions.Plugin",true)!.GetField("<Log>k__BackingField",flags)!};
        var previousLogs=pluginLogs.Select(f=>f.GetValue(null)).ToArray();
        if(loggerType==null)
        {
            var loggerBase=pluginLogs[0].FieldType;var log=loggerBase.GetMethods(flags).Single(m=>m.Name=="Log"&&m.IsAbstract);
            var assembly=AssemblyBuilder.DefineDynamicAssembly(new AssemblyName("BeatInsight.ControlledLogger"),AssemblyBuilderAccess.RunAndCollect);
            var type=assembly.DefineDynamicModule("Logger").DefineType("ControlledLogger",System.Reflection.TypeAttributes.Public,loggerBase);
            type.DefineDefaultConstructor(System.Reflection.MethodAttributes.Public);
            var method=type.DefineMethod("Log",System.Reflection.MethodAttributes.Public|System.Reflection.MethodAttributes.Virtual,typeof(void),log.GetParameters().Select(p=>p.ParameterType).ToArray());
            var il=method.GetILGenerator();il.Emit(OpCodes.Ldarg_1);il.Emit(OpCodes.Box,log.GetParameters()[0].ParameterType);il.Emit(OpCodes.Ldarg_2);
            il.Emit(OpCodes.Call,typeof(HeckMapChecks).GetMethod(nameof(PluginLog))!);il.Emit(OpCodes.Ret);type.DefineMethodOverride(method,log);loggerType=type.CreateType()!;
        }
        var logger=Activator.CreateInstance(loggerType)!;
        for(int i=0;i<pluginLogs.Length;i++)pluginLogs[i].SetValue(null,logger);
        try
        {
        var trackType=heck.GetType("Heck.Animation.Track",true)!;
        if(!propertiesRegistered)foreach(string library in new[]{"Heck","NoodleExtensions","Chroma"})
        {
            using var module=ModuleDefinition.ReadModule(Path.Combine(gameRoot,"Plugins",library+".dll"));
            foreach(var method in module.Types.Single(t=>t.FullName==library+".Plugin").Methods.Where(m=>m.HasBody))
            foreach(var i in method.Body.Instructions)
            {
                if(i.Operand is not GenericInstanceMethod call||call.DeclaringType.FullName!="Heck.Animation.Track"||call.Name is not ("RegisterProperty" or "RegisterPathProperty"))continue;
                if(i.Previous.Operand is not string alias||i.Previous.Previous.Operand is not string name)throw new InvalidDataException("Registration literals changed");
                Type type=call.GenericArguments.Single().FullName switch {"System.Single"=>typeof(float),"UnityEngine.Vector3"=>typeof(Vector3),"UnityEngine.Vector4"=>typeof(Vector4),"UnityEngine.Quaternion"=>typeof(Quaternion),_=>throw new InvalidDataException("Unvalidated registration type")};
                trackType.GetMethods(flags).Single(m=>m.Name==call.Name&&m.IsGenericMethodDefinition).MakeGenericMethod(type).Invoke(null,new object[]{name,alias});
            }
        }
        propertiesRegistered=true;
        var managerType=heck.GetType("Heck.Deserialize.DeserializerManager",true)!;
        var original=managerType.GetMethod("DeserializeBeatmapData",flags)!;
        var logStubs=new Dictionary<MethodInfo,DynamicMethod>();
        var compiled=InstalledTranspilerCompiler.Compile(original,typeof(HeckMapChecks).GetMethod(nameof(Identity))!,(code,operand)=>
        {
            if(operand is MethodInfo method&&method.DeclaringType?.FullName=="SiraUtil.Logging.SiraLog")
            {
                if(method.Name is not ("Trace" or "Error")||method.GetParameters().Length!=1)throw new InvalidDataException("Manager logging ABI changed");
                if(!logStubs.TryGetValue(method,out var stub))
                {
                    stub=new DynamicMethod("ControlledMapLog"+method.Name,typeof(void),new[]{method.DeclaringType,typeof(object)},typeof(HeckMapChecks).Module,true);
                    var il=stub.GetILGenerator();il.Emit(OpCodes.Ldarg_0);il.Emit(OpCodes.Ldarg_1);il.Emit(method.Name=="Error"?OpCodes.Ldc_I4_1:OpCodes.Ldc_I4_0);
                    il.Emit(OpCodes.Call,typeof(HeckMapChecks).GetMethod(nameof(Log))!);il.Emit(OpCodes.Ret);logStubs[method]=stub;
                }
                return(OpCodes.Call,stub);
            }
            return(code,operand);
        },exceptionBlocks:true);
        var manager=Activator.CreateInstance(managerType,flags,null,new object?[]{null},null)!;
        foreach(var binding in new[]{("Heck",heck.GetType("Heck.CustomDataDeserializer",true)!),("NoodleExtensions",noodle.GetType("NoodleExtensions.CustomDataDeserializer",true)!),("Chroma",chroma.GetType("Chroma.CustomDataDeserializer",true)!)})
        {
            var registration=managerType.GetMethod("Register",flags)!.Invoke(manager,new object[]{binding.Item1,binding.Item2})!;
            registration.GetType().GetProperty("Enabled",flags)!.SetValue(registration,true);
        }
        var disabled=managerType.GetMethod("Register",flags)!.Invoke(manager,new object[]{"probe-disabled",heck.GetType("Heck.CustomDataDeserializer",true)!})!;
        var levelType=original.GetParameters()[0].ParameterType;
        var level=RuntimeHelpers.GetUninitializedObject(levelType);levelType.GetField("beatsPerMinute",flags)!.SetValue(level,bpm);
        object?[] args={manager,level,map,originalMap??map,leftHanded,null,null};logErrors.Clear();
        compiled.Method.Invoke(null,args);
        var tracks=(IDictionary)args[5]!;
        var bindings=((IEnumerable)args[6]!).Cast<object>().ToArray();var results=new List<object>();int failed=0;
        if(bindings.Length!=3||(bool)disabled.GetType().GetProperty("Enabled",flags)!.GetValue(disabled)!
            ||disabled.GetType().GetField("_instance",flags)!.GetValue(disabled)!=null)failed++;
        int expectedObjects=((IEnumerable)map.GetType().GetProperty("beatmapObjectDatas")!.GetValue(map)!).Cast<object>().Count();
        if(expectedObjects!=OracleBridge.GetItems(map).OfType<BeatmapObjectData>().Count())failed++;
        foreach(var binding in bindings)
        {
            string id=(string)binding.GetType().GetField("Item1")!.GetValue(binding)!;
            var data=binding.GetType().GetField("Item2")!.GetValue(binding)!;
            var objects=(IDictionary)data.GetType().GetProperty("ObjectCustomDatas",flags)!.GetValue(data)!;
            var events=(IDictionary)data.GetType().GetProperty("CustomEventCustomDatas",flags)!.GetValue(data)!;
            var ordinaryEvents=(IDictionary)data.GetType().GetProperty("EventCustomDatas",flags)!.GetValue(data)!;
            var ordinaryResolve=data.GetType().GetMethods(flags).Single(m=>m.Name=="Resolve"&&m.IsGenericMethodDefinition&&m.GetParameters().Length==2&&m.GetParameters()[0].ParameterType==typeof(BeatmapEventData));
            var mapEvents=OracleBridge.GetItems(map).OfType<BeatmapEventData>().ToHashSet(ReferenceEqualityComparer.Instance);
            foreach(DictionaryEntry entry in ordinaryEvents)
            {
                object?[] resolved={entry.Key,null};
                if(!mapEvents.Contains(entry.Key)||!(bool)ordinaryResolve.MakeGenericMethod(entry.Value!.GetType()).Invoke(data,resolved)!||!ReferenceEquals(entry.Value,resolved[1]))failed++;
            }
            int sharedTrackReferences=0;
            if(id!="Chroma"&&objects.Count!=expectedObjects)failed++;
            var resolve=data.GetType().GetMethods(flags).Single(m=>m.Name=="Resolve"&&m.IsGenericMethodDefinition&&m.GetParameters().Length==2&&m.GetParameters()[0].ParameterType==typeof(BeatmapObjectData));
            foreach(DictionaryEntry entry in objects)
            {
                if(!OracleBridge.GetItems(map).Any(item=>ReferenceEquals(item,entry.Key))){failed++;continue;}
                object?[] resolved={entry.Key,null};
                if(!(bool)resolve.MakeGenericMethod(entry.Value!.GetType()).Invoke(data,resolved)!||!ReferenceEquals(entry.Value,resolved[1]))failed++;
                if(entry.Value!.GetType().GetProperty("Track",flags)?.GetValue(entry.Value) is IEnumerable assigned)
                    foreach(var track in assigned)
                    {if(!tracks.Values.Cast<object>().Any(t=>ReferenceEquals(t,track)))failed++;else sharedTrackReferences++;}
            }
            var eventResolve=data.GetType().GetMethods(flags).Single(m=>m.Name=="Resolve"&&m.IsGenericMethodDefinition&&m.GetParameters().Length==2&&m.GetParameters()[0].ParameterType.Name=="CustomEventData");
            int sharedEventTrackReferences=0;
            foreach(DictionaryEntry entry in events)
            {
                object?[] resolved={entry.Key,null};
                if(!(bool)eventResolve.MakeGenericMethod(entry.Value!.GetType()).Invoke(data,resolved)!||!ReferenceEquals(entry.Value,resolved[1]))failed++;
                if(entry.Value.GetType().GetProperty("Track",flags)?.GetValue(entry.Value) is IEnumerable directTracks)
                    foreach(var track in directTracks)
                    {if(!tracks.Values.Cast<object>().Any(t=>ReferenceEquals(t,track)))failed++;else sharedEventTrackReferences++;}
                if(entry.Value.GetType().FullName=="Heck.HeckCoroutineEventData"&&entry.Value.GetType().GetProperty("CoroutineInfos",flags)?.GetValue(entry.Value) is IEnumerable infos)
                    foreach(var info in infos)
                    {
                        if(info==null)continue;
                        var property=info.GetType().GetProperty("Track",flags)??throw new InvalidDataException("Unknown coroutine info: "+info.GetType().FullName);
                        var track=property.GetValue(info);
                        if(!tracks.Values.Cast<object>().Any(t=>ReferenceEquals(t,track)))failed++;else sharedEventTrackReferences++;
                    }
            }
            // Remap replaces all dictionary references; it does not translate map item keys.
            var dataType=data.GetType();var constructor=dataType.GetConstructors(flags).Single();
            object EmptyData()=>constructor.Invoke(constructor.GetParameters().Select(p=>Activator.CreateInstance(p.ParameterType)).ToArray());
            var target=EmptyData();var remap=dataType.GetMethod("Remap",flags)!;
            remap.Invoke(target,new[]{data});int remapChecks=0;
            foreach(string property in new[]{"ObjectCustomDatas","EventCustomDatas","CustomEventCustomDatas"})
            {
                if(!ReferenceEquals(dataType.GetProperty(property,flags)!.GetValue(target),dataType.GetProperty(property,flags)!.GetValue(data)))failed++;
                remapChecks++;
            }
            foreach(DictionaryEntry entry in objects)
            {
                object?[] resolved={entry.Key,null};
                if(!(bool)resolve.MakeGenericMethod(entry.Value!.GetType()).Invoke(target,resolved)!||!ReferenceEquals(entry.Value,resolved[1]))failed++;
                remapChecks++;
            }
            foreach(DictionaryEntry entry in events)
            {
                object?[] resolved={entry.Key,null};
                if(!(bool)eventResolve.MakeGenericMethod(entry.Value!.GetType()).Invoke(target,resolved)!||!ReferenceEquals(entry.Value,resolved[1]))failed++;
                remapChecks++;
            }
            var isolated=EmptyData();int registeredCopies=0;
            foreach(DictionaryEntry entry in objects)
            {
                var copy=((BeatmapObjectData)entry.Key).GetCopy();
                object?[] missing={copy,null};
                if((bool)resolve.MakeGenericMethod(entry.Value!.GetType()).Invoke(target,missing)!)failed++;
                dataType.GetMethod("RegisterNewObject",flags)!.Invoke(isolated,new[]{copy,entry.Value});
                object?[] found={copy,null};
                if(!(bool)resolve.MakeGenericMethod(entry.Value.GetType()).Invoke(isolated,found)!||!ReferenceEquals(entry.Value,found[1]))failed++;
                registeredCopies++;
            }
            remap.Invoke(target,new[]{isolated});
            foreach(DictionaryEntry entry in objects)
            {
                object?[] absent={entry.Key,null};
                if((bool)resolve.MakeGenericMethod(entry.Value!.GetType()).Invoke(target,absent)!)failed++;
                remapChecks++;
            }
            remap.Invoke(target,new[]{data});
            if(((IDictionary)dataType.GetProperty("ObjectCustomDatas",flags)!.GetValue(target)!).Count!=objects.Count)failed++;
            capture?.Invoke(id,data);
            results.Add(new{id,objects=objects.Count,events=events.Count,ordinaryEvents=ordinaryEvents.Count,sharedTrackReferences,sharedEventTrackReferences,remapChecks,registeredCopies});
        }
        var objectCallbacks=NoodleObjectCallbacksChecks.Run(gameRoot,map,bpm,args[6]!);
        failed+=(int)JObject.FromObject(objectCallbacks)["failed"]!;
        var burstRegistration=HeckBurstRegistrationChecks.Run(gameRoot,map,args[6]!);
        failed+=(int)JObject.FromObject(burstRegistration)["failed"]!;
        int structuralFailed=failed;
        int unityQuaternionFailures=0;
        bool onlyQuaternionFailures=logErrors.Count>0&&logErrors.Count%2==0;
        for(int i=0;i+1<logErrors.Count;i+=2)
        {
            bool expected=logErrors[i].StartsWith("Could not parse custom data for custom event [AnimateTrack]",StringComparison.Ordinal)
                &&logErrors[i+1].StartsWith("System.Security.SecurityException:",StringComparison.Ordinal)
                &&logErrors[i+1].Contains("UnityEngine.Quaternion.Internal_FromEulerRad",StringComparison.Ordinal)
                &&logErrors[i+1].Contains("Heck.Animation.QuaternionPointDefinition.CreatePointData",StringComparison.Ordinal);
            onlyQuaternionFailures&=expected;if(expected)unityQuaternionFailures++;
        }
        failed+=logErrors.Count;
        return new{cases=1,failed,structuralFailed,onlyQuaternionFailures,unityQuaternionFailures,trackNames=tracks.Keys.Cast<string>().Order(StringComparer.Ordinal).ToArray(),bindings=results,burstRegistration,objectCallbacks,
            errors=logErrors.ToArray(),managerExceptionRegions=original.GetMethodBody()!.ExceptionHandlingClauses.Count,
            scope="Full installed Heck DeserializerManager with actual register/enable/Create/early-deserialize/module-deserialize; real track builder and point definitions; Heck, Noodle and Chroma modules including environment/material early track registration. Manager SiraLog calls routed to memory; no plugin startup, environment object construction, event dispatch, DI lifecycle or all-mod ordering."};
        }
        finally{for(int i=0;i<pluginLogs.Length;i++)pluginLogs[i].SetValue(null,previousLogs[i]);}
    }
}
