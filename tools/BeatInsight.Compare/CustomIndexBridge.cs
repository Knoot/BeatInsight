using System.Reflection;
using System.Runtime.Loader;

namespace BeatInsight.Compare;

public static class CustomIndexBridge
{
    static MethodInfo? objects,events;
    public static void Add(object map,object item,bool isEvent)
    {
        if(map.GetType().FullName!="CustomJSONData.CustomBeatmap.CustomBeatmapData")return;
        if(objects==null)
        {
            var type=AssemblyLoadContext.Default.Assemblies.Single(a=>a.GetName().Name=="CustomJSONData").GetType("CustomJSONData.CustomBeatmap.CustomBeatmapData",true)!;
            const BindingFlags flags=BindingFlags.Public|BindingFlags.NonPublic|BindingFlags.Static;
            objects=type.GetMethod("AddBeatmapObjectData",flags)!;events=type.GetMethod("InsertBeatmapEventData",flags)!;
        }
        (isEvent?events!:objects!).Invoke(null,new[]{map,item});
    }
}
