using System.Collections;
using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.Loader;
using BeatInsight.Formats;
using Mono.Cecil;
using Newtonsoft.Json.Linq;
using UnityEngine;
using VariableDefinition=Mono.Cecil.Cil.VariableDefinition;
using Code=Mono.Cecil.Cil.Code;

namespace BeatInsight.Compare;

static class NoodleWallClockChecks
{
    public static object Run(string gameRoot)
    {
        foreach(var pin in new[]{("NoodleExtensions","FE50124E33631EED7ECB78EA1A19BB7EA64CC12974B0298DAD991672C2ABDA95"),
            ("Heck","9E5163F09E122713543FB69F89799D1121FB712820E6B4350F7A30C5D790793C"),
            ("CustomJSONData","4F9F8EFD8012159D8995B6D936E3C21B6AA6A35E319686FA3E9B04C2B6331F7C")})
            if(Comparison.HashFile(Path.Combine(gameRoot,"Plugins",pin.Item1+".dll"))!=pin.Item2)throw new InvalidDataException("Reinspect changed wall clock provider");
        string mainPath=Path.Combine(gameRoot,"Beat Saber_Data/Managed/Main.dll");
        if(Comparison.HashFile(mainPath)!="A276C8A50ACFAD69A3DD3A82CDE08571C3262344702B9B014C89756771B4495E")throw new InvalidDataException("Reinspect changed wall start time");
        using var extension=ModuleDefinition.ReadModule(Path.Combine(gameRoot,"Plugins/NoodleExtensions.dll"));
        var update=extension.Types.Single(t=>t.Name=="ObstacleUpdateNoodlifier").Methods.Single(m=>m.Name=="Prefix");
        if(update.Parameters[2].Name!="____startTimeOffset")throw new InvalidDataException("Wall clock input changed");
        var clock=update.Body.Instructions.Single(i=>i.Operand is MethodReference m&&m.DeclaringType.FullName=="IAudioTimeSource"&&m.Name=="get_songTime");
        var sequence=new List<Mono.Cecil.Cil.Instruction>();
        for(var i=clock.Next;i!=null;i=i.Next){sequence.Add(i);if(i.OpCode.Code==Code.Div)break;}
        if(!sequence.Select(i=>i.OpCode.Code).SequenceEqual(new[]{Code.Ldarg_3,Code.Sub,Code.Ldloc_3,Code.Sub,Code.Ldloc_S,Code.Ldloc_S,Code.Add,Code.Div})
            ||((VariableDefinition)sequence[4].Operand).Index!=4||((VariableDefinition)sequence[5].Operand).Index!=5)throw new InvalidDataException("Wall normalized time expression changed");
        using var main=ModuleDefinition.ReadModule(mainPath);
        var movement=main.Types.Single(t=>t.Name=="ObstacleController").Methods.Single(m=>m.HasBody&&m.Body.Instructions.Any(i=>i.Operand is FieldReference f&&f.Name=="_startTimeOffset"&&i.OpCode.Code==Code.Stfld));
        var write=movement.Body.Instructions.First(i=>i.Operand is FieldReference f&&f.Name=="_startTimeOffset"&&i.OpCode.Code==Code.Stfld);
        if(write.Previous.OpCode.Code!=Code.Sub||write.Previous.Previous.Operand is not MethodReference half||half.Name!="get_halfJumpDuration")throw new InvalidDataException("Wall start offset derivation changed");
        // Execute the pinned arithmetic slice with explicit scalar inputs. All
        // object access, time overrides and Transform writes remain excluded.
        var dynamic=new DynamicMethod("InstalledWallNormalizedTime",typeof(float),Enumerable.Repeat(typeof(float),5).ToArray(),typeof(NoodleWallClockChecks).Module,true);
        var il=dynamic.GetILGenerator();il.Emit(OpCodes.Ldarg_0);
        foreach(var instruction in sequence)
        {
            switch(instruction.OpCode.Code)
            {
                case Code.Ldarg_3:il.Emit(OpCodes.Ldarg_1);break;
                case Code.Ldloc_3:il.Emit(OpCodes.Ldarg_2);break;
                case Code.Ldloc_S:if(((VariableDefinition)instruction.Operand).Index==4)il.Emit(OpCodes.Ldarg_3);else il.Emit(OpCodes.Ldarg_S,(byte)4);break;
                case Code.Sub:il.Emit(OpCodes.Sub);break;
                case Code.Add:il.Emit(OpCodes.Add);break;
                case Code.Div:il.Emit(OpCodes.Div);break;
                default:throw new InvalidDataException("Unexpected arithmetic instruction");
            }
        }
        il.Emit(OpCodes.Ret);var evaluate=(Func<float,float,float,float,float,float>)dynamic.CreateDelegate(typeof(Func<float,float,float,float,float,float>));
        const BindingFlags flags=BindingFlags.Public|BindingFlags.NonPublic|BindingFlags.Instance;
        var noodle=AssemblyLoadContext.Default.LoadFromAssemblyPath(Path.Combine(gameRoot,"Plugins/NoodleExtensions.dll"));
        var custom=AssemblyLoadContext.Default.LoadFromAssemblyPath(Path.Combine(gameRoot,"Plugins/CustomJSONData.dll"));
        var metadataType=custom.GetType("CustomJSONData.CustomBeatmap.CustomData",true)!;
        var obstacleCtor=custom.GetType("CustomJSONData.CustomBeatmap.CustomObstacleData",true)!.GetConstructors().Single();
        var parsedCtor=noodle.GetType("NoodleExtensions.NoodleObstacleData",true)!.GetConstructors(flags).Single();var cp=parsedCtor.GetParameters();
        var helperType=noodle.GetType("NoodleExtensions.Animation.AnimationHelper",true)!;
        var offset=helperType.GetMethod("GetObjectOffset",flags)!;
        var points=JArray.Parse("[[-4,0,0,0.125],[0,0,0,0.375,\"easeOutSine\"]]");
        NoodleVectorCurve.TryRead(points,out var own);
        object Convert(JToken t)=>t is JArray a?a.Select(Convert).ToList():t.Type==JTokenType.String?(object)t.Value<string>()!:(float)t;
        var metadata=(IDictionary)Activator.CreateInstance(metadataType)!;var animation=(IDictionary)Activator.CreateInstance(metadataType)!;
        animation["offsetPosition"]=Convert(points);metadata["animation"]=animation;
        int cases=0,failed=0,frontRestored=0,frontMoving=0;float maxClockError=0;var failures=new List<object>();
        foreach(float wallTime in new[]{0f,60f,300f})foreach(float halfJump in new[]{.25f,.5f,1.5f})
        foreach(float duration in new[]{0f,.05f,.5f,2f,8f})foreach(float move in new[]{0f,.5f,1f})foreach(bool mirror in new[]{false,true})
        {
            float jump=2*halfJump,start=wallTime-move-halfJump;
            var item=obstacleCtor.Invoke(new object[]{wallTime,2f,duration,0,2,Enum.ToObject(obstacleCtor.GetParameters()[5].ParameterType,2),1f,3,3,metadata,new Version(3,0)});
            var parsed=parsedCtor.Invoke(new object[]{item,metadata,120f,Activator.CreateInstance(cp[3].ParameterType)!,Activator.CreateInstance(cp[4].ParameterType)!,false,mirror});
            var helper=Activator.CreateInstance(helperType,flags,null,new object[]{mirror},null)!;
            var probes=new[]{-halfJump,0f,duration*.5f,duration,duration+halfJump};
            for(int phase=0;phase<probes.Length;phase++)
            {
                float elapsed=probes[phase];
                float song=wallTime+elapsed;float progress=evaluate(song,start,move,jump,duration);
                float expectedProgress=(song-wallTime+halfJump)/(jump+duration);
                float error=Math.Abs(progress-expectedProgress);maxClockError=Math.Max(maxClockError,error);
                object?[] args=new object?[offset.GetParameters().Length];args[0]=parsed.GetType().GetProperty("AnimationObject",flags)!.GetValue(parsed);args[2]=progress;offset.Invoke(helper,args);
                var actual=(Vector3)args[3]!;var expected=own!.At(progress);
                bool pass=error<.0001f&&Math.Abs(actual.x-(mirror?-expected.X:expected.X)*.6f)<1e-5&&actual.y==0&&actual.z==0&&args.Skip(4).All(v=>v==null);
                if(phase==1){if(own.IsSettledZeroAt(progress))frontRestored++;else frontMoving++;}
                cases++;if(!pass){failed++;if(failures.Count<10)failures.Add(new{wallTime,halfJump,duration,move,mirror,elapsed,progress,expectedProgress,error});}
            }
        }
        return new{schemaVersion=1,cases,failed,maxClockError,frontRestored,frontMoving,failures,gameStarted=false,referenceUsed=false,
            scope="pinned ObstacleUpdate clock arithmetic extracted and executed; wall start-offset IL audited, actual obstacle constructors and AnimationHelper; varied jump/wall duration and both hands; nominal front-plane time only, no full collision interval/player bounds, time overrides, jump preference resolution or Unity lifecycle; diagnostic only"};
    }
}
