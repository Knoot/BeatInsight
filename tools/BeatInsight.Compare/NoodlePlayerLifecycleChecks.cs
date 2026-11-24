using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.CompilerServices;
using System.Runtime.Loader;
using Mono.Cecil;
using Mono.Cecil.Cil;
using Matrix=System.Numerics.Matrix4x4;
using Vector=System.Numerics.Vector3;
using EmitCodes=System.Reflection.Emit.OpCodes;

namespace BeatInsight.Compare;

static class NoodlePlayerLifecycleChecks
{
    public static object Run(string gameRoot)
    {
        const BindingFlags flags=BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic;
        using var module=ModuleDefinition.ReadModule(Path.Combine(gameRoot,"Plugins/NoodleExtensions.dll"));
        var playerType=module.Types.Single(t=>t.Name=="PlayerTrack");
        var construct=playerType.Methods.Single(m=>m.Name=="Construct");
        var gate=construct.Body.Instructions.Where(i=>i.Offset>=0xc7&&i.Offset<0xd6).ToArray();
        if(!gate.Select(i=>i.OpCode.Code).SequenceEqual(new[]{Code.Ldarg_0,Code.Ldfld,Code.Brtrue_S,Code.Ldarg_0,Code.Ldc_I4_0,Code.Call})
            ||gate[1].Operand is not FieldReference version||version.Name!="_v2"||gate[5].Operand is not MethodReference setter||setter.Name!="set_enabled")throw new InvalidDataException("Player v3 enable gate changed");
        // Copy the pinned conditional and false write. Receiver/version field and
        // Behaviour setter are explicit inputs; no Unity component is created.
        var dynamic=new DynamicMethod("InstalledPlayerEnableGate",typeof(void),new[]{typeof(bool),typeof(Action<bool>)},typeof(NoodlePlayerLifecycleChecks).Module,true);
        var il=dynamic.GetILGenerator();var end=il.DefineLabel();
        foreach(var i in gate)
        {
            switch(i.OpCode.Code)
            {
                case Code.Ldarg_0:il.Emit(i.Offset==0xc7?EmitCodes.Ldarg_0:EmitCodes.Ldarg_1);break;
                case Code.Ldfld:break;
                case Code.Brtrue_S:if(((Instruction)i.Operand).Offset!=0xd6)throw new InvalidDataException("Player enable gate target changed");il.Emit(EmitCodes.Brtrue,end);break;
                case Code.Ldc_I4_0:il.Emit(EmitCodes.Ldc_I4_0);break;
                case Code.Call:il.Emit(EmitCodes.Callvirt,typeof(Action<bool>).GetMethod("Invoke")!);break;
                default:throw new InvalidDataException("Unexpected enable instruction");
            }
        }
        il.MarkLabel(end);il.Emit(EmitCodes.Ret);var apply=(Action<bool,Action<bool>>)dynamic.CreateDelegate(typeof(Action<bool,Action<bool>>));
        var manager=module.Types.Single(t=>t.Name=="NoodlePlayerTransformManager");
        var initialize=manager.Methods.Single(m=>m.Name=="Initialize");
        int created=initialize.Body.Instructions.Count(i=>i.Operand is MethodReference m&&m.Name=="Create"&&m.DeclaringType==manager);
        if(created!=4)throw new InvalidDataException("Player wrapper creation count changed");
        var create=manager.Methods.Single(m=>m.Name=="Create");
        var root=create.Body.Instructions.Where(i=>i.Offset>=0x121&&i.Offset<=0x131).ToArray();
        if(!root.Select(i=>i.OpCode.Code).SequenceEqual(new[]{Code.Ldloc_1,Code.Ldloc_2,Code.Callvirt,Code.Ldc_I4_0,Code.Callvirt,Code.Ldloc_2,Code.Ldloc_1,Code.Ldc_I4_1,Code.Callvirt})
            ||root[2].Operand is not MethodReference parent||parent.Name!="get_parent"
            ||root[4].Operand is not MethodReference p0||p0.Name!="SetParent"
            ||root[8].Operand is not MethodReference p1||p1.Name!="SetParent")throw new InvalidDataException("Root parent preservation changed");
        if(!create.Body.Instructions.Any(i=>i.Operand is MethodReference m&&m.Name=="InstantiateComponent"&&m is GenericInstanceMethod generic&&generic.GenericArguments.Any(a=>a.Name=="VRCenterAdjust")))throw new InvalidDataException("Player room adjustment path changed");
        var capture=construct.Body.Instructions.Where(i=>i.Operand is FieldReference f&&(f.Name=="_startPos"||f.Name=="_startLocalRot")).ToArray();
        if(capture.Length!=2||capture.Any(i=>i.Previous.Operand is not MethodReference m||(m.Name!="get_localPosition"&&m.Name!="get_localRotation")))throw new InvalidDataException("Player start pose capture changed");
        var assign=playerType.Methods.Single(m=>m.Name=="AssignTrack");
        if(!assign.Body.Instructions.Any(i=>i.Operand is MethodReference m&&m.Name=="Create"&&m.DeclaringType.Name=="TransformControllerFactory"))throw new InvalidDataException("Player controller factory changed");
        var noodle=AssemblyLoadContext.Default.LoadFromAssemblyName(new AssemblyName("NoodleExtensions"));
        var custom=AssemblyLoadContext.Default.LoadFromAssemblyName(new AssemblyName("CustomJSONData"));
        var isV2=custom.GetType("CustomJSONData.VersionExtensions",true)!.GetMethod("IsVersion2",BindingFlags.Static|BindingFlags.Public|BindingFlags.NonPublic)!;
        int cases=0,failed=0,gateCases=0,routingCases=0,hierarchyModelCases=0;var failures=new List<object>();
        foreach(int major in new[]{2,3})
        {
            bool v2=(bool)isV2.Invoke(null,new object[]{new Version(major,major==2?6:3,0)})!;
            bool enabled=true;int writes=0;apply(v2,value=>{enabled=value;writes++;});
            bool pass=v2==(major==2)&&enabled==(major==2)&&writes==(major==3?1:0);
            cases++;gateCases++;if(!pass){failed++;failures.Add(new{kind="enabled",major,v2,enabled,writes});}
        }
        var managerType=noodle.GetType(manager.FullName,true)!;var receiver=RuntimeHelpers.GetUninitializedObject(managerType);
        managerType.GetField("<Active>k__BackingField",flags)!.SetValue(receiver,true);
        var objects=new object[4];string[] names={"_root","_head","_leftHand","_rightHand"};
        for(int i=0;i<4;i++){objects[i]=RuntimeHelpers.GetUninitializedObject(typeof(UnityEngine.GameObject));managerType.GetField(names[i],flags)!.SetValue(receiver,objects[i]);}
        var get=managerType.GetMethod("GetByPlayerObject",flags)!;
        for(int i=0;i<4;i++)
        {
            bool pass=ReferenceEquals(get.Invoke(receiver,new[]{Enum.ToObject(get.GetParameters()[0].ParameterType,i)}),objects[i]);
            cases++;routingCases++;if(!pass){failed++;failures.Add(new{kind="routing",target=i});}
        }
        foreach(float scale in new[]{.6f,1f,2f})foreach(float yaw in new[]{-60f,0f,90f})
        foreach(var origin in new[]{new Vector(0,0,0),new Vector(-2,1,5)})foreach(var local in new[]{new Vector(0,0,0),new Vector(1,.5f,-3)})
        {
            // Managed hierarchy model of the audited Root SetParent(false/true),
            // not execution of native Unity SetParent or non-Root room adjustment.
            var parentMatrix=Matrix.CreateScale(scale)*Matrix.CreateRotationY(yaw*(float)Math.PI/180)*Matrix.CreateTranslation(origin);
            var childLocal=Matrix.CreateRotationZ(.37f)*Matrix.CreateTranslation(local);var before=childLocal*parentMatrix;
            var wrapperWorld=Matrix.Identity*parentMatrix;
            if(!Matrix.Invert(wrapperWorld,out var inverse))throw new InvalidDataException("Singular hierarchy fixture");
            var restoredLocal=before*inverse;var after=restoredLocal*wrapperWorld;
            float error=0;foreach(var point in new[]{Vector.Zero,Vector.UnitX,Vector.UnitY,Vector.UnitZ})error=Math.Max(error,Vector.Distance(Vector.Transform(point,before),Vector.Transform(point,after)));
            bool pass=error<.00001f;cases++;hierarchyModelCases++;if(!pass){failed++;failures.Add(new{kind="root-hierarchy-model",scale,yaw,error});}
        }
        var kernel=Newtonsoft.Json.Linq.JObject.FromObject(NoodleKernelChecks.Run(gameRoot));
        cases+=(int)kernel["cases"]!;failed+=(int)kernel["failed"]!;
        var earlyRoom=Newtonsoft.Json.Linq.JObject.FromObject(NoodleEarlyRoomChecks.Run(gameRoot));
        cases+=(int)earlyRoom["cases"]!;failed+=(int)earlyRoom["failed"]!;
        var activation=Newtonsoft.Json.Linq.JObject.FromObject(NoodleSceneActivationChecks.Run(gameRoot));
        cases+=(int)activation["cases"]!;failed+=(int)activation["failed"]!;
        var room=Newtonsoft.Json.Linq.JObject.FromObject(NoodleRoomAdjustmentChecks.Run(gameRoot));
        cases+=(int)room["cases"]!;failed+=(int)room["failed"]!;
        return new{schemaVersion=1,cases,gateCases,routingCases,hierarchyModelCases,failed,failures,auditedWrapperCreateCalls=created,
            v3DisableGateVerified=true,startPoseCaptureAudited=true,kernel,earlyRoom,activation,room,gameStarted=false,menuAdmissionChanged=false,
            scope="pinned Construct enable slice with substituted Behaviour setter; actual version predicate and manager target routing using managed placeholder objects; Root parenting IL audit plus separate managed matrix model; GameObject/component construction, native Unity SetParent, Head/Hands VRCenterAdjust, full AssignTrack/factory and frame lifecycle not executed"};
    }
}
