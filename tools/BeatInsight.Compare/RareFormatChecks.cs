using BeatInsight.Core;
using BeatInsight.Formats;
using Newtonsoft.Json.Linq;

namespace BeatInsight.Compare;

static class RareFormatChecks
{
    public static object Run(OriginalOracle oracle,string gameRoot,string reportPath,string? libraryRequests=null)
    {
        string fixtures=Path.Combine(Path.GetDirectoryName(Path.GetFullPath(reportPath))!,"rare-format-fixtures");
        Directory.CreateDirectory(fixtures);
        var cases=new List<(string Name,JObject Json,float Bpm)>();
        JObject Note(float beat,int x,int color=0)=>new() {["_time"]=beat,["_lineIndex"]=x,["_lineLayer"]=0,["_type"]=color,["_cutDirection"]=1};
        JObject Arc(float head,float tail,int hx,int tx)=>new() {["_headTime"]=head,["_tailTime"]=tail,["_headLineIndex"]=hx,["_tailLineIndex"]=tx,
            ["_headLineLayer"]=0,["_tailLineLayer"]=0,["_colorType"]=0,["_headCutDirection"]=1,["_tailCutDirection"]=1};
        JObject Map(string version)=>new() {["_version"]=version,["_notes"]=new JArray(Note(2,0),Note(4,1),Note(8,3,1)),["_events"]=new JArray(),["_sliders"]=new JArray()};
        foreach(string version in new[]{"2.0.0","2.5.0","2.6.0"})
        {
            var arc=Map(version);arc["_sliders"]=new JArray(Arc(2,4,0,1));cases.Add(("arc-"+version,arc,120));
            var joined=Map(version);joined["_sliders"]=new JArray(Arc(2,4,0,1),Arc(4,8,1,3));cases.Add(("joined-arcs-"+version,joined,120));
            var unsorted=Map(version);unsorted["_sliders"]=new JArray(Arc(4,8,1,3),Arc(2,4,0,1));cases.Add(("unsorted-arcs-"+version,unsorted,120));
            var bomb=Map(version);bomb["_notes"]![1]!["_type"]=3;bomb["_sliders"]=new JArray(Arc(2,4,0,1));cases.Add(("bomb-tail-"+version,bomb,120));
            var missing=Map(version);missing["_sliders"]=new JArray(Arc(1,3,2,2));cases.Add(("missing-arc-notes-"+version,missing,120));
            var bpmArc=Map(version);bpmArc["_sliders"]=new JArray(Arc(2,4,0,1));
            bpmArc["_events"]=new JArray(new JObject {["_time"]=3,["_type"]=100,["_value"]=120,["_floatValue"]=240});cases.Add(("arc-bpm-boundary-"+version,bpmArc,120));
            var duplicate=Map(version);duplicate["_events"]=new JArray(
                new JObject {["_time"]=2,["_type"]=100,["_value"]=0,["_floatValue"]=60},
                new JObject {["_time"]=2,["_type"]=100,["_value"]=0,["_floatValue"]=240});cases.Add(("same-beat-bpm-"+version,duplicate,120));
            var unsortedBpm=Map(version);unsortedBpm["_events"]=new JArray(
                new JObject {["_time"]=4,["_type"]=100,["_value"]=0,["_floatValue"]=180},
                new JObject {["_time"]=2,["_type"]=100,["_value"]=0,["_floatValue"]=60});cases.Add(("unsorted-bpm-"+version,unsortedBpm,120));
        }
        foreach(string version in new[]{"2.0.0","2.4.9","2.5.0","2.6.0"})
        foreach(int type in new[]{10,100})
        foreach(var values in new[]{(0,180f),(180,90f),(0,0f),(-1,120f)})
        {
            var map=Map(version);
            map["_events"]=new JArray(new JObject {["_time"]=2,["_type"]=type,["_value"]=values.Item1,["_floatValue"]=values.Item2});
            cases.Add(($"bpm-{version}-{type}-{values.Item1}-{values.Item2}",map,120));
        }
        var noVersion=Map("2.0.0");noVersion.Remove("_version");noVersion["_sliders"]=new JArray(Arc(2,4,0,1));cases.Add(("missing-version",noVersion,120));
        cases.Add(("unknown-major",Map("5.0.0"),120));
        cases.Add(("malformed-version",Map("invalid"),120));
        cases.Add(("zero-initial-bpm",Map("2.6.0"),0));
        var emptyLighting=Map("2.2.0");emptyLighting["_notes"]=new JArray();
        emptyLighting["_events"]=new JArray(new JObject {["_time"]=2,["_type"]=10,["_value"]=0,["_floatValue"]=0});cases.Add(("empty-gameplay-invalid-lighting",emptyLighting,120));
        var late=Map("2.6.0");late["_events"]=new JArray(new JObject {["_time"]=20,["_type"]=100,["_value"]=0,["_floatValue"]=0});cases.Add(("unused-late-invalid-bpm",late,120));
        var badWall=(JObject)emptyLighting.DeepClone();badWall["_obstacles"]=new JArray(new JObject {["_time"]=1,["_duration"]=7,["_lineIndex"]=1,["_width"]=2,["_type"]=0});cases.Add(("wall-crossing-invalid-bpm",badWall,120));
        int libraryCases=0;
        if(libraryRequests!=null)
            foreach(var request in JArray.Parse(File.ReadAllText(libraryRequests)))
            {
                string folder=(string)request["Folder"]!;
                var info=LocalMap.Info(folder);
                float bpm=(float?)(info["_beatsPerMinute"]??info["audio"]?["bpm"])??0;
                var json=JObject.Parse(File.ReadAllText(LocalMap.SafePath(folder,(string)request["File"]!)));
                cases.Add(("rejected-library-"+libraryCases++,json,bpm));
            }
        var results=new List<object>();int failed=0,rejected=0;
        foreach(var fixture in cases)
        {
            string folder=Path.Combine(fixtures,fixture.Name);Directory.CreateDirectory(folder);
            File.WriteAllText(Path.Combine(folder,"Info.dat"),new JObject {["_version"]="2.1.0",["_beatsPerMinute"]=fixture.Bpm,
                ["audio"]=new JObject {["songDuration"]=300},["_difficultyBeatmapSets"]=new JArray(new JObject {["_beatmapCharacteristicName"]="Standard",
                    ["_difficultyBeatmaps"]=new JArray(new JObject {["_difficulty"]="Expert",["_beatmapFilename"]="map.dat"})})}.ToString());
            string json=fixture.Json.ToString();File.WriteAllText(Path.Combine(folder,"map.dat"),json);
            object? game=null;string? nativeError=null;
            bool nativeInvalid=false;
            try
            {
                game=GameLoader.Load(gameRoot,json,fixture.Bpm);
                nativeInvalid=OracleBridge.GetItems(game).Any(item=>item switch {
                    NoteData n=>!float.IsFinite(n.time)||n.time<0,
                    SliderData s=>!float.IsFinite(s.time)||!float.IsFinite(s.tailTime)||s.time<0||s.tailTime<s.time,
                    ObstacleData w=>!float.IsFinite(w.time)||!float.IsFinite(w.duration)||w.duration<0,
                    _=>false});
            }
            catch(Exception e){nativeError=(e.InnerException??e).GetType().Name;}
            var differences=new Dictionary<string,object>();string status="pass";
            try
            {
                var map=LocalMap.Load(new MapCase {Folder=folder,Characteristic="Standard",Difficulty="Expert",File="map.dat"});
                if(nativeError!=null||nativeInvalid)differences["unsafe-acceptance"]="Our loader accepted an input without a usable native timeline";
                else
                {
                    var nativeAnnotations=OracleBridge.GetItems(game!).OfType<NoteData>().Where(n=>(int)n.scoringType>0)
                        .Select(n=>(Time:Math.Round(n.time,5),X:n.lineIndex,Y:(int)n.noteLineLayer,Color:(int)n.colorType,Score:(int)n.scoringType))
                        .OrderBy(n=>n.Time).ThenBy(n=>n.X).ThenBy(n=>n.Y).ThenBy(n=>n.Color).ThenBy(n=>n.Score).ToArray();
                    var expected=oracle.Analyze(map.Input,game);
                    var actual=CompatibleAnalysis.Analyze(map.Input,history:OriginalOracle.HistoryFixture(map.Input));
                    foreach(var d in Comparison.Diff(expected,actual))differences[d.Key]=d.Value;
                    var notes=map.Input.Notes.Where(n=>n.ScoringType<=3).ToArray();
                    int inputDiff=Comparison.InputDiff(oracle.LastPrepared.Notes,notes);
                    if(inputDiff>0&&!(differences.Count==0&&Comparison.SameSimultaneousNotes(oracle.LastPrepared.Notes,notes)))differences["prepared-notes"]=inputDiff;
                    var ourAnnotations=map.Input.Notes.Select(n=>(Time:Math.Round(n.Time,5),n.X,n.Y,n.Color,Score:n.ScoringType))
                        .OrderBy(n=>n.Time).ThenBy(n=>n.X).ThenBy(n=>n.Y).ThenBy(n=>n.Color).ThenBy(n=>n.Score).ToArray();
                    if(!nativeAnnotations.SequenceEqual(ourAnnotations))differences["arc-annotations"]="Head/tail scoring annotations differ from native notes";
                }
            }
            catch(NotSupportedException e)
            {
                status="unsupported";rejected++;
                if(nativeError==null&&!nativeInvalid)differences["unexpected-rejection"]=e.Message;
            }
            catch(Exception e){differences["error"]=(e.InnerException??e).Message;}
            if(differences.Count>0){status="mismatch";failed++;}
            results.Add(new {fixture.Name,status,nativeInvalid,nativeError,differences});
        }
        return new {schemaVersion=1,profileVersion=4,cases=cases.Count,libraryCases,failed,rejected,gameStarted=false,userHistoryRead=false,
            scope="synthetic v2 sliders, version gates and legacy BPM preprocessing against installed loader and reference IL",results};
    }
}
