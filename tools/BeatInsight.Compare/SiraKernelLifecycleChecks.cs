using System.Collections;
using System.Reflection;
using System.Reflection.Emit;

namespace BeatInsight.Compare;

public class ControlledAsyncInitializerProxy:DispatchProxy
{
    internal Func<CancellationToken,Task>? Callback;
    protected override object? Invoke(MethodInfo? method,object?[]? arguments)
    {
        if(method?.Name!="InitializeAsync"||arguments is not [CancellationToken token])throw new InvalidDataException("Unexpected async initializer call");
        return Callback!(token);
    }
}

static class SiraKernelLifecycleChecks
{
    const BindingFlags flags=BindingFlags.Public|BindingFlags.NonPublic|BindingFlags.Instance|BindingFlags.Static;
    sealed class QueueScheduler:TaskScheduler
    {
        readonly Queue<Task> queue=new();
        internal readonly List<Task> Scheduled=new();
        protected override IEnumerable<Task> GetScheduledTasks()=>queue.ToArray();
        protected override void QueueTask(Task task){queue.Enqueue(task);Scheduled.Add(task);}
        protected override bool TryExecuteTaskInline(Task task,bool previouslyQueued)=>false;
        internal void Drain(){int count=0;while(queue.TryDequeue(out var task)){if(++count>20)throw new InvalidDataException("Unbounded kernel scheduling");TryExecuteTask(task);}}
        internal void Clear(){queue.Clear();Scheduled.Clear();}
    }
    static TaskFactory? factory;
    public static TaskFactory Factory()=>factory??throw new InvalidOperationException("Missing controlled task factory");

    internal static (object Evidence,WeakReference[] Released) Run(Assembly sira)
    {
        if(factory!=null)throw new InvalidOperationException("Nested Sira kernel lifecycle check");
        var kernelType=sira.GetType("SiraUtil.Zenject.Internal.SiraKernel",true)!;
        var ctor=kernelType.GetConstructors(flags).Single();
        var init=kernelType.GetMethod("Initialize",flags)!;var dispose=kernelType.GetMethod("Dispose",flags)!;
        var sourceField=kernelType.GetField("_cancellationTokenSource",flags)!;
        var contract=ctor.GetParameters()[1].ParameterType.GetGenericArguments().Single();
        var scheduler=new QueueScheduler();
        var released=new List<WeakReference>{new(scheduler)};int factoryAdapters=0,calls=0;
        var compiled=InstalledTranspilerCompiler.Compile(init,typeof(HeckMapChecks).GetMethod(nameof(HeckMapChecks.Identity))!,(code,operand)=>
        {
            if(operand is MethodInfo m&&m.DeclaringType?.FullName=="IPA.Utilities.Async.UnityMainThreadTaskScheduler"&&m.Name=="get_Factory")
            {factoryAdapters++;return(OpCodes.Call,typeof(SiraKernelLifecycleChecks).GetMethod(nameof(Factory))!);}
            return(code,operand);
        },exceptionBlocks:true).Method;
        if(factoryAdapters!=1)throw new InvalidDataException("Sira kernel scheduler edge changed");
        var results=new List<object>();
        factory=new TaskFactory(CancellationToken.None,TaskCreationOptions.None,TaskContinuationOptions.None,scheduler);
        try
        {
            foreach(string mode in new[]{"empty","complete-and-resume","cancel-before-dispatch","faulted-task","throw-sync","cancel-await","di-managers"})
            {
                var list=(IList)Activator.CreateInstance(ctor.GetParameters()[1].ParameterType)!;
                var tokens=new List<CancellationToken>();var proxies=new List<ControlledAsyncInitializerProxy>();
                TaskCompletionSource? pending=mode is "complete-and-resume" or "cancel-await"?new():null;
                int count=mode=="empty"?0:mode=="complete-and-resume"?3:mode=="cancel-before-dispatch"?2:1;
                for(int i=0;i<count;i++)
                {
                    bool waits=i==2||mode=="cancel-await";var proxy=(ControlledAsyncInitializerProxy)DispatchProxy.Create(contract,typeof(ControlledAsyncInitializerProxy));
                    proxy.Callback=token=>{calls++;tokens.Add(token);
                        if(mode=="throw-sync")throw new InvalidDataException("Controlled initializer failure");
                        if(mode=="faulted-task")return Task.FromException(new InvalidDataException("Controlled initializer failure"));
                        return waits?pending!.Task:Task.CompletedTask;};
                    list.Add(proxy);proxies.Add(proxy);released.Add(new(proxy));
                }
                var log=Activator.CreateInstance(ctor.GetParameters()[0].ParameterType,true)!;
                released.Add(new(log));
                object? initManager=null,disposalManager=null;MethodInfo? managerInitialize=null;
                bool diSingleton=false,repeatedManagerInitRejected=false,managerCanceled=false;int managerDispatchAdapters=0;
                object kernel;
                if(mode=="di-managers")
                {
                    var zenject=AppDomain.CurrentDomain.GetAssemblies().Single(a=>a.GetName().Name=="Zenject");
                    var containerType=zenject.GetType("Zenject.DiContainer",true)!;
                    var container=Activator.CreateInstance(containerType)!;
                    void BindInstance(Type type,object value)=>containerType.GetMethods(flags).Single(m=>m.Name=="BindInstance"&&m.IsGenericMethodDefinition).MakeGenericMethod(type).Invoke(container,new[]{value});
                    void BindSelf(Type type)
                    {
                        var binding=containerType.GetMethods(flags).Single(m=>m.Name=="Bind"&&m.IsGenericMethodDefinition&&m.GetGenericArguments().Length==1&&m.GetParameters().Length==0).MakeGenericMethod(type).Invoke(container,null)!;
                        var self=binding.GetType().GetMethod("ToSelf",flags)!.Invoke(binding,null)!;self.GetType().GetMethod("AsSingle",flags)!.Invoke(self,null);
                    }
                    object Resolve(Type type)=>containerType.GetMethod("Resolve",new[]{typeof(Type)})!.Invoke(container,new object[]{type})!;
                    BindInstance(log.GetType(),log);foreach(var proxy in proxies)BindInstance(contract,proxy);
                    var kernelBinding=containerType.GetMethods(flags).Single(m=>m.Name=="BindInterfacesAndSelfTo"&&m.IsGenericMethodDefinition&&m.GetParameters().Length==0).MakeGenericMethod(kernelType).Invoke(container,null)!;
                    kernelBinding.GetType().GetMethod("AsSingle",flags)!.Invoke(kernelBinding,null);
                    var initializableType=zenject.GetType("Zenject.InitializableManager",true)!;
                    var disposableType=zenject.GetType("Zenject.DisposableManager",true)!;
                    BindSelf(initializableType);BindSelf(disposableType);
                    kernel=Resolve(kernelType);initManager=Resolve(initializableType);disposalManager=Resolve(disposableType);
                    diSingleton=ReferenceEquals(kernel,Resolve(kernelType));
                    if(!diSingleton)throw new InvalidDataException("Sira DI singleton changed");
                    int dispatchAdapters=0;
                    managerInitialize=InstalledTranspilerCompiler.Compile(initializableType.GetMethod("Initialize",flags)!,typeof(HeckMapChecks).GetMethod(nameof(HeckMapChecks.Identity))!,(code,operand)=>
                    {
                        if(operand is MethodInfo m&&m.DeclaringType?.FullName=="Zenject.IInitializable"&&m.Name=="Initialize")
                        {
                            dispatchAdapters++;var shim=new DynamicMethod("ControlledSiraInitializeDispatch",typeof(void),new[]{m.DeclaringType},typeof(SiraKernelLifecycleChecks).Module,true);
                            var il=shim.GetILGenerator();il.Emit(OpCodes.Ldarg_0);il.Emit(OpCodes.Castclass,kernelType);il.Emit(OpCodes.Call,compiled);il.Emit(OpCodes.Ret);return(OpCodes.Call,shim);
                        }
                        return(code,operand);
                    },exceptionBlocks:true).Method;
                    if(dispatchAdapters!=1)throw new InvalidDataException("Sira manager dispatch edge changed");
                    managerDispatchAdapters=dispatchAdapters;
                    foreach(var value in new[]{container,initManager,disposalManager})released.Add(new(value));
                    // Check the actual injected list rather than the unused input list.
                    list=(IList)kernelType.GetField("_asyncInitializables",flags)!.GetValue(kernel)!;
                }
                else kernel=ctor.Invoke(new object?[]{log,list});
                var source=(CancellationTokenSource)sourceField.GetValue(kernel)!;
                released.Add(new(kernel));released.Add(new(source));released.Add(new(list));
                if(pending!=null)released.Add(new(pending));
                int before=calls;
                try
                {
                    if(mode=="empty")init.Invoke(kernel,null);
                    else if(initManager!=null)managerInitialize!.Invoke(null,new[]{initManager});
                    else compiled.Invoke(null,new[]{kernel});
                    if(calls!=before)throw new InvalidDataException("Kernel bypassed queued scheduler");
                    if(mode=="cancel-before-dispatch")dispose.Invoke(kernel,null);
                    scheduler.Drain();
                    if(pending!=null)
                    {
                        var outer=scheduler.Scheduled.OfType<Task<Task>>().ToArray();
                        if(outer.Length!=count||outer.Any(t=>!t.IsCompletedSuccessfully)||outer.Count(t=>!t.Result.IsCompleted)!=1)
                            throw new InvalidDataException("Kernel pending await state changed");
                        if(mode=="cancel-await"){dispose.Invoke(kernel,null);pending.SetCanceled(source.Token);}else pending.SetResult();
                        scheduler.Drain();
                        if(outer.Any(t=>!t.Result.IsCompletedSuccessfully))throw new InvalidDataException("Kernel continuation did not complete");
                    }
                    if(disposalManager!=null)
                    {
                        disposalManager.GetType().GetMethod("Dispose",flags)!.Invoke(disposalManager,null);
                        managerCanceled=source.IsCancellationRequested;
                        if(!managerCanceled)throw new InvalidDataException("DI manager did not cancel Sira kernel");
                        bool rejected=false;
                        try{managerInitialize!.Invoke(null,new[]{initManager});}
                        catch(TargetInvocationException e)when(e.InnerException?.GetType().FullName=="Zenject.ZenjectException"){rejected=true;}
                        if(!rejected)throw new InvalidDataException("Repeated Sira manager initialization was accepted");
                        repeatedManagerInitRejected=rejected;
                    }
                    dispose.Invoke(kernel,null);dispose.Invoke(kernel,null);
                    bool canceled=source.IsCancellationRequested&&tokens.All(t=>t==source.Token&&t.IsCancellationRequested);
                    int invoked=calls-before;int canceledTasks=scheduler.Scheduled.Count(t=>t.IsCanceled);
                    if(!canceled||invoked!=(mode is "empty" or "cancel-before-dispatch"?0:count)||canceledTasks!=(mode=="cancel-before-dispatch"?2:0)
                        ||scheduler.Scheduled.OfType<Task<Task>>().Any(t=>!t.IsCompletedSuccessfully&&mode!="cancel-before-dispatch"||t.IsCompletedSuccessfully&&!t.Result.IsCompletedSuccessfully))
                        throw new InvalidDataException("Sira kernel cancellation lifecycle changed");
                    results.Add(new{mode,initializers=count,invoked,canceledTasks,canceled,scheduled=scheduler.Scheduled.Count,
                        diSingleton,managerDispatchAdapters,repeatedManagerInitRejected,managerCanceled});
                }
                finally{foreach(var proxy in proxies)proxy.Callback=null;scheduler.Clear();source.Dispose();}
            }
        }
        finally{factory=null;scheduler.Clear();}
        return(new{cases=7,failed=0,factoryAdapters,calls,factoryCleared=factory==null,results,
            scope="Native SiraKernel constructor/Dispose, empty Initialize; nonempty Initialize changes only Unity task-factory getter. Native async closure catches sync/task failures and pending cancellation with actual default SiraLog and null output logger. One real DI container resolves kernel and both managers; compiled InitializableManager redirects one interface call to adapted Initialize, native DisposableManager cancels kernel. Error/Critical methods execute without an output sink; log text/emission and Unity scheduling not validated. Fixture disposes CTS after assertions."},released.ToArray());
    }
}
