using System.Collections;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.Loader;
using UnityEngine;

namespace BeatInsight.Compare;

static class NoodleBurstMovementChecks
{
    public static object Run(Assembly main,Assembly noodle,SliderData chain,IReadOnlyList<NoteData> slices,object bindings)
    {
        const BindingFlags flags=BindingFlags.Public|BindingFlags.NonPublic|BindingFlags.Instance|BindingFlags.Static;
        var data=((IEnumerable)bindings).Cast<object>().Single(b=>(string)b.GetType().GetField("Item1")!.GetValue(b)! =="NoodleExtensions");
        data=data.GetType().GetField("Item2")!.GetValue(data)!;
        var originalType=main.GetType("VariableMovementDataProvider",true)!;
        var original=RuntimeHelpers.GetUninitializedObject(originalType);
        var player=RuntimeHelpers.GetUninitializedObject(main.GetType("PlayerTransforms",true)!);
        originalType.GetField("_playerTransforms",flags)!.SetValue(original,player);
        var init=originalType.GetMethod("Init",flags)!;
        var movementType=main.GetType("BeatmapObjectSpawnMovementData",true)!;
        var movement=RuntimeHelpers.GetUninitializedObject(movementType);
        movementType.GetField("_startHalfJumpDurationInBeats",flags)!.SetValue(movement,4f);
        movementType.GetField("_maxHalfJumpDistance",flags)!.SetValue(movement,18f);
        var wrapperType=noodle.GetType("NoodleExtensions.Managers.NoodleMovementDataProvider",true)!;
        string[] properties={"halfJumpDuration","jumpDuration","jumpDistance","noteJumpSpeed","spawnAheadTime","moveStartPosition","moveEndPosition","jumpEndPosition"};
        var overrides=wrapperType.GetFields(flags).Where(f=>f.Name.EndsWith("Override",StringComparison.Ordinal)).ToArray();
        int failed=0,comparisons=0,resets=0,attachmentChecks=0,poolChecks=0,poolAllocations=0;
        foreach(int mode in new[]{1,2})
        {
            var parameters=init.GetParameters();
            object[] args=parameters.Select(p=>p.Name switch {
                "startHalfJumpDurationInBeats"=>(object)4f,"maxHalfJumpDistance"=>18f,"noteJumpMovementSpeed"=>20f,"bpm"=>120f,"minRelativeNoteJumpSpeed"=>0f,
                "noteJumpValueType"=>Enum.ToObject(p.ParameterType,mode),"noteJumpValue"=>mode==1?0f:.5f,
                "centerPosition"=>new Vector3(0,0,0),"forwardVector"=>new Vector3(0,0,1),_=>throw new InvalidDataException("Movement Init ABI changed")}).ToArray();
            init.Invoke(original,args);
            var wrapper=RuntimeHelpers.GetUninitializedObject(wrapperType);
            wrapperType.GetField("_original",flags)!.SetValue(wrapper,original);
            wrapperType.GetField("_deserializedData",flags)!.SetValue(wrapper,data);
            wrapperType.GetField("_movementData",flags)!.SetValue(wrapper,movement);
            wrapperType.GetField("_oneBeatDuration",flags)!.SetValue(wrapper,.5f);
            wrapperType.GetField("_noteJumpStartBeatOffset",flags)!.SetValue(wrapper,mode==1?0f:.5f);
            wrapperType.GetField("_noteJumpValueType",flags)!.SetValue(wrapper,Enum.ToObject(wrapperType.GetField("_noteJumpValueType",flags)!.FieldType,mode));
            var initialize=wrapperType.GetMethod("InitObject",flags)!;
            object?[] Read()=>properties.Select(p=>wrapperType.GetProperty(p,flags)!.GetValue(wrapper)).ToArray();
            var attachment=slices.Count==0?(object)new{failed=0,checks=0}:NoodleMovementAttachmentChecks.Run(main,noodle,wrapper,original,slices[0],chain);
            var attachmentReport=Newtonsoft.Json.Linq.JObject.FromObject(attachment);
            failed+=(int)attachmentReport["failed"]!;attachmentChecks+=(int)attachmentReport["checks"]!;
            if(slices.Count>0)
            {
                var allocation=Newtonsoft.Json.Linq.JObject.FromObject(NoodleMovementPoolChecks.Run(wrapper,chain,slices[0]));
                failed+=(int)allocation["failed"]!;poolChecks+=(int)allocation["checks"]!;poolAllocations+=(int)allocation["factoryCalls"]!;
            }
            initialize.Invoke(wrapper,new object[]{chain});var expected=Read();
            foreach(var slice in slices)
            {
                initialize.Invoke(wrapper,new object[]{slice});var actual=Read();
                for(int i=0;i<expected.Length;i++){if(!Equals(expected[i],actual[i]))failed++;comparisons++;}
                initialize.Invoke(wrapper,new object[]{slice.GetCopy()});
                if(overrides.Any(f=>f.GetValue(wrapper)!=null))failed++;
                resets++;
                for(int i=0;i<properties.Length;i++)
                {
                    var value=originalType.GetProperty(properties[i],flags)!.GetValue(original);
                    if(!Equals(value,wrapperType.GetProperty(properties[i],flags)!.GetValue(wrapper)))failed++;
                    comparisons++;
                }
            }
        }
        return new{failed,comparisons,resets,attachmentChecks,poolChecks,poolAllocations,nativeAccessors=NativeFieldAccessorChecks.Enabled,scope="Actual NoodleMovementDataProvider.InitObject with manager Noodle data copied by patched Heck spawner; parent/slice jump timing, speed, distance and position properties plus unregistered-key reset. Controlled movement receivers/base settings; pool attachment tested through controlled field adapters and actual Zenject pool; no Unity movement."};
    }
}
