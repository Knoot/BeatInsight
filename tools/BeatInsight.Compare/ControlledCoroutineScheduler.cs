using System.Reflection;
using System.Collections;
using UnityEngine;

namespace BeatInsight.Compare;

public sealed class ControlledCoroutineScheduler:IDisposable
{
    internal static System.Reflection.Emit.DynamicMethod CompileDispatch(System.Reflection.MethodInfo dispatch)
    {
        int startSites=0,stopSites=0;
        var result=InstalledTranspilerCompiler.Compile(dispatch,typeof(HeckMapChecks).GetMethod(nameof(HeckMapChecks.Identity))!,(code,operand)=>
        {
            if(operand is MethodInfo method&&method.DeclaringType?.FullName=="UnityEngine.MonoBehaviour")
            {
                if(method.Name=="StartCoroutine"&&method.GetParameters().Single().ParameterType==typeof(IEnumerator))
                {startSites++;return(System.Reflection.Emit.OpCodes.Call,typeof(ControlledCoroutineScheduler).GetMethod(nameof(ControlledCoroutineScheduler.Start))!);}
                if(method.Name=="StopCoroutine"&&method.GetParameters().Single().ParameterType==typeof(Coroutine))
                {stopSites++;return(System.Reflection.Emit.OpCodes.Call,typeof(ControlledCoroutineScheduler).GetMethod(nameof(ControlledCoroutineScheduler.Stop))!);}
                throw new InvalidDataException("Unknown coroutine scheduler call");
            }
            return(code,operand);
        },exceptionBlocks:true).Method;
        if(startSites!=2||stopSites!=1)throw new InvalidDataException("Timed dispatcher scheduling sites changed");
        return result;
    }
    public static ControlledCoroutineScheduler? Current;
    readonly Dictionary<Coroutine,IEnumerator> active=new(ReferenceEqualityComparer.Instance);
    public int Starts,Stops;
    public int Count=>active.Count;
    public static Coroutine Start(object? unused,IEnumerator iterator)
    {
        var scheduler=Current??throw new InvalidOperationException("Missing controlled scheduler");
        var token=(Coroutine)System.Runtime.CompilerServices.RuntimeHelpers.GetUninitializedObject(typeof(Coroutine));
        GC.SuppressFinalize(token); // No engine handle exists; never call Unity's release finalizer.
        scheduler.active.Add(token,iterator);scheduler.Starts++;return token;
    }
    public static void Stop(object? unused,Coroutine token)
    {
        var scheduler=Current??throw new InvalidOperationException("Missing controlled scheduler");
        if(scheduler.active.Remove(token,out var iterator)){(iterator as IDisposable)?.Dispose();scheduler.Stops++;}
    }
    public void Tick()
    {
        foreach(var entry in active.ToArray())if(!entry.Value.MoveNext()){(entry.Value as IDisposable)?.Dispose();active.Remove(entry.Key);}
    }
    public void Dispose(){foreach(var iterator in active.Values)(iterator as IDisposable)?.Dispose();active.Clear();}
}
