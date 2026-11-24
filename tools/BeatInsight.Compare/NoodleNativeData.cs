using System.Reflection;
using System.Runtime.Loader;
using Newtonsoft.Json.Linq;

namespace BeatInsight.Compare;

static class NoodleNativeData
{
    static Assembly? customData;
    static object? registration;
    public static object Deserialize(string gameRoot,JObject root,string major)
    {
        if(customData==null)
        {
            string dataPath=Path.Combine(gameRoot,"Plugins/CustomJSONData.dll"),extensionPath=Path.Combine(gameRoot,"Plugins/NoodleExtensions.dll");
            if(Comparison.HashFile(dataPath)!="4F9F8EFD8012159D8995B6D936E3C21B6AA6A35E319686FA3E9B04C2B6331F7C"||
                Comparison.HashFile(extensionPath)!="FE50124E33631EED7ECB78EA1A19BB7EA64CC12974B0298DAD991672C2ABDA95")throw new InvalidDataException("Reinspect changed Noodle/custom data deserializer");
            var loadedCustomData=AssemblyLoadContext.Default.LoadFromAssemblyPath(dataPath);
            var extension=AssemblyLoadContext.Default.LoadFromAssemblyPath(extensionPath);
            var deserializer=loadedCustomData.GetType("CustomJSONData.CustomBeatmap.CustomJSONDataDeserializer",true)!;
            registration=deserializer.GetMethod("Register",BindingFlags.Public|BindingFlags.NonPublic|BindingFlags.Static)!
                .MakeGenericMethod(extension.GetType("NoodleExtensions.FakeNotesJson",true)!).Invoke(null,null)!;
            deserializer.GetProperty("Enabled")!.SetValue(registration,true);
            customData=loadedCustomData;
        }
        string name=major=="2"?"Version2_6_0AndEarlierCustomBeatmapSaveData":"Version3CustomBeatmapSaveData";
        return customData.GetType("CustomJSONData.CustomBeatmap."+name,true)!.GetMethod("Deserialize",BindingFlags.Public|BindingFlags.NonPublic|BindingFlags.Static)!
            .Invoke(null,new object[]{root.ToString()})!;
    }
}
