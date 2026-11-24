using System.Reflection;
using System.Reflection.Emit;
using Mono.Cecil;
using Mono.Cecil.Cil;
using Newtonsoft.Json.Linq;
using UnityEngine;
using EmitOpCodes=System.Reflection.Emit.OpCodes;

namespace BeatInsight.Compare;

static class NoodleScaleCompositionChecks
{
    delegate void WallGeometry(object? unused,float width,float height,float length,out Vector3 center,out Vector3 size);
    public static object Run(string gameRoot)
    {
        var audits=new List<object>();
        foreach(var pinned in new[]{("Main","A276C8A50ACFAD69A3DD3A82CDE08571C3262344702B9B014C89756771B4495E"),("GameplayCore","C45267116BB2AE61104E994AC24E9A86378055A3FEDB62B6872EEE51C2FA8C7C")})
        {
            string hash=Comparison.HashFile(Path.Combine(gameRoot,"Beat Saber_Data/Managed",pinned.Item1+".dll"));
            if(hash!=pinned.Item2)throw new InvalidDataException("Reinspect changed scale composition consumer: "+pinned.Item1);
            audits.Add(new{assembly=pinned.Item1,sha256=hash});
        }
        using var main=ModuleDefinition.ReadModule(Path.Combine(gameRoot,"Beat Saber_Data/Managed/Main.dll"));
        using var core=ModuleDefinition.ReadModule(Path.Combine(gameRoot,"Beat Saber_Data/Managed/GameplayCore.dll"));
        using var noodle=ModuleDefinition.ReadModule(Path.Combine(gameRoot,"Plugins/NoodleExtensions.dll"));
        MethodDefinition Method(ModuleDefinition module,string type,string name)=>module.Types.Single(t=>t.FullName==type).Methods.Single(m=>m.Name==name);
        bool Calls(MethodDefinition method,string owner,string name)=>method.Body.Instructions.Any(i=>i.Operand is MethodReference r&&r.DeclaringType.FullName==owner&&r.Name==name);
        var collision=Method(main,"PlayerHeadAndObstacleInteraction","RefreshIntersectingObstacles");
        var cut=Method(core,"NoteBasicCutInfoHelper","GetBasicCutInfo");
        if(!Calls(collision,"UnityEngine.Transform","InverseTransformPoint")||!Calls(collision,"UnityEngine.Bounds","Contains")||!Calls(collision,"ObstacleController","get_bounds"))
            throw new InvalidDataException("Obstacle collision space changed");
        if(!Calls(cut,"UnityEngine.Transform","InverseTransformVector"))throw new InvalidDataException("Cut-vector transform changed");
        foreach(string type in new[]{"Note","Obstacle","Slider"})
        {
            var method=noodle.Types.Single(t=>t.FullName=="NoodleExtensions.HarmonyPatches.Objects."+type+"UpdateNoodlifier").Methods.Single(m=>m.HasBody&&Calls(m,"NoodleExtensions.NoodleObjectData","get_InternalScale"));
            if(!Calls(method,"NoodleExtensions.NoodleObjectData","get_InternalScale")||!Calls(method,"UnityEngine.Vector3","Scale")||!Calls(method,"UnityEngine.Transform","set_localScale"))
                throw new InvalidDataException("Object scale composition changed: "+type);
        }
        foreach(string type in new[]{"Note","Obstacle"})
        {
            var init=Method(noodle,"NoodleExtensions.HarmonyPatches.Objects."+type+"InitNoodlifier","Postfix");
            int Local(Mono.Cecil.Cil.Instruction i)=>i.OpCode.Code switch
            {Code.Ldloc_0=>0,Code.Ldloc_1=>1,Code.Ldloc_2=>2,Code.Ldloc_3=>3,Code.Ldloc_S or Code.Ldloc=>((VariableDefinition)i.Operand).Index,_=>-1};
            var scaleWrite=init.Body.Instructions.Single(i=>i.Operand is MethodReference r&&r.DeclaringType.FullName=="UnityEngine.Transform"&&r.Name=="set_localScale");
            var internalWrite=init.Body.Instructions.Single(i=>i.Operand is MethodReference r&&r.DeclaringType.FullName=="NoodleExtensions.NoodleObjectData"&&r.Name=="set_InternalScale");
            if(Local(scaleWrite.Previous)<0||Local(scaleWrite.Previous)!=Local(internalWrite.Previous))throw new InvalidDataException("Initial and stored scale values diverged: "+type);
        }
        var geometry=Method(main,"StretchableObstacle","CalculateObstacleTransformProperties");
        if(!geometry.HasThis||geometry.Parameters[3].Name!="localPosition"||geometry.Parameters[4].Name!="size")throw new InvalidDataException("Wall geometry output slots changed");
        var set=Method(main,"StretchableObstacle","SetObstacleCoreTransformProperties");
        if(!Calls(set,"UnityEngine.Bounds",".ctor"))throw new InvalidDataException("Wall local bounds construction changed");
        // Execute the installed, stateless geometry prefix. Exclude visual core-offset
        // access and Transform writes, which require a Unity host. No game IL is saved.
        var dynamic=new DynamicMethod("InstalledWallBoundsPrefix",typeof(void),new[]{typeof(object),typeof(float),typeof(float),typeof(float),typeof(Vector3).MakeByRefType(),typeof(Vector3).MakeByRefType()},typeof(NoodleScaleCompositionChecks).Module,true);
        var il=dynamic.GetILGenerator();int copied=0,writes=0;
        foreach(var instruction in geometry.Body.Instructions)
        {
            if(instruction.Operand is ParameterDefinition end&&end.Name=="scale")break;
            switch(instruction.OpCode.Code)
            {
                case Code.Ldarg_1:il.Emit(EmitOpCodes.Ldarg_1);break;
                case Code.Ldarg_2:il.Emit(EmitOpCodes.Ldarg_2);break;
                case Code.Ldarg_3:il.Emit(EmitOpCodes.Ldarg_3);break;
                case Code.Ldarg_S:
                    var p=(ParameterDefinition)instruction.Operand;il.Emit(EmitOpCodes.Ldarg_S,(byte)(p.Index+1));break;
                case Code.Ldc_R4:il.Emit(EmitOpCodes.Ldc_R4,(float)instruction.Operand);break;
                case Code.Mul:il.Emit(EmitOpCodes.Mul);break;
                case Code.Newobj:
                    if(instruction.Operand is not MethodReference ctor||ctor.DeclaringType.FullName!="UnityEngine.Vector3"||ctor.Parameters.Count!=3)throw new InvalidDataException("Unexpected geometry constructor");
                    il.Emit(EmitOpCodes.Newobj,typeof(Vector3).GetConstructor(new[]{typeof(float),typeof(float),typeof(float)})!);break;
                case Code.Stobj:
                    if(((TypeReference)instruction.Operand).FullName!="UnityEngine.Vector3")throw new InvalidDataException("Unexpected geometry output type");
                    il.Emit(EmitOpCodes.Stobj,typeof(Vector3));writes++;break;
                default:throw new InvalidDataException("Unvalidated wall geometry instruction: "+instruction.OpCode);
            }
            copied++;
        }
        if(writes!=2)throw new InvalidDataException("Wall geometry prefix did not produce both bounds outputs");
        il.Emit(EmitOpCodes.Ret);var calculate=(WallGeometry)dynamic.CreateDelegate(typeof(WallGeometry));
        var helperReport=JObject.FromObject(NoodleArcOpacityChecks.Run(gameRoot,new[]{"Obstacle"},new[]{"identity-scale","identity-scale-frames"}));
        if((int)helperReport["failed"]! !=0)throw new InvalidDataException("Installed signed-scale helper contract failed");
        Vector3 Offset(bool mirror)
        {
            var value=helperReport["results"]!.First(r=>(bool)r["mirror"]! ==mirror&&(int)r["major"]! ==3&&(string?)r["variant"]=="identity-scale"&&(float)r["time"]! ==.5f)["scaleOffset"]!;
            return new Vector3((float)value[0]!, (float)value[1]!, (float)value[2]!);
        }
        bool Inside(Vector3 p,Vector3 center,Vector3 size)=>Math.Abs(p.x-center.x)<=size.x*.5f&&Math.Abs(p.y-center.y)<=size.y*.5f&&Math.Abs(p.z-center.z)<=size.z*.5f;
        var results=new List<object>();int cases=0,failed=0,samples=0;
        foreach(float width in new[]{.6f,1.2f,2.4f})foreach(float height in new[]{.6f,1.8f})foreach(float length in new[]{1f,3f})foreach(float angle in new[]{0f,30f,90f,180f,270f})foreach(bool mirror in new[]{false,true})
        {
            cases++;calculate(null,width,height,length,out var center,out var size);
            var offset=Offset(mirror);var composed=Vector3.Scale(Vector3.one,offset);
            bool pass=center.x==0&&center.y==height*.5f&&center.z==length*.5f&&size.x==width&&size.y==height&&size.z==length;
            double c=Math.Cos(angle*Math.PI/180),s=Math.Sin(angle*Math.PI/180);
            for(int x=-8;x<=8;x++)for(int y=-4;y<=12;y++)
            {
                // Inverse root rotation, then inverse signed scale, as in the game's
                // InverseTransformPoint -> local Bounds.Contains collision path.
                var local=new Vector3((float)(c*x*.3+s*y*.3),(float)(-s*x*.3+c*y*.3),length*.5f);
                var scaled=new Vector3(local.x/composed.x,local.y/composed.y,local.z/composed.z);
                if(Inside(local,center,size)!=Inside(scaled,center,size))pass=false;
                samples++;
            }
            if(!pass)failed++;results.Add(new{width,height,length,angle,mirror,centerX=center.x,scaleX=composed.x,pass});
        }
        // Controls: the symmetry proof must not admit arbitrary pivots or scale.
        bool displacedChanges=Inside(new Vector3(.6f,1,1),new Vector3(.5f,1,1),new Vector3(1,2,2))!=Inside(new Vector3(-.6f,1,1),new Vector3(.5f,1,1),new Vector3(1,2,2));
        bool nonUnitChanges=Inside(new Vector3(.75f,1,1),new Vector3(0,1,1),new Vector3(1,2,2))!=Inside(new Vector3(.375f,1,1),new Vector3(0,1,1),new Vector3(1,2,2));
        cases+=2;if(!displacedChanges)failed++;if(!nonUnitChanges)failed++;
        return new{schemaVersion=1,profileVersion=BeatInsight.Formats.LocalMap.AnalysisProfileVersion,cases,failed,samples,copiedGeometryInstructions=copied,audits,helperCases=(int)helperReport["cases"]!,displacedChanges,nonUnitChanges,results,
            gameStarted=false,referenceUsed=false,scope="installed stateless wall-geometry IL prefix, actual signed-scale helper and Vector3 composition; audited collision inverse space; independent local-slab membership gold; Unity Transform/native Contains and note prefab hierarchy not executed"};
    }
}
