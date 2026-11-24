using System.Collections;
using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.CompilerServices;
using System.Runtime.Loader;
using Newtonsoft.Json.Linq;

namespace BeatInsight.Compare;

// Executes installed Affinity wrapper generation while intercepting only detour installation/removal.
sealed class AffinitySpawnPatchSession:IDisposable
{
    const BindingFlags flags=BindingFlags.Public|BindingFlags.NonPublic|BindingFlags.Instance|BindingFlags.Static;
    internal static AffinitySpawnPatchSession? Current;
    readonly object generator;
    readonly Type generatorType,attributeType,patchKind;
    readonly DynamicMethod generate,dispose;
    readonly IDictionary cache;
    readonly List<(MethodInfo Native,MethodInfo Wrapper,FieldInfo Field,MethodBase Original)> wrappers=new();
    readonly List<WeakReference> receivers=new();
    readonly List<WeakReference> kernelObjects=new();
    readonly List<(MethodBase Original,MethodInfo Wrapper)> captured=new();
    readonly Dictionary<string,MethodInfo> geometry=new(StringComparer.Ordinal);
    DynamicMethod? modernGenerate;
    Type? generatingReturnType;
    int passes,unpatches;
    int assemblyFactoryCalls,harmonyOwnerCalls;
    readonly Dictionary<(AssemblyBuilder Assembly,string Name),ModuleBuilder> constructorModules=new();
    bool bound;
    internal JObject Lifetime {get;}=new();
    internal AffinitySpawnPatchSession(string gameRoot)
    {
        if(Current!=null)throw new InvalidOperationException("Nested Affinity spawn session");
        foreach(var pin in new[]{("Libs/MonoMod.Utils.dll","B712EC7781C5BFC9F8A1B65527A43478BCBD0F022A7ED2D2B1823923DC2C1DE8"),("Plugins/SiraUtil.dll","B4C0242EFC5E7DA7C7B18D9FC09298B2B8A0D099393A55E9D235A93A7F0ED02B"),("Libs/0Harmony.dll","9846EB9EDF64A5E2473342C75B8C5AB37293FB4C27418BC7A5B9A156AA0D7214")})
            if(Comparison.HashFile(Path.Combine(gameRoot,pin.Item1))!=pin.Item2)throw new InvalidDataException("Reinspect changed Affinity generator");
        var sira=AssemblyLoadContext.Default.LoadFromAssemblyPath(Path.Combine(gameRoot,"Plugins/SiraUtil.dll"));
        generatorType=sira.GetType("SiraUtil.Affinity.Harmony.Generator.DynamicHarmonyPatchGenerator",true)!;
        attributeType=sira.GetType("SiraUtil.Affinity.AffinityPatchAttribute",true)!;
        patchKind=sira.GetType("SiraUtil.Affinity.AffinityPatchType",true)!;
        generator=RuntimeHelpers.GetUninitializedObject(generatorType);
        Current=this;
        try{InitializeGenerator();}
        finally{Current=null;}
        var assembly=((ModuleBuilder)generatorType.GetField("_moduleBuilder",flags)!.GetValue(generator)!).Assembly;
        var cacheField=generatorType.GetField("_patchCache",flags)!;
        cache=(IDictionary)cacheField.GetValue(generator)!;
        int patchSites=0,unpatchSites=0;
        (OpCode,object?) Rewrite(OpCode code,object? operand)
        {
            if(operand is not MethodInfo m||m.DeclaringType?.FullName!="HarmonyLib.Harmony")return(code,operand);
            string target;
            if(m.Name=="Patch"){patchSites++;target=nameof(CapturePatch);}
            else if(m.Name=="Unpatch"&&m.GetParameters().Length==2&&m.GetParameters()[1].ParameterType==typeof(MethodInfo))
            {unpatchSites++;target=nameof(CaptureUnpatch);}
            else throw new InvalidDataException("Unexpected Affinity detour API");
            var shim=new DynamicMethod("ControlledAffinity"+m.Name,m.ReturnType,new[]{m.DeclaringType}.Concat(m.GetParameters().Select(p=>p.ParameterType)).ToArray(),typeof(AffinitySpawnPatchSession).Module,true);
            var il=shim.GetILGenerator();
            for(int i=0;i<=m.GetParameters().Length;i++)il.Emit(OpCodes.Ldarg,i);
            il.Emit(OpCodes.Call,typeof(AffinitySpawnPatchSession).GetMethod(target,flags)!);il.Emit(OpCodes.Ret);
            return(OpCodes.Call,shim);
        }
        generate=InstalledTranspilerCompiler.Compile(generatorType.GetMethod("Patch",flags)!,typeof(HeckMapChecks).GetMethod(nameof(HeckMapChecks.Identity))!,Rewrite).Method;
        dispose=InstalledTranspilerCompiler.Compile(generatorType.GetMethod("Dispose",flags)!,typeof(HeckMapChecks).GetMethod(nameof(HeckMapChecks.Identity))!,Rewrite,exceptionBlocks:true).Method;
        if(patchSites!=1||unpatchSites!=1)throw new InvalidDataException("Affinity detour interception changed");
        Lifetime["collectibleAssembly"]=assembly.IsCollectible;Lifetime["generatorConstructors"]=2;
        Current=this;
    }
    public static MethodInfo? CapturePatch(object? harmony,MethodBase original,object? prefix,object? postfix,object? transpiler,object? finalizer,object? manipulator)
    {
        var owner=Current??throw new InvalidOperationException("Missing Affinity capture session");
        if(prefix==null||postfix!=null||transpiler!=null||finalizer!=null||manipulator!=null)throw new InvalidDataException("Unexpected Affinity patch kinds");
        var wrapper=(MethodInfo)prefix.GetType().GetField("method",flags)!.GetValue(prefix)!;
        owner.captured.Add((original,wrapper));return null;
    }
    public static void CaptureUnpatch(object? harmony,MethodBase original,MethodInfo wrapper)
    {
        var owner=Current??throw new InvalidOperationException("Missing Affinity unpatch session");
        if(!owner.captured.Any(p=>p.Original==original&&p.Wrapper==wrapper))throw new InvalidDataException("Unexpected Affinity unpatch target");
        owner.unpatches++;
    }
    internal IReadOnlyDictionary<string,MethodInfo> Bind(object receiver)
    {
        if(Current!=this)throw new ObjectDisposedException(nameof(AffinitySpawnPatchSession));
        if(bound)throw new InvalidOperationException("Affinity receiver retained between maps");
        try
        {
            foreach(string name in new[]{"NoteNoodlePatch","ObstacleNoodlePatch","SliderNoodlePatch"})
            {
                var native=receiver.GetType().GetMethod(name,flags)!;
                var existing=wrappers.SingleOrDefault(w=>w.Native==native);
                if(existing.Wrapper!=null)
                {
                    existing.Field.SetValue(null,Delegate.CreateDelegate(existing.Field.FieldType,receiver,native));continue;
                }
                var attribute=native.GetCustomAttributes(true).Single(a=>attributeType.IsInstanceOfType(a));
                int before=captured.Count;
                var wrapper=(MethodInfo)generate.Invoke(null,new[]{generator,receiver,native,Enum.Parse(patchKind,"Prefix"),attribute,(object)400,Array.Empty<string>(),Array.Empty<string>()})!;
                if(captured.Count!=before+1||captured[^1].Wrapper!=wrapper)throw new InvalidDataException("Affinity wrapper was not captured");
                var field=wrapper.DeclaringType!.GetField("_delegate",flags)!;
                var actual=(Delegate)field.GetValue(null)!;
                if(!ReferenceEquals(actual.Target,receiver)||actual.Method!=native||!wrapper.IsStatic||wrapper.ReturnType!=native.ReturnType
                    ||!wrapper.GetParameters().Select(p=>(p.Name,p.ParameterType,p.Attributes)).SequenceEqual(native.GetParameters().Select(p=>(p.Name,p.ParameterType,p.Attributes))))
                    throw new InvalidDataException("Affinity wrapper signature or receiver changed");
                wrappers.Add((native,wrapper,field,captured[^1].Original));
                geometry.Add(native.Name,ComposeGeometry(native,wrapper,captured[^1].Original));
            }
            if(cache.Count!=3||wrappers.Count!=3)throw new InvalidDataException("Affinity wrapper cache changed");
            if(passes==0)
            {
                var kernel=AffinityKernelChecks.Run(generatorType.Assembly,generator,receiver,wrappers.ToDictionary(w=>w.Native,w=>w.Wrapper));
                Lifetime["kernel"]=JObject.FromObject(kernel.Evidence);kernelObjects.AddRange(kernel.Released);
            }
            bound=true;passes++;receivers.Add(new WeakReference(receiver));
            return wrappers.ToDictionary(w=>w.Native.Name,w=>w.Wrapper,StringComparer.Ordinal);
        }
        catch{Unbind();throw;}
    }
    internal void Unbind(){foreach(var w in wrappers)w.Field.SetValue(null,null);bound=false;}
    internal MethodInfo Geometry(string name)=>geometry[name];
    void InitializeGenerator()
    {
        var constructor=generatorType.GetConstructors(flags).Single(c=>!c.IsStatic);
        var metadataType=constructor.GetParameters()[0].ParameterType;
        var metadata=RuntimeHelpers.GetUninitializedObject(metadataType);
        var manifestField=metadataType.GetField("manifest",flags)!;
        var manifest=RuntimeHelpers.GetUninitializedObject(manifestField.FieldType);
        manifestField.FieldType.GetField("Name",flags)!.SetValue(manifest,"BeatInsight");
        manifestField.FieldType.GetField("Author",flags)!.SetValue(manifest,"Controlled");
        manifestField.SetValue(metadata,manifest);
        metadataType.GetField("<Assembly>k__BackingField",flags)!.SetValue(metadata,typeof(AffinitySpawnPatchSession).Assembly);
        var shared=generatorType.GetField("_assemblyBuilder",flags)!;
        var previous=shared.GetValue(null);
        var utils=AssemblyLoadContext.Default.Assemblies.Single(a=>a.GetName().Name=="MonoMod.Utils");
        var dmdType=utils.GetType("MonoMod.Utils.DynamicMethodDefinition",true)!;
        using var dmd=(IDisposable)Activator.CreateInstance(dmdType,new object[]{constructor})!;
        var definition=(Mono.Cecil.MethodDefinition)dmdType.GetProperty("Definition")!.GetValue(dmd)!;
        int adapted=0,harmonyAdapters=0,moduleAdapters=0;
        foreach(var instruction in definition.Body.Instructions.ToArray())
            if(instruction.Operand is Mono.Cecil.MethodReference method&&method.DeclaringType.FullName=="System.AppDomain"&&method.Name=="DefineDynamicAssembly")
            {
                instruction.OpCode=Mono.Cecil.Cil.OpCodes.Call;
                instruction.Operand=definition.Module.ImportReference(typeof(AffinitySpawnPatchSession).GetMethod(nameof(ModernAssembly),flags)!);adapted++;
            }
            else if(instruction.Operand is Mono.Cecil.MethodReference target&&target.DeclaringType.FullName=="HarmonyLib.Harmony"&&target.Name==".ctor")
            {
                instruction.OpCode=Mono.Cecil.Cil.OpCodes.Call;
                instruction.Operand=definition.Module.ImportReference(typeof(AffinitySpawnPatchSession).GetMethod(nameof(ControlledHarmonyOwner),flags)!);
                definition.Body.GetILProcessor().InsertAfter(instruction,Mono.Cecil.Cil.Instruction.Create(Mono.Cecil.Cil.OpCodes.Castclass,target.DeclaringType));
                harmonyAdapters++;
            }
            else if(instruction.Operand is Mono.Cecil.MethodReference lookup&&lookup.DeclaringType.FullName=="System.Reflection.Emit.AssemblyBuilder"&&lookup.Name=="GetDynamicModule")
            {
                instruction.OpCode=Mono.Cecil.Cil.OpCodes.Call;
                instruction.Operand=definition.Module.ImportReference(typeof(AffinitySpawnPatchSession).GetMethod(nameof(ModernModuleLookup),flags)!);moduleAdapters++;
            }
        if(adapted!=1||harmonyAdapters!=1||moduleAdapters!=1)throw new InvalidDataException("Affinity constructor host adapters changed");
        try
        {
            var compiled=GenerateModern(dmd,constructor);
            compiled.Invoke(null,new[]{generator,metadata});
            if(shared.GetValue(null)==null||generatorType.GetField("_moduleBuilder",flags)!.GetValue(generator)==null
                ||(string?)generatorType.GetField("_id",flags)!.GetValue(generator)!="com.controlled.beatinsight.affinity")
                throw new InvalidDataException("Affinity constructor did not initialize generator");
            constructorModules.Add(((AssemblyBuilder)shared.GetValue(null)!, (string)generatorType.GetField("_name",flags)!.GetValue(generator)!),
                (ModuleBuilder)generatorType.GetField("_moduleBuilder",flags)!.GetValue(generator)!);
            var reused=RuntimeHelpers.GetUninitializedObject(generatorType);
            compiled.Invoke(null,new[]{reused,metadata});
            if(!ReferenceEquals(generatorType.GetField("_moduleBuilder",flags)!.GetValue(generator),generatorType.GetField("_moduleBuilder",flags)!.GetValue(reused))
                ||ReferenceEquals(generatorType.GetField("_patchCache",flags)!.GetValue(generator),generatorType.GetField("_patchCache",flags)!.GetValue(reused))
                ||assemblyFactoryCalls!=(previous==null?1:0)||harmonyOwnerCalls!=2)
                throw new InvalidDataException("Affinity constructor module reuse changed");
            Lifetime["constructorAssemblyAdapters"]=adapted;
            Lifetime["constructorHarmonyAdapters"]=harmonyAdapters;
            Lifetime["constructorModuleAdapters"]=moduleAdapters;
            Lifetime["constructorAssemblyCalls"]=assemblyFactoryCalls;Lifetime["constructorHarmonyCalls"]=harmonyOwnerCalls;
            Lifetime["constructorModuleReused"]=true;
        }
        finally
        {
            shared.SetValue(null,previous);
            constructorModules.Clear();
            Lifetime["staticAssemblyRestored"]=ReferenceEquals(shared.GetValue(null),previous);
        }
    }
    public static AssemblyBuilder ModernAssembly(AppDomain domain,AssemblyName name,AssemblyBuilderAccess access)
    {
        (Current??throw new InvalidOperationException("Missing constructor session")).assemblyFactoryCalls++;
        return AssemblyBuilder.DefineDynamicAssembly(name,access);
    }
    public static object? ControlledHarmonyOwner(string id)
    {
        if(id!="com.controlled.beatinsight.affinity")throw new InvalidDataException("Unexpected Affinity Harmony owner");
        (Current??throw new InvalidOperationException("Missing constructor session")).harmonyOwnerCalls++;
        return null;
    }
    public static ModuleBuilder? ModernModuleLookup(AssemblyBuilder assembly,string name)
    {
        var owner=Current??throw new InvalidOperationException("Missing constructor session");
        return owner.constructorModules.TryGetValue((assembly,name),out var module)?module:assembly.GetDynamicModule(name);
    }
    MethodInfo ComposeGeometry(MethodInfo native,MethodInfo wrapper,MethodBase original)
    {
        var module=(ModuleBuilder)generatorType.GetField("_moduleBuilder",flags)!.GetValue(generator)!;
        var type=module.DefineType("Counted"+native.Name,TypeAttributes.Public|TypeAttributes.Abstract|TypeAttributes.Sealed);
        var parameters=native.GetParameters();
        var method=type.DefineMethod("Prefix",MethodAttributes.Public|MethodAttributes.Static,native.ReturnType,parameters.Select(p=>p.ParameterType).ToArray());
        for(int i=0;i<parameters.Length;i++)method.DefineParameter(i+1,parameters[i].Attributes,parameters[i].Name);
        var il=method.GetILGenerator();for(int i=0;i<parameters.Length;i++)il.Emit(OpCodes.Ldarg,i);
        il.Emit(OpCodes.Call,wrapper);il.Emit(OpCodes.Call,typeof(ControlledObjectSpawnerProxy).GetMethod(nameof(ControlledObjectSpawnerProxy.RecordPrefix))!);il.Emit(OpCodes.Ret);
        var counted=type.CreateType()!.GetMethod("Prefix")!;
        var harmony=AssemblyLoadContext.Default.Assemblies.Single(a=>a.GetName().Name=="0Harmony");
        var patchInfoType=harmony.GetType("HarmonyLib.PatchInfo",true)!;var info=Activator.CreateInstance(patchInfoType)!;
        patchInfoType.GetMethod("AddPrefix",flags)!.Invoke(info,new object[]{counted,"beatinsight.controlled.spawn",400,Array.Empty<string>(),Array.Empty<string>(),false});
        var utils=AssemblyLoadContext.Default.Assemblies.Single(a=>a.GetName().Name=="MonoMod.Utils");
        var dmdType=utils.GetType("MonoMod.Utils.DynamicMethodDefinition",true)!;
        using var dmd=(IDisposable)Activator.CreateInstance(dmdType,new object[]{original})!;
        var definition=dmdType.GetProperty("Definition")!.GetValue(dmd)!;
        var contextType=utils.GetType("MonoMod.Cil.ILContext",true)!;
        using var context=(IDisposable)Activator.CreateInstance(contextType,new[]{definition})!;
        harmony.GetType("HarmonyLib.Public.Patching.HarmonyManipulator",true)!.GetMethods(flags)
            .Single(m=>m.Name=="Manipulate"&&m.IsStatic&&m.GetParameters().Length==3).Invoke(null,new[]{original,info,context});
        return GenerateModern(dmd,original);
    }
    MethodInfo GenerateModern(object dmd,MethodBase original)
    {
        var utils=AssemblyLoadContext.Default.Assemblies.Single(a=>a.GetName().Name=="MonoMod.Utils");
        var generatorBackend=utils.GetType("MonoMod.Utils.DMDEmitDynamicMethodGenerator",true)!;
        if(modernGenerate==null)
        {
            int constructors=0,setters=0;
            modernGenerate=InstalledTranspilerCompiler.Compile(generatorBackend.GetMethod("_Generate",flags)!,typeof(HeckMapChecks).GetMethod(nameof(HeckMapChecks.Identity))!,(code,operand)=>
            {
                if(operand is ConstructorInfo c&&c.DeclaringType==typeof(DynamicMethod))
                {constructors++;return(OpCodes.Call,typeof(AffinitySpawnPatchSession).GetMethod(nameof(ModernDynamicMethod),flags)!);}
                if(operand is MethodInfo m&&m.DeclaringType==typeof(FieldInfo)&&m.Name=="SetValue")
                {setters++;return(OpCodes.Call,typeof(AffinitySpawnPatchSession).GetMethod(nameof(SkipLegacyReturnTypeWrite),flags)!);}
                return(code,operand);
            }).Method;
            if(constructors!=1||setters!=1)throw new InvalidDataException("MonoMod dynamic method return-type shim changed");
        }
        generatingReturnType=original is MethodInfo method?method.ReturnType:typeof(void);
        try
        {
            return (MethodInfo)modernGenerate.Invoke(null,new object?[]{RuntimeHelpers.GetUninitializedObject(generatorBackend),dmd,null})!;
        }
        finally{generatingReturnType=null;}
    }
    public static DynamicMethod ModernDynamicMethod(string name,Type ignored,Type[] parameters,Type owner,bool skipVisibility)
        =>new(name,Current?.generatingReturnType??throw new InvalidOperationException("Missing generated return type"),parameters,owner,skipVisibility);
    public static void SkipLegacyReturnTypeWrite(FieldInfo? field,object method,object value)
    {if(method is not DynamicMethod dynamic||dynamic.ReturnType!=(Type)value)throw new InvalidDataException("Generated return type was not preserved");}
    public void Dispose()
    {
        if(Current!=this)return;
        try
        {
            Unbind();dispose.Invoke(null,new[]{generator});
            int retainedFields=wrappers.Count(w=>w.Field.GetValue(null)!=null);
            GC.Collect();GC.WaitForPendingFinalizers();GC.Collect();
            int retainedReceivers=receivers.Count(w=>w.IsAlive);
            int retainedKernel=kernelObjects.Count(w=>w.IsAlive);
            if(Lifetime["kernel"] is JObject kernel){kernel["weakReferences"]=kernelObjects.Count;kernel["retainedObjects"]=retainedKernel;
                if(retainedKernel>0)kernel["retainedIndices"]=new JArray(kernelObjects.Select((w,i)=>new{w,i}).Where(x=>x.w.IsAlive).Select(x=>x.i));}
            Lifetime["generatedWrappers"]=wrappers.Count;Lifetime["bindingPasses"]=passes;
            Lifetime["harmonyComposedMethods"]=geometry.Count;
            Lifetime["capturedPatchCalls"]=captured.Count;Lifetime["capturedUnpatchCalls"]=unpatches;
            Lifetime["retainedDelegates"]=retainedFields;Lifetime["retainedReceivers"]=retainedReceivers;
            Lifetime["cacheCount"]=cache.Count;Lifetime["disposed"]=true;
            if(retainedFields!=0||retainedReceivers!=0||retainedKernel!=0||cache.Count!=0||unpatches!=wrappers.Count)throw new InvalidDataException("Affinity wrapper lifetime retained data: "+Lifetime.ToString(Newtonsoft.Json.Formatting.None));
        }
        finally{Current=null;wrappers.Clear();receivers.Clear();kernelObjects.Clear();captured.Clear();geometry.Clear();modernGenerate=null;}
    }
}
