using System.Linq.Expressions;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.Loader;
using UnityEngine;
using BeatInsight.Core;
using BeatInsight.Formats;
using Newtonsoft.Json.Linq;

namespace BeatInsight.Compare;

static class NoodleSpawnChecks
{
    public static object Run(string gameRoot,string reportPath)
    {
        const BindingFlags flags=BindingFlags.Public|BindingFlags.NonPublic|BindingFlags.Instance;
        GameLoader.Load(gameRoot,"""{"version":"3.3.0","colorNotes":[],"obstacles":[]}""",120,noodleExtensions:true);
        string path=Path.Combine(gameRoot,"Beat Saber_Data/Managed/Main.dll");
        if(Comparison.HashFile(path)!="A276C8A50ACFAD69A3DD3A82CDE08571C3262344702B9B014C89756771B4495E")throw new InvalidDataException("Reinspect changed burst spawner");
        var main=AssemblyLoadContext.Default.LoadFromAssemblyPath(path);
        var spawner=main.GetType("BurstSliderSpawner",true)!;
        var process=spawner.GetMethod("ProcessSliderData",BindingFlags.Public|BindingFlags.NonPublic|BindingFlags.Static)!;
        var spawnType=main.GetType("SliderSpawnData",true)!;
        var movementType=main.GetType("VariableMovementDataProvider",true)!;
        var movement=RuntimeHelpers.GetUninitializedObject(movementType);
        movementType.GetField("_halfJumpDuration",flags)!.SetValue(movement,1f);
        var callbackType=process.GetParameters()[4].ParameterType;
        var callbackParams=callbackType.GetMethod("Invoke")!.GetParameters().Select(p=>Expression.Parameter(p.ParameterType,p.Name)).ToArray();
        var emitted=new List<NoteData>();
        var callback=Expression.Lambda(callbackType,Expression.Call(Expression.Constant(emitted),typeof(List<NoteData>).GetMethod("Add")!,callbackParams[0]),callbackParams).Compile();
        var custom=AssemblyLoadContext.Default.Assemblies.Single(a=>a.GetName().Name=="CustomJSONData");
        var metadataType=custom.GetType("CustomJSONData.CustomBeatmap.CustomData",true)!;
        var factory=custom.GetType("CustomJSONData.CustomBeatmap.CustomSliderData",true)!.GetMethod("CreateCustomBurstSliderData")!;
        var noodle=AssemblyLoadContext.Default.Assemblies.Single(a=>a.GetName().Name=="NoodleExtensions");
        var dataCtor=noodle.GetType("NoodleExtensions.NoodleSliderData",true)!.GetConstructors(flags).Single();
        var dataParams=dataCtor.GetParameters();
        var results=new List<object>();int cases=0,failed=0;
        string folder=Path.Combine(Path.GetDirectoryName(Path.GetFullPath(reportPath))!,"orphan-fixtures");Directory.CreateDirectory(folder);
        File.WriteAllText(Path.Combine(folder,"Info.dat"),new JObject {["_version"]="2.1.0",["_beatsPerMinute"]=120,["audio"]=new JObject {["songDuration"]=10},
            ["_difficultyBeatmapSets"]=new JArray(new JObject {["_beatmapCharacteristicName"]="Standard",["_difficultyBeatmaps"]=new JArray(new JObject {["_difficulty"]="Expert",["_beatmapFilename"]="map.dat",["_customData"]=new JObject {["_requirements"]=new JArray("Noodle Extensions")}})})}.ToString());
        foreach(string geometry in new[]{"base","head","tail","both"})
        foreach(float duration in new[]{0f,2f})foreach(int count in new[]{1,2,4,16})foreach(bool hasHead in new[]{false,true})foreach(bool spawnFlag in new[]{false,true})
        {
            cases++;emitted.Clear();var parameters=factory.GetParameters();var values=new object?[parameters.Length];
            for(int i=0;i<values.Length;i++)
            {
                values[i]=parameters[i].ParameterType.IsValueType?Activator.CreateInstance(parameters[i].ParameterType):null;
                if(parameters[i].Name=="customData")values[i]=Activator.CreateInstance(metadataType);
                else if(parameters[i].Name=="version")values[i]=new Version(3,3,0);
                else if(parameters[i].Name=="headTime")values[i]=1f;
                else if(parameters[i].Name=="tailTime")values[i]=1f+duration;
                else if(parameters[i].Name=="headBeat")values[i]=2f;
                else if(parameters[i].Name=="headCutDirection")values[i]=Enum.ToObject(parameters[i].ParameterType,1);
                else if(parameters[i].Name=="sliceCount")values[i]=count;
                else if(parameters[i].Name=="squishAmount")values[i]=1f;
            }
            var slider=(SliderData)factory.Invoke(null,values)!;slider.SetHasHeadNote(hasHead);
            var metadata=slider.GetType().GetProperty("customData")!.GetValue(slider)!;
            if(geometry=="head"||geometry=="both")((System.Collections.IDictionary)metadata)["coordinates"]=new List<object>{-2.5f,.75f};
            if(geometry=="tail"||geometry=="both")((System.Collections.IDictionary)metadata)["tailCoordinates"]=new List<object>{3.25f,4.5f};
            var parsed=dataCtor.Invoke(new object[]{slider,metadata,120f,Activator.CreateInstance(dataParams[3].ParameterType)!,Activator.CreateInstance(dataParams[4].ParameterType)!,false,false});
            float? Read(string property)=>(float?)parsed.GetType().GetProperty(property,flags)!.GetValue(parsed);
            bool coordinates=Read("StartX")==((geometry=="head"||geometry=="both")?-2.5f:(float?)null)&&Read("TailStartY")==((geometry=="tail"||geometry=="both")?4.5f:(float?)null);
            var spawn=Activator.CreateInstance(spawnType,new object[]{new Vector3(Read("StartX")??0,Read("StartY")??0,0),0f,new Vector3(Read("TailStartX")??1,Read("TailStartY")??1,0),0f})!;
            process.Invoke(null,new[]{slider,spawn,(object)spawnFlag,movement,callback});
            var times=emitted.Select(n=>n.time).ToArray();
            bool pass=coordinates&&times.Length==count-1&&emitted.All(n=>(int)n.cutDirection==8)
                &&(count==1||Math.Abs(times[0]-(1f+duration/(count-1)))<1e-5&&Math.Abs(times[^1]-(1+duration))<1e-5);
            if(!hasHead&&duration>0&&count>1)
            {
                var fixture=new JObject {["version"]="3.3.0",["colorNotes"]=new JArray(),["burstSliders"]=new JArray(new JObject {["b"]=2,["x"]=0,["y"]=0,["c"]=0,["d"]=1,["tb"]=6,["tx"]=0,["ty"]=0,["sc"]=count,["s"]=1})};
                fixture["burstSliders"]![0]!["customData"]=JObject.FromObject(metadata);
                File.WriteAllText(Path.Combine(folder,"map.dat"),fixture.ToString());
                var own=LocalMap.Load(new MapCase {Folder=folder,File="map.dat",Characteristic="Standard",Difficulty="Expert"},AnalysisFeatures.FirstNotes|AnalysisFeatures.Average);
                pass=pass&&own.Input.Notes.Length==1&&Math.Abs(own.Input.Notes[0].Time-times[0])<1e-5&&own.Input.Notes[0].Direction==8&&own.Input.Notes[0].SliceCount==count-1
                    &&CompatibleAnalysis.Analyze(own.Input,reproduceKnownDefects:false).LeftBins.Sum()==1;
            }
            if(!pass)failed++;
            results.Add(new {geometry,duration,count,hasHead,spawnFlag,times,status=pass?"pass":"mismatch"});
        }
        return new {schemaVersion=1,profileVersion=BeatInsight.Formats.LocalMap.AnalysisProfileVersion,cases,failed,spawnFlagName=process.GetParameters()[2].Name,referenceUsed=false,gameStarted=false,
            scope="installed unpatched BurstSliderSpawner with controlled spawn data and half jump duration; callback captures generated notes; Noodle/Heck runtime transpilers and full lifecycle not executed",results};
    }
}
