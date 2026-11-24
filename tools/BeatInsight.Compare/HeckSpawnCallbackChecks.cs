using System.Collections;
using System.Reflection;

namespace BeatInsight.Compare;

// Observe availability at the real spawner callback, before later postchecks.
sealed class HeckSpawnCallbackChecks
{
    const BindingFlags flags=BindingFlags.Public|BindingFlags.NonPublic|BindingFlags.Instance|BindingFlags.Static;
    readonly object bindings;
    readonly List<NoteData> emitted;
    internal SliderData? Chain;
    internal int Checks,Failed;
    internal HeckSpawnCallbackChecks(object bindings,List<NoteData> emitted){this.bindings=bindings;this.emitted=emitted;}
    public void Capture(NoteData note)
    {
        if(Chain==null)throw new InvalidOperationException("Missing callback chain");
        foreach(var binding in (IEnumerable)bindings)
        {
            var data=binding.GetType().GetField("Item2")!.GetValue(binding)!;
            var objects=(IDictionary)data.GetType().GetProperty("ObjectCustomDatas",flags)!.GetValue(data)!;
            var parent=objects[Chain];
            bool expected=parent?.GetType().GetInterfaces().Any(i=>i.IsGenericType&&i.GetGenericTypeDefinition().FullName=="Heck.Deserialize.ICopyable`1")==true;
            if(objects.Contains(note)!=expected)Failed++;
            if(expected&&objects.Contains(note))
            {
                var child=objects[note]!;
                var resolve=data.GetType().GetMethods(flags).Single(m=>m.Name=="Resolve"&&m.IsGenericMethodDefinition&&m.GetParameters().Length==2&&m.GetParameters()[0].ParameterType==typeof(BeatmapObjectData));
                object?[] args={note,null};
                if(!(bool)resolve.MakeGenericMethod(child.GetType()).Invoke(data,args)!||!ReferenceEquals(child,args[1])||ReferenceEquals(child,parent))Failed++;
            }
            Checks++;
        }
        emitted.Add(note);
    }
}
