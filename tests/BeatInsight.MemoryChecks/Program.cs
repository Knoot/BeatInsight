using System.Runtime.CompilerServices;
using System.Text.Json;
using BeatInsight.Core;

int checks=0;
void Check(bool condition,string message){if(!condition)throw new Exception(message);checks++;}
Task Dispatch(Action action){action();return Task.CompletedTask;}
long baseline=Collect();
using var coordinator=new SelectionCoordinator<Payload,Payload>((request,token)=>Task.FromResult(new Payload()),Dispatch);
var references=Churn(coordinator,5000);
await coordinator.ClearAsync();
long afterChurn=Collect();
Check(references.All(r=>!r.IsAlive),"Old requests/results survived replacement and ClearAsync");
GC.KeepAlive(coordinator);

var canceled=await CancellationBurst(Dispatch);
long afterCancellation=Collect();
Check(canceled.All(r=>!r.IsAlive),"Canceled requests survived drain and disposal");

var disposal=new SelectionCoordinator<Payload,Payload>((r,t)=>Task.FromResult(r),Dispatch);
var owner=Subscribe(disposal);
Collect();Check(owner.IsAlive,"Event owner must be retained while subscribed");
disposal.Dispose();Collect();
Check(!owner.IsAlive,"Dispose retained a Changed subscriber");
GC.KeepAlive(disposal);

var delayed=new SelectionCoordinator<Payload,Payload>((r,t)=>DelayedLoad(r),Dispatch);
var late=StartDelayed(delayed);
await delayed.ClearAsync();Collect();
Check(late.Request.IsAlive,"A running non-cooperative load must retain its request until completion");
ReleaseDelayed();await late.Work;
Collect();Check(!late.Request.IsAlive,"Completed canceled load retained its request");
delayed.Dispose();GC.KeepAlive(delayed);

Console.WriteLine(JsonSerializer.Serialize(new {checks,replacements=5000,canceledRequests=1000,
    retainedRequestsAndResults=references.Count(r=>r.IsAlive)+canceled.Count(r=>r.IsAlive),
    managedHeapBytes=new {baseline,afterChurn,afterCancellation,final=Collect()},
    scope="Managed request/result/event ownership only; native Unity objects require game profiling"},new JsonSerializerOptions {WriteIndented=true}));

[MethodImpl(MethodImplOptions.NoInlining)]
static WeakReference[] Churn(SelectionCoordinator<Payload,Payload> coordinator,int count)
{
    var references=new List<WeakReference>();
    for(int i=0;i<count;i++)
    {
        var request=new Payload();references.Add(new WeakReference(request));
        coordinator.SelectAsync(request).GetAwaiter().GetResult();
        references.Add(new WeakReference(coordinator.Current.Result!));
    }
    return references.ToArray();
}

[MethodImpl(MethodImplOptions.NoInlining)]
static async Task<WeakReference[]> CancellationBurst(Func<Action,Task> dispatch)
{
    using var coordinator=new SelectionCoordinator<Payload,Payload>(async (request,token)=> {
        await Task.Delay(Timeout.Infinite,token).ConfigureAwait(false);return request;
    },dispatch);
    var references=new List<WeakReference>();var tasks=new List<Task>();
    for(int i=0;i<1000;i++)
    {
        var request=new Payload();references.Add(new WeakReference(request));tasks.Add(coordinator.SelectAsync(request));
    }
    await coordinator.ClearAsync();await Task.WhenAll(tasks).WaitAsync(TimeSpan.FromSeconds(30));
    return references.ToArray();
}

[MethodImpl(MethodImplOptions.NoInlining)]
static WeakReference Subscribe(SelectionCoordinator<Payload,Payload> coordinator)
{
    var owner=new Observer();coordinator.Changed+=owner.Changed;return new WeakReference(owner);
}

[MethodImpl(MethodImplOptions.NoInlining)]
static (WeakReference Request,Task Work) StartDelayed(SelectionCoordinator<Payload,Payload> coordinator)
{
    var request=new Payload();return (new WeakReference(request),coordinator.SelectAsync(request));
}
static Task<Payload> DelayedLoad(Payload request)=>DelayGate.Wait(request);
static void ReleaseDelayed()=>DelayGate.Release();

[MethodImpl(MethodImplOptions.NoInlining)]
static long Collect()
{
    GC.Collect(GC.MaxGeneration,GCCollectionMode.Forced,true,true);GC.WaitForPendingFinalizers();
    GC.Collect(GC.MaxGeneration,GCCollectionMode.Forced,true,true);return GC.GetTotalMemory(false);
}

sealed class Payload {public readonly byte[] Bytes=new byte[32768];}
sealed class Observer
{
    readonly Payload owned=new();
    public void Changed(SelectionState<Payload,Payload> state)=>GC.KeepAlive(owned);
}
static class DelayGate
{
    static readonly TaskCompletionSource gate=new(TaskCreationOptions.RunContinuationsAsynchronously);
    public static async Task<Payload> Wait(Payload request){await gate.Task.ConfigureAwait(false);return request;}
    public static void Release()=>gate.SetResult();
}
