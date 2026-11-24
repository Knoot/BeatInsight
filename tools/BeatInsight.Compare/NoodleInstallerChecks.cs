using System.Collections;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.Loader;

namespace BeatInsight.Compare;

static class NoodleInstallerChecks
{
    const BindingFlags flags=BindingFlags.Public|BindingFlags.NonPublic|BindingFlags.Instance|BindingFlags.Static;
    static FieldInfo Field(Type type,string name){for(Type? t=type;t!=null;t=t.BaseType)if(t.GetField(name,flags|BindingFlags.DeclaredOnly) is FieldInfo f)return f;throw new MissingFieldException(type.FullName,name);}
    public static object Run(string gameRoot)
    {
        if(!NativeFieldAccessorChecks.Enabled)throw new InvalidOperationException("Native accessor runtime required");
        foreach(var pin in new[]{("Beat Saber_Data/Managed/Tweening.dll","52FBA922F7A8DE0A448A295C413B4355A998DC3266F7E773844DFC717B070B37"),("Plugins/Heck.dll","9E5163F09E122713543FB69F89799D1121FB712820E6B4350F7A30C5D790793C"),("Plugins/NoodleExtensions.dll","FE50124E33631EED7ECB78EA1A19BB7EA64CC12974B0298DAD991672C2ABDA95"),("Beat Saber_Data/Managed/Main.dll","A276C8A50ACFAD69A3DD3A82CDE08571C3262344702B9B014C89756771B4495E"),("Beat Saber_Data/Managed/Zenject.dll","2031FF13D801ACCF409E3F4D02ADB9C61AA7A07DA8E23A8A7F996716D0E82E49")})
            if(Comparison.HashFile(Path.Combine(gameRoot,pin.Item1))!=pin.Item2)throw new InvalidDataException("Reinspect changed player installer");

        var noodle=AssemblyLoadContext.Default.LoadFromAssemblyPath(Path.Combine(gameRoot,"Plugins/NoodleExtensions.dll"));
        var heck=AssemblyLoadContext.Default.LoadFromAssemblyPath(Path.Combine(gameRoot,"Plugins/Heck.dll"));
        var main=AssemblyLoadContext.Default.LoadFromAssemblyPath(Path.Combine(gameRoot,"Beat Saber_Data/Managed/Main.dll"));
        var zenject=AssemblyLoadContext.Default.LoadFromAssemblyPath(Path.Combine(gameRoot,"Beat Saber_Data/Managed/Zenject.dll"));
        var containerType=zenject.GetType("Zenject.DiContainer",true)!;
        var featureType=noodle.GetType("NoodleExtensions.FeaturesModule",true)!;
        var installerType=noodle.GetType("NoodleExtensions.Installers.NoodlePlayerInstaller",true)!;
        var poolType=noodle.GetType("NoodleExtensions.Managers.NoodleMovementDataProvider+Pool",true)!;
        var helperType=noodle.GetType("NoodleExtensions.HarmonyPatches.SmallFixes.InitializedSpawnMovementData",true)!;
        var wrapperType=noodle.GetType("NoodleExtensions.Managers.NoodleMovementDataProvider",true)!;
        int failed=0,checks=0,callbackSteps=0,subscriptionChecks=0;var results=new List<object>();
        foreach(bool active in new[]{false,true})foreach(int mode in new[]{1,2})foreach(bool dynamicNjs in new[]{false,true})
        {
            var map=GameLoader.Load(gameRoot,"""{"version":"3.3.0","colorNotes":[{"b":2,"x":0,"y":0,"c":0,"d":1}]}""",120,noodleExtensions:true);
            if(dynamicNjs)
            {
                var eventType=typeof(NoteData).Assembly.GetType("NoteJumpSpeedEventData",true)!;
                var eventCtor=eventType.GetConstructors(flags).Single(c=>!c.IsStatic);var ease=eventCtor.GetParameters()[2].ParameterType;
                foreach(var item in new (float Time,float Value,string Curve)[]{(0f,0f,"None"),(1f,10f,"Linear"),(2f,-5f,"Linear"),(3f,0f,"Linear"),(4f,5f,"None")})
                {
                    var ev=eventCtor.Invoke(new[]{(object)item.Time,item.Value,Enum.Parse(ease,item.Curve),false});
                    map.GetType().GetMethod("InsertBeatmapEventData",flags)!.Invoke(map,new[]{ev});
                }
                map.GetType().GetMethod("ProcessAndSortBeatmapData",flags)!.Invoke(map,null);
            }
            var container=Activator.CreateInstance(containerType)!;
            void Bind(Type contract,object value,string? id=null)
            {
                var binder=containerType.GetMethods(flags).Single(m=>m.Name=="BindInstance"&&m.IsGenericMethodDefinition).MakeGenericMethod(contract).Invoke(container,new[]{value})!;
                if(id!=null)binder.GetType().GetMethod("WithId",flags)!.Invoke(binder,new object[]{id});
            }
            object Resolve(Type type)=>containerType.GetMethods(flags).Single(m=>m.Name=="Resolve"&&!m.IsGenericMethod&&m.GetParameters().Length==1&&m.GetParameters()[0].ParameterType==typeof(Type)).Invoke(container,new object[]{type})!;
            bool Has(Type type)=>(bool)containerType.GetMethods(flags).Single(m=>m.Name=="HasBinding"&&!m.IsGenericMethod&&m.GetParameters().Length==1&&m.GetParameters()[0].ParameterType==typeof(Type)).Invoke(container,new object[]{type})!;
            var feature=Activator.CreateInstance(featureType,true)!;Field(featureType,"<Active>k__BackingField").SetValue(feature,active);Bind(featureType,feature);
            var instantiate=containerType.GetMethods(flags).Single(m=>m.Name=="Instantiate"&&!m.IsGenericMethod&&m.GetParameters().Length==1&&m.GetParameters()[0].ParameterType==typeof(Type));
            var installer=instantiate.Invoke(container,new object[]{installerType})!;
            installerType.GetMethod("InstallBindings",flags)!.Invoke(installer,null);
            bool installed=Has(poolType)&&Has(helperType)&&Has(noodle.GetType("NoodleExtensions.Managers.SpawnDataManager",true)!);
            if(installed!=active||Has(poolType)!=active||Has(helperType)!=active||Has(noodle.GetType("NoodleExtensions.Managers.SpawnDataManager",true)!)!=active)failed++;checks++;
            if(!active){results.Add(new{active,mode,dynamicNjs,installed});continue;}
            var helperCtor=helperType.GetConstructors(flags).Single();var dependencies=helperCtor.GetParameters();
            var initType=dependencies[0].ParameterType;
            var initCtor=initType.GetConstructors(flags).Single();
            var init=initCtor.Invoke(new[]{(object)120f,4,20f,Enum.ToObject(initCtor.GetParameters()[3].ParameterType,mode),mode==1?0f:.75f});
            Bind(initType,init);
            var height=RuntimeHelpers.GetUninitializedObject(main.GetType("PlayerHeightToJumpOffsetYProvider",true)!);Bind(dependencies[1].ParameterType,height);
            var movement=Activator.CreateInstance(main.GetType("BeatmapObjectSpawnMovementData",true)!,true)!;
            var controller=RuntimeHelpers.GetUninitializedObject(dependencies[2].ParameterType);
            Field(dependencies[2].ParameterType,"_beatmapObjectSpawnMovementData").SetValue(controller,movement);Bind(dependencies[2].ParameterType,controller);
            Bind(dependencies[3].ParameterType,map);
            var originalType=main.GetType("VariableMovementDataProvider",true)!;
            string[] mapEvents={"beatmapEventDataWasInsertedEvent","beatmapEventDataWillBeRemovedEvent","beatmapEventDataWasRemovedEvent"};
            int Subscribers(object receiver,Type type,string name)=>((Delegate?)Field(type,name).GetValue(receiver))?.GetInvocationList().Length??0;
            var subscribersBefore=mapEvents.Select(name=>Subscribers(map,map.GetType(),name)).ToArray();
            var callbacksType=main.GetType("BeatmapCallbacksController",true)!;
            var callbacksInit=Activator.CreateInstance(callbacksType.GetNestedType("InitData",flags)!,new object[]{map,0f,false})!;
            var callbacks=Activator.CreateInstance(callbacksType,new[]{callbacksInit})!;
            var original=Activator.CreateInstance(originalType,new object?[]{RuntimeHelpers.GetUninitializedObject(main.GetType("PlayerTransforms",true)!),callbacks})!;
            try
            {
            Bind(dependencies[4].ParameterType,original);Bind(originalType,original);
            var dataType=heck.GetType("Heck.Deserialize.DeserializedData",true)!;var dataCtor=dataType.GetConstructors(flags).Single();
            var data=dataCtor.Invoke(dataCtor.GetParameters().Select(p=>Activator.CreateInstance(p.ParameterType)).ToArray());Bind(dataType,data,"NoodleExtensions");
            var pool=Resolve(poolType);var helper=Resolve(helperType);
            var inactive=Field(poolType,"_inactiveItems").GetValue(pool)!;int Inactive()=>(int)inactive.GetType().GetProperty("Count")!.GetValue(inactive)!;
            if(!ReferenceEquals(pool,Resolve(poolType))||!ReferenceEquals(helper,Resolve(helperType))||Inactive()!=40)failed++;checks++;
            if(!ReferenceEquals(Field(helperType,"<MovementData>k__BackingField").GetValue(helper),movement))failed++;checks++;
            float half=(float)originalType.GetProperty("halfJumpDuration",flags)!.GetValue(original)!;
            float expectedHalf=mode==1?.5f:.75f;
            if(Math.Abs(half-expectedHalf)>1e-5||(float)originalType.GetProperty("noteJumpSpeed",flags)!.GetValue(original)! !=20f
                ||(int)movement.GetType().GetProperty("noteLinesCount",flags)!.GetValue(movement)! !=4)failed++;checks++;
            var note=OracleBridge.GetItems(map).OfType<NoteData>().Single();
            var item=poolType.GetMethod("Spawn",flags)!.Invoke(pool,new object[]{note})!;
            if(!ReferenceEquals(Field(wrapperType,"_original").GetValue(item),original)||!ReferenceEquals(Field(wrapperType,"_deserializedData").GetValue(item),data)||Inactive()!=39)failed++;checks++;
            poolType.BaseType!.BaseType!.GetMethod("Despawn",flags)!.Invoke(pool,new[]{item});
            if(Inactive()!=40||(int)Field(poolType,"_activeCount").GetValue(pool)! !=0)failed++;checks++;
            var poolStress=InstalledMovementPoolStressChecks.Run(pool,note);
            if(Subscribers(callbacks,callbacksType,"willStartProcessingCallbacksThisFrameEvent")!=1)failed++;subscriptionChecks++;
            foreach(float time in dynamicNjs?new[]{0f,.25f,.5f,.75f,1f,1.25f,1.5f,1.75f,2f,2.25f,2.5f,2.75f,3f,4f,4.01f}:new[]{0f,.1f,1f})
            {
                callbacksType.GetMethod("ManualUpdate",flags)!.Invoke(callbacks,new object[]{time});
                float expectedSpeed=!dynamicNjs?20:time<=1?20+10*time:time<=2?30-15*(time-1):time<=3?15+5*(time-2):time<=4?20:25;
                float expectedCurrentHalf=mode==1?.5f/Math.Min(expectedSpeed/20,1):.75f;
                if(Math.Abs((float)originalType.GetProperty("noteJumpSpeed",flags)!.GetValue(original)!-expectedSpeed)>1e-5
                    ||Math.Abs((float)originalType.GetProperty("halfJumpDuration",flags)!.GetValue(original)!-expectedCurrentHalf)>1e-5)failed++;
                callbackSteps++;
            }
            results.Add(new{active,mode,dynamicNjs,installed,initialSize=40,halfJumpDuration=half,activeAfter=0,inactiveAfter=Inactive(),poolStress});
            }
            finally
            {
                try{originalType.GetMethod("Dispose",flags)!.Invoke(original,null);}
                finally{callbacksType.GetMethod("Dispose",flags)!.Invoke(callbacks,null);}
                if(Subscribers(callbacks,callbacksType,"willStartProcessingCallbacksThisFrameEvent")!=0)failed++;subscriptionChecks++;
                for(int i=0;i<mapEvents.Length;i++)
                {if(Subscribers(map,map.GetType(),mapEvents[i])!=subscribersBefore[i])failed++;subscriptionChecks++;}
            }
        }
        return new{cases=8,checks,failed,callbackSteps,subscriptionChecks,results,scope="Actual full NoodlePlayerInstaller.InstallBindings including Active gate and real BindMemoryPool WithInitialSize(40); installer DI construction, actual InitializedSpawnMovementData constructor/Init and selected pool/provider resolution. Controlled InitData, zero-height/player and spawn-controller receivers; real BeatmapCallbacksController and VariableMovementDataProvider constructors, managed callback steps and Dispose subscription cleanup; other lazy affinity bindings not instantiated, no ResolveRoots/Unity lifecycle/scene execution."};
    }
}
