using System.Reflection;
using System.Reflection.Emit;
using Mono.Cecil;
using Mono.Cecil.Cil;
using UnityEngine;
using Matrix=System.Numerics.Matrix4x4;
using Vector=System.Numerics.Vector3;
using EmitCodes=System.Reflection.Emit.OpCodes;

namespace BeatInsight.Compare;

static class NoodleRoomAdjustmentChecks
{
    public static object Run(string gameRoot)
    {
        string path=Path.Combine(gameRoot,"Beat Saber_Data/Managed/Main.dll");
        if(Comparison.HashFile(path)!="A276C8A50ACFAD69A3DD3A82CDE08571C3262344702B9B014C89756771B4495E")throw new InvalidDataException("Reinspect changed room adjustment");
        using var module=ModuleDefinition.ReadModule(path);
        var type=module.Types.Single(t=>t.Name=="VRCenterAdjust");
        var start=type.Methods.Single(m=>m.Name=="Start");
        var sequence=start.Body.Instructions.Where(i=>i.Offset>=0x1a).ToArray();
        var dynamic=new DynamicMethod("InstalledRoomStartGate",typeof(void),new[]{typeof(Vector3),typeof(Action),typeof(Action)},typeof(NoodleRoomAdjustmentChecks).Module,true);
        var il=dynamic.GetILGenerator();var center=il.DeclareLocal(typeof(Vector3));var labels=sequence.ToDictionary(i=>i,i=>il.DefineLabel());
        il.Emit(EmitCodes.Ldarg_0);
        foreach(var i in sequence)
        {
            il.MarkLabel(labels[i]);
            switch(i.OpCode.Code)
            {
                case Code.Stloc_0:il.Emit(EmitCodes.Stloc,center);break;
                case Code.Ldloc_0:il.Emit(EmitCodes.Ldloc,center);break;
                case Code.Ldfld:
                    var field=(FieldReference)i.Operand;
                    if(field.DeclaringType.FullName!="UnityEngine.Vector3"||!new[]{"x","y","z"}.Contains(field.Name))throw new InvalidDataException("Changed room center reader");
                    il.Emit(EmitCodes.Ldfld,typeof(Vector3).GetField(field.Name)!);break;
                case Code.Ldc_R4:il.Emit(EmitCodes.Ldc_R4,(float)i.Operand);break;
                case Code.Bgt_S:il.Emit(EmitCodes.Bgt,labels[(Instruction)i.Operand]);break;
                case Code.Ble_Un_S:il.Emit(EmitCodes.Ble_Un,labels[(Instruction)i.Operand]);break;
                case Code.Ldarg_0:
                    if(i.Next.Operand is not MethodReference next)throw new InvalidDataException("Changed room callback receiver");
                    il.Emit(next.Name=="ResetRoom"?EmitCodes.Ldarg_1:next.Name=="SetRoomTransformOffset"?EmitCodes.Ldarg_2:throw new InvalidDataException("Unknown room callback"));break;
                case Code.Call:
                    var method=(MethodReference)i.Operand;
                    if(method.DeclaringType.FullName=="UnityEngine.Mathf"&&method.Name=="Abs")il.Emit(EmitCodes.Call,typeof(Mathf).GetMethod("Abs",new[]{typeof(float)})!);
                    else if(method.DeclaringType.Name=="VRCenterAdjust"&&(method.Name=="ResetRoom"||method.Name=="SetRoomTransformOffset"))il.Emit(EmitCodes.Callvirt,typeof(Action).GetMethod("Invoke")!);
                    else throw new InvalidDataException("Changed room gate call");break;
                case Code.Ret:il.Emit(EmitCodes.Ret);break;
                default:throw new InvalidDataException("Unexpected room gate instruction");
            }
        }
        var apply=(Action<Vector3,Action,Action>)dynamic.CreateDelegate(typeof(Action<Vector3,Action,Action>));
        var set=type.Methods.Single(m=>m.Name=="SetRoomTransformOffset");
        bool Calls(MethodDefinition m,string name)=>m.Body.Instructions.Any(i=>(i.OpCode.Code==Code.Call||i.OpCode.Code==Code.Callvirt)&&i.Operand is MethodReference r&&r.Name==name);
        if(!Calls(set,"SetLocalPositionAndRotation")||!Calls(set,"Euler")||!set.Body.Instructions.Any(i=>i.Operand is FieldReference f&&f.Name=="center")
            ||!set.Body.Instructions.Any(i=>i.Operand is FieldReference f&&f.Name=="rotation"))throw new InvalidDataException("Room pose assignment changed");
        if(Calls(type.Methods.Single(m=>m.Name=="OnEnable"),"SetRoomTransformOffset")||!Calls(type.Methods.Single(m=>m.Name=="OnEnable"),"add_roomTransformOffsetDidUpdateEvent")
            ||!Calls(type.Methods.Single(m=>m.Name=="OnDisable"),"remove_roomTransformOffsetDidUpdateEvent"))throw new InvalidDataException("Room enable lifecycle changed");
        int cases=0,failed=0,gateCases=0,poseModelCases=0,orderModelCases=0,lateStartCounterexamples=0;float maxError=0;var failures=new List<object>();
        foreach(float amount in new[]{0f,4.1f,-4.1f,MathF.BitIncrement(4.1f),MathF.BitDecrement(-4.1f),float.NaN,float.PositiveInfinity,float.NegativeInfinity})foreach(int axis in new[]{0,1,2})
        {
            var value=new Vector3(axis==0?amount:0,axis==1?amount:0,axis==2?amount:0);var events=new List<string>();
            apply(value,()=>events.Add("reset"),()=>events.Add("apply"));bool reset=Math.Abs(amount)>4.1f;
            bool pass=events.SequenceEqual(reset?new[]{"reset","apply"}:new[]{"apply"});
            cases++;gateCases++;if(!pass){failed++;failures.Add(new{kind="room-start-gate",axis,amount=amount.ToString(System.Globalization.CultureInfo.InvariantCulture)});}
        }
        float Error(Matrix a,Matrix b)
        {
            float error=0;foreach(var p in new[]{Vector.Zero,Vector.UnitX,Vector.UnitY,Vector.UnitZ})error=Math.Max(error,Vector.Distance(Vector.Transform(p,a),Vector.Transform(p,b)));return error;
        }
        foreach(var roomCenter in new[]{Vector.Zero,new Vector(.3f,1,-.7f),new Vector(-2,0,3)})foreach(float yaw in new[]{-90f,0f,45f,180f})
        {
            float radians=yaw*(float)Math.PI/180;var room=Matrix.CreateRotationY(radians)*Matrix.CreateTranslation(roomCenter);
            var quaternion=new Quaternion(0,(float)Math.Sin(radians*.5f),0,(float)Math.Cos(radians*.5f));
            foreach(var point in new[]{Vector.Zero,Vector.UnitX,Vector.UnitY,Vector.UnitZ})
            {
                var actual=quaternion*new Vector3(point.X,point.Y,point.Z);
                var matrixPoint=Vector.Transform(point,room);float x=(float)Math.Cos(radians)*point.X+(float)Math.Sin(radians)*point.Z+roomCenter.X;
                float y=point.Y+roomCenter.Y,z=-(float)Math.Sin(radians)*point.X+(float)Math.Cos(radians)*point.Z+roomCenter.Z;
                float error=Math.Max(Vector.Distance(matrixPoint,new Vector(x,y,z)),Vector.Distance(new Vector(actual.x,actual.y,actual.z)+roomCenter,new Vector(x,y,z)));maxError=Math.Max(maxError,error);
                cases++;poseModelCases++;if(error>.00001f){failed++;failures.Add(new{kind="room-pose-model",yaw,error});}
            }
            Matrix.Invert(room,out var inverseRoom);
            foreach(var parent in new[]{Matrix.Identity,inverseRoom,Matrix.CreateRotationY(.3f)*Matrix.CreateTranslation(1,2,3)})foreach(bool startBeforeReparent in new[]{false,true})
            {
                var before=Matrix.CreateRotationZ(.2f)*Matrix.CreateTranslation(.2f,1.7f,-.5f);
                // The new RoomOffset is parented with worldPositionStays=true,
                // hence its world pose is identity until Start applies room pose.
                var atReparent=startBeforeReparent?room*parent:Matrix.Identity;
                Matrix.Invert(atReparent,out var inverse);var childLocal=before*inverse;var after=childLocal*room*parent;
                bool neutral=Error(room*parent,Matrix.Identity)<.00001f;
                float error=Error(before,after);bool expectedPreserved=startBeforeReparent||neutral;
                bool preserved=error<.0001f;cases++;orderModelCases++;
                if(!startBeforeReparent&&!preserved)lateStartCounterexamples++;
                if(preserved!=expectedPreserved){failed++;failures.Add(new{kind="room-order-model",yaw,startBeforeReparent,error,expectedPreserved});}
            }
        }
        return new{schemaVersion=1,cases,gateCases,poseModelCases,orderModelCases,lateStartCounterexamples,maxError,failed,failures,
            gameStarted=false,settingsRead=false,settingsWritten=false,menuAdmissionChanged=false,
            scope="pinned Start threshold IL with explicit center and substituted ResetRoom/Apply callbacks; actual Mathf Abs; audited room pose/subscribe calls, independent scalar/matrix/managed quaternion models; two hypothetical Start orders, not observed Unity scheduling or bound scene hierarchy; native Euler, SetParent, actual settings and VRCenterAdjust components not executed"};
    }
}
