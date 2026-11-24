using System.Reflection;
using System.Reflection.Emit;
using Mono.Cecil;
using Mono.Cecil.Cil;
using Q=System.Numerics.Quaternion;
using V=System.Numerics.Vector3;
using M=System.Numerics.Matrix4x4;
using EmitCodes=System.Reflection.Emit.OpCodes;

namespace BeatInsight.Compare;

// Controlled receivers for extracted IL. These are not Unity Transform emulation.
public sealed class LookNoteData { public bool DisableLook {get;set;} }
public sealed class LookUpdate { public LookNoteData? NoodleData {get;set;} }
public sealed class LookContext { public LookUpdate Update=new(); }
public sealed class LookPose
{
    public LookPose? Parent {get;set;}
    public V Position {get;set;}
    public V Scale=V.One;
    Q local=Q.Identity;
    public int WorldWrites,LocalWrites;
    public Q LocalRotation {get=>local;set{local=Q.Normalize(value);LocalWrites++;}}
    public Q Rotation {get=>Parent==null?local:Q.Normalize(Parent.Rotation*local);set{local=Parent==null?Q.Normalize(value):Q.Normalize(Q.Inverse(Parent.Rotation)*value);WorldWrites++;}}
    public V Up=>V.Transform(V.UnitY,Rotation);
    public V TransformPoint(V point)=>V.Transform(point*Scale,Rotation)+Position;
}
public sealed class LookHead
{
    public LookPose Origin=new();
    public V Point;
    public int Reads;
    public bool RejectReads;
    public V HeadPseudoLocalPos {get{Reads++;if(RejectReads)throw new InvalidOperationException("Unexpected head read");return Point;}}
}
public static class LookMath
{
    public static Q Multiply(Q a,Q b)=>a*b;
    public static V Rotate(Q a,V b)=>V.Transform(b,a);
    public static Q Inverse(Q value)=>Q.Inverse(value);
    public static Q Slerp(Q a,Q b,float t)=>Q.Normalize(Q.Slerp(a,b,Math.Clamp(t,0,1)));
    public static Q Lerp(Q a,Q b,float t)=>Q.Normalize(Q.Lerp(a,b,Math.Clamp(t,0,1)));
    public static float ScalarLerp(float a,float b,float t)=>a+(b-a)*Math.Clamp(t,0,1);
    public static V Normalized(ref V value)=>V.Normalize(value);
    public static bool Different(LookPose? a,LookPose? b)=>!ReferenceEquals(a,b);
    public static Q LookRotation(V forward,V up)
    {
        var f=V.Normalize(forward);var right=V.Normalize(V.Cross(up,f));var u=V.Cross(f,right);
        return Q.Normalize(Q.CreateFromRotationMatrix(new M(right.X,right.Y,right.Z,0,u.X,u.Y,u.Z,0,f.X,f.Y,f.Z,0,0,0,0,1)));
    }
}

static class NoodleLookChecks
{
    delegate void Look(LookContext receiver,float time,Q start,Q middle,Q end,LookHead head,LookPose root,LookPose child,Q inverseWorld,bool towards);
    static bool Same(Q a,Q b)=>1-MathF.Abs(Q.Dot(Q.Normalize(a),Q.Normalize(b)))<.00002f;
    static Q Z(float angle)=>Q.CreateFromAxisAngle(V.UnitZ,angle*MathF.PI/180);
    static Q Y(float angle)=>Q.CreateFromAxisAngle(V.UnitY,angle*MathF.PI/180);

    public static object Run(string gameRoot)
    {
        string path=Path.Combine(gameRoot,"Plugins/NoodleExtensions.dll");
        if(Comparison.HashFile(path)!="FE50124E33631EED7ECB78EA1A19BB7EA64CC12974B0298DAD991672C2ABDA95")throw new InvalidDataException("Reinspect changed note look handler");
        using var module=ModuleDefinition.ReadModule(path);
        var method=module.Types.Single(t=>t.Name=="NoteJumpNoodlifier").Methods.Single(m=>m.Name=="DoNoteLook");
        string mainPath=Path.Combine(gameRoot,"Beat Saber_Data/Managed/Main.dll");
        if(Comparison.HashFile(mainPath)!="A276C8A50ACFAD69A3DD3A82CDE08571C3262344702B9B014C89756771B4495E")throw new InvalidDataException("Reinspect changed look flag forwarding");
        using var main=ModuleDefinition.ReadModule(mainPath);
        var rotateGate=CompileRotateGate(main);
        var look=Compile(method);
        int cases=0,failed=0;var failures=new List<object>();
        void Check(bool pass,object scenario){cases++;if(!pass){failed++;if(failures.Count<16)failures.Add(scenario);}}
        (LookContext Context,LookHead Head,LookPose Root,LookPose Child) Fixture(bool parent,float childAngle,float headX,V translation,bool disable)
        {
            var ancestor=parent?new LookPose {LocalRotation=Y(30)}:null;
            var root=new LookPose {Parent=ancestor,LocalRotation=Y(-15),Position=new V(1,1.5f,2)+translation};
            var child=new LookPose {Parent=root,LocalRotation=Z(childAngle)};
            var head=new LookHead {Origin=new LookPose {LocalRotation=Y(-10),Position=new V(.2f,.1f,-.5f)+translation},Point=new V(headX,1.6f,0),RejectReads=disable};
            root.WorldWrites=root.LocalWrites=child.WorldWrites=child.LocalWrites=0;
            return(new LookContext {Update=new LookUpdate {NoodleData=new LookNoteData {DisableLook=disable}}},head,root,child);
        }
        foreach(bool parent in new[]{false,true})foreach(float childAngle in new[]{0f,90f})foreach(float headX in new[]{-.3f,.3f})
        foreach(float time in new[]{0f,.12499f,.125f,.375f,.49f,.5f})foreach(string mode in new[]{"disabled","no-towards","towards"})
        {
            bool disable=mode=="disabled",towards=mode!="no-towards";
            var a=Fixture(parent,childAngle,headX,V.Zero,disable);
            if(!towards)a.Head.RejectReads=true;
            Q end=Z(-45),inverse=Y(-20);
            look(a.Context,time,end,end,end,a.Head,a.Root,a.Child,inverse,towards);
            bool pass=disable?Same(a.Child.LocalRotation,end)&&a.Child.LocalWrites==1&&a.Child.WorldWrites==0&&a.Head.Reads==0:
                a.Child.WorldWrites==1&&a.Child.LocalWrites==0&&a.Head.Reads==(towards?1:0);
            if(!disable&&(!towards||time==0))pass&=Same(a.Child.Rotation,a.Root.Rotation*end);
            pass&=a.Root.WorldWrites==0&&a.Root.LocalWrites==0;
            var translated=Fixture(parent,childAngle,headX,new V(13,-7,5),disable);
            look(translated.Context,time,end,end,end,translated.Head,translated.Root,translated.Child,inverse,towards);
            pass&=Same(a.Child.Rotation,translated.Child.Rotation);
            if(!disable&&towards&&time==.5f)
            {
                // Endpoint forward direction is checked without constructing a gold quaternion.
                Q combined=inverse*(a.Root.Parent==null?Q.Identity:Q.Inverse(a.Root.Parent.Rotation));
                V head=a.Head.Origin.TransformPoint(a.Head.Point),position=a.Root.Position;
                float shift=.8f*(V.Transform(position,combined).Y-V.Transform(head,combined).Y);
                V adjusted=head+V.Transform(V.UnitY,Q.Inverse(combined))*shift;
                V expected=V.Transform(V.Transform(V.Normalize(position-adjusted),combined),a.Root.Rotation);
                pass&=V.Distance(V.Transform(V.UnitZ,a.Child.Rotation),expected)<.0001f;
            }
            Check(pass,new{parent,childAngle,headX,time,mode});
        }
        // The interpolation endpoints have independent, fixed expected rotations.
        foreach(bool parent in new[]{false,true})foreach(var endpoint in new[]{(Time:0f,Angle:-90f),(Time:.125f,Angle:0f),(Time:.375f,Angle:90f)})
        {
            var f=Fixture(parent,0,0,V.Zero,false);f.Head.RejectReads=true;
            look(f.Context,endpoint.Time,Z(-90),Z(0),Z(90),f.Head,f.Root,f.Child,Q.Identity,false);
            Check(Same(f.Child.Rotation,f.Root.Rotation*Z(endpoint.Angle)),new{kind="interpolation-endpoint",parent,endpoint});
        }
        int historyCases=0;
        foreach(float headX in new[]{-.4f,0f,.4f})foreach(bool parent in new[]{false,true})
        {
            Q At(float last)
            {
                var f=Fixture(parent,0,headX,V.Zero,false);
                // Caller gate is a model of the separately audited transpiler gate.
                foreach(float time in new[]{0f,last,.5f,.51f})
                    if(time<.5f)look(f.Context,time,Z(-90),Z(0),Z(90),f.Head,f.Root,f.Child,Q.Identity,true);
                if(f.Child.WorldWrites!=2)throw new InvalidDataException("Controlled caller gate write count: "+f.Child.WorldWrites+" local: "+f.Child.LocalWrites+" root: "+f.Root.WorldWrites+" head: "+f.Head.Reads+" failures: "+failed);
                return f.Child.Rotation;
            }
            Check(!Same(At(.3f),At(.49f)),new{kind="last-update-dependency",headX,parent});historyCases++;
        }
        foreach(float time in new[]{0f,.125f,.49f})
        {
            var f=Fixture(true,0,0,V.Zero,false);f.Context.Update.NoodleData=null;f.Head.RejectReads=true;
            look(f.Context,time,Z(20),Z(20),Z(20),f.Head,f.Root,f.Child,Q.Identity,false);
            Check(Same(f.Child.Rotation,f.Root.Rotation*Z(20))&&f.Child.WorldWrites==1&&f.Head.Reads==0,new{kind="absent-note-data",time});
        }
        foreach(int gameplayType in new[]{0,1,2,3})Check(rotateGate(gameplayType)==(gameplayType==0),new{kind="installed-rotate-flag",gameplayType});
        var left=Fixture(false,0,-.6f,V.Zero,false);var right=Fixture(false,0,.6f,V.Zero,false);
        look(left.Context,.49f,Z(0),Z(0),Z(0),left.Head,left.Root,left.Child,Q.Identity,true);
        look(right.Context,.49f,Z(0),Z(0),Z(0),right.Head,right.Root,right.Child,Q.Identity,true);
        Check(!Same(left.Child.Rotation,right.Child.Rotation),new{kind="head-position-dependency"});
        return new{cases,failed,copiedInstructions=method.Body.Instructions.Count,historyCases,rotationFlagCases=4,flagForwardingAudited=true,failures,
            scope="Installed complete DoNoteLook IL with controlled data/head/pose receivers and managed quaternion/vector bindings; translation and branch invariants, fixed interpolation/forward endpoints, head and last-update counterexamples; caller gate modeled from separate audit; native Unity quaternion/Transform/collider/lifecycle and all-patch ordering not executed"};
    }

    static Func<int,bool> CompileRotateGate(ModuleDefinition main)
    {
        MethodDefinition Method(string type)=>main.Types.Single(t=>t.Name==type).Methods.Single(m=>m.Name=="Init");
        static bool Call(Instruction i,string type,string name)=>i.Operand is MethodReference m&&m.DeclaringType.Name==type&&m.Name==name;
        var init=Method("GameNoteController");
        var write=init.Body.Instructions.Single(i=>i.OpCode.Code==Code.Stloc_1);
        if(write.Previous.OpCode.Code!=Code.Ceq||write.Previous.Previous.OpCode.Code!=Code.Ldc_I4_0
            ||!Call(write.Previous.Previous.Previous,"NoteData","get_gameplayType")||write.Previous.Previous.Previous.Previous.OpCode.Code!=Code.Ldarg_1)
            throw new InvalidDataException("Ordinary-note rotation gate changed");
        var controller=init.Body.Instructions.Single(i=>Call(i,"NoteController","Init"));
        if(controller.Previous.Previous.OpCode.Code!=Code.Ldloc_1)throw new InvalidDataException("Rotation gate forwarding changed");
        foreach(var link in new[]{("NoteController","NoteMovement"),("NoteMovement","NoteJump")})
        {
            var call=Method(link.Item1).Body.Instructions.Single(i=>Call(i,link.Item2,"Init"));
            if(call.Previous.Previous.Operand is not ParameterDefinition p||p.Name!="rotateTowardsPlayer")throw new InvalidDataException("Look flag is no longer forwarded unchanged");
        }
        var store=Method("NoteJump").Body.Instructions.Single(i=>i.OpCode.Code==Code.Stfld&&i.Operand is FieldReference f&&f.Name=="_rotateTowardsPlayer");
        if(store.Previous.Operand is not ParameterDefinition flag||flag.Name!="rotateTowardsPlayer")throw new InvalidDataException("Stored look flag changed");
        var dynamic=new DynamicMethod("InstalledOrdinaryNoteLookFlag",typeof(bool),new[]{typeof(int)},typeof(NoodleLookChecks).Module,true);
        var il=dynamic.GetILGenerator();il.Emit(EmitCodes.Ldarg_0); // Controlled gameplayType replaces the simple data getter.
        foreach(var instruction in new[]{write.Previous.Previous,write.Previous})
            il.Emit((System.Reflection.Emit.OpCode)typeof(EmitCodes).GetField(instruction.OpCode.Code.ToString())!.GetValue(null)!);
        il.Emit(EmitCodes.Ret);return (Func<int,bool>)dynamic.CreateDelegate(typeof(Func<int,bool>));
    }

    static Look Compile(MethodDefinition method)
    {
        string[] names={"num2","startRotation","middleRotation","endRotation","playerTransforms","rotatedObject","baseTransform","inverseWorldRotation","rotateTowardsPlayer"};
        if(!method.HasThis||!method.Parameters.Select(p=>p.Name).SequenceEqual(names)||method.Body.ExceptionHandlers.Count!=0)throw new InvalidDataException("Changed note look ABI");
        var dynamic=new DynamicMethod("InstalledDoNoteLookControlledReceivers",typeof(void),new[]{typeof(LookContext),typeof(float),typeof(Q),typeof(Q),typeof(Q),typeof(LookHead),typeof(LookPose),typeof(LookPose),typeof(Q),typeof(bool)},typeof(NoodleLookChecks).Module,true);
        var il=dynamic.GetILGenerator();
        Type Map(TypeReference type)=>type.FullName switch {"NoodleExtensions.NoodleBaseNoteData"=>typeof(LookNoteData),"UnityEngine.Quaternion"=>typeof(Q),"UnityEngine.Vector3"=>typeof(V),"System.Single"=>typeof(float),_=>throw new InvalidDataException("Unexpected look local: "+type.FullName)};
        foreach(var variable in method.Body.Variables)il.DeclareLocal(Map(variable.VariableType));
        var labels=method.Body.Instructions.ToDictionary(i=>i,_=>il.DefineLabel());
        MethodInfo Math(string name)=>typeof(LookMath).GetMethod(name)!;
        MethodInfo Bind(MethodReference call)=>call.DeclaringType.FullName switch
        {
            "NoodleExtensions.HarmonyPatches.Objects.NoteUpdateNoodlifier"=>typeof(LookUpdate).GetProperty("NoodleData")!.GetMethod!,
            "NoodleExtensions.NoodleBaseNoteData"=>typeof(LookNoteData).GetProperty("DisableLook")!.GetMethod!,
            "PlayerTransforms"=>typeof(LookHead).GetProperty("HeadPseudoLocalPos")!.GetMethod!,
            "UnityEngine.Transform"=>call.Name switch {"get_parent"=>typeof(LookPose).GetProperty("Parent")!.GetMethod!,"get_rotation"=>typeof(LookPose).GetProperty("Rotation")!.GetMethod!,"set_rotation"=>typeof(LookPose).GetProperty("Rotation")!.SetMethod!,"set_localRotation"=>typeof(LookPose).GetProperty("LocalRotation")!.SetMethod!,"get_position"=>typeof(LookPose).GetProperty("Position")!.GetMethod!,"get_up"=>typeof(LookPose).GetProperty("Up")!.GetMethod!,"TransformPoint"=>typeof(LookPose).GetMethod("TransformPoint")!,_=>throw new InvalidDataException("Unexpected pose call: "+call)},
            "UnityEngine.Object" when call.Name=="op_Inequality"=>Math("Different"),
            "UnityEngine.Mathf"=>call.Name switch {"Sin"=>typeof(MathF).GetMethod("Sin")!,"Lerp"=>Math("ScalarLerp"),_=>throw new InvalidDataException("Unexpected scalar call")},
            "UnityEngine.Quaternion"=>call.Name switch {"op_Multiply"=>Math(call.Parameters[1].ParameterType.Name=="Quaternion"?"Multiply":"Rotate"),"Slerp"=>Math("Slerp"),"Lerp"=>Math("Lerp"),"Inverse"=>Math("Inverse"),"LookRotation"=>Math("LookRotation"),_=>throw new InvalidDataException("Unexpected quaternion call")},
            "UnityEngine.Vector3"=>call.Name switch {"get_up"=>typeof(V).GetProperty("UnitY")!.GetMethod!,"get_normalized"=>Math("Normalized"),"Dot"=>typeof(V).GetMethod("Dot")!,"op_Multiply"=>typeof(V).GetMethod("op_Multiply",new[]{typeof(V),typeof(float)})!,"op_Addition"=>typeof(V).GetMethod("op_Addition")!,"op_Subtraction"=>typeof(V).GetMethod("op_Subtraction",new[]{typeof(V),typeof(V)})!,_=>throw new InvalidDataException("Unexpected vector call")},
            _=>throw new InvalidDataException("Unexpected look call: "+call)
        };
        foreach(var instruction in method.Body.Instructions)
        {
            il.MarkLabel(labels[instruction]);
            var opcode=(System.Reflection.Emit.OpCode)typeof(EmitCodes).GetField(instruction.OpCode.Code.ToString())!.GetValue(null)!;
            switch(instruction.Operand)
            {
                case null:il.Emit(opcode);break;
                case float value:il.Emit(opcode,value);break;
                case ParameterDefinition parameter:il.Emit(opcode,(byte)(parameter.Name=="rotatedObject"?7:parameter.Name=="baseTransform"?6:parameter.Index+1));break;
                case VariableDefinition variable:il.Emit(opcode,(byte)variable.Index);break;
                case Instruction target:il.Emit(opcode,labels[target]);break;
                case FieldReference field:
                    var bound=field.DeclaringType.FullName switch {"UnityEngine.Vector3"=>typeof(V).GetField(field.Name.ToUpperInvariant()),"PlayerTransforms" when field.Name=="_originParentTransform"=>typeof(LookHead).GetField("Origin"),"NoodleExtensions.HarmonyPatches.Objects.NoteJumpNoodlifier" when field.Name=="_noteUpdateNoodlifier"=>typeof(LookContext).GetField("Update"),_=>null};
                    il.Emit(opcode,bound??throw new InvalidDataException("Unexpected look field: "+field));break;
                case MethodReference call:
                    var boundMethod=Bind(call);il.Emit(boundMethod.IsStatic?EmitCodes.Call:opcode,boundMethod);break;
                default:throw new InvalidDataException("Unexpected look instruction: "+instruction);
            }
        }
        return (Look)dynamic.CreateDelegate(typeof(Look));
    }
}
