using System.Reflection;

namespace BeatInsight.Compare;

public class ControlledAffinityDisposalProxy:DispatchProxy
{
    internal Action? Callback;
    protected override object? Invoke(MethodInfo? method,object?[]? arguments)
    {
        if(method?.Name!="Dispose"||(arguments?.Length??0)!=0)
            throw new InvalidDataException("Unexpected Affinity disposal proxy call");
        (Callback??throw new InvalidOperationException("Missing Affinity disposal callback"))();
        return null;
    }
}
