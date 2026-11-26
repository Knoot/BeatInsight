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
        if(major is not ("2" or "3"))throw new NotSupportedException("Installed custom data loader supports only verified v2/v3 formats");
        var declared=root[major=="2"?"_version":"version"];
        if(declared!=null&&(declared.Type!=JTokenType.String||!Version.TryParse((string?)declared,out var version)||version.Major.ToString()!=major))
            throw new NotSupportedException("Custom loader format and canonical JSON version disagree");
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
