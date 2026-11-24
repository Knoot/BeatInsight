using System.Collections;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.Loader;
using UnityEngine;

namespace BeatInsight.Compare;

static class NoodleScaleNoteChecks
{
    public static object Run(string gameRoot,object heckData,object noodleData)
    {
        const BindingFlags flags=BindingFlags.Public|BindingFlags.NonPublic|BindingFlags.Instance|BindingFlags.Static;
        var heck=heckData.GetType().Assembly;var noodle=AssemblyLoadContext.Default.Assemblies.Single(a=>a.GetName().Name=="NoodleExtensions");
        var main=AssemblyLoadContext.Default.LoadFromAssemblyPath(Path.Combine(gameRoot,"Beat Saber_Data/Managed/Main.dll"));
        var ownerType=heck.GetType("Heck.Animation.Events.CoroutineEvent",true)!;var owner=RuntimeHelpers.GetUninitializedObject(ownerType);
        var callbacksType=main.GetType("BeatmapCallbacksController",true)!;var callbacks=RuntimeHelpers.GetUninitializedObject(callbacksType);
        var clock=callbacksType.GetField("_songTime",flags)!;ownerType.GetField("_beatmapCallbacksController",flags)!.SetValue(owner,callbacks);
        var bpm=DispatchProxy.Create(ownerType.GetField("_bpmController",flags)!.FieldType,typeof(ControlledBpmProxy));((ControlledBpmProxy)bpm).Bpm=174;
        ownerType.GetField("_bpmController",flags)!.SetValue(owner,bpm);ownerType.GetField("_deserializedData",flags)!.SetValue(owner,heckData);
        var dispatch=ownerType.GetMethod("StartEventCoroutine",flags)!;var compiled=ControlledCoroutineScheduler.CompileDispatch(dispatch);
        var events=((IDictionary)heckData.GetType().GetProperty("CustomEventCustomDatas",flags)!.GetValue(heckData)!).Keys.Cast<BeatmapDataItem>().OrderBy(e=>e.time).ToArray();
        var notes=(IDictionary)noodleData.GetType().GetProperty("ObjectCustomDatas",flags)!.GetValue(noodleData)!;
        int failed=notes.Count==6&&events.Length==2?0:1,samples=0;var rows=new List<object>();
        var helperType=noodle.GetType("NoodleExtensions.Animation.AnimationHelper",true)!;
        var helper=Activator.CreateInstance(helperType,flags,null,new object[]{false},null)!;var offset=helperType.GetMethod("GetObjectOffset",flags)!;
        var entries=new List<DictionaryEntry>();foreach(DictionaryEntry entry in notes)entries.Add(entry);
        object? common=null;
        foreach(DictionaryEntry entry in notes)
        {
            var assigned=((IEnumerable)entry.Value!.GetType().GetProperty("Track",flags)!.GetValue(entry.Value)!).Cast<object>().ToArray();
            if(assigned.Length!=1)failed++;
            else if(common==null)common=assigned[0];else if(!ReferenceEquals(common,assigned[0]))failed++;
        }
        using var scheduler=new ControlledCoroutineScheduler();var previous=ControlledCoroutineScheduler.Current;ControlledCoroutineScheduler.Current=scheduler;
        try
        {
            int next=0;
            foreach(var note in entries.OrderBy(e=>((BeatmapDataItem)e.Key).time))
            {
                var key=(NoteData)note.Key;
                while(next<events.Length&&events[next].time<=key.time)
                {
                    clock.SetValue(callbacks,events[next].time);
                    compiled.Invoke(null,new[]{owner,events[next],Enum.ToObject(dispatch.GetParameters()[1].ParameterType,0)});scheduler.Tick();next++;
                }
                clock.SetValue(callbacks,key.time);scheduler.Tick();
                object?[] args=new object?[offset.GetParameters().Length];args[0]=note.Value!.GetType().GetProperty("AnimationObject",flags)!.GetValue(note.Value);
                args[1]=note.Value.GetType().GetProperty("Track",flags)!.GetValue(note.Value);args[2]=.5f;offset.Invoke(helper,args);
                var actual=args[5] is Vector3 v?new[]{v.x,v.y,v.z}:Array.Empty<float>();float expected=key.beat<=477?.5f:1;
                if(actual.Length!=3||actual.Any(value=>Math.Abs(value-expected)>.0001f))failed++;
                if(args[3]!=null||args[4]!=null||args[6]!=null||args[7]!=null||args[8]!=null||args[9]!=null)failed++;
                samples++;rows.Add(new{key.beat,expected,actual});
            }
            if(scheduler.Count!=0||scheduler.Starts!=2)failed++;
        }
        finally{ControlledCoroutineScheduler.Current=previous;}
        return new{failed,samples,rows,scope="Six err candidate notes and two size events through real custom loader/Heck manager/Noodle bindings and timed dispatcher/AnimationHelper. Same Track instance checked across notes, source event timing and scale output checked. No note pose/collider/mesh neutrality or support admission."};
    }
}
