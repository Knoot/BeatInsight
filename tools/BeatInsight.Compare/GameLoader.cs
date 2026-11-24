using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.Loader;
using Newtonsoft.Json.Linq;

namespace BeatInsight.Compare;

static class GameLoader
{
    public static object[] Describe(object data) => OracleBridge.GetItems(data).Select(item=> item switch {
        NoteData n => (object)new {kind="note",n.time,n.beat,n.lineIndex,n.noteLineLayer,n.colorType,n.cutDirection,n.scoringType},
        SliderData s => new {kind="slider",s.time,s.beat,s.sliderType,s.headLineIndex,s.headLineLayer,s.colorType,s.hasHeadNote,s.sliceCount,s.tailTime,s.tailLineIndex,s.tailLineLayer},
        _ => (object)new {kind=item.GetType().Name,item.time}
    }).ToArray();
    // JsonUtility requires the Unity native host. Populate the same serialized public fields
    // instead; all beat/time/object conversions below are executed by the installed game.
    static object? Read(JToken? token, Type type)
    {
        if (token == null || token.Type == JTokenType.Null)
        {
            if(type.IsArray)return Array.CreateInstance(type.GetElementType()!,0);
            if (type.IsGenericType && type.GetGenericTypeDefinition() == typeof(List<>)) return Activator.CreateInstance(type);
            if (type.FullName == "BeatmapSaveDataCommon.BasicEventTypesWithKeywords" || type.Name == "SpecialEventKeywordFiltersData") return Read(new JObject(), type);
            return type.IsValueType ? Activator.CreateInstance(type) : null;
        }
        if (type.IsEnum) return Enum.ToObject(type, token.Value<int>());
        if (type.IsPrimitive || type == typeof(string)) return token.ToObject(type);
        if (type.IsArray)
        {
            var array = Array.CreateInstance(type.GetElementType()!, token.Count());
            int i = 0; foreach (var item in token) array.SetValue(Read(item, type.GetElementType()!), i++);
            return array;
        }
        if (type.IsGenericType && type.GetGenericTypeDefinition() == typeof(List<>))
        {
            var list = (System.Collections.IList)Activator.CreateInstance(type)!;
            foreach (var item in token) list.Add(Read(item, type.GetGenericArguments()[0]));
            return list;
        }
        var value = RuntimeHelpers.GetUninitializedObject(type);
        for (var current = type; current != null && current != typeof(object); current = current.BaseType)
            foreach (var f in current.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly))
                f.SetValue(value, Read(token[f.Name], f.FieldType));
        if(NativeFakeRegistry.Enabled&&token is JObject)
        {
            JToken? fake=type.Namespace=="BeatmapSaveDataVersion2_6_0AndEarlier"?token["_customData"]?["_fake"]:type.Namespace=="BeatmapSaveDataVersion3"?token["customData"]?["NE_fake"]:null;
            if(fake?.Type==JTokenType.Boolean&&(bool)fake)NativeFakeRegistry.Mark(value);
        }
        return value;
    }
    public static object Load(string gameRoot, string json, float bpm, string? audioJson = null, object? gameplayModifiers = null,bool mappingExtensions=false,bool noodleExtensions=false)
    {
        MappingNativeRuntime.Enabled=mappingExtensions;
        NativeFakeRegistry.Enabled=noodleExtensions;
        var asm = AssemblyLoadContext.Default.Assemblies.Single(a=>a.GetName().Name=="DataModels");
        var root = JObject.Parse(json);
        // Lighting is outside this oracle's scope and requires a Unity environment.
        // Keep all gameplay objects, BPM and rotation inputs unchanged.
        foreach(string field in new[]{"lightColorEventBoxGroups","lightRotationEventBoxGroups","lightTranslationEventBoxGroups","vfxEventBoxGroups"})
            if(root[field]!=null)root[field]=new JArray();
        string version = ((string?)(root["version"] ?? root["_version"]) ?? "2").Split('.')[0];
        string ns = version switch { "2" => "BeatmapSaveDataVersion2_6_0AndEarlier", "3" => "BeatmapSaveDataVersion3", "4" => "BeatmapSaveDataVersion4", _ => throw new NotSupportedException("Unsupported map version") };
        string loaderNs = ns.Replace("SaveData", "DataLoader");
        var loader = asm.GetType(loaderNs + ".BeatmapDataLoader", true)!;
        var method = loader.GetMethod("GetBeatmapDataFromSaveData", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static)!;
        var parameters = method.GetParameters();
        var args = new object?[parameters.Length];
        for (int i = 0; i < args.Length; i++)
        {
            var p = parameters[i];
            args[i] = p.ParameterType.IsValueType ? Activator.CreateInstance(p.ParameterType) : null;
            if (p.ParameterType.FullName == ns + ".BeatmapSaveData") args[i] = noodleExtensions?NoodleNativeData.Deserialize(gameRoot,root,version):Read(root, p.ParameterType);
            else if(p.ParameterType.FullName=="GameplayModifiers") args[i]=gameplayModifiers;
            else if (p.ParameterType.FullName == "BeatmapLevelSaveDataVersion4.AudioSaveData")
                args[i] = Read(JObject.Parse(audioJson ?? throw new InvalidDataException("Missing AudioData")), p.ParameterType);
            else if (p.ParameterType == typeof(float)) args[i] = bpm;
            else if (p.ParameterType.FullName == "EnvironmentKeywords") args[i] = Activator.CreateInstance(p.ParameterType, new object?[] { null });
            else if(p.ParameterType.FullName=="BeatmapSaveDataVersion4.LightshowSaveData") args[i]=Read(new JObject(),p.ParameterType);
        }
        return method.Invoke(null, args)!;
    }
}
