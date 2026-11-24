using System.Collections;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.Loader;
using Mono.Cecil;

namespace BeatInsight.Compare;

public class ControlledInitializerProxy : DispatchProxy
{
    public Action Callback=null!;
    protected override object? Invoke(MethodInfo? method,object?[]? args)
    {
        if(method?.Name!="Initialize")throw new NotSupportedException("Unexpected initializer call");
        Callback();return null;
    }
}

static class NoodleKernelChecks
{
    public static object Run(string gameRoot)
    {
        string path=Path.Combine(gameRoot,"Beat Saber_Data/Managed/Zenject.dll");
        if(Comparison.HashFile(path)!="2031FF13D801ACCF409E3F4D02ADB9C61AA7A07DA8E23A8A7F996716D0E82E49")throw new InvalidDataException("Reinspect changed DI startup");
        const BindingFlags flags=BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic;
        using var module=ModuleDefinition.ReadModule(path);
        bool Calls(MethodDefinition method,string type,string name)=>method.Body.Instructions.Any(i=>i.Operand is MethodReference m&&m.DeclaringType.FullName==type&&m.Name==name);
        var kernel=module.Types.Single(t=>t.Name=="MonoKernel");
        if(!Calls(kernel.Methods.Single(m=>m.Name=="Start"),"Zenject.MonoKernel","Initialize")
            ||!Calls(kernel.Methods.Single(m=>m.Name=="Initialize"),"Zenject.InitializableManager","Initialize"))throw new InvalidDataException("Kernel synchronous entry changed");
        var context=module.Types.Single(t=>t.Name=="SceneContext");
        if(!Calls(context.Methods.Single(m=>m.Name=="Awake"),"Zenject.RunnableContext","Initialize")
            ||!Calls(context.Methods.Single(m=>m.Name=="Resolve"),"Zenject.DiContainer","ResolveRoots"))throw new InvalidDataException("Scene context resolve phase changed");
        var assembly=AssemblyLoadContext.Default.LoadFromAssemblyPath(path);
        var managerType=assembly.GetType("Zenject.InitializableManager",true)!;
        var ctor=managerType.GetConstructors(flags).Single(c=>c.GetParameters().Length==2);
        var interfaceType=ctor.GetParameters()[0].ParameterType.GetGenericArguments()[0];
        var kernelType=assembly.GetType("Zenject.SceneKernel",true)!;
        var baseType=kernelType.BaseType!;var start=baseType.GetMethod("Start",flags)!;
        int cases=0,failed=0;var results=new List<object>();
        foreach(int count in new[]{0,1,4})foreach(bool repeat in new[]{false,true})
        {
            var trace=new List<int>();var items=(IList)Activator.CreateInstance(ctor.GetParameters()[0].ParameterType)!;
            for(int i=0;i<count;i++)
            {
                int current=i;var proxy=DispatchProxy.Create(interfaceType,typeof(ControlledInitializerProxy));
                ((ControlledInitializerProxy)proxy).Callback=()=>trace.Add(current);items.Add(proxy);
            }
            var priorities=Activator.CreateInstance(ctor.GetParameters()[1].ParameterType)!;
            var manager=ctor.Invoke(new object[]{items,priorities});var receiver=RuntimeHelpers.GetUninitializedObject(kernelType);
            baseType.GetField("_initializableManager",flags)!.SetValue(receiver,manager);
            start.Invoke(receiver,null);bool completeBeforeReturn=trace.SequenceEqual(Enumerable.Range(0,count));
            if(repeat)start.Invoke(receiver,null);
            bool pass=completeBeforeReturn&&trace.SequenceEqual(Enumerable.Range(0,count));cases++;if(!pass)failed++;
            results.Add(new{count,repeat,completeBeforeReturn,pass});
        }
        return new{schemaVersion=1,cases,failed,results,gameStarted=false,menuAdmissionChanged=false,
            scope="actual MonoKernel.Start/Initialize and InitializableManager constructor/Initialize with controlled IInitializable callbacks; repeated Start guard; SceneContext Awake/Resolve call audit; no Unity scheduler, real scene injection, manager GameObject creation or room Start execution"};
    }
}
