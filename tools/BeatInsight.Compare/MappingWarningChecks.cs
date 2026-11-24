using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.Loader;
using BeatInsight.Formats;
using BeatInsight.WallAlert;
using Newtonsoft.Json.Linq;

namespace BeatInsight.Compare;

static class MappingWarningChecks
{
    public static object Run(string gameRoot,string reportPath,string? requestsPath=null)
    {
        string folder=Path.Combine(Path.GetDirectoryName(Path.GetFullPath(reportPath))!,"mapping-warning-fixture");Directory.CreateDirectory(folder);
        File.WriteAllText(Path.Combine(folder,"Info.dat"),"""{"_version":"2.1.0","_beatsPerMinute":120,"audio":{"songDuration":30},"_difficultyBeatmapSets":[{"_beatmapCharacteristicName":"Standard","_difficultyBeatmaps":[{"_difficulty":"Expert","_beatmapFilename":"map.dat","_customData":{"_requirements":["Mapping Extensions"]}}]}]}""");
        JObject Wall(float beat,int x,int width,int layer=3000)=>new() {["b"]=beat,["x"]=x,["y"]=layer,["w"]=width,["h"]=2000,["d"]=1};
        File.WriteAllText(Path.Combine(folder,"map.dat"),new JObject {["version"]="3.3.0",["colorNotes"]=new JArray(new JObject {["b"]=1,["x"]=0,["y"]=0,["c"]=0,["d"]=1}),
            ["obstacles"]=new JArray(Wall(2,2000,1500),Wall(2,2500,2500),Wall(2.25f,2000,3000),Wall(5,2000,1499),Wall(5,2500,2500),Wall(8,2000,3000),Wall(11,2000,3000,1000),Wall(12,2000,3000,5000))}.ToString());
        var requests=new List<MapCase> {new() {Folder=folder,Characteristic="Standard",Difficulty="Expert",File="map.dat"}};
        if(requestsPath!=null)requests.AddRange(JArray.Parse(File.ReadAllText(requestsPath)).Select(t=>t.ToObject<MapCase>()!));
        string managed=Path.Combine(gameRoot,"Beat Saber_Data/Managed");
        if(Comparison.HashFile(Path.Combine(managed,"GameplayCore.dll"))!="C45267116BB2AE61104E994AC24E9A86378055A3FEDB62B6872EEE51C2FA8C7C")throw new InvalidDataException("Reinspect changed gameplay transform");
        var core=AssemblyLoadContext.Default.LoadFromAssemblyPath(Path.Combine(managed,"GameplayCore.dll"));
        var models=AssemblyLoadContext.Default.Assemblies.Single(a=>a.GetName().Name=="DataModels");
        var modifiersType=core.GetType("GameplayModifiers",true)!;
        var speedType=modifiersType.GetNestedType("SongSpeed")!;var obstaclesType=modifiersType.GetNestedType("EnabledObstacleType")!;
        var transform=models.GetType("BeatmapDataTransformHelper",true)!.GetMethod("CreateTransformedBeatmapData")!;
        var level=RuntimeHelpers.GetUninitializedObject(models.GetType("BeatmapLevel",true)!);level.GetType().GetField("levelID")!.SetValue(level,"custom_level_warning_fixture");
        var results=new List<object>();int failed=0,libraryCases=0,matrixCases=0;long maxSignalBytes=0;
        foreach(var request in requests)
        {
            var map=LocalMap.Load(request);var raw=GameLoader.Load(gameRoot,map.Json,map.Bpm,map.AudioJson,mappingExtensions:true);
            var obstacles=OracleBridge.GetItems(raw).OfType<ObstacleData>().ToArray();
            var snapshot=obstacles.Select(Snapshot).ToArray();var differences=new Dictionary<string,object>();
            int[] schedule=MappingSquatSchedule.Build(snapshot);
            if(schedule.Length!=WallAlertAdapter.Count(map.Json,map.Bpm,true))differences["menu"]="Loaded obstacle schedule differs from menu count";
            if(request==requests[0]&&schedule.Length!=2)differences["fixture"]="Expected two synthetic warning groups";
            long allocated=Play(obstacles,schedule,out bool replayed);maxSignalBytes=Math.Max(maxSignalBytes,allocated);
            if(allocated!=0||!replayed)differences["playback"]=new {allocated,replayed};
            if(differences.Count>0)failed++;
            results.Add(new {name=Path.GetFileName(request.Folder),signals=schedule.Length,differences});
            if(request!=requests[0]){libraryCases++;continue;}
            foreach(string speed in new[]{"Normal","Faster","Slower","SuperFast"})foreach(string obstacleMode in Enum.GetNames(obstaclesType))
            {
                var modifiers=Activator.CreateInstance(modifiersType)!;
                modifiersType.GetField("_songSpeed",BindingFlags.Instance|BindingFlags.NonPublic)!.SetValue(modifiers,Enum.Parse(speedType,speed));
                modifiersType.GetField("_enabledObstacleType",BindingFlags.Instance|BindingFlags.NonPublic)!.SetValue(modifiers,Enum.Parse(obstaclesType,obstacleMode));
                var parameters=transform.GetParameters();var args=new object?[parameters.Length];args[0]=raw;args[1]=level;args[2]=modifiers;args[3]=false;
                for(int i=4;i<args.Length;i++){var type=parameters[i].ParameterType;if(type.IsByRef)type=type.GetElementType()!;args[i]=type.IsValueType?Activator.CreateInstance(type):null;}
                var data=transform.Invoke(null,args)!;var native=OracleBridge.GetItems(data).OfType<ObstacleData>().ToArray();var projected=native.Select(Snapshot).ToArray();
                int[] plan=MappingSquatSchedule.Build(projected);var errors=new List<string>();
                if(native.Length==0&&plan.Length!=0)errors.Add("Removed obstacles produced signals");
                if(Convert.ToInt32(Enum.Parse(obstaclesType,obstacleMode))==0&&plan.Length!=schedule.Length)errors.Add("Speed preset changed prepared warning groups");
                if(Convert.ToInt32(Enum.Parse(obstaclesType,obstacleMode))==2&&(native.Length!=0||plan.Length!=0))errors.Add("No-obstacles mode retained warnings");
                foreach(var obstacle in native)if(!obstacles.Any(w=>w.time==obstacle.time&&w.beat==obstacle.beat))errors.Add("Speed preset changed source obstacle time");
                foreach(float filter in new[]{0f,1f,3f,30f})
                    if(MappingSquatSchedule.Build(projected,filter).Any(i=>native[i].time<filter))errors.Add("Initial callback filter ignored");
                long bytes=Play(native,plan,out bool replay);maxSignalBytes=Math.Max(maxSignalBytes,bytes);
                if(bytes!=0||!replay)errors.Add("Playback allocated or did not reset");
                if(errors.Count>0)failed++;matrixCases++;
                results.Add(new {speed,obstacleMode,obstacles=native.Length,signals=plan.Length,errors});
            }
        }
        return new {schemaVersion=1,profileVersion=BeatInsight.Formats.LocalMap.AnalysisProfileVersion,libraryCases,matrixCases,failed,maxSignalBytes,gameStarted=false,userHistoryRead=false,
            scope="prepared warning schedules from loaded obstacles and installed gameplay transforms; native callback lifecycle and first render not executed",results};
    }
    static SquatWallData Snapshot(ObstacleData w)=>new(w.time,w.beat,w.endBeat,w.lineIndex,(int)w.lineLayer,w.width,w.height);
    static long Play(ObstacleData[] obstacles,int[] schedule,out bool replayed)
    {
        var triggers=new Dictionary<ObstacleData,int>();for(int i=0;i<schedule.Length;i++)triggers.Add(obstacles[schedule[i]],i);
        var playback=new SquatWarningPlayback(schedule.Length);
        int Run(){int count=0;foreach(var obstacle in obstacles)if(triggers.TryGetValue(obstacle,out int index)&&playback.Observe(index))count++;return count;}
        for(int i=0;i<100;i++){playback.Reset();Run();}
        playback.Reset();long before=GC.GetAllocatedBytesForCurrentThread();int first=Run(),duplicate=Run();playback.Reset();int repeated=Run();
        long allocated=GC.GetAllocatedBytesForCurrentThread()-before;replayed=first==schedule.Length&&duplicate==0&&repeated==schedule.Length;
        triggers.Clear();return allocated;
    }
}
