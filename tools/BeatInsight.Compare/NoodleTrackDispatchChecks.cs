using System.Collections;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.Loader;
using Mono.Cecil;
using UnityEngine;

namespace BeatInsight.Compare;

public class ControlledBpmProxy : DispatchProxy
{
    public float Bpm;
    protected override object? Invoke(MethodInfo? method,object?[]? args)
        =>method?.Name=="get_currentBpm"?Bpm:throw new NotSupportedException("Unexpected BPM controller access: "+method?.Name);
}

static class NoodleTrackDispatchChecks
{
    public static object Run(string gameRoot)
    {
        foreach(var pin in new[]{("Plugins/Heck.dll","9E5163F09E122713543FB69F89799D1121FB712820E6B4350F7A30C5D790793C"),
            ("Plugins/CustomJSONData.dll","4F9F8EFD8012159D8995B6D936E3C21B6AA6A35E319686FA3E9B04C2B6331F7C"),
            ("Beat Saber_Data/Managed/Main.dll","A276C8A50ACFAD69A3DD3A82CDE08571C3262344702B9B014C89756771B4495E")})
            if(Comparison.HashFile(Path.Combine(gameRoot,pin.Item1))!=pin.Item2)throw new InvalidDataException("Reinspect changed event dispatch provider");
        const BindingFlags flags=BindingFlags.Public|BindingFlags.NonPublic|BindingFlags.Instance;
        var heck=AssemblyLoadContext.Default.LoadFromAssemblyPath(Path.Combine(gameRoot,"Plugins/Heck.dll"));
        var main=AssemblyLoadContext.Default.LoadFromAssemblyPath(Path.Combine(gameRoot,"Beat Saber_Data/Managed/Main.dll"));
        var custom=AssemblyLoadContext.Default.LoadFromAssemblyPath(Path.Combine(gameRoot,"Plugins/CustomJSONData.dll"));
        var ownerType=heck.GetType("Heck.Animation.Events.CoroutineEvent",true)!;
        var owner=RuntimeHelpers.GetUninitializedObject(ownerType);
        var callbacksType=main.GetType("BeatmapCallbacksController",true)!;
        var callbacks=RuntimeHelpers.GetUninitializedObject(callbacksType);
        var songTime=callbacksType.GetField("_songTime",flags)!;
        ownerType.GetField("_beatmapCallbacksController",flags)!.SetValue(owner,callbacks);
        var bpm=DispatchProxy.Create(ownerType.GetField("_bpmController",flags)!.FieldType,typeof(ControlledBpmProxy));
        ownerType.GetField("_bpmController",flags)!.SetValue(owner,bpm);
        var contextCtor=heck.GetType("Heck.Deserialize.DeserializedData",true)!.GetConstructors(flags).Single();
        var contextArgs=contextCtor.GetParameters().Select(p=>Activator.CreateInstance(p.ParameterType)!).ToArray();
        var eventDictionary=(IDictionary)contextArgs[0];
        ownerType.GetField("_deserializedData",flags)!.SetValue(owner,contextCtor.Invoke(contextArgs));
        var eventType=custom.GetType("CustomJSONData.CustomBeatmap.CustomEventData",true)!;
        var metadataType=custom.GetType("CustomJSONData.CustomBeatmap.CustomData",true)!;
        var parsedType=heck.GetType("Heck.HeckCoroutineEventData",true)!;
        var infoType=parsedType.GetNestedType("CoroutineInfo",BindingFlags.Public|BindingFlags.NonPublic)!;
        var trackType=heck.GetType("Heck.Animation.Track",true)!;
        var propertyType=heck.GetType("Heck.Animation.Property`1",true)!.MakeGenericType(typeof(Vector3));
        var curveType=heck.GetType("Heck.Animation.Vector3PointDefinition",true)!;
        var dispatch=ownerType.GetMethod("StartEventCoroutine",flags)!;
        var coroutine=ownerType.GetMethod("AnimateTrackCoroutine",flags)!;
        var valueProperty=propertyType.GetProperty("Value",flags)!;
        object Curve(bool constant)=>Activator.CreateInstance(curveType,flags,null,new object[]{constant?
            new List<object>{2f,3f,4f}:new List<object>{new List<object>{-2f,0f,1f,0f},new List<object>{2f,3f,4f,1f}}},null)!;
        var results=new List<object>();int cases=0,failed=0,orderedFastReplacements=0;
        foreach(float currentBpm in new[]{90f,120f,180f})foreach(float start in new[]{0f,3.25f})
        foreach(int repeat in new[]{0,2})foreach(string branch in new[]{"zero","late","constant","clear"})foreach(bool previous in new[]{false,true})
        {
            ((ControlledBpmProxy)bpm).Bpm=currentBpm;
            float beats=branch=="zero"?0:2;
            float seconds=60*beats/currentBpm;
            float time=branch=="late"?MathF.BitIncrement(start+seconds*(repeat+1)):start;
            songTime.SetValue(callbacks,time);
            var property=Activator.CreateInstance(propertyType,true)!;
            if(previous)valueProperty.SetValue(property,new Vector3(17,18,19));
            var track=Activator.CreateInstance(trackType,true)!;
            var unrelated=Activator.CreateInstance(propertyType,true)!;
            valueProperty.SetValue(unrelated,new Vector3(31,32,33));
            var unrelatedTrack=Activator.CreateInstance(trackType,true)!;
            ((IDictionary)trackType.GetField("_properties",flags)!.GetValue(unrelatedTrack)!)["offsetPosition"]=unrelated;
            object? points=branch=="clear"?null:Curve(branch=="constant");
            var ev=Activator.CreateInstance(eventType,new object[]{start,"AnimateTrack",Activator.CreateInstance(metadataType)!,new Version(3,0)})!;
            var parsed=RuntimeHelpers.GetUninitializedObject(parsedType);
            parsedType.GetField("<Duration>k__BackingField",flags)!.SetValue(parsed,beats);
            parsedType.GetField("<Repeat>k__BackingField",flags)!.SetValue(parsed,repeat);
            var infos=Array.CreateInstance(infoType,1);infos.SetValue(Activator.CreateInstance(infoType,flags,null,new[]{points,property,track},null),0);
            parsedType.GetField("<CoroutineInfos>k__BackingField",flags)!.SetValue(parsed,infos);
            eventDictionary[ev]=parsed;
            dispatch.Invoke(owner,new[]{ev,Enum.ToObject(dispatch.GetParameters()[1].ParameterType,0)});
            object? actual=valueProperty.GetValue(property);
            bool pass=branch=="clear"?actual==null:actual is Vector3 v&&v.x==2&&v.y==3&&v.z==4;
            pass&=propertyType.GetProperty("Coroutine",flags)!.GetValue(property)==null;
            if(branch=="zero")
            {
                var other=Activator.CreateInstance(curveType,flags,null,new object[]{new List<object>{7f,8f,9f}},null)!;
                infos.SetValue(Activator.CreateInstance(infoType,flags,null,new[]{other,property,track},null),0);
                dispatch.Invoke(owner,new[]{ev,Enum.ToObject(dispatch.GetParameters()[1].ParameterType,0)});
                pass&=valueProperty.GetValue(property) is Vector3 middle&&middle.x==7&&middle.y==8&&middle.z==9;
                infos.SetValue(Activator.CreateInstance(infoType,flags,null,new[]{points,property,track},null),0);
                dispatch.Invoke(owner,new[]{ev,Enum.ToObject(dispatch.GetParameters()[1].ParameterType,0)});
                pass&=valueProperty.GetValue(property) is Vector3 final&&final.x==2&&final.y==3&&final.z==4;
                orderedFastReplacements++;
            }
            pass&=valueProperty.GetValue(unrelated) is Vector3 sentinel&&sentinel.x==31&&sentinel.y==32&&sentinel.z==33;
            cases++;if(!pass)failed++;results.Add(new{currentBpm,start,repeat,branch,previous,pass});
            eventDictionary.Remove(ev);
        }
        // Explicit host cancellation: Dispose the real old iterator, then invoke
        // the replacement iterator on the same property. Unity StopCoroutine is
        // audited below, not replaced with this manual operation.
        int replacements=0;
        foreach(float replaceAt in new[]{.125f,.5f,.875f})foreach(bool constant in new[]{false,true})
        {
            var property=Activator.CreateInstance(propertyType,true)!;var track=Activator.CreateInstance(trackType,true)!;
            var fn=Enum.ToObject(coroutine.GetParameters()[5].ParameterType,0);
            var old=(IEnumerator)coroutine.Invoke(owner,new object[]{Curve(false),property,track,1f,0f,fn,0,false})!;
            songTime.SetValue(callbacks,replaceAt);old.MoveNext();((IDisposable)old).Dispose();
            var replacement=(IEnumerator)coroutine.Invoke(owner,new object[]{Curve(constant),property,track,1f,replaceAt,fn,0,false})!;
            replacement.MoveNext();var after=valueProperty.GetValue(property);
            bool pass=!old.MoveNext()&&Equals(after,valueProperty.GetValue(property));
            songTime.SetValue(callbacks,replaceAt+2);replacement.MoveNext();
            pass&=valueProperty.GetValue(property) is Vector3 v&&v.x==2&&v.y==3&&v.z==4;
            replacements++;cases++;if(!pass)failed++;
        }
        using var module=ModuleDefinition.ReadModule(Path.Combine(gameRoot,"Plugins/Heck.dll"));
        var body=module.Types.Single(t=>t.Name=="CoroutineEvent").Methods.Single(m=>m.Name=="StartEventCoroutine").Body.Instructions;
        int stop=body.Select((i,n)=>(i,n)).Single(p=>p.i.Operand is MethodReference m&&m.Name=="StopCoroutine").n;
        int clear=body.Select((i,n)=>(i,n)).Single(p=>p.i.Operand is MethodReference m&&m.Name=="Null").n;
        int launch=body.Select((i,n)=>(i,n)).First(p=>p.i.Operand is MethodReference m&&m.Name=="StartCoroutine").n;
        if(!(stop<clear&&clear<launch))throw new InvalidDataException("Cancellation ordering changed");
        return new{schemaVersion=1,cases,failed,replacements,orderedFastReplacements,results,gameStarted=false,referenceUsed=false,
            scope="actual StartEventCoroutine fast paths with controlled parsed event/context and BPM provider; zero duration, expired callback, single-point and clear; actual Dispose/manual replacement; StopCoroutine ordering audited, Unity scheduling/cancellation and same-time callback order not executed"};
    }
}
