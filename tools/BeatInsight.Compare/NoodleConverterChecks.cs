using System.Collections;
using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.Loader;
using Newtonsoft.Json.Linq;

namespace BeatInsight.Compare;

// Executes installed converter transpilers and their emitted methods in an isolated CLI.
// No Harmony detours, gameplay lifecycle or files in the game are changed.
static class NoodleConverterChecks
{
    public static object Run(string gameRoot)
    {
        GameLoader.Load(gameRoot,"""{"version":"3.3.0","colorNotes":[]}""",120,noodleExtensions:true);
        const BindingFlags flags=BindingFlags.Public|BindingFlags.NonPublic|BindingFlags.Instance|BindingFlags.Static;
        var custom=AssemblyLoadContext.Default.Assemblies.Single(a=>a.GetName().Name=="CustomJSONData");
        var game=AssemblyLoadContext.Default.Assemblies.Single(a=>a.GetName().Name=="DataModels");
        var harmony=AssemblyLoadContext.Default.LoadFromAssemblyName(new AssemblyName("0Harmony"));
        var patchType=custom.GetType("CustomJSONData.HarmonyPatches.ConvertersCustomify",true)!;
        object Get(object value,string name)=>value.GetType().GetProperty(name,flags)?.GetValue(value)??value.GetType().GetField(name,flags)?.GetValue(value)
            ??throw new InvalidDataException("Native source member changed: "+name);
        int cases=0,failed=0;var results=new List<object>();
        var rotationOutputs=new List<object>();
        var converters=new[]{("colorNotes","ColorNoteConverter","ColorNoteConvertV3"),("bombNotes","BombNoteConverter","BombNoteConvertV3"),
            ("obstacles","ObstacleConverter","ObstacleConvertV3"),("sliders","SliderConverter","SliderConvertV3"),("burstSliders","BurstSliderConverter","BurstSliderConverterV3")};
        foreach(var binding in converters)
        {
            var type=game.GetType("BeatmapDataLoaderVersion3.BeatmapDataLoader+"+binding.Item2,true)!;
            var original=type.GetMethod("Convert",flags)!;var compiled=InstalledTranspilerCompiler.Compile(original,patchType.GetMethod(binding.Item3,flags)!);
            var output=JObject.FromObject(ConverterRotationOutputChecks.Run(gameRoot,binding.Item1,type,original,compiled.Method));
            failed+=(int)output["failed"]!;rotationOutputs.Add(new{kind=binding.Item1,report=output});
            foreach(bool bpmChange in new[]{false,true})foreach(bool rotation in new[]{false,true})foreach(bool fake in new[]{false,true})foreach(int lane in new[]{0,2})foreach(int direction in new[]{1,8})
            {
                var data=new JObject {["probeIdentity"]="converter-probe",["coordinates"]=new JArray(-.5,1.25),["uninteractable"]=true};
                var item=new JObject {["b"]=4,["x"]=lane,["y"]=1,["c"]=0,["d"]=direction,["tb"]=6,["tx"]=2,["ty"]=2,["tc"]=1,["sc"]=3,["s"]=1,["customData"]=data};
                if(binding.Item1=="obstacles"){item["d"]=2;item["w"]=2;item["h"]=3;}
                var root=new JObject {["version"]="3.3.0",["colorNotes"]=new JArray(),[binding.Item1]=new JArray(item),
                    ["bpmEvents"]=bpmChange?new JArray(new JObject {["b"]=2,["m"]=180}):new JArray(),
                    ["rotationEvents"]=rotation?new JArray(new JObject {["b"]=2,["e"]=0,["r"]=15}):new JArray()};
                if(fake)data["NE_fake"]=true;
                var saved=NoodleNativeData.Deserialize(gameRoot,root,"3");
                var source=((IEnumerable)Get(saved,binding.Item1)).Cast<object>().Single();
                var metadataBefore=JToken.FromObject(Get(source,"customData"));
                var bpmCtor=game.GetType("BpmTimeProcessor",true)!.GetConstructors(flags).Single(c=>c.GetParameters().Length==2&&c.GetParameters()[1].ParameterType.GenericTypeArguments[0].Name=="BpmChangeEventData");
                var rotationCtor=game.GetType("RotationTimeProcessor",true)!.GetConstructors(flags).Single(c=>c.GetParameters().Length==1&&c.GetParameters()[0].ParameterType.GenericTypeArguments.FirstOrDefault()?.Name=="RotationEventData");
                var instance=Activator.CreateInstance(type,flags,null,new[]{bpmCtor.Invoke(new[]{(object)120f,Get(saved,"bpmEvents")}),rotationCtor.Invoke(new[]{Get(saved,"rotationEvents")})},null)!;
                var plain=original.Invoke(instance,new[]{source})!;
                var actual=compiled.Method.Invoke(null,new[]{instance,source})!;
                bool pass=actual.GetType().Name.StartsWith("Custom",StringComparison.Ordinal)&&ReferenceEquals(Get(actual,"customData"),Get(source,"customData"))
                    &&((Version)Get(actual,"version")).Major==3&&compiled.Patched>compiled.Original
                    &&Math.Abs(((BeatmapObjectData)actual).time-(bpmChange?5f/3:2f))<.00001f
                    &&JToken.DeepEquals(metadataBefore,JToken.FromObject(Get(source,"customData")));
                var differences=new List<string>();
                foreach(var property in plain.GetType().GetProperties(BindingFlags.Instance|BindingFlags.Public).Where(p=>p.GetIndexParameters().Length==0))
                {
                    if(!Equals(property.GetValue(plain),property.GetValue(actual))){differences.Add(property.Name);pass=false;}
                }
                cases++;if(!pass)failed++;results.Add(new{kind=binding.Item1,bpmChange,rotation,fake,lane,direction,pass,differences,time=((BeatmapObjectData)actual).time,compiled.Original,compiled.Patched});
            }
        }
        var rotationEvents=JObject.FromObject(RotationEventBoundaryChecks.Run(gameRoot));
        failed+=(int)rotationEvents["failed"]!;
        var legacyRotationOutputs=JObject.FromObject(LegacyConverterRotationOutputChecks.Run(gameRoot));
        failed+=(int)legacyRotationOutputs["failed"]!;
        var independentDensity=JObject.FromObject(IndependentDensityChecks.Run(gameRoot));failed+=(int)independentDensity["failed"]!;
        return new{cases,failed,rotationEvents,rotationOutputs,legacyRotationOutputs,independentDensity,harmonySha256=Comparison.HashFile(harmony.Location),results,
            scope="Actual installed v3 converter transpilers emitted/executed with genuine custom save-data metadata; native BPM/rotation processors and raw converter comparison. No complete loader/map factory, row processor, event dispatch, copy ordering, Noodle data constructor or support admission."};
    }
}
