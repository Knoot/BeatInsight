using System;
using System.Threading;
using System.Threading.Tasks;

namespace BeatInsight.Core;

public enum SelectionStatus { Idle, Loading, Ready, Unsupported, Unavailable, Error }

public sealed class SelectionState<TRequest,TResult> where TRequest:class where TResult:class
{
    public long Revision {get;}
    public SelectionStatus Status {get;}
    public TRequest? Request {get;}
    public TResult? Result {get;}
    public string? Message {get;}
    public SelectionState(long revision,SelectionStatus status,TRequest? request=null,TResult? result=null,string? message=null)
    {Revision=revision;Status=status;Request=request;Result=result;Message=message;}
}

// Dispatch is supplied by the host (Unity main thread in the mod). All result
// publication, including queued callbacks, is guarded by the selection revision.
public sealed class SelectionCoordinator<TRequest,TResult>:IDisposable where TRequest:class where TResult:class
{
    readonly Func<TRequest,CancellationToken,Task<TResult>> load;
    readonly Func<Action,Task> dispatch;
    readonly object sync=new object();
    readonly SemaphoreSlim worker=new SemaphoreSlim(1,1);
    CancellationTokenSource? cancellation;
    long revision;
    bool disposed;
    SelectionState<TRequest,TResult> current=new SelectionState<TRequest,TResult>(0,SelectionStatus.Idle);
    public event Action<SelectionState<TRequest,TResult>>? Changed;
    public SelectionState<TRequest,TResult> Current {get {lock(sync)return current;}}

    public SelectionCoordinator(Func<TRequest,CancellationToken,Task<TResult>> load,Func<Action,Task> dispatch)
    {this.load=load??throw new ArgumentNullException(nameof(load));this.dispatch=dispatch??throw new ArgumentNullException(nameof(dispatch));}

    static void Cancel(CancellationTokenSource? source)
    {try {source?.Cancel();}catch(ObjectDisposedException){/* The owning request already finished. */}}

    public Task SelectAsync(TRequest request)
    {
        if(request==null)throw new ArgumentNullException(nameof(request));
        CancellationTokenSource? previous;CancellationTokenSource source;long id;
        lock(sync)
        {
            if(disposed)throw new ObjectDisposedException(nameof(SelectionCoordinator<TRequest,TResult>));
            previous=cancellation;cancellation=source=new CancellationTokenSource();id=++revision;
            current=new SelectionState<TRequest,TResult>(id,SelectionStatus.Loading,request);
        }
        Cancel(previous);
        return RunAsync(id,request,source);
    }

    async Task Publish(long id,SelectionStatus status,TRequest? request=null,TResult? result=null,string? message=null)
    {
        await dispatch(()=> {
            lock(sync)
            {
                if(disposed||revision!=id)return;
                current=new SelectionState<TRequest,TResult>(id,status,request,result,message);
                Changed?.Invoke(current);
            }
        }).ConfigureAwait(false);
    }

    async Task RunAsync(long id,TRequest request,CancellationTokenSource source)
    {
        bool entered=false;
        try
        {
            await Publish(id,SelectionStatus.Loading,request).ConfigureAwait(false);
            await worker.WaitAsync(source.Token).ConfigureAwait(false);entered=true;
            source.Token.ThrowIfCancellationRequested();
            var result=await load(request,source.Token).ConfigureAwait(false);
            source.Token.ThrowIfCancellationRequested();
            await Publish(id,SelectionStatus.Ready,request,result).ConfigureAwait(false);
        }
        catch(OperationCanceledException) when(source.IsCancellationRequested) { }
        catch(NotSupportedException e){await Publish(id,SelectionStatus.Unsupported,request,message:e.Message).ConfigureAwait(false);}
        catch(System.IO.FileNotFoundException e){await Publish(id,SelectionStatus.Unavailable,request,message:e.Message).ConfigureAwait(false);}
        catch(System.IO.DirectoryNotFoundException e){await Publish(id,SelectionStatus.Unavailable,request,message:e.Message).ConfigureAwait(false);}
        catch(Exception e){await Publish(id,SelectionStatus.Error,request,message:e.Message).ConfigureAwait(false);}
        finally
        {
            if(entered)worker.Release();
            lock(sync){if(ReferenceEquals(cancellation,source))cancellation=null;}
            source.Dispose();
        }
    }

    public Task ClearAsync()=>ResetAsync(SelectionStatus.Idle,null);
    public Task UnavailableAsync(string message)=>ResetAsync(SelectionStatus.Unavailable,message);

    Task ResetAsync(SelectionStatus status,string? message)
    {
        CancellationTokenSource? previous;long id;
        lock(sync)
        {
            if(disposed)return Task.CompletedTask;
            previous=cancellation;cancellation=null;id=++revision;
            current=new SelectionState<TRequest,TResult>(id,status,message:message);
        }
        Cancel(previous);
        return Publish(id,status,message:message);
    }

    public void Dispose()
    {
        CancellationTokenSource? previous;
        lock(sync)
        {
            if(disposed)return;
            disposed=true;previous=cancellation;cancellation=null;
            current=new SelectionState<TRequest,TResult>(++revision,SelectionStatus.Idle);
            Changed=null;
        }
        Cancel(previous);
        // In-flight requests own their CTS. Do not dispose the semaphore while
        // a non-cooperative load can still complete and release it.
    }
}
