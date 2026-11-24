using System.Reflection;
using System.Reflection.Emit;
using Mono.Cecil;
using Mono.Cecil.Cil;
using EmitCodes=System.Reflection.Emit.OpCodes;

namespace BeatInsight.Compare;

static class NoodleSceneActivationChecks
{
    delegate void ActivateLoop(ref List<string>.Enumerator roots,Action<string,bool> activate);
    public static object Run(string gameRoot)
    {
        string path=Path.Combine(gameRoot,"Beat Saber_Data/Managed/BGLib.AppFlow.dll");
        if(Comparison.HashFile(path)!="A8BDDC75AF796BE673C65012156628E7B0181794DF9B90AEE35DE96E99CCF745")throw new InvalidDataException("Reinspect scene activation protocol");
        using var module=ModuleDefinition.ReadModule(path);
        var type=module.Types.Single(t=>t.Name=="GameScenesManager");
        var method=type.Methods.Single(m=>m.Name=="ActivatePresentedSceneRootObjects");
        var sequence=method.Body.Instructions.Where(i=>i.Offset>=0x52&&i.Offset<=0x68).ToArray();
        if(!sequence.Select(i=>i.OpCode.Code).SequenceEqual(new[]{Code.Br_S,Code.Ldloca_S,Code.Call,Code.Ldc_I4_1,Code.Callvirt,Code.Ldloca_S,Code.Call,Code.Brtrue_S}))throw new InvalidDataException("Root activation loop changed");
        if(method.Body.ExceptionHandlers.Count!=2||method.Body.ExceptionHandlers.Any(h=>h.HandlerType!=ExceptionHandlerType.Finally)||method.ReturnType.FullName!="System.Void")throw new InvalidDataException("Root activation lifecycle changed");
        int CallOffset(string declaringType,string name)=>method.Body.Instructions.Single(i=>i.Operand is MethodReference m&&m.DeclaringType.FullName==declaringType&&m.Name==name).Offset;
        if(!(CallOffset("UnityEngine.SceneManagement.Scene","GetRootGameObjects")<CallOffset("UnityEngine.Random","InitState")
            &&CallOffset("UnityEngine.Random","InitState")<0x52&&CallOffset("UnityEngine.GameObject","SetActive")==0x5c))throw new InvalidDataException("Root collection/write phases changed");
        var dynamic=new DynamicMethod("InstalledRootActivationLoop",typeof(void),new[]{typeof(List<string>.Enumerator).MakeByRefType(),typeof(Action<string,bool>)},typeof(NoodleSceneActivationChecks).Module,true);
        var il=dynamic.GetILGenerator();var target=il.DeclareLocal(typeof(string));var active=il.DeclareLocal(typeof(bool));var labels=sequence.ToDictionary(i=>i,i=>il.DefineLabel());
        foreach(var i in sequence)
        {
            il.MarkLabel(labels[i]);
            switch(i.OpCode.Code)
            {
                case Code.Br_S:il.Emit(EmitCodes.Br,labels[(Instruction)i.Operand]);break;
                case Code.Brtrue_S:il.Emit(EmitCodes.Brtrue,labels[(Instruction)i.Operand]);break;
                case Code.Ldloca_S:
                    if(((VariableDefinition)i.Operand).Index!=4)throw new InvalidDataException("Root enumerator changed");il.Emit(EmitCodes.Ldarg_0);break;
                case Code.Ldc_I4_1:il.Emit(EmitCodes.Ldc_I4_1);break;
                case Code.Call:
                    var call=(MethodReference)i.Operand;
                    if(call.Name=="get_Current")il.Emit(EmitCodes.Call,typeof(List<string>.Enumerator).GetProperty("Current")!.GetMethod!);
                    else if(call.Name=="MoveNext")il.Emit(EmitCodes.Call,typeof(List<string>.Enumerator).GetMethod("MoveNext")!);
                    else throw new InvalidDataException("Unexpected root enumeration call");break;
                case Code.Callvirt:
                    if(i.Operand is not MethodReference set||set.DeclaringType.FullName!="UnityEngine.GameObject"||set.Name!="SetActive")throw new InvalidDataException("Root setter changed");
                    il.Emit(EmitCodes.Stloc,active);il.Emit(EmitCodes.Stloc,target);il.Emit(EmitCodes.Ldarg_1);il.Emit(EmitCodes.Ldloc,target);il.Emit(EmitCodes.Ldloc,active);
                    il.Emit(EmitCodes.Callvirt,typeof(Action<string,bool>).GetMethod("Invoke")!);break;
                default:throw new InvalidDataException("Unexpected activation instruction");
            }
        }
        il.Emit(EmitCodes.Ret);var apply=(ActivateLoop)dynamic.CreateDelegate(typeof(ActivateLoop));
        var coroutine=type.NestedTypes.Single(t=>t.Name.StartsWith("<ScenesTransitionCoroutine>",StringComparison.Ordinal)).Methods.Single(m=>m.Name=="MoveNext");
        bool HasCall(int offset,string name)=>coroutine.Body.Instructions.Any(i=>i.Offset==offset&&i.Operand is MethodReference m&&m.Name==name);
        bool Constant(int offset,Code code)=>coroutine.Body.Instructions.Any(i=>i.Offset==offset&&i.OpCode.Code==code);
        if(!HasCall(0x1cb,"LoadSceneFromAddressablesAsync")||!Constant(0x1bc,Code.Ldc_I4_0)||!HasCall(0x259,"ActivateAsync")||!HasCall(0x29a,"ActivatePresentedSceneRootObjects")
            ||!HasCall(0x35a,"LoadSceneFromAddressablesAsync")||!Constant(0x34b,Code.Ldc_I4_1)||!HasCall(0x36c,"LoadSceneAsync")||!Constant(0x366,Code.Ldc_I4_1)||!HasCall(0x417,"ActivatePresentedSceneRootObjects"))throw new InvalidDataException("Single/multi scene activation branches changed");
        int cases=0,failed=0;var results=new List<object>();
        foreach(var roots in new[]{Array.Empty<string>(),new[]{"only"},new[]{"standard/a","standard/b","game/core"},new[]{"standard/a","game/core","standard/a"}})foreach(bool reverse in new[]{false,true})
        {
            var list=(reverse?roots.Reverse():roots).ToList();var trace=new List<(string Root,bool Active)>();var enumerator=list.GetEnumerator();
            try{apply(ref enumerator,(root,on)=>trace.Add((root,on)));}finally{enumerator.Dispose();}
            bool pass=trace.Select(x=>x.Root).SequenceEqual(list)&&trace.All(x=>x.Active);cases++;if(!pass)failed++;
            results.Add(new{roots=list,reverse,pass});
        }
        return new{schemaVersion=1,cases,failed,results,singleSceneActivateOnLoad=false,multipleScenesActivateOnLoad=true,
            gameStarted=false,menuAdmissionChanged=false,
            scope="pinned ActivatePresentedSceneRootObjects activation-loop IL with GameObject IDs replaced by strings and SetActive by callback; actual List enumerator, fixture-owned disposal; root collection/finally and single/multi coroutine branch audit only; native scene loading, SetActive/OnEnable, coroutine resume vs MonoKernel.Start ordering not executed"};
    }
}
