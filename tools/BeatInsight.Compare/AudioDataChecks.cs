using BeatInsight.Core;
using BeatInsight.Formats;
using Newtonsoft.Json.Linq;

namespace BeatInsight.Compare;

static class AudioDataChecks
{
    public static object Run(OriginalOracle oracle,string gameRoot,string reportPath)
    {
        string fixtures=Path.Combine(Path.GetDirectoryName(Path.GetFullPath(reportPath))!,"audio-data-fixtures");Directory.CreateDirectory(fixtures);
        JObject Row(int si,int ei,float sb,float eb)=>new() {["si"]=si,["ei"]=ei,["sb"]=sb,["eb"]=eb};
        JObject Audio(params JObject[] rows)=>new() {["version"]="4.0.0",["songFrequency"]=44100,["bpmData"]=new JArray(rows)};
        JObject Map(params float[] beats)=>new() {["version"]="4.1.0",["colorNotes"]=new JArray(beats.Select((beat,i)=>new JObject {["b"]=beat,["i"]=i%2})),
            ["colorNotesData"]=new JArray(new JObject {["x"]=0,["y"]=0,["c"]=0,["d"]=1},new JObject {["x"]=3,["y"]=0,["c"]=1,["d"]=1})};
        var cases=new List<(string Name,JObject Map,JObject? Audio,bool SchemaRejected)>();
        void Add(string name,JObject audio,params float[] beats)=>cases.Add((name,Map(beats.Length>0?beats:new[]{1f,3f,5f}),audio,false));
        Add("normal",Audio(Row(0,88200,0,4)));
        Add("sample-rounding",Audio(Row(0,88095,0,1),Row(88095,125161,1,2)),.99f,1,1.01f,2,3);
        Add("sample-gap",Audio(Row(0,88200,0,4),Row(132300,220500,4,8)),3.99f,4,4.01f,8);
        Add("sample-overlap",Audio(Row(0,88200,0,4),Row(44100,132300,4,8)),3.99f,4,4.01f,8);
        Add("beat-gap",Audio(Row(0,88200,0,4),Row(88200,176400,6,10)),4,5,6,7);
        Add("beat-overlap",Audio(Row(0,88200,0,6),Row(88200,176400,4,8)),3,4,5,6);
        Add("nonzero-start",Audio(Row(88200,176400,4,8)),1,3,4,5,9);
        Add("after-final-range",Audio(Row(0,88200,0,4)),4,5,8);
        Add("repeated-start-beat",Audio(Row(0,88200,0,4),Row(88200,176400,0,4)),0,1,3,4);
        Add("zero-sample-range",Audio(Row(0,0,0,4)));
        Add("negative-sample-range",Audio(Row(88200,0,0,4)));
        Add("zero-beat-range",Audio(Row(0,88200,0,0)));
        Add("negative-beat-range",Audio(Row(0,88200,4,0)));
        Add("unused-invalid-range",Audio(Row(0,88200,0,4),Row(88200,88200,10,14)),1,2,3);
        Add("earlier-invalid-range",Audio(Row(0,0,0,4),Row(88200,176400,4,8)),5,6,7);
        Add("unsorted-starts",Audio(Row(88200,176400,4,8),Row(0,88200,0,4)),1,3,5);
        Add("empty-ranges",Audio());
        cases.Add(("empty-map-empty-ranges",Map(),Audio(),false));
        foreach(int frequency in new[]{0,-44100}){var audio=Audio(Row(0,88200,0,4));audio["songFrequency"]=frequency;Add("frequency-"+frequency,audio);}
        var missingFrequency=Audio(Row(0,88200,0,4));missingFrequency.Remove("songFrequency");cases.Add(("missing-frequency",Map(1),missingFrequency,true));
        var missingRanges=Audio();missingRanges.Remove("bpmData");cases.Add(("missing-ranges",Map(1),missingRanges,true));
        var missingSample=Audio(Row(0,88200,0,4));((JObject)missingSample["bpmData"]![0]!).Remove("si");cases.Add(("implicit-zero-sample-start",Map(1),missingSample,false));
        cases.Add(("missing-audio-file",Map(1),null,true));
        cases.Add(("missing-audio-filename",Map(1),Audio(Row(0,88200,0,4)),true));
        cases.Add(("malformed-audio-json",Map(1),Audio(),true));
        var missingData=Map(1);missingData.Remove("colorNotesData");cases.Add(("missing-note-data",missingData,Audio(Row(0,88200,0,4)),true));
        var badIndex=Map(1);badIndex["colorNotes"]![0]!["i"]=-1;cases.Add(("negative-note-index",badIndex,Audio(Row(0,88200,0,4)),true));
        var highIndex=Map(1);highIndex["colorNotes"]![0]!["i"]=99;cases.Add(("out-of-range-note-index",highIndex,Audio(Row(0,88200,0,4)),true));
        var nullData=Map(1);nullData["colorNotesData"]![0]=null;cases.Add(("null-note-data",nullData,Audio(Row(0,88200,0,4)),true));
        var implicitIndex=Map(1);((JObject)implicitIndex["colorNotes"]![0]!).Remove("i");cases.Add(("implicit-zero-note-index",implicitIndex,Audio(Row(0,88200,0,4)),false));
        var missingArcData=Map(1,3);missingArcData["arcs"]=new JArray(new JObject {["hb"]=1,["tb"]=3,["hi"]=0,["ti"]=1,["ai"]=0});cases.Add(("missing-arc-data",missingArcData,Audio(Row(0,88200,0,4)),true));
        var missingChainData=Map(1);missingChainData["chains"]=new JArray(new JObject {["hb"]=1,["tb"]=2,["i"]=0,["ci"]=0});cases.Add(("missing-chain-data",missingChainData,Audio(Row(0,88200,0,4)),true));
        var missingBombData=Map(1);missingBombData["bombNotes"]=new JArray(new JObject {["b"]=2,["i"]=0});cases.Add(("missing-bomb-data",missingBombData,Audio(Row(0,88200,0,4)),true));
        var missingWallData=Map(1);missingWallData["obstacles"]=new JArray(new JObject {["b"]=2,["i"]=0});cases.Add(("missing-wall-data",missingWallData,Audio(Row(0,88200,0,4)),true));
        var invalidCollection=Map();invalidCollection["colorNotes"]="invalid";cases.Add(("invalid-collection-type",invalidCollection,Audio(Row(0,88200,0,4)),true));
        Add("unused-earlier-zero-beat-span",Audio(Row(0,88200,0,0),Row(88200,176400,4,8)),5,6,7);
        var oversizedFrequency=Audio(Row(0,88200,0,4));oversizedFrequency["songFrequency"]=(long)int.MaxValue+1;cases.Add(("oversized-frequency",Map(1),oversizedFrequency,true));
        var oversizedSample=Audio(Row(0,88200,0,4));oversizedSample["bpmData"]![0]!["ei"]=(long)int.MaxValue+1;cases.Add(("oversized-sample",Map(1),oversizedSample,true));
        var invalidBeat=Audio(Row(0,88200,0,4));invalidBeat["bpmData"]![0]!["sb"]="invalid";cases.Add(("invalid-beat-value",Map(1),invalidBeat,true));
        var invalidRow=Audio();invalidRow["bpmData"]=new JArray(42);cases.Add(("invalid-range-type",Map(1),invalidRow,true));
        var results=new List<object>();int failed=0,rejected=0;
        foreach(var fixture in cases)
        {
            string folder=Path.Combine(fixtures,fixture.Name);Directory.CreateDirectory(folder);
            var info=new JObject {["version"]="4.0.1",["audio"]=new JObject {["bpm"]=120,["songDuration"]=100,["audioDataFilename"]="AudioData.dat"},
                ["difficultyBeatmaps"]=new JArray(new JObject {["characteristic"]="Standard",["difficulty"]="Expert",["beatmapDataFilename"]="map.dat"})};
            if(fixture.Name=="missing-audio-filename")((JObject)info["audio"]!).Remove("audioDataFilename");
            File.WriteAllText(Path.Combine(folder,"Info.dat"),info.ToString());File.WriteAllText(Path.Combine(folder,"map.dat"),fixture.Map.ToString());
            string? audioJson=fixture.Name=="malformed-audio-json"?"{":fixture.Audio?.ToString();
            if(audioJson!=null)File.WriteAllText(Path.Combine(folder,"AudioData.dat"),audioJson);
            object? game=null;string? nativeError=null;bool invalid=false;
            try{game=GameLoader.Load(gameRoot,fixture.Map.ToString(),120,audioJson);invalid=OracleBridge.GetItems(game).OfType<NoteData>().Any(n=>!float.IsFinite(n.time)||n.time<0);}
            catch(Exception e){nativeError=(e.InnerException??e).GetType().Name;}
            var differences=new Dictionary<string,object>();string status="pass";
            try
            {
                var map=LocalMap.Load(new MapCase {Folder=folder,Characteristic="Standard",Difficulty="Expert",File="map.dat"});
                if(fixture.SchemaRejected)differences["missing-data-accepted"]="Mandatory data was silently defaulted";
                else if(nativeError!=null||invalid)differences["unsafe-acceptance"]="No usable native gameplay timeline";
                else
                {
                    var expected=oracle.Analyze(map.Input,game);
                    var actual=CompatibleAnalysis.Analyze(map.Input,history:OriginalOracle.HistoryFixture(map.Input));
                    foreach(var difference in Comparison.Diff(expected,actual))differences[difference.Key]=difference.Value;
                    if(Comparison.InputDiff(oracle.LastPrepared.Notes,map.Input.Notes.Where(n=>n.ScoringType<=3).ToArray())!=0)differences["prepared-notes"]="Native note inputs differ";
                }
            }
            catch(NotSupportedException e){status="unsupported";rejected++;if(!fixture.SchemaRejected&&nativeError==null&&!invalid)differences["unexpected-rejection"]=e.Message;}
            catch(Exception e){differences["error"]=(e.InnerException??e).Message;}
            if(differences.Count>0){failed++;status="mismatch";}
            results.Add(new {fixture.Name,status,nativeError,invalid,fixture.SchemaRejected,differences});
        }
        return new {schemaVersion=1,profileVersion=BeatInsight.Formats.LocalMap.AnalysisProfileVersion,cases=cases.Count,failed,rejected,gameStarted=false,userHistoryRead=false,
            scope="v4 sample-anchored timing and referenced gameplay data; malformed mandatory metadata is explicitly unsupported",results};
    }
}
