using System.Collections;
using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.CompilerServices;

namespace BeatInsight.Compare;

public static class NoodleMovementAttachmentChecks
{
    public static object? ControlledPool;
    static DynamicMethod? attach,detach,sliderAttach,sliderDetach;
    const BindingFlags flags=BindingFlags.Public|BindingFlags.NonPublic|BindingFlags.Instance|BindingFlags.Static;
    static FieldInfo Field(Type t,string name){for(Type? p=t;p!=null;p=p.BaseType)if(p.GetField(name,flags|BindingFlags.DeclaredOnly) is FieldInfo f)return f;throw new MissingFieldException(t.FullName,name);}
    public static object Run(Assembly main,Assembly noodle,object wrapper,object original,BeatmapObjectData key,SliderData chain)
    {
        var poolType=noodle.GetType("NoodleExtensions.Managers.NoodleMovementDataProvider+Pool",true)!;
        var pool=Activator.CreateInstance(poolType,true)!;
        var baseType=poolType.BaseType!.BaseType!;
        var construct=baseType.GetMethod("Construct",flags)!;
        var container=Activator.CreateInstance(construct.GetParameters()[1].ParameterType)!;
        construct.Invoke(pool,new object?[]{null,container,null});
        var inactive=Field(poolType,"_inactiveItems").GetValue(pool)!;
        inactive.GetType().GetMethod("Push")!.Invoke(inactive,new[]{wrapper});
        var patchType=noodle.GetType("NoodleExtensions.HarmonyPatches.ObjectProcessing.ReplaceMovementDataProvider",true)!;
        bool native=NativeFieldAccessorChecks.Enabled;
        object? patchOwner=null;
        if(native)
        {
            container.GetType().GetMethods(flags).Single(m=>m.Name=="BindInstance"&&m.IsGenericMethodDefinition).MakeGenericMethod(poolType).Invoke(container,new[]{pool});
            var instantiate=container.GetType().GetMethods(flags).Single(m=>m.Name=="Instantiate"&&!m.IsGenericMethod&&m.GetParameters().Length==1&&m.GetParameters()[0].ParameterType==typeof(Type));
            patchOwner=instantiate.Invoke(container,new object[]{patchType});
            if(patchOwner==null||patchOwner.GetType()!=patchType||!ReferenceEquals(Field(patchType,"_noodleProviderPool").GetValue(patchOwner),pool))throw new InvalidDataException("Native movement patch pool injection differs");
        }
        if(!native&&attach==null)
        {
            var poolStub=new DynamicMethod("ControlledMovementPool",poolType,new[]{patchType},typeof(NoodleMovementAttachmentChecks).Module,true);
            var il=poolStub.GetILGenerator();il.Emit(OpCodes.Ldsfld,typeof(NoodleMovementAttachmentChecks).GetField(nameof(ControlledPool))!);il.Emit(OpCodes.Castclass,poolType);il.Emit(OpCodes.Ret);
            var stubs=new Dictionary<MethodInfo,DynamicMethod>();
            (OpCode,object?) Rewrite(OpCode code,object? operand)
            {
                if(operand is FieldInfo field&&field.DeclaringType==patchType)
                {
                    if(field.Name=="_noodleProviderPool")return(OpCodes.Call,poolStub);
                    if(code==OpCodes.Ldsfld)return(OpCodes.Ldnull,null);
                }
                if(operand is MethodInfo invoke&&invoke.Name=="Invoke"&&invoke.DeclaringType?.FullName?.StartsWith("IPA.Utilities.FieldAccessor",StringComparison.Ordinal)==true)
                {
                    if(!stubs.TryGetValue(invoke,out var stub))
                    {
                        var reference=invoke.GetParameters().Single().ParameterType;
                        var component=reference.GetElementType()!;
                        var target=Field(component,"_variableMovementDataProvider");
                        if(!invoke.ReturnType.IsByRef||target.FieldType!=invoke.ReturnType.GetElementType())throw new InvalidDataException("Movement accessor contract changed");
                        stub=new DynamicMethod("ControlledMovementSlot"+component.Name,invoke.ReturnType,new[]{typeof(object),reference},typeof(NoodleMovementAttachmentChecks).Module,true);
                        var body=stub.GetILGenerator();body.Emit(OpCodes.Ldarg_1);body.Emit(OpCodes.Ldind_Ref);body.Emit(OpCodes.Ldflda,target);body.Emit(OpCodes.Ret);stubs[invoke]=stub;
                    }
                    return(OpCodes.Call,stub);
                }
                return(code,operand);
            }
            attach=InstalledTranspilerCompiler.Compile(patchType.GetMethod("ReplaceNoteMovement",flags)!,typeof(HeckMapChecks).GetMethod(nameof(HeckMapChecks.Identity))!,Rewrite).Method;
            detach=InstalledTranspilerCompiler.Compile(patchType.GetMethod("DespawnNoteMovement",flags)!,typeof(HeckMapChecks).GetMethod(nameof(HeckMapChecks.Identity))!,Rewrite).Method;
            sliderAttach=InstalledTranspilerCompiler.Compile(patchType.GetMethod("ReplaceSliderMovement",flags)!,typeof(HeckMapChecks).GetMethod(nameof(HeckMapChecks.Identity))!,Rewrite).Method;
            sliderDetach=InstalledTranspilerCompiler.Compile(patchType.GetMethod("DespawnSliderMovement",flags)!,typeof(HeckMapChecks).GetMethod(nameof(HeckMapChecks.Identity))!,Rewrite).Method;
            if(stubs.Count!=7)throw new InvalidDataException("Note movement accessor count changed");
        }
        int failed=0,checks=0;var previous=ControlledPool;if(!native)ControlledPool=pool;
        try
        {
            foreach(string typeName in new[]{"GameNoteController","BurstSliderGameNoteController","SliderController"})
            {
                bool slider=typeName=="SliderController";
                MethodInfo activeAttach=native?patchType.GetMethod(slider?"ReplaceSliderMovement":"ReplaceNoteMovement",flags)!:slider?sliderAttach!:attach!;
                MethodInfo activeDetach=native?patchType.GetMethod(slider?"DespawnSliderMovement":"DespawnNoteMovement",flags)!:slider?sliderDetach!:detach!;
                var type=main.GetType(typeName,true)!;var controller=RuntimeHelpers.GetUninitializedObject(type);
                var moveField=Field(type,slider?"_sliderMovement":"_noteMovement");var movement=RuntimeHelpers.GetUninitializedObject(moveField.FieldType);moveField.SetValue(controller,movement);
                var slots=new List<(object Receiver,FieldInfo Field)>{(movement,Field(moveField.FieldType,"_variableMovementDataProvider"))};
                foreach(string childName in slider?Array.Empty<string>():new[]{"_floorMovement","_jump","_waiting"})
                {
                    var childField=Field(moveField.FieldType,childName);var child=RuntimeHelpers.GetUninitializedObject(childField.FieldType);childField.SetValue(movement,child);
                    slots.Add((child,Field(childField.FieldType,"_variableMovementDataProvider")));
                }
                if(typeName=="BurstSliderGameNoteController")slots.Add((controller,Field(type,"_variableMovementDataProvider")));
                if(slider)slots.Add((controller,Field(type,"_variableMovementDataProvider")));
                foreach(var slot in slots)slot.Field.SetValue(slot.Receiver,original);
                activeDetach.Invoke(patchOwner,native?new object?[]{controller}:new object?[]{null,controller});
                if((int)Field(poolType,"_activeCount").GetValue(pool)! !=0)failed++;
                checks++;
                for(int reuse=0;reuse<2;reuse++)
                {
                    activeAttach.Invoke(patchOwner,native?new object?[]{controller,slider?chain:key}:new object?[]{null,controller,slider?chain:key});
                    foreach(var slot in slots){if(!ReferenceEquals(slot.Field.GetValue(slot.Receiver),wrapper))failed++;checks++;}
                    if((int)Field(poolType,"_activeCount").GetValue(pool)! !=1)failed++;
                    activeDetach.Invoke(patchOwner,native?new object?[]{controller}:new object?[]{null,controller});
                    if((int)Field(poolType,"_activeCount").GetValue(pool)! !=0||(int)inactive.GetType().GetProperty("Count")!.GetValue(inactive)! !=1)failed++;
                    checks+=2;
                }
            }
        }
        finally{ControlledPool=previous;}
        return new{failed,checks,nativeAccessors=native,nativePatchConstructor=native,scope=native?"Actual DiContainer.Instantiate/ReplaceMovementDataProvider constructor, note/burst/slider attach/detach bodies and actual BSIPA accessors with in-memory modern return-type compatibility. Real prewarmed Zenject pool; controlled controller receivers/base movement, no Unity lifecycle or all-mod ordering.":"Installed ReplaceNoteMovement/DespawnNoteMovement IL with direct byref field adapters and controlled pool-field read; real Zenject prewarmed pool Spawn/Despawn/Reinitialize. Managed controller receivers for normal/burst notes and slider controllers, original-provider negative despawn; allocation/factory, Unity component lifecycle and Harmony ordering excluded."};
    }
}
