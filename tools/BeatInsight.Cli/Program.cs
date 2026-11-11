using BeatInsight.Core;
using BeatInsight.Formats;
using BeatInsight.WallAlert;
using Newtonsoft.Json;

try
{
    if(args.Length<3)throw new ArgumentException("Usage: <map folder> <characteristic> <difficulty> [SongPlayData.json] [exact levelId] [BeatInsight.Deaths.json]");
    string folder=Path.GetFullPath(args[0]);
    var test=LocalMap.Cases(folder).Single(t=>t.Characteristic==args[1]&&t.Difficulty==args[2]);
    var map=LocalMap.Load(test);
    List<PlayRecord> selected=new();
    string historyStatus="not-requested";
    if(args.Length>=4)
    {
        if(!File.Exists(args[3]))historyStatus="source-missing";
        else if(args.Length<5)historyStatus="exact-levelId-required";
        else
        {
            int rank=test.Difficulty switch {"Easy"=>0,"Normal"=>1,"Hard"=>2,"Expert"=>3,"ExpertPlus"=>4,_=>throw new ArgumentException("Unknown difficulty")};
            var records=JsonConvert.DeserializeObject<Dictionary<string,List<PlayRecord>>>(File.ReadAllText(args[3]))??throw new InvalidDataException("Null history root");
            historyStatus=records.TryGetValue(PlayHistory.Key(args[4],rank,test.Characteristic),out var found)?"available":"key-absent";
            selected=(found??new()).Where(r=>r.IsPractice!=true).ToList();
        }
    }
    var compatible=CompatibleAnalysis.Analyze(map.Input,history:selected);
    var corrected=CompatibleAnalysis.Analyze(map.Input,reproduceKnownDefects:false,history:selected);
    int historyObjects=map.Input.Notes.Where(n=>n.ScoringType<=3).Sum(n=>Math.Max(1,n.SliceCount));
    var ownHistory=args.Length>=6?DeathHistoryStore.Read(args[5]):new DeathHistoryReadResult {Status="not-requested"};
    int difficultyRank=test.Difficulty switch {"Easy"=>0,"Normal"=>1,"Hard"=>2,"Expert"=>3,"ExpertPlus"=>4,_=>throw new ArgumentException("Unknown difficulty")};
    string? historyKey=args.Length>=5&&!string.IsNullOrWhiteSpace(args[4])?PlayHistory.Key(args[4],difficultyRank,test.Characteristic):null;
    string mapHash=Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(map.Json)));
    var legacyMarkers=DeathHistory.LegacyMarkers(selected,map.Input);
    var ownMarkers=historyKey==null?Array.Empty<DeathMarker>():DeathHistory.OwnMarkers(ownHistory.Records,historyKey,map.Input.Duration,mapHash);
    Console.WriteLine(JsonConvert.SerializeObject(new {
        schemaVersion=3,profile="vanilla-neutral",test.Characteristic,test.Difficulty,map.Version,map.Features,
        implementation="independent; no reference implementation DLL is loaded by this CLI",
        compatible,corrected,
        median=new {left=WindowStatistics.ActiveWindowMedian(corrected.LeftBins),right=WindowStatistics.ActiveWindowMedian(corrected.RightBins),unit="swings/s",policy=WindowStatistics.ActiveHandMedianPolicy},
        squats=WallAlertAdapter.Analyze(map.Json,map.Bpm),
        history=new {status=historyStatus,aggregates=PlayHistory.Aggregate(selected),positions=compatible.HistoryIndices.Select((index,i)=>new {index,status=index>=historyObjects?"invalid-index":"available",seconds=index>=historyObjects?(float?)null:compatible.HistorySeconds[i],normalized=index>=historyObjects?(float?)null:compatible.HistoryNormalized[i]}),policy="reference implementation selects LastNote>0, Date descending, latest 100; invalid indices explicitly flagged here; chain links repeat head time"},
        deathMarkers=new {legacyStatus=historyStatus,ownStatus=historyKey==null&&args.Length>=6?"exact-levelId-required":ownHistory.Status,markers=legacyMarkers.Concat(ownMarkers).ToArray(),policy="yellow crosses: imported history; red crosses: BeatInsight; practice excluded; modifiers preserved without implicit speed rescaling"}
    },Formatting.Indented));
    return 0;
}
catch(Exception e){Console.Error.WriteLine(e.Message);return 1;}
