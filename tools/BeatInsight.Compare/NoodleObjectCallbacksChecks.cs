using System.Collections;
using System.Linq.Expressions;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.Loader;
using BeatInsight.Formats;

namespace BeatInsight.Compare;

static class NoodleObjectCallbacksChecks
{
    const BindingFlags flags=BindingFlags.Public|BindingFlags.NonPublic|BindingFlags.Instance|BindingFlags.Static;
    public static object Run(string gameRoot,BeatmapData map,float bpm,object moduleBindings)
    {
        var binding=((IEnumerable)moduleBindings).Cast<object>().Single(b=>(string)b.GetType().GetField("Item1")!.GetValue(b)! =="NoodleExtensions");
        var data=binding.GetType().GetField("Item2")!.GetValue(binding)!;
        var objects=(IDictionary)data.GetType().GetProperty("ObjectCustomDatas",flags)!.GetValue(data)!;
        var noodle=AssemblyLoadContext.Default.Assemblies.Single(a=>a.GetName().Name=="NoodleExtensions");
        var main=AssemblyLoadContext.Default.LoadFromAssemblyPath(Path.Combine(gameRoot,"Beat Saber_Data/Managed/Main.dll"));
        var managerType=noodle.GetType("NoodleExtensions.Managers.NoodleObjectsCallbacksManager",true)!;
        var spawnType=noodle.GetType("NoodleExtensions.Managers.SpawnDataManager",true)!;
        var spawnCtor=spawnType.GetConstructors(flags).Single();var sp=spawnCtor.GetParameters();
        var helperType=sp[0].ParameterType;var helperCtor=helperType.GetConstructors(flags).Single();var hp=helperCtor.GetParameters();
        var initCtor=sp[1].ParameterType.GetConstructors(flags).Single();
        var init=initCtor.Invoke(new[]{(object)bpm,4,20f,Enum.ToObject(initCtor.GetParameters()[3].ParameterType,1),0f});
        var movement=Activator.CreateInstance(main.GetType("BeatmapObjectSpawnMovementData",true)!,true)!;
        var controller=RuntimeHelpers.GetUninitializedObject(hp[2].ParameterType);hp[2].ParameterType.GetField("_beatmapObjectSpawnMovementData",flags)!.SetValue(controller,movement);
        var variable=Activator.CreateInstance(sp[2].ParameterType,new object?[]{RuntimeHelpers.GetUninitializedObject(main.GetType("PlayerTransforms",true)!),null})!;
        using var variableLifetime=(IDisposable)variable;
        var height=RuntimeHelpers.GetUninitializedObject(main.GetType("PlayerHeightToJumpOffsetYProvider",true)!);
        var helper=helperCtor.Invoke(new[]{init,height,controller,map,variable});
        var spawn=spawnCtor.Invoke(new[]{helper,init,variable,data});
        var ctor=managerType.GetConstructors(flags).Single();
        var callbackInit=Activator.CreateInstance(ctor.GetParameters()[0].ParameterType,new object[]{map,0f,false})!;
        var manager=ctor.Invoke(new object?[]{callbackInit,spawn,data,null});
        var patch=NoodleCallbackPatchSession.Current??throw new InvalidOperationException("Missing callback patch session");
        int patchRegistrationChecks=0,patchFailures=0;
        patch.Bind(manager);
        try
        {
            var controllerType=controller.GetType();
            void Set(string name,object value)=>controllerType.GetField(name,flags)!.SetValue(controller,value);
            var ordinaryCallbacks=Activator.CreateInstance(main.GetType("BeatmapCallbacksController",true)!,new[]{callbackInit})!;
            Set("_initData",init);Set("_jumpOffsetYProvider",height);Set("_variableMovementDataProvider",variable);
            Set("_beatmapData",map);Set("_beatmapCallbacksController",ordinaryCallbacks);Set("_disableSpawning",true);
            int initializedEvents=0;Set("didInitEvent",(Action)(()=>initializedEvents++));
            patch.Start(controller);
            if(!(bool)controllerType.GetField("_isInitialized",flags)!.GetValue(controller)!||initializedEvents!=1)patchFailures++;
            patchRegistrationChecks++;
            var callbacks=managerType.GetField("_callbacksInTime",flags)!.GetValue(manager)!;
            var registered=(IDictionary)callbacks.GetType().GetField("_callbacks",flags)!.GetValue(callbacks)!;
            foreach(var item in new[]{("_noteDataCallbackWrapper",typeof(NoteData),"HandleNoteDataCallback"),("_obstacleDataCallbackWrapper",typeof(ObstacleData),"HandleObstacleDataCallback"),("_sliderDataCallbackWrapper",typeof(SliderData),"HandleSliderDataCallback")})
            {
                var wrapper=controllerType.GetField(item.Item1,flags)!.GetValue(controller)!;
                var callback=(Delegate)wrapper.GetType().GetField("_callback",flags)!.GetValue(wrapper)!;
                if(!ReferenceEquals(callback.Target,controller)||callback.Method.Name!=item.Item3
                    ||registered[item.Item2] is not IEnumerable values||!values.Cast<object>().Any(x=>ReferenceEquals(x,wrapper)))patchFailures++;
                patchRegistrationChecks++;
            }
            var normal=(IDictionary)ordinaryCallbacks.GetType().GetField("_callbacksInTimes",flags)!.GetValue(ordinaryCallbacks)!;
            if(normal.Count!=0)patchFailures++;patchRegistrationChecks++;
            if(registered.Count!=3)patchFailures++;patchRegistrationChecks++;
        }
        catch{patch.Unbind();managerType.GetMethod("Dispose",flags)!.Invoke(manager,null);throw;}
        using var dispatch=new NoodleSpawnDispatchChecks(noodle,spawn,controller);
        dispatch.Attach(controller);
        var emitted=new List<BeatmapObjectData>();
        foreach(var type in new[]{typeof(NoteData),typeof(ObstacleData),typeof(SliderData)})
        {
            var register=managerType.GetMethod("AddBeatmapCallback",flags)!.MakeGenericMethod(type);var callbackType=register.GetParameters()[1].ParameterType;
            var parameter=Expression.Parameter(type,"item");
            var converted=Expression.Convert(parameter,typeof(BeatmapObjectData));
            var body=Expression.Block(Expression.Call(Expression.Constant(emitted),typeof(List<BeatmapObjectData>).GetMethod("Add")!,converted),
                Expression.Call(Expression.Constant(dispatch),typeof(NoodleSpawnDispatchChecks).GetMethod(nameof(NoodleSpawnDispatchChecks.Verify))!,converted));
            var callback=Expression.Lambda(callbackType,body,parameter).Compile();
            register.Invoke(manager,new object[]{0f,callback});
        }
        int failed=patchFailures,aheadChecks=0,steps=0,comparisons=0;
        var all=((IEnumerable)map.GetType().GetProperty("beatmapObjectDatas")!.GetValue(map)!).Cast<BeatmapObjectData>().ToArray();
        var entries=new List<(BeatmapObjectData Item,float Entry,float End)>();
        foreach(var item in all)
        {
            var parsed=objects[item]!;float? Read(string name)=>(float?)parsed.GetType().GetProperty(name,flags)!.GetValue(parsed);
            float ahead=Read("InternalAheadTime")??throw new InvalidDataException("Missing native callback ahead time");
            var own=WallJumpMovement.Resolve(new JumpSettingsSnapshot(0,0,.5f),bpm,20,0,Read("Njs"),Read("SpawnOffset"));
            if(Math.Abs(ahead-(.5f+own.HalfJumpSeconds))>.0001f)failed++;aheadChecks++;
            float end=item switch {NoteData n=>n.time,ObstacleData w=>w.time+w.duration,SliderData s=>s.tailTime,_=>0};
            entries.Add((item,item.time-ahead,end));
        }
        var ordered=entries.OrderBy(e=>e.Entry).ToArray();
        var expectedAhead=new Dictionary<BeatmapObjectData,float>(ReferenceEqualityComparer.Instance);
        foreach(var e in entries)
            expectedAhead.Add(e.Item,(float)objects[e.Item]!.GetType().GetProperty("InternalAheadTime",flags)!.GetValue(objects[e.Item])!);
        var ticks=ordered.SelectMany(e=>new[]{e.Entry-.001f,e.Entry,e.Entry+.001f,e.End}).Distinct().Order().ToArray();
        var expected=new List<BeatmapObjectData>();int next=0,checkedItems=0;
        try
        {
            foreach(float time in ticks)
            {
                while(next<ordered.Length&&ordered[next].Entry<=time)
                {
                    var entry=ordered[next++];
                    if(entry.Item.time>=0&&time<entry.End&&entry.Item is NoteData or ObstacleData or SliderData)expected.Add(entry.Item);
                }
                patch.Update(time);steps++;
                if(emitted.Count!=expected.Count)failed++;
                for(int i=checkedItems;i<Math.Min(emitted.Count,expected.Count);i++){if(!ReferenceEquals(emitted[i],expected[i]))failed++;comparisons++;}
                checkedItems=expected.Count;
                int count=emitted.Count;patch.Update(time);
                if(emitted.Count!=count)failed++;
            }
        }
        finally{patch.Unbind();managerType.GetMethod("Dispose",flags)!.Invoke(manager,null);}
        int lateCallbacks=0;
        if(entries.Count>0)
        {
            var lateCounters=new List<Counter>();
            var late=ctor.Invoke(new object?[]{callbackInit,spawn,data,null});
            try
            {
                foreach(var type in new[]{typeof(NoteData),typeof(ObstacleData),typeof(SliderData)})
                {
                    var register=managerType.GetMethod("AddBeatmapCallback",flags)!.MakeGenericMethod(type);
                    var parameter=Expression.Parameter(type,"item");
                    // Use a void callback with an independently owned receiver for the late-start check.
                    var counter=new Counter();
                    var body=Expression.Block(Expression.PostIncrementAssign(Expression.Field(Expression.Constant(counter),nameof(Counter.Value))),Expression.Empty());
                    register.Invoke(late,new object[]{0f,Expression.Lambda(register.GetParameters()[1].ParameterType,body,parameter).Compile()});
                    lateCounters.Add(counter);
                }
                managerType.GetMethod("ManualUpdate",flags)!.Invoke(late,new object[]{entries.Max(e=>Math.Max(e.Entry,e.End))+1f});
                lateCallbacks=lateCounters.Sum(c=>c.Value);if(lateCallbacks!=0)failed++;
            }
            finally{managerType.GetMethod("Dispose",flags)!.Invoke(late,null);lateCounters.Clear();}
        }
        int rewindChecks=0,startFilterChecks=0,reloadChecks=0;
        foreach(float filter in new[]{0f,all.Length>0?all[all.Length/2].time:1f}.Distinct())
        {
            var filteredInit=Activator.CreateInstance(ctor.GetParameters()[0].ParameterType,new object[]{map,filter,false})!;
            var receiver=ctor.Invoke(new object?[]{filteredInit,spawn,data,null});
            var received=new List<BeatmapObjectData>();
            try
            {
                Register(managerType,receiver,received);
                var positiveTicks=ticks.Where(t=>t>0).Append(.001f).Distinct().Order().ToArray();
                void Replay()
                {
                    int cursor=0,compared=0;var gold=new List<BeatmapObjectData>();
                    foreach(float time in positiveTicks)
                    {
                        while(cursor<ordered.Length&&ordered[cursor].Entry<=time)
                        {
                            var e=ordered[cursor++];
                            if(e.Item.time>=filter&&time<e.End&&e.Item is NoteData or ObstacleData or SliderData)gold.Add(e.Item);
                        }
                        managerType.GetMethod("ManualUpdate",flags)!.Invoke(receiver,new object[]{time});
                        if(received.Count!=gold.Count)failed++;
                        for(int i=compared;i<Math.Min(received.Count,gold.Count);i++)
                            if(!ReferenceEquals(received[i],gold[i]))failed++;
                        compared=gold.Count;
                        startFilterChecks++;
                    }
                }
                Replay();
                int before=received.Count;
                managerType.GetMethod("ManualUpdate",flags)!.Invoke(receiver,new object[]{-1f});
                if(received.Count!=before)failed++;rewindChecks++;
                managerType.GetMethod("OnRewind",flags)!.Invoke(receiver,null);
                received.Clear();
                managerType.GetMethod("ManualUpdate",flags)!.Invoke(receiver,new object[]{0f});
                if(received.Count!=0)failed++;rewindChecks++;
                Replay();

                // Reload rebuilds ahead times and the sorted list; rewind resets the cursor separately.
                var callbacks=managerType.GetField("_callbacksInTime",flags)!.GetValue(receiver)!;
                var cursorField=callbacks.GetType().GetField("lastProcessedNode",flags)!;
                var oldCursor=cursorField.GetValue(callbacks);
                var firstField=managerType.GetField("_firstNode",flags)!;
                var oldFirst=firstField.GetValue(receiver);
                foreach(var item in all)objects[item]!.GetType().GetProperty("InternalAheadTime",flags)!.SetValue(objects[item],null);
                managerType.GetMethod("OnReload",flags)!.Invoke(receiver,null);
                foreach(var item in all)
                {
                    var p=objects[item]!;float ahead=(float)p.GetType().GetProperty("InternalAheadTime",flags)!.GetValue(p)!;
                    if(Math.Abs(ahead-expectedAhead[item])>.0001f)failed++;
                    reloadChecks++;
                }
                if(!ReferenceEquals(oldCursor,cursorField.GetValue(callbacks)))failed++;reloadChecks++;
                if(all.Length>0&&ReferenceEquals(oldFirst,firstField.GetValue(receiver)))failed++;reloadChecks++;
                managerType.GetMethod("OnRewind",flags)!.Invoke(receiver,null);received.Clear();Replay();
            }
            finally{managerType.GetMethod("Dispose",flags)!.Invoke(receiver,null);}
        }
        foreach(var item in all.Where(i=>i is NoteData or ObstacleData or SliderData).GroupBy(i=>i.GetType()).Select(g=>g.First()))
        {
            dispatch.CheckDisabled(item);
            dispatch.Capture((BeatmapObjectData)item.GetCopy());
        }
        var spawnDispatch=dispatch.Evidence();failed+=(int)Newtonsoft.Json.Linq.JObject.FromObject(spawnDispatch)["failed"]!;
        return new{failed,objects=all.Length,aheadChecks,steps,callbacks=emitted.Count,comparisons,lateCallbacks,rewindChecks,startFilterChecks,reloadChecks,patchRegistrationChecks,spawnDispatch,
            scope="Actual NoodleObjectsCallbacksManager and SpawnDataManager. Real reorder constructor, three native delegates and installed transpiler applied to actual spawn controller Start; native callback wrappers redirected to Noodle, observer verifies identity/order through actual UpdateNoodleCallback. Existing wrappers inspected before diagnostic rebinding to compiled handlers on controlled spawner receiver; no Unity spawning, affinity scheduling, full reloader or all-mod patch ordering."};
    }
    static void Register(Type managerType,object manager,List<BeatmapObjectData> received)
    {
        foreach(var type in new[]{typeof(NoteData),typeof(ObstacleData),typeof(SliderData)})
        {
            var register=managerType.GetMethod("AddBeatmapCallback",flags)!.MakeGenericMethod(type);
            var parameter=Expression.Parameter(type,"item");
            var body=Expression.Call(Expression.Constant(received),typeof(List<BeatmapObjectData>).GetMethod("Add")!,Expression.Convert(parameter,typeof(BeatmapObjectData)));
            register.Invoke(manager,new object[]{0f,Expression.Lambda(register.GetParameters()[1].ParameterType,body,parameter).Compile()});
        }
    }
    sealed class Counter {public int Value=0;}
}
