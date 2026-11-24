using System.Reflection;

namespace BeatInsight.Compare;

public class ControlledLateDisposalProxy:DispatchProxy
{
    internal Action? Callback;
    protected override object? Invoke(MethodInfo? method,object?[]? arguments)
    {
        if(method?.Name!="LateDispose"||(arguments?.Length??0)!=0)throw new InvalidDataException("Unexpected late disposal control call");
        Callback!();return null;
    }
}
public class HighLateDisposalProxy:ControlledLateDisposalProxy { }
public class LowLateDisposalProxy:ControlledLateDisposalProxy { }

static class NativeDisposalBindingsChecks
{
    const BindingFlags flags=BindingFlags.Public|BindingFlags.NonPublic|BindingFlags.Instance|BindingFlags.Static;
    sealed class HighDisposable(List<string> trace):IDisposable { public void Dispose()=>trace.Add("ordinary-high"); }
    sealed class LowDisposable(List<string> trace):IDisposable { public void Dispose()=>trace.Add("ordinary-low"); }

    internal static (object Evidence,WeakReference[] Released) Run(Assembly zenject,object kernel,List<string> trace)
    {
        var containerType=zenject.GetType("Zenject.DiContainer",true)!;
        var managerType=zenject.GetType("Zenject.DisposableManager",true)!;
        var lateType=kernel.GetType().GetInterfaces().Single(t=>t.FullName=="Zenject.ILateDisposable");
        var container=Activator.CreateInstance(containerType)!;
        void Bind(Type contract,object value)=>containerType.GetMethods(flags).Single(m=>m.Name=="BindInstance"&&m.IsGenericMethodDefinition).MakeGenericMethod(contract).Invoke(container,new[]{value});
        void Priority(string method,Type type,int value)=>containerType.GetMethod(method,new[]{typeof(Type),typeof(int)})!.Invoke(container,new object[]{type,value});
        var high=new HighDisposable(trace);var low=new LowDisposable(trace);
        var lateHigh=(ControlledLateDisposalProxy)DispatchProxy.Create(lateType,typeof(HighLateDisposalProxy));
        var lateLow=(ControlledLateDisposalProxy)DispatchProxy.Create(lateType,typeof(LowLateDisposalProxy));
        lateHigh.Callback=()=>trace.Add("late-high");lateLow.Callback=()=>trace.Add("late-low");
        // Deliberately bind in the opposite order to the required disposal order.
        Bind(typeof(IDisposable),low);Bind(typeof(IDisposable),high);
        Bind(lateType,lateLow);Bind(lateType,kernel);Bind(lateType,lateHigh);
        Priority("BindDisposableExecutionOrder",typeof(HighDisposable),10);
        Priority("BindDisposableExecutionOrder",typeof(LowDisposable),-10);
        Priority("BindLateDisposableExecutionOrder",lateHigh.GetType(),10);
        Priority("BindLateDisposableExecutionOrder",kernel.GetType(),0);
        Priority("BindLateDisposableExecutionOrder",lateLow.GetType(),-10);
        var binding=containerType.GetMethods(flags).Single(m=>m.Name=="Bind"&&m.IsGenericMethodDefinition&&m.GetGenericArguments().Length==1&&m.GetParameters().Length==0).MakeGenericMethod(managerType).Invoke(container,null)!;
        var self=binding.GetType().GetMethod("ToSelf",flags)!.Invoke(binding,null)!;
        self.GetType().GetMethod("AsSingle",flags)!.Invoke(self,null);
        var resolve=containerType.GetMethod("Resolve",new[]{typeof(Type)})!;
        var manager=resolve.Invoke(container,new object[]{managerType})!;
        bool singleton=ReferenceEquals(manager,resolve.Invoke(container,new object[]{managerType}));
        bool repeatedDisposeRejected=false,repeatedLateDisposeRejected=false;
        try
        {
            managerType.GetMethod("Dispose",Type.EmptyTypes)!.Invoke(manager,null);
            if(!trace.SequenceEqual(new[]{"ordinary-high","ordinary-low"}))throw new InvalidDataException("Ordinary DI disposal priority changed");
            managerType.GetMethod("LateDispose",Type.EmptyTypes)!.Invoke(manager,null);
            var expected=new[]{"ordinary-high","ordinary-low","late-high","kernel","late-low"};
            if(!trace.SequenceEqual(expected)||!singleton)throw new InvalidDataException("Late DI disposal priority changed");
            foreach(string operation in new[]{"Dispose","LateDispose"})
            {
                try{managerType.GetMethod(operation,Type.EmptyTypes)!.Invoke(manager,null);}
                catch(TargetInvocationException e)when(e.InnerException?.GetType().FullName=="Zenject.ZenjectException")
                {if(operation=="Dispose")repeatedDisposeRejected=true;else repeatedLateDisposeRejected=true;}
            }
            if(!repeatedDisposeRejected||!repeatedLateDisposeRejected||!trace.SequenceEqual(expected))throw new InvalidDataException("Repeated DI disposal changed trace");
        }
        finally{lateHigh.Callback=null;lateLow.Callback=null;}
        return(new{singleton,repeatedDisposeRejected,repeatedLateDisposeRejected,trace=trace.ToArray(),priorities=new[]{10,0,-10},
            callbacksCleared=lateHigh.Callback==null&&lateLow.Callback==null,
            scope="Real separate DiContainer: BindInstance receivers, BindDisposableExecutionOrder/BindLateDisposableExecutionOrder, Bind/ToSelf/AsSingle and Resolve DisposableManager. Native constructor injects lists and named priorities; native Dispose/LateDispose run unchanged. Separate scope excludes unrelated SiraKernel lifecycle."},
            new[]{container,manager,high,low,lateHigh,lateLow}.Select(o=>new WeakReference(o)).ToArray());
    }
}
