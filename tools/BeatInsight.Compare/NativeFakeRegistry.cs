using System.Runtime.CompilerServices;

namespace BeatInsight.Compare;

// Carries custom metadata through native conversion without guessing from time/
// position: fake and real objects can have identical gameplay fields.
public static class NativeFakeRegistry
{
    sealed class Flag { }
    sealed class Source { public readonly object Value;public Source(object value){Value=value;} }
    static readonly ConditionalWeakTable<object,Source> sources=new();
    public static bool CaptureSources;
    public static bool TryGetSource(object item,out object? source)
    {if(sources.TryGetValue(item,out var found)){source=found.Value;return true;}source=null;return false;}
    static readonly ConditionalWeakTable<object,Flag> items=new();
    public static bool Enabled;
    public static bool IsFake(object? item)=>item!=null&&items.TryGetValue(item,out _);
    public static void Mark(object item){if(!IsFake(item))items.Add(item,new Flag());}
    public static object? Converted(object? result,object source)
    {
        if(Enabled&&result!=null)
        {
            if(CaptureSources)sources.GetValue(result,_=>new Source(source));
            bool fake=IsFake(source);
            if(!fake&&source.GetType().GetProperty("customData")?.GetValue(source) is System.Collections.IDictionary data)
                fake=data.Contains("_fake")&&data["_fake"] is true||data.Contains("NE_fake")&&data["NE_fake"] is true;
            if(fake)Mark(result);
        }
        return result;
    }
}
