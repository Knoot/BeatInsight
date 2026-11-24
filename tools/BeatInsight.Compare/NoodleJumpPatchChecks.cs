using System.Collections;
using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.Loader;
using System.Runtime.CompilerServices;
using Mono.Cecil;
using Mono.Cecil.Cil;

namespace BeatInsight.Compare;

static class NoodleJumpPatchChecks
{
    public static object Run(ModuleDefinition main,ModuleDefinition noodle)
    {
        const BindingFlags flags=BindingFlags.Public|BindingFlags.NonPublic|BindingFlags.Instance;
        var harmony=AssemblyLoadContext.Default.LoadFromAssemblyName(new AssemblyName("0Harmony"));
        var game=AssemblyLoadContext.Default.LoadFromAssemblyName(new AssemblyName("Main"));
        var extension=AssemblyLoadContext.Default.Assemblies.Single(a=>a.GetName().Name=="NoodleExtensions");
        var patchType=extension.GetType("NoodleExtensions.HarmonyPatches.Objects.NoteJumpNoodlifier",true)!;
        var original=game.GetType("NoteJump",true)!.GetMethod("ManualUpdate",flags)!;
        var reader=harmony.GetType("HarmonyLib.PatchProcessor",true)!.GetMethods().Single(m=>m.Name=="GetOriginalInstructions"
            &&m.GetParameters().Length==2&&m.GetParameters()[1].ParameterType==typeof(ILGenerator));
        var labelHost=new DynamicMethod("NoteJumpAuditLabels",typeof(void),Type.EmptyTypes);
        var readerArgs=new object?[]{original,labelHost.GetILGenerator()};
        var source=reader.Invoke(null,readerArgs)!;
        var instructionType=harmony.GetType("HarmonyLib.CodeInstruction",true)!;
        var opcode=instructionType.GetField("opcode")!;var operand=instructionType.GetField("operand")!;var labels=instructionType.GetField("labels")!;
        System.Reflection.Emit.OpCode Code(object instruction)=>(System.Reflection.Emit.OpCode)opcode.GetValue(instruction)!;
        object? Operand(object instruction)=>operand.GetValue(instruction);
        int LocalRotationWrites(IEnumerable instructions)=>instructions.Cast<object>().Count(i=>Operand(i) is MethodInfo m&&m.DeclaringType?.FullName=="UnityEngine.Transform"&&m.Name=="set_localRotation");
        int before=LocalRotationWrites((IEnumerable)source);
        // The installed delegate emitter targets the game's Mono runtime and cannot
        // generate its wrappers on this stand's CLR. Supply typed, never-invoked
        // call operands instead; execute the installed transpiler without registering
        // delegates or changing any loaded game method.
        RuntimeHelpers.RunClassConstructor(patchType.TypeHandle);
        object instance=RuntimeHelpers.GetUninitializedObject(patchType);
        foreach(var binding in new[]{("_doNoteLook","DoNoteLook"),("_noteJumpTimeAdjust","NoteJumpTimeAdjust"),
            ("_definiteNoteJump","DefiniteNoteJump"),("_getDefinitePosition","<.ctor>b__22_0")})
        {
            var method=patchType.GetMethod(binding.Item2,flags)!;
            var stub=new DynamicMethod("Audit"+binding.Item1,method.ReturnType,method.GetParameters().Select(p=>p.ParameterType).ToArray());
            var stubIL=stub.GetILGenerator();
            if(method.ReturnType!=typeof(void))
            {
                var local=stubIL.DeclareLocal(method.ReturnType);stubIL.Emit(System.Reflection.Emit.OpCodes.Ldloca,local);
                stubIL.Emit(System.Reflection.Emit.OpCodes.Initobj,method.ReturnType);stubIL.Emit(System.Reflection.Emit.OpCodes.Ldloc,local);
            }
            stubIL.Emit(System.Reflection.Emit.OpCodes.Ret);
            patchType.GetField(binding.Item1,flags)!.SetValue(instance,
                Activator.CreateInstance(instructionType,new object[]{System.Reflection.Emit.OpCodes.Call,stub})!);
        }
        var dispatch=patchType.GetField("_doNoteLook",flags)!.GetValue(instance)!;
        var patched=((IEnumerable)patchType.GetMethod("Transpiler",flags)!.Invoke(instance,new[]{source})!).Cast<object>().ToArray();
        int lookIndex=Array.FindIndex(patched,i=>Equals(Operand(i),Operand(dispatch))&&Code(i)==Code(dispatch));
        if(before!=2||LocalRotationWrites(patched)!=0||lookIndex<0
            ||patched.Count(i=>Equals(Operand(i),Operand(dispatch))&&Code(i)==Code(dispatch))!=1)
            throw new InvalidDataException("Installed note-look replacement changed");
        int gate=-1;
        for(int i=2;i<lookIndex;i++)
            if(Code(patched[i-2])==System.Reflection.Emit.OpCodes.Ldloc_1&&Code(patched[i-1])==System.Reflection.Emit.OpCodes.Ldc_R4
                &&Operand(patched[i-1]) is float value&&value==.5f
                &&(Code(patched[i])==System.Reflection.Emit.OpCodes.Bge_Un||Code(patched[i])==System.Reflection.Emit.OpCodes.Bge_Un_S))gate=i;
        if(gate<0||Operand(patched[gate]) is not Label destination)throw new InvalidDataException("Patched jump phase gate changed");
        int target=Array.FindIndex(patched,i=>((IEnumerable)labels.GetValue(i)!).Cast<Label>().Contains(destination));
        if(target<=lookIndex)throw new InvalidDataException("Jump phase gate no longer skips the look update");

        var look=noodle.Types.Single(t=>t.FullName==patchType.FullName).Methods.Single(m=>m.Name=="DoNoteLook");
        int Calls(string type,string name)=>look.Body.Instructions.Count(i=>i.Operand is MethodReference m&&m.DeclaringType.FullName==type&&m.Name==name);
        if(Calls("PlayerTransforms","get_headPseudoLocalPos")!=1||Calls("UnityEngine.Quaternion","LookRotation")!=1
            ||Calls("UnityEngine.Transform","set_rotation")!=2||Calls("UnityEngine.Transform","set_localRotation")!=1
            ||Calls("NoodleExtensions.NoodleBaseNoteData","get_DisableLook")!=1)
            throw new InvalidDataException("Installed look pose dependencies changed");
        var trailing=main.Types.Single(t=>t.FullName=="ProModeTrailingCollidersController").Methods.Single(m=>m.Name=="HandleNoteDidMoveInJumpPhase");
        if(!trailing.Body.Instructions.Any(i=>i.Operand is MethodReference m&&m.DeclaringType.FullName=="UnityEngine.Transform"&&m.Name=="set_position")
            ||!trailing.Body.Instructions.Any(i=>i.Operand is MethodReference m&&m.DeclaringType.FullName=="NoteMovement"&&m.Name=="get_prevLocalPosition"))
            throw new InvalidDataException("Pro trailing collider movement changed");
        var size=main.Types.Single(t=>t.FullName=="NoteBigCuttableColliderSize").Methods.Single(m=>m.Name=="HandleNoteControllerDidInit");
        if(size.Body.Instructions.Count(i=>i.Operand is MethodReference m&&m.DeclaringType.FullName=="UnityEngine.Mathf"&&m.Name=="Max")!=1
            ||size.Body.Instructions.Count(i=>i.Operand is MethodReference m&&m.DeclaringType.FullName=="UnityEngine.BoxCollider"&&m.Name=="set_size")!=2)
            throw new InvalidDataException("Dot collider resizing changed");
        return new{transpilerExecuted=true,originalLocalRotationWrites=before,patchedLocalRotationWrites=0,
            lookDispatchCount=1,halfJumpGatePreserved=true,headPositionDependency=true,worldRotationWrites=2,disabledLookLocalRotationWrites=1,
            proTrailingCollidersMove=true,normalDotColliderResizes=true,harmonySha256=Comparison.HashFile(harmony.Location),
            delegateOperandsAdapted=true,delegateRegistryUsed=false,
            scope="installed Harmony IL reader and Noodle transpiler executed with typed delegate call stand-ins; patched phase guard and look/collider consumers audited; game methods not patched, delegate emitter and live Unity lifecycle not executed"};
    }
}
