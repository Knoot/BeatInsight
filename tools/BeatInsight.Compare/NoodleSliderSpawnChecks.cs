using System.Collections;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.Loader;
using UnityEngine;

namespace BeatInsight.Compare;

static class NoodleSliderSpawnChecks
{
    public static (object Spawn,object Evidence) Run(Assembly main,SliderData chain,object bindings)
    {
        const BindingFlags flags=BindingFlags.Public|BindingFlags.NonPublic|BindingFlags.Instance|BindingFlags.Static;
        var binding=((IEnumerable)bindings).Cast<object>().Single(b=>(string)b.GetType().GetField("Item1")!.GetValue(b)! =="NoodleExtensions");
        var data=binding.GetType().GetField("Item2")!.GetValue(binding)!;
        var objects=(IDictionary)data.GetType().GetProperty("ObjectCustomDatas",flags)!.GetValue(data)!;
        var parsed=objects[chain]!;
        var noodle=AssemblyLoadContext.Default.Assemblies.Single(a=>a.GetName().Name=="NoodleExtensions");
        var movementType=main.GetType("BeatmapObjectSpawnMovementData",true)!;
        var movement=RuntimeHelpers.GetUninitializedObject(movementType);
        movementType.GetField("_noteLinesCount",flags)!.SetValue(movement,4);
        movementType.GetField("_rightVec",flags)!.SetValue(movement,new Vector3(1,0,0));
        movementType.GetField("_jumpOffsetYProvider",flags)!.SetValue(movement,RuntimeHelpers.GetUninitializedObject(main.GetType("PlayerHeightToJumpOffsetYProvider",true)!));
        var managerType=noodle.GetType("NoodleExtensions.Managers.SpawnDataManager",true)!;
        var manager=RuntimeHelpers.GetUninitializedObject(managerType);
        managerType.GetField("_movementData",flags)!.SetValue(manager,movement);
        managerType.GetField("_deserializedData",flags)!.SetValue(manager,data);
        object?[] args={chain,null};
        bool fallback=(bool)managerType.GetMethod("GetSliderSpawnData",flags)!.Invoke(manager,args)!;
        var spawn=args[1]!;int failed=fallback?1:0;
        float? Read(string name)=>(float?)parsed.GetType().GetProperty(name,flags)!.GetValue(parsed);
        bool disable=(bool)parsed.GetType().GetProperty("DisableGravity",flags)!.GetValue(parsed)!;
        float hx=(Read("StartX") is float sx?sx+2:chain.headLineIndex),hy=Read("StartY")??(float)chain.headLineLayer;
        float tx=(Read("TailStartX") is float ex?ex+2:chain.tailLineIndex),ty=Read("TailStartY")??(float)chain.tailLineLayer;
        float hl=disable?hy:Read("InternalStartNoteLineLayer")!.Value,tl=disable?ty:Read("InternalTailStartNoteLineLayer")!.Value;
        var expectedHead=new Vector3((hx-1.5f)*.6f,.25f+.6f*hl,0);
        var expectedTail=new Vector3((tx-1.5f)*.6f,.25f+.6f*tl,0);
        var head=(Vector3)spawn.GetType().GetField("headNoteOffset")!.GetValue(spawn)!;
        var tail=(Vector3)spawn.GetType().GetField("tailNoteOffset")!.GetValue(spawn)!;
        bool Near(Vector3 a,Vector3 b)=>Math.Abs(a.x-b.x)<1e-5&&Math.Abs(a.y-b.y)<1e-5&&Math.Abs(a.z-b.z)<1e-5;
        if(!Near(head,expectedHead)||!Near(tail,expectedTail))failed++;
        float hg=(float)spawn.GetType().GetField("headGravityBase")!.GetValue(spawn)!,tg=(float)spawn.GetType().GetField("tailGravityBase")!.GetValue(spawn)!;
        if(Math.Abs(hg-(.858333f+.525f*hy-(.25f+.6f*hl)))>1e-5||Math.Abs(tg-(.858333f+.525f*ty-(.25f+.6f*tl)))>1e-5)failed++;
        object?[] missing={chain.GetCopy(),null};
        if(!(bool)managerType.GetMethod("GetSliderSpawnData",flags)!.Invoke(manager,missing)!)failed++;
        return(spawn,new{failed,fallback,head=new[]{head.x,head.y,head.z},tail=new[]{tail.x,tail.y,tail.z},hg,tg,disable,scope="Installed GetSliderSpawnData on actual manager binding with row caches; scalar gold for offset/gravity, absent-key fallback. Controlled four lanes/right vector/zero jump offset; no Unity transforms or scheduler."});
    }
}
