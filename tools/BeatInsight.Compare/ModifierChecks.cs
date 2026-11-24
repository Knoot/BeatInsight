using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.Loader;
using BeatInsight.Core;
using BeatInsight.Formats;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace BeatInsight.Compare;

// Research harness: invokes the installed gameplay transform, not Unity gameplay.
// Does not change the default vanilla-neutral profile or write user history.
static class ModifierChecks
{
    const BindingFlags Fields=BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic;
    public static object Run(OriginalOracle oracle,string gameRoot,string mapsRoot,string manifest,string reportPath)
    {
        string managed=Path.Combine(gameRoot,"Beat Saber_Data/Managed");
        var core=AssemblyLoadContext.Default.LoadFromAssemblyPath(Path.GetFullPath(Path.Combine(managed,"GameplayCore.dll")));
        string coreHash=Comparison.HashFile(Path.Combine(managed,"GameplayCore.dll"));
        if(coreHash!="C45267116BB2AE61104E994AC24E9A86378055A3FEDB62B6872EEE51C2FA8C7C")throw new InvalidDataException("GameplayCore changed; reinspect modifier semantics.");
        var models=AssemblyLoadContext.Default.LoadFromAssemblyPath(Path.GetFullPath(Path.Combine(managed,"DataModels.dll")));
        var modifiersType=core.GetType("GameplayModifiers",true)!;
        var speedType=modifiersType.GetNestedType("SongSpeed")!;
        var obstacleType=modifiersType.GetNestedType("EnabledObstacleType")!;
        var transform=models.GetType("BeatmapDataTransformHelper",true)!.GetMethod("CreateTransformedBeatmapData")!;
        var level=RuntimeHelpers.GetUninitializedObject(models.GetType("BeatmapLevel",true)!);
        level.GetType().GetField("levelID")!.SetValue(level,"custom_level_modifier_fixture");
        var cases=new List<(string Name,LoadedMap Map)>();
        foreach(var selected in JObject.Parse(File.ReadAllText(manifest))["maps"]!)
        {
            string folder=Path.Combine(mapsRoot,(string)selected["Folder"]!);
            var request=LocalMap.Cases(folder).Single(c=>c.Characteristic==(string?)selected["Characteristic"]&&c.Difficulty==(string?)selected["Difficulty"]&&
                (selected["File"]==null||c.File==(string?)selected["File"]));
            cases.Add((Path.GetFileName(folder)+"/"+request.Characteristic+"/"+request.Difficulty,LocalMap.Load(request)));
        }
        string fixtureFolder=Path.Combine(Path.GetDirectoryName(Path.GetFullPath(reportPath))!,"fixtures");
        Directory.CreateDirectory(fixtureFolder);
        File.WriteAllText(Path.Combine(fixtureFolder,"Info.dat"),"""{"_version":"2.1.0","_beatsPerMinute":120,"audio":{"songDuration":10},"_difficultyBeatmapSets":[{"_beatmapCharacteristicName":"Standard","_difficultyBeatmaps":[{"_difficulty":"Expert","_beatmapFilename":"map.dat"}]}]}""");
        File.WriteAllText(Path.Combine(fixtureFolder,"map.dat"),"""{"version":"3.3.0","colorNotes":[{"b":1,"x":0,"y":0,"c":0,"d":0},{"b":2,"x":3,"y":1,"c":1,"d":1},{"b":4,"x":1,"y":0,"c":0,"d":1}],"bombNotes":[{"b":2,"x":2,"y":0},{"b":3,"x":0,"y":2}],"sliders":[{"b":1,"x":0,"y":0,"c":0,"tb":3,"tx":0,"ty":2}],"burstSliders":[{"b":4,"x":1,"y":0,"c":0,"d":1,"tb":4.5,"tx":1,"ty":1,"sc":4,"s":1}],"obstacles":[{"b":3,"x":0,"y":0,"d":1,"w":1,"h":5},{"b":5,"x":1,"y":2,"d":1,"w":2,"h":3},{"b":8,"x":2,"y":1,"d":1,"w":1,"h":4}]}""");
        cases.Add(("synthetic-bombs-arc-chain-wall-heights",LocalMap.Load(LocalMap.Cases(fixtureFolder).Single())));
        var neutralRaw=cases.ToDictionary(c=>c.Name,c=>Comparison.Hash(JsonConvert.SerializeObject(GameLoader.Describe(GameLoader.Load(gameRoot,c.Map.Json,c.Map.Bpm,c.Map.AudioJson,mappingExtensions:c.Map.MappingExtensions)))));
        var results=new List<object>();int failed=0;
        foreach(var test in cases)
        foreach(string speed in new[]{"Normal","Faster","Slower","SuperFast"})
        foreach(string obstacles in Enum.GetNames(obstacleType))
        foreach(bool noBombs in new[]{false,true})
        {
            var modifiers=Activator.CreateInstance(modifiersType)!;
            modifiersType.GetField("_songSpeed",Fields)!.SetValue(modifiers,Enum.Parse(speedType,speed));
            modifiersType.GetField("_enabledObstacleType",Fields)!.SetValue(modifiers,Enum.Parse(obstacleType,obstacles));
            modifiersType.GetField("_noBombs",Fields)!.SetValue(modifiers,noBombs);
            float speedMultiplier=(float)modifiersType.GetProperty("songSpeedMul")!.GetValue(modifiers)!;
            float expectedSpeed=speed switch {"Normal"=>1f,"Faster"=>1.2f,"Slower"=>.85f,"SuperFast"=>1.5f,_=>throw new Exception("Unknown speed")};
            if(speedMultiplier!=expectedSpeed)throw new Exception("Speed mapping changed");
            var raw=GameLoader.Load(gameRoot,test.Map.Json,test.Map.Bpm,test.Map.AudioJson,modifiers,test.Map.MappingExtensions);
            bool rawLoaderUnchanged=neutralRaw[test.Name]==Comparison.Hash(JsonConvert.SerializeObject(GameLoader.Describe(raw)));
            var parameters=transform.GetParameters();var args=new object?[parameters.Length];
            args[0]=raw;args[1]=level;args[2]=modifiers;args[3]=false;
            for(int i=4;i<args.Length;i++)
            {
                var type=parameters[i].ParameterType;
                if(type.IsByRef)type=type.GetElementType()!;
                args[i]=type.IsValueType?Activator.CreateInstance(type):null;
            }
            var transformed=transform.Invoke(null,args)!;
            // Independently specified projection; speed intentionally leaves song time unchanged.
            int obstacleValue=Convert.ToInt32(Enum.Parse(obstacleType,obstacles));
            var ownInput=new MapInput {Duration=test.Map.Input.Duration,
                Notes=test.Map.Input.Notes.Where(n=>!noBombs||n.Color!=-1).ToArray(),
                Walls=test.Map.Input.Walls.Where(w=>obstacleValue==0||obstacleValue==1&&w.Height==3).ToArray()};
            var history=OriginalOracle.HistoryFixture(ownInput);
            var original=oracle.Analyze(ownInput,transformed);
            var compatible=CompatibleAnalysis.Analyze(ownInput,history:history);
            var differences=Comparison.Diff(original,compatible);
            if(!rawLoaderUnchanged)differences["rawLoader"]=new {reason="Raw object loading changed under selected modifiers"};
            var ownNotes=ownInput.Notes.Where(n=>n.ScoringType<=3).ToArray();
            int ordered=Comparison.InputDiff(oracle.LastPrepared.Notes,ownNotes);
            bool equivalent=ordered>0&&differences.Count==0&&Comparison.SameSimultaneousNotes(oracle.LastPrepared.Notes,ownNotes);
            if(ordered>0&&!equivalent)differences["preparedNotes"]=new {mismatches=ordered};
            var items=OracleBridge.GetItems(transformed);
            int bombs=items.OfType<NoteData>().Count(n=>(int)n.colorType==-1);
            var expectedTimes=OracleBridge.GetItems(raw).OfType<NoteData>().Where(n=>!noBombs||(int)n.colorType!=-1).Select(n=>n.time).ToArray();
            var actualTimes=items.OfType<NoteData>().Select(n=>n.time).ToArray();
            if(!expectedTimes.SequenceEqual(actualTimes))differences["objectTimes"]=new {reason="Gameplay filter changed note times or order"};
            var expectedWalls=ownInput.Walls.Select(w=>new {time=w.Time,duration=w.Duration,x=w.X,y=w.Y,width=w.Width,height=w.Height}).ToArray();
            // Match the analysis contract: original analysis and LocalMap both
            // discard non-positive walls; the gameplay copy itself can retain them.
            var actualWalls=items.OfType<ObstacleData>().Where(w=>w.width>0&&w.duration>0).Select(w=>new {time=w.time,duration=w.duration,x=w.lineIndex,y=w.lineLayer,width=w.width,height=w.height}).ToArray();
            if(JsonConvert.SerializeObject(expectedWalls)!=JsonConvert.SerializeObject(actualWalls))differences["walls"]=new {expectedWalls,actualWalls};
            if(differences.Count>0)failed++;
            results.Add(new {map=test.Name,speed,speedMultiplier,obstacles,noBombs,rawLoaderUnchanged,status=differences.Count==0?"pass":"mismatch",bombs,
                walls=items.OfType<ObstacleData>().Select(w=>new {w.time,w.duration,w.height}).ToArray(),
                preparedNotes=oracle.LastPrepared.Notes.Length,equivalentSimultaneousOrder=equivalent,
                metrics=Comparison.Metrics(original),historySelected=original.HistoryIndices.Length,differences});
        }
        var dependencies=new[]{"Beat Saber_Data/Managed/BeatmapCore.dll","Beat Saber_Data/Managed/DataModels.dll","Beat Saber_Data/Managed/GameplayCore.dll","Beat Saber_Data/Managed/Main.dll",Path.GetRelativePath(gameRoot,ReferenceFiles.Get("Utility",gameRoot))}
            .Select(file=>new {file,sha256=Comparison.HashFile(Path.Combine(gameRoot,file))}).ToArray();
        return new {schemaVersion=1,profile="modifier-research-song-timeline",gameplayCoreHash=coreHash,dependencies,
            maps=cases.Count,cases=results.Count,failed,liveGameplayTested=false,history="synthetic; event capture not tested",results};
    }
}
