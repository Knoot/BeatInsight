using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.CompilerServices;
using System.Runtime.Loader;
using Mono.Cecil;
using C=Mono.Cecil.Cil;
namespace BeatInsight.Compare;
static class NativeFieldAccessorChecks
{
    internal static bool Enabled {get;private set;}
    internal static void Initialize(string root)
    {
        if(Comparison.HashFile(Path.Combine(root,"Beat Saber_Data/Managed/IPA.Loader.dll"))!="3315F09A33B97DB776206299D37002E50FC64EA84825640CE63EEEF177C5A0AC")throw new InvalidDataException("Reinspect changed accessor IL");
using(var module=ModuleDefinition.ReadModule(Path.Combine(root,"Beat Saber_Data/Managed/IPA.Loader.dll")))
{
 var type=module.Types.Single(t=>t.FullName=="IPA.Utilities.FieldAccessor`2");
 var method=type.Methods.Single(m=>m.Name=="MakeAccessor");var il=method.Body.GetILProcessor();
 var original=method.Body.Instructions.ToArray();
 il.InsertAfter(original.Single(i=>i.Offset==0x74),il.Create(C.OpCodes.Callvirt,module.ImportReference(typeof(Type).GetMethod("MakeByRefType")!)));
 foreach(var instruction in original.Where(i=>i.Offset>=0xa2&&i.Offset<=0xb7))il.Remove(instruction);
 using var stream=new MemoryStream();module.Write(stream);stream.Position=0;AssemblyLoadContext.Default.LoadFromStream(stream);Enabled=true;
}
    }
    internal static object LoadedOwnership()
    {
        const BindingFlags flags=BindingFlags.Public|BindingFlags.NonPublic|BindingFlags.Static;
        var noodle=AssemblyLoadContext.Default.Assemblies.Single(a=>a.GetName().Name=="NoodleExtensions");
        var custom=AssemblyLoadContext.Default.Assemblies.Single(a=>a.GetName().Name=="CustomJSONData");
        var fields=new List<FieldInfo>();
        var patch=noodle.GetType("NoodleExtensions.HarmonyPatches.ObjectProcessing.ReplaceMovementDataProvider",true)!;
        foreach(string name in new[]{"_noteMovementMovementDataProvider","_noteFloorMovementMovementDataProvider","_noteJumpMovementDataProvider","_noteWaitingMovementDataProvider","_burstSliderMovementDataProvider","_sliderControllerMovementDataProvider","_sliderMovementMovementDataProvider"})fields.Add(patch.GetField(name,flags)!);
        fields.Add(custom.GetType("CustomJSONData.CustomBeatmap.CustomBeatmapData",true)!.GetField("_beatmapDataItemsPerTypeAccessor",flags)!);
        fields.Add(custom.GetType("CustomJSONData.CustomBeatmap.CustomBeatmapDataSortedListForTypeAndIds",true)!.GetField("_itemsAccessor",flags)!);
        int captured=0,native=0;
        foreach(var field in fields)
        {
            var accessor=(Delegate)field.GetValue(null)!;
            if(accessor.Target!=null)captured++;
            if(accessor.Method.Name.StartsWith("<>_accessor__",StringComparison.Ordinal))native++;
        }
        if(captured!=0||native!=9)throw new InvalidDataException("Accessor adapter or receiver capture remains");
        return new{delegates=fields.Count,nativeDelegates=native,capturedReceivers=captured};
    }
    internal static object Run(string root)
    {
        if(Comparison.HashFile(Path.Combine(root,"Beat Saber_Data/Managed/Main.dll"))!="A276C8A50ACFAD69A3DD3A82CDE08571C3262344702B9B014C89756771B4495E")throw new InvalidDataException("Reinspect changed movement fields");
        if(Comparison.HashFile(Path.Combine(root,"Plugins/NoodleExtensions.dll"))!="FE50124E33631EED7ECB78EA1A19BB7EA64CC12974B0298DAD991672C2ABDA95")throw new InvalidDataException("Reinspect changed accessor registration");
var flags=BindingFlags.Public|BindingFlags.NonPublic|BindingFlags.Static|BindingFlags.Instance;
var noodle=AssemblyLoadContext.Default.LoadFromAssemblyPath(Path.Combine(root,"Plugins/NoodleExtensions.dll"));
var typePatch=noodle.GetType("NoodleExtensions.HarmonyPatches.ObjectProcessing.ReplaceMovementDataProvider",true)!;
RuntimeHelpers.RunClassConstructor(typePatch.TypeHandle);
var main=AssemblyLoadContext.Default.LoadFromAssemblyPath(Path.Combine(root,"Beat Saber_Data/Managed/Main.dll"));
int cases=0,guards=0;
foreach(string fieldName in new[]{"_noteMovementMovementDataProvider","_noteFloorMovementMovementDataProvider","_noteJumpMovementDataProvider","_noteWaitingMovementDataProvider","_burstSliderMovementDataProvider","_sliderControllerMovementDataProvider","_sliderMovementMovementDataProvider"})
{
 var accessor=typePatch.GetField(fieldName,flags)!;var invoke=accessor.FieldType.GetMethod("Invoke")!;
 var receiverType=invoke.GetParameters().Single().ParameterType.GetElementType()!;
 var target=receiverType.GetField("_variableMovementDataProvider",flags)!;
 var receiver=RuntimeHelpers.GetUninitializedObject(receiverType);var value=RuntimeHelpers.GetUninitializedObject(main.GetType("VariableMovementDataProvider",true)!);
 var setter=new DynamicMethod("NativeAccessorSet",typeof(void),new[]{typeof(object),typeof(object)},typeof(NativeFieldAccessorChecks).Module,true);
 var il=setter.GetILGenerator();var local=il.DeclareLocal(receiverType);
 il.Emit(OpCodes.Ldarg_0);il.Emit(OpCodes.Castclass,receiverType);il.Emit(OpCodes.Stloc,local);
 il.Emit(OpCodes.Ldsfld,accessor);il.Emit(OpCodes.Ldloca,local);il.Emit(OpCodes.Callvirt,invoke);
 il.Emit(OpCodes.Ldarg_1);il.Emit(OpCodes.Castclass,target.FieldType);il.Emit(OpCodes.Stind_Ref);il.Emit(OpCodes.Ret);
 setter.Invoke(null,new[]{receiver,value});if(!ReferenceEquals(value,target.GetValue(receiver)))throw new InvalidDataException("Accessor failed: "+fieldName);
 cases++;
 var accessorDefinition=accessor.FieldType.Assembly.GetType("IPA.Utilities.FieldAccessor`2",true)!;
 var correct=accessorDefinition.MakeGenericType(receiverType,target.FieldType);
 var get=correct.GetMethod("GetAccessor",flags)!;
 try{get.Invoke(null,new object[]{"__missing_movement_field_control"});throw new InvalidDataException("Missing accessor field accepted");}
 catch(TargetInvocationException e)when(e.GetBaseException() is MissingFieldException){guards++;}
 var wrong=accessorDefinition.MakeGenericType(receiverType,typeof(object));
 try{wrong.GetMethod("GetAccessor",flags)!.Invoke(null,new object[]{"_variableMovementDataProvider"});throw new InvalidDataException("Wrong accessor field type accepted");}
 catch(TargetInvocationException e)when(e.GetBaseException() is ArgumentException){guards++;}
 if(((Delegate)accessor.GetValue(null)!).Target!=null)throw new InvalidDataException("Accessor captured controller");
}
return new{cases=cases+guards,writes=cases,guards,failed=0,scope="Installed Noodle static accessor registration and seven actual BSIPA byref delegate invocations. IPA MakeAccessor in memory uses modern DynamicMethod byref return construction instead of obsolete private-field hack; setters only observe writes. Controlled receivers, no Unity/DI lifecycle; primary movement matrix still uses field adapters."};
    }
}
