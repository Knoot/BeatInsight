using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.Loader;

namespace BeatInsight.Compare;

public static class HeckReloadChecks
{
    public static object? ControlledContainer;
    public static object Run(string gameRoot,IReadOnlyDictionary<string,object> baseline,IReadOnlyDictionary<string,object> replacements)
    {
        const BindingFlags flags=BindingFlags.Public|BindingFlags.NonPublic|BindingFlags.Instance|BindingFlags.Static;
        string path=Path.Combine(gameRoot,"Beat Saber_Data/Managed/Zenject.dll");
        if(Comparison.HashFile(path)!="2031FF13D801ACCF409E3F4D02ADB9C61AA7A07DA8E23A8A7F996716D0E82E49")throw new InvalidDataException("Reinspect changed reload DI contract");
        var zenject=AssemblyLoadContext.Default.LoadFromAssemblyPath(path);
        var containerType=zenject.GetType("Zenject.DiContainer",true)!;
        var container=Activator.CreateInstance(containerType)!;
        var heck=AssemblyLoadContext.Default.Assemblies.Single(a=>a.GetName().Name=="Heck");
        var reloadType=heck.GetType("Heck.ReLoad.ReLoader",true)!;
        var callback=reloadType.GetMethods(flags).Single(m=>m.Name=="<Reload>b__37_0");
        var containerStub=new System.Reflection.Emit.DynamicMethod("ControlledReloadContainer",containerType,new[]{reloadType},typeof(HeckReloadChecks).Module,true);
        var il=containerStub.GetILGenerator();il.Emit(System.Reflection.Emit.OpCodes.Ldsfld,typeof(HeckReloadChecks).GetField(nameof(ControlledContainer))!);
        il.Emit(System.Reflection.Emit.OpCodes.Castclass,containerType);il.Emit(System.Reflection.Emit.OpCodes.Ret);
        int adapted=0;
        var compiled=InstalledTranspilerCompiler.Compile(callback,typeof(HeckMapChecks).GetMethod(nameof(HeckMapChecks.Identity))!,(code,operand)=>
        {
            if(operand is FieldInfo field&&field.Name=="_container"&&field.DeclaringType==reloadType){adapted++;return(System.Reflection.Emit.OpCodes.Call,containerStub);}
            return(code,operand);
        });
        if(adapted!=1)throw new InvalidDataException("Reload container consumer changed");
        var previous=ControlledContainer;ControlledContainer=container;
        try
        {
        var targets=new Dictionary<string,object>();string[] properties={"CustomEventCustomDatas","EventCustomDatas","ObjectCustomDatas"};
        foreach(var pair in baseline)
        {
            var dataType=pair.Value.GetType();
            var target=dataType.GetConstructors(flags).Single().Invoke(properties.Select(p=>dataType.GetProperty(p,flags)!.GetValue(pair.Value)).ToArray());
            var binder=containerType.GetMethods(flags).Single(m=>m.Name=="BindInstance"&&m.IsGenericMethodDefinition).MakeGenericMethod(dataType).Invoke(container,new[]{target})!;
            binder.GetType().GetMethod("WithId",flags)!.Invoke(binder,new object[]{pair.Key});
            targets[pair.Key]=target;
        }
        int failed=0,checks=0;
        var replaced=new HashSet<string>(StringComparer.Ordinal);
        foreach(var pair in replacements)
        {
            var tuple=Activator.CreateInstance(callback.GetParameters()[0].ParameterType,new[]{(object)pair.Key,pair.Value})!;
            compiled.Method.Invoke(null,new object?[]{null,tuple});replaced.Add(pair.Key);
            foreach(var target in targets)
            foreach(string property in properties)
            {
                object expected=replaced.Contains(target.Key)?replacements[target.Key]:baseline[target.Key];
                var prop=expected.GetType().GetProperty(property,flags)!;
                if(!ReferenceEquals(prop.GetValue(target.Value),prop.GetValue(expected)))failed++;
                checks++;
            }
        }
        foreach(var pair in baseline)
        {
            var tuple=Activator.CreateInstance(callback.GetParameters()[0].ParameterType,new[]{(object)pair.Key,pair.Value})!;
            compiled.Method.Invoke(null,new object?[]{null,tuple});
        }
        foreach(var pair in targets)
        foreach(string property in properties)
        {
            var prop=pair.Value.GetType().GetProperty(property,flags)!;
            if(!ReferenceEquals(prop.GetValue(pair.Value),prop.GetValue(baseline[pair.Key])))failed++;
            checks++;
        }
        return new{cases=1,failed,checks,modules=targets.Count,scope="Installed reload Remap callback IL and real Zenject BindInstance/WithId/ResolveId; sole container field read redirected to controlled container to avoid unrelated BSIPA static initialization. No full Reload lifecycle, Unity teardown or coroutine restart."};
        }
        finally{ControlledContainer=previous;}
    }
}
