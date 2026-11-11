using BeatInsight.Compare;
using BeatInsight.Core;
using BeatInsight.Formats;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System.Security.Cryptography;

try
{
    if(args.Length == 0) throw new ArgumentException("Supply gameRoot as the first argument.");
    string gameRoot = Path.GetFullPath(args[0]);
    var oracle = new OriginalOracle(gameRoot);
    if(args.Length==5&&args[1]=="--check-menu-data")
    {
        var report=MenuDataChecks.Run(oracle,gameRoot,args[2],args[3]);
        File.WriteAllText(args[4],JsonConvert.SerializeObject(report,Formatting.Indented));
        return JObject.FromObject(report)["failed"]!.Value<int>()==0?0:2;
    }
    if(args.Length==5&&args[1]=="--check-modifiers")
    {
        var report=ModifierChecks.Run(oracle,gameRoot,args[2],args[3],args[4]);
        File.WriteAllText(args[4],JsonConvert.SerializeObject(report,Formatting.Indented));
        return JObject.FromObject(report)["failed"]!.Value<int>()==0?0:2;
    }
    if(args.Length>=4&&args[1]=="--diagnose")
    {
        var diagnosticRequest=JsonConvert.DeserializeObject<MapCase>(File.ReadAllText(args[2]))!;
        var map=LocalMap.Load(diagnosticRequest);
        var game=GameLoader.Load(gameRoot,map.Json,map.Bpm,map.AudioJson);
        File.WriteAllText(args[3],JsonConvert.SerializeObject(new {input=map.Input,game=GameLoader.Describe(game)},Formatting.Indented));
        return 0;
    }
    if (args.Length > 1 && args[1] == "--self-test")
    {
        var left=new MapNote {Time=1,Color=0,X=0};var right=new MapNote {Time=1,Color=1,X=3};
        if(Comparison.InputDiff([left,right],[right,left])!=0)throw new Exception("Cross-hand tie equivalence failed");
        var otherLeft=new MapNote {Time=1,Color=0,X=2};
        if(Comparison.InputDiff([left,otherLeft],[otherLeft,left])==0)throw new Exception("Same-hand tie order must be preserved");
        if(!Comparison.SameSimultaneousNotes([left,otherLeft],[otherLeft,left]))throw new Exception("Same-time content equivalence failed");
        if(Comparison.SameSimultaneousNotes([left],[otherLeft]))throw new Exception("Different simultaneous objects must fail");
        var firstOrder=new MapInput {Duration=10,Notes=[new(){Time=1,Direction=0},new(){Time=1,Direction=1}]};
        var secondOrder=new MapInput {Duration=10,Notes=firstOrder.Notes.Reverse().ToArray()};
        if(Comparison.Diff(CompatibleAnalysis.Analyze(firstOrder),CompatibleAnalysis.Analyze(secondOrder)).Count==0)throw new Exception("Order-sensitive outputs must detect simultaneous permutation effects");
        var random = new Random(1948);
        foreach(var input in new[] {
            new MapInput {Duration=10},
            new MapInput {Duration=0},
            new MapInput {Duration=10,Notes=[new(){Time=1,Direction=8}]},
            new MapInput {Duration=10,Notes=[new(){Time=.99f},new(){Time=1.01f},new(){Time=1.03f,Color=1}]}
        })
        {
            var difference=Comparison.Diff(oracle.Analyze(input),CompatibleAnalysis.Analyze(input,history:OriginalOracle.HistoryFixture(input)));
            if(difference.Count>0)throw new Exception("Edge fixture: "+string.Join(",",difference.Keys));
        }
        foreach(int x in new[]{-2,0,1,2,3,5})foreach(int width in new[]{1,2,4})
        {
            var input=new MapInput {Duration=10,Notes=[new(){Time=1}],Walls=[new(){Time=2,Duration=5,X=x,Width=width,Height=5}]};
            var difference=Comparison.Diff(oracle.Analyze(input),CompatibleAnalysis.Analyze(input,history:OriginalOracle.HistoryFixture(input)));
            if(difference.Count>0)throw new Exception($"Wall end fixture x={x}, width={width}: "+string.Join(",",difference.Keys));
        }
        for (int i = 0; i < 1500; i++)
        {
            var input = new MapInput { Duration = 10, Notes = Enumerable.Range(0, 50).Select(_ => new MapNote { Time = random.Next(0, i%2==0?1000:100)/100f, X=random.Next(4), Y=random.Next(3), Color=random.Next(2), Direction=random.Next(9) }).OrderBy(n=>n.Time).ToArray() };
            var differences = Comparison.Diff(oracle.Analyze(input), CompatibleAnalysis.Analyze(input,history:OriginalOracle.HistoryFixture(input)));
            if (differences.Count>0) throw new Exception($"Synthetic case {i}: {string.Join(",", differences.Keys)}");
        }
        Console.WriteLine("1500 deterministic synthetic comparisons, 4 edge fixtures and 18 wall end fixtures passed");
        return 0;
    }
    if(args.Length>=3&&args[1]=="--batch")
    {
        int failed=0;
        var jobs=JsonConvert.DeserializeObject<Dictionary<string,string>[]>(File.ReadAllText(args[2]))!;
        foreach(var job in jobs){int code=RunCase(oracle,gameRoot,job["request"],job["result"]);if(code!=0&&code!=3)failed++;}
        return failed==0?0:2;
    }
    if (args.Length < 4 || args[1] != "--case") throw new ArgumentException("Usage: <gameRoot> --case <request.json> <result.json> OR --batch <jobs.json> OR --self-test");
    return RunCase(oracle,gameRoot,args[2],args[3]);
}
catch (Exception e) { Console.Error.WriteLine(e); return 5; }

static int RunCase(OriginalOracle oracle,string gameRoot,string requestPath,string resultPath)
{
    var request = JsonConvert.DeserializeObject<MapCase>(File.ReadAllText(requestPath))!;
    object record; int exitCode;
    try
    {
        var map = LocalMap.Load(request);
        var game = GameLoader.Load(gameRoot, map.Json, map.Bpm, map.AudioJson);
        var original = oracle.Analyze(map.Input, game);
        var historyFixture=OriginalOracle.HistoryFixture(map.Input);
        var own = CompatibleAnalysis.Analyze(map.Input,history:historyFixture);
        var corrected = CompatibleAnalysis.Analyze(map.Input, reproduceKnownDefects:false,history:historyFixture);
        var differences = Comparison.Diff(original, own);
        var ownPrepared=map.Input.Notes.Where(n=>n.ScoringType<=3).ToArray();
        int inputDifferences = Comparison.InputDiff(oracle.LastPrepared.Notes, ownPrepared);
        bool equivalentSimultaneousOrder=inputDifferences>0&&differences.Count==0&&Comparison.SameSimultaneousNotes(oracle.LastPrepared.Notes,ownPrepared);
        if (inputDifferences>0&&!equivalentSimultaneousOrder) differences["preparedNotes"] = new { mismatches=inputDifferences };
        var correctedDifferences = Comparison.Diff(original, corrected);
        int excluded = map.Input.Notes.Count(n=>n.ScoringType==6);
        string? artifact = null;
        if (differences.Count>0)
        {
            artifact = Path.GetFileNameWithoutExtension(resultPath)+".details.json";
            File.WriteAllText(Path.Combine(Path.GetDirectoryName(Path.GetFullPath(resultPath))!,artifact), JsonConvert.SerializeObject(new { request, input=map.Input, oracleInput=oracle.LastPrepared, original, compatible=own, corrected, differences }, Formatting.Indented));
        }
        record = new {
            schemaVersion=3,profileVersion=2, status=differences.Count==0?"pass":"mismatch", profile="vanilla-neutral", request,
            mapVersion=map.Version, features=map.Features,
            mapSha256=Comparison.Hash(map.Json), audioDataSha256=map.AudioJson==null?null:Comparison.Hash(map.AudioJson),
            duration=map.Input.Duration, originalHash=oracle.LevelHash, previewHash=oracle.PreviewHash,
            original=Comparison.Metrics(original), compatible=Comparison.Metrics(own), corrected=Comparison.Metrics(corrected),
            resultDigests=new { original=Comparison.Hash(JsonConvert.SerializeObject(original)), compatible=Comparison.Hash(JsonConvert.SerializeObject(own)) },
            differences, preparedOrder=new {equivalentSimultaneousOrder,orderedDifferences=inputDifferences}, knownDefects=excluded>0?new object[] {new {id="REF-001", excludedCombinedArcNotes=excluded}}:Array.Empty<object>(),
            correctedDifferences,
            median=new {left=WindowStatistics.ActiveWindowMedian(corrected.LeftBins),right=WindowStatistics.ActiveWindowMedian(corrected.RightBins),unit="swings/s",policy=WindowStatistics.ActiveHandMedianPolicy},
            squats=BeatInsight.WallAlert.WallAlertAdapter.Analyze(map.Json,map.Bpm),
            history=new { status="synthetic-compared",records=historyFixture.Length,selected=own.HistoryIndices.Length,source="deterministic-fixture-not-user-history",eventCapture="not-compared" },
            artifact
        };
        exitCode=differences.Count==0?0:2;
    }
    catch (NotSupportedException e) {record=new {schemaVersion=3,profileVersion=2,status="unsupported",request,error=e.Message};exitCode=3;}
    catch (Exception e) {record=new {schemaVersion=3,profileVersion=2,status="error",request,error=e.ToString()};exitCode=4;}
    File.WriteAllText(resultPath,JsonConvert.SerializeObject(record,Formatting.Indented));
    Console.WriteLine(JsonConvert.SerializeObject(new {result=Path.GetFileName(resultPath),exitCode}));
    return exitCode;
}
