using BeatInsight.Core;
using BeatInsight.Formats;
using BeatInsight.WallAlert;
using Newtonsoft.Json.Linq;

int checks = 0;
void Equal(double actual, double expected)
{
    if (actual != expected) throw new Exception($"Expected {expected}, got {actual}");
    checks++;
}
Equal(WindowStatistics.Median([]), 0);
Equal(WindowStatistics.Median([0, 0, 0]), 0);
Equal(WindowStatistics.Median([2, 2, 2, 2]), 2);
Equal(WindowStatistics.Median([0, 0, 20, 0, 0]), 0);
Equal(WindowStatistics.Median([1, 4, 2]), 2);
Equal(WindowStatistics.Median([1, 4, 2, 3]), 2.5);
Equal(WindowStatistics.ActiveWindowMedian([]),0);
Equal(WindowStatistics.ActiveWindowMedian([0,0,0]),0);
Equal(WindowStatistics.ActiveWindowMedian([0,0,20,0,0]),20);
Equal(WindowStatistics.ActiveWindowMedian([0,2,0,3,0,4,0]),3);
Equal(WindowStatistics.ActiveWindowMedian([0,1,0,4,0,2,0,3,0]),2.5);
Equal(WindowStatistics.ActiveWindowMedian([0,2,2,2,0]),2);
var partialWindows=CompatibleAnalysis.Analyze(new MapInput {Duration=100,Notes=[
    new(){Time=10.95f},new(){Time=11.05f},new(){Time=11.15f},new(){Time=11.25f},
    new(){Time=20.1f,Color=1},new(){Time=22.1f,Color=1},new(){Time=22.2f,Color=1},
    new(){Time=22.3f,Color=1},new(){Time=22.4f,Color=1},new(){Time=22.5f,Color=1}
],Walls=[new(){Time=90,Duration=9,X=1,Width=1}]},reproduceKnownDefects:false);
Equal(WindowStatistics.ActiveWindowMedian(partialWindows.LeftBins),2); // [1,3], partial seconds have equal weight.
Equal(WindowStatistics.ActiveWindowMedian(partialWindows.RightBins),3); // [1,5], independent hand bounds and skipped pause.
Equal(WindowStatistics.ActiveWindowAverage([0, 2, 0, 4]), 3);
var marks = PlayHistory.Aggregate([new() { LastNote = -1 }, new() { LastNote = 3 }, new() { LastNote = 3, Param = 2 }]);
Equal(marks.Length, 1);
Equal(marks[0].Attempts, 2);
Equal(marks[0].ModifierMasks.Length, 2);
if (PlayHistory.Key("synthetic", 3, "Standard") != "synthetic___3___Standard") throw new Exception("Key mismatch");
checks++;
void Check(bool condition,string name) {if(!condition)throw new Exception(name);checks++;}
var map=new MapInput {Duration=100,Notes=[new(){Time=10},new(){Time=20,SliceCount=3}]};
var legacy=DeathHistory.LegacyMarkers([new(){LastNote=1},new(){LastNote=2,Param=256|16|(1<<25)},new(){LastNote=99},new(){LastNote=-1},new(){LastNote=1,IsPractice=true}],map);
Equal(legacy.Length,3);
Check(legacy[0].Source=="legacy"&&legacy[0].ColorHex=="#FFFF00"&&legacy[0].Shape=="cross","Legacy marker style");
Check(legacy[0].Modifiers.LegacyParam==null&&legacy[0].Modifiers.SpeedPreset==null,"Legacy absent Param remains unknown");
Check(legacy[1].Modifiers.NoBombs==true&&legacy[1].Modifiers.SpeedPreset=="faster"&&legacy[1].Modifiers.LegacyParam==(256|16|(1<<25)),"Preserve unknown modifier bits");
Check(legacy[2].PositionStatus=="invalid-index"&&legacy[2].SongTimeSeconds==null,"Invalid legacy index has no fake zero position");
Check(HistoryModifiers.FromLegacy(256|512).SpeedPreset=="unknown-conflicting-flags","Conflicting speeds remain unknown");
var modifiers=new HistoryModifiers {NoBombs=true,Obstacles="full-height-only",SpeedPreset="faster",SpeedMultiplier=1.2,Additional=new(){{"future-option","kept"}}};
var death=DeathHistory.Create("map___3___Standard",25,1000,modifiers,false,true)!;
modifiers.Additional["future-option"]="changed-after-capture";
Check(death.Modifiers!.Additional["future-option"]=="kept","Capture a snapshot, not mutable gameplay settings");
var oldRecord=Newtonsoft.Json.JsonConvert.DeserializeObject<PlayRecord>("{\"Date\":100,\"LastNote\":1,\"UnknownFutureField\":true}")!;
Check(oldRecord.Param==null&&DeathHistory.LegacyMarkers([oldRecord],map).Length==1,"Import legacy JSON with absent Param and unknown fields");
Check(DeathHistory.Create("map",25,0,modifiers,true,true)==null,"Ignore practice capture");
Check(DeathHistory.Create("map",25,0,modifiers,false,false)==null,"Ignore successful play");
var own=DeathHistory.OwnMarkers([death,new(){MapKey=death.MapKey,SongTimeSeconds=30,IsPractice=true},new(){MapKey="other",SongTimeSeconds=30}],death.MapKey,100);
Equal(own.Length,1);
Check(own[0].Source=="beatinsight"&&own[0].ColorHex=="#FF0000"&&own[0].Shape=="cross","Own marker style");
Equal(own[0].NormalizedPosition!.Value,.25);
Equal(own[0].SongTimeSeconds!.Value,25); // Never divide song position by speed a second time.
var stale=DeathHistory.OwnMarkers([new(){MapKey=death.MapKey,BeatmapSha256="old",SongTimeSeconds=10}],death.MapKey,100,"new");
Check(stale[0].PositionStatus=="beatmap-changed"&&stale[0].SongTimeSeconds==null,"Changed beatmap is flagged");
string scratch=BeatInsight.Tooling.LocalArtifacts.PathFor("history-checks",Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(scratch);
string file=Path.Combine(scratch,DeathHistoryStore.FileName),legacyFile=Path.Combine(scratch,"SongPlayData.json");
File.WriteAllText(legacyFile,"{\"legacy\":[]}");string before=File.ReadAllText(legacyFile);
Check(!DeathHistoryStore.Append(file,null)&&!File.Exists(file),"Practice creates no file");
Check(DeathHistoryStore.Append(file,death),"Persist own death");
var loaded=DeathHistoryStore.Read(file);
Check(loaded.Status=="available"&&loaded.Records.Count==1&&loaded.Records[0].Modifiers!.Additional["future-option"]=="kept","Own history roundtrip");
Equal(loaded.Records[0].Modifiers!.SpeedMultiplier!.Value,1.2);
bool refused=false;try{DeathHistoryStore.Append(legacyFile,death);}catch(ArgumentException){refused=true;}
Check(refused&&File.ReadAllText(legacyFile)==before,"Never write reference implementation history");
var document=JObject.Parse(File.ReadAllText(file));document["futureRoot"]="preserved";((JObject)document["deaths"]![0]!)["futureRecord"]="preserved";
File.WriteAllText(file,document.ToString());DeathHistoryStore.Append(file,death);
var saved=JObject.Parse(File.ReadAllText(file));
Check((string?)saved["futureRoot"]=="preserved"&&(string?)saved["deaths"]![0]!["futureRecord"]=="preserved","Preserve unknown root and record fields");
var oldModifiers=(JObject)saved["deaths"]![0]!["Modifiers"]!;
oldModifiers.Remove("LegacyParam");oldModifiers["OldProviderParam"]=256|16|(1<<25);
File.WriteAllText(file,saved.ToString());
Check(DeathHistoryStore.Read(file).Records[0].Modifiers!.LegacyParam==(256|16|(1<<25)),"Read earlier provider-prefixed modifier fields without losing unknown bits");
File.WriteAllText(file,"{\"schemaVersion\":99,\"deaths\":[]}");
Check(DeathHistoryStore.Read(file).Status=="unsupported-schema","Future schema does not crash");
refused=false;try{DeathHistoryStore.Append(file,death);}catch(InvalidDataException){refused=true;}
Check(refused,"Never overwrite future schema");
File.WriteAllText(file,"{\"schemaVersion\":1,\"deaths\":[{}]}");
Check(DeathHistoryStore.Read(file).Status=="invalid-data","Missing song time is not invented as zero");
LoadedMap LoadSynthetic(JObject data,JObject? audioData=null,AnalysisFeatures features=AnalysisFeatures.All,float initialBpm=120,string[]? requiredExtensions=null,float movementSpeed=0,string characteristic="Standard",bool includeSwingWindowWalls=true)
{
    var folder=Path.Combine(scratch,Guid.NewGuid().ToString("N"));Directory.CreateDirectory(folder);
    var info=new JObject { ["version"]="4.0.1",["audio"]=new JObject {["bpm"]=initialBpm,["songDuration"]=100,["audioDataFilename"]="AudioData.dat"},
        ["difficultyBeatmaps"]=new JArray(new JObject {["characteristic"]=characteristic,["difficulty"]="Expert",["beatmapDataFilename"]="map.dat",["noteJumpMovementSpeed"]=movementSpeed})};
    if(requiredExtensions!=null)info["difficultyBeatmaps"]![0]!["customData"]=new JObject {["requirements"]=new JArray(requiredExtensions)};
    File.WriteAllText(Path.Combine(folder,"Info.dat"),info.ToString());File.WriteAllText(Path.Combine(folder,"map.dat"),data.ToString());
    if(audioData!=null)File.WriteAllText(Path.Combine(folder,"AudioData.dat"),audioData.ToString());
    return LocalMap.Load(LocalMap.Cases(folder).Single(),features,includeSwingWindowWalls);
}
using(var visualStream=System.Reflection.Assembly.GetExecutingAssembly().GetManifestResourceStream("BeatInsight.IsolatedVisualTrack.json")!)
using(var visualReader=new StreamReader(visualStream))
{
    var source=JObject.Parse(visualReader.ReadToEnd());
    var vanilla=(JObject)source.DeepClone();vanilla.Remove("customData");
    foreach(var note in vanilla["colorNotes"]!)((JObject)note).Remove("customData");
    var baseline=LoadSynthetic(vanilla);
    foreach(bool arrays in new[]{false,true})foreach(bool constant in new[]{false,true})foreach(int repeat in new[]{0,2})
    {
        var root=(JObject)source.DeepClone();var data=root["customData"]!["customEvents"]![2]!["d"]!;
        if(arrays){foreach(var note in root["colorNotes"]!)note["customData"]!["track"]=new JArray("notes","other");data["track"]=new JArray("notes","other");}
        if(constant)data["color"]=new JArray(.25,.5,2,.75);
        data["repeat"]=repeat;
        var actual=LoadSynthetic(root,requiredExtensions:["Noodle Extensions","Chroma"]);
        Check(Newtonsoft.Json.JsonConvert.SerializeObject(actual.Input)==Newtonsoft.Json.JsonConvert.SerializeObject(baseline.Input),"Isolated visual note tracks preserve prepared geometry, arcs and timing");
        Check(actual.Json==root.ToString(),"Visual track admission preserves original JSON and fingerprint input");
    }
    foreach(bool arcOnly in new[]{false,true})
    {
        var root=(JObject)source.DeepClone();root["sliders"]![0]!["customData"]=new JObject {["track"]="notes"};
        if(arcOnly)foreach(var note in root["colorNotes"]!)((JObject)note["customData"]!).Remove("track");
        Check(Newtonsoft.Json.JsonConvert.SerializeObject(LoadSynthetic(root,requiredExtensions:["Noodle Extensions","Chroma"]).Input)==Newtonsoft.Json.JsonConvert.SerializeObject(baseline.Input),"Color-only arc/shared note bindings preserve prepared gameplay geometry");
    }
    var opacityRoot=(JObject)source.DeepClone();opacityRoot["customData"]!["customEvents"]![2]!["d"]!["dissolve"]=new JArray(.25);
    opacityRoot["customData"]!["customEvents"]![2]!["d"]!["dissolveArrow"]=new JArray(.75);
    Check(Newtonsoft.Json.JsonConvert.SerializeObject(LoadSynthetic(opacityRoot,requiredExtensions:["Noodle Extensions","Chroma"]).Input)==Newtonsoft.Json.JsonConvert.SerializeObject(baseline.Input),"Isolated combined color and opacity retain nominal geometry");
    var shadowedPlayer=JObject.Parse("""{"version":"3.3.0","colorNotes":[{"b":1,"c":0,"d":1}],"customData":{"customEvents":[{"b":0,"t":"AssignPlayerToTrack","_type":"AnimateTrack","d":{"track":"player"}}]}}""");
    bool shadowRejected=false;try{LoadSynthetic(shadowedPlayer,requiredExtensions:["Noodle Extensions"]);}catch(NotSupportedException){shadowRejected=true;}
    Check(shadowRejected,"An ignored legacy event alias cannot hide a canonical v3 player assignment");
    var ignoredPlayerAlias=(JObject)shadowedPlayer.DeepClone();ignoredPlayerAlias["customData"]!["customEvents"]![0]!["t"]="AnimateTrack";
    ignoredPlayerAlias["customData"]!["customEvents"]![0]!["_type"]="AssignPlayerToTrack";
    ignoredPlayerAlias["customData"]!["customEvents"]![0]!["d"]!["dissolve"]=new JArray(1);
    Check(LoadSynthetic(ignoredPlayerAlias,requiredExtensions:["Noodle Extensions"]).Input.Notes.Length==1,"An ignored legacy type does not become a v3 player event");
    var changes=new List<(string,Action<JObject>)> {
        ("geometry",r=>r["customData"]!["customEvents"]![2]!["d"]!["offsetPosition"]=new JArray(1,0,0)),
        ("path",r=>r["customData"]!["customEvents"]![2]!["t"]="AssignPathAnimation"),
        ("environment",r=>r["customData"]!["environment"]![0]!["track"]="notes"),
        ("environment-array",r=>r["customData"]!["environment"]![0]!["track"]=new JArray("scene","notes")),
        ("material",r=>r["customData"]!["materials"]!["fixture"]!["track"]="notes"),
        ("inline-material",r=>r["customData"]!["environment"]![0]!["geometry"]=new JObject {["type"]="Cube",["material"]=new JObject {["track"]="notes"}}),
        ("parent",r=>r["customData"]!["customEvents"]![0]!["d"]!["parentTrack"]="notes"),
        ("child",r=>r["customData"]!["customEvents"]![0]!["d"]!["childrenTracks"]=new JArray("notes")),
        ("unknown-event",r=>((JArray)r["customData"]!["customEvents"]!).Add(new JObject {["b"]=0,["t"]="InvokeEvent",["d"]=new JObject()})),
        ("player",r=>((JArray)r["customData"]!["customEvents"]!).Add(new JObject {["b"]=0,["t"]="AssignPlayerToTrack",["d"]=new JObject {["track"]="player"}})),
        ("invalid-color",r=>r["customData"]!["customEvents"]![2]!["d"]!["color"]=new JArray(1,2,3)),
        ("nonfinite-color",r=>r["customData"]!["customEvents"]![2]!["d"]!["color"]=new JArray(1,2,double.NaN,1)),
        ("named-color",r=>r["customData"]!["customEvents"]![2]!["d"]!["color"]="color definition"),
        ("duplicate-color-time",r=>r["customData"]!["customEvents"]![2]!["d"]!["color"]![1]![4]=0),
        ("unknown-easing",r=>((JArray)r["customData"]!["customEvents"]![2]!["d"]!["color"]![1]!).Add("unknown")),
        ("fractional-repeat",r=>r["customData"]!["customEvents"]![2]!["d"]!["repeat"]=1.5),
        ("negative-duration",r=>r["customData"]!["customEvents"]![2]!["d"]!["duration"]=-1),
        ("fake-shared",r=>{var n=(JObject)r["colorNotes"]![0]!.DeepClone();n["customData"]!["NE_fake"]=true;((JArray)r["colorNotes"]!).Add(n);}),
        ("bomb-shared",r=>r["bombNotes"]=new JArray(new JObject {["b"]=4,["x"]=0,["y"]=0,["customData"]=new JObject {["track"]="notes"}})),
        ("wall-shared",r=>r["obstacles"]=new JArray(new JObject {["b"]=4,["x"]=0,["y"]=2,["w"]=4,["h"]=3,["d"]=1,["customData"]=new JObject {["track"]="notes"}})),
        ("object-animation",r=>r["colorNotes"]![0]!["customData"]!["animation"]=new JObject {["scale"]=new JArray(.5,.5,.5)}),
        ("legacy-root",r=>r["_customData"]=new JObject {["_environment"]=new JArray(new JObject())}),
        ("legacy-track",r=>r["colorNotes"]![0]!["customData"]!["_track"]="notes"),
        ("malformed-material",r=>r["customData"]!["materials"]!["fixture"]="invalid")
    };
    foreach(var (name,change) in changes)
    {
        var root=(JObject)source.DeepClone();change(root);bool rejected=false;
        try{LoadSynthetic(root,requiredExtensions:["Noodle Extensions","Chroma"]);}catch(NotSupportedException){rejected=true;}
        Check(rejected,"Isolated visual track boundary: "+name);
    }
}
var legacyBpm=LoadSynthetic(JObject.Parse("""{"_version":"2.0.0","_notes":[{"_time":1,"_type":0,"_cutDirection":1}],"_events":[{"_time":0,"_type":10,"_value":2,"_floatValue":1}]}"""));
foreach(var raw in new[]{
    JObject.Parse("""{"_version":"2.6.0","_notes":[{"_time":2,"_lineIndex":0,"_lineLayer":0,"_type":0,"_cutDirection":1},{"_time":4,"_lineIndex":3,"_lineLayer":1,"_type":1,"_cutDirection":0}],"_obstacles":[{"_time":6,"_duration":2,"_lineIndex":1,"_type":1,"_width":2}]}"""),
    JObject.Parse("""{"version":"3.3.0","colorNotes":[{"b":2,"x":0,"y":0,"c":0,"d":1},{"b":4,"x":3,"y":1,"c":1,"d":0}],"obstacles":[{"b":6,"d":2,"x":1,"y":2,"w":2,"h":3}]}"""),
    JObject.Parse("""{"version":"4.1.0","colorNotes":[{"b":2,"i":0},{"b":4,"i":1}],"colorNotesData":[{"x":0,"y":0,"c":0,"d":1},{"x":3,"y":1,"c":1,"d":0}],"obstacles":[{"b":6,"i":0}],"obstaclesData":[{"x":1,"y":2,"w":2,"h":3,"d":2}]}""")})
{
    var audio=JObject.Parse("""{"songFrequency":48000,"bpmData":[{"si":0,"ei":480000,"sb":0,"eb":20}]}""");
    foreach(var mask in new[]{AnalysisFeatures.All,AnalysisFeatures.Average,AnalysisFeatures.FirstNotes,AnalysisFeatures.Density,AnalysisFeatures.Squats,AnalysisFeatures.OwnDeaths})
    {
        var disk=LoadSynthetic(raw,audio,features:mask);
        var docs=MapDocuments.Bundled(raw.ToString(),audio.ToString(),120,100,"Standard","Expert");
        var memory=LocalMap.LoadDocuments(docs,"Standard","Expert",mask);
        Check(Newtonsoft.Json.JsonConvert.SerializeObject(disk.Input)==Newtonsoft.Json.JsonConvert.SerializeObject(memory.Input),"Memory/file map inputs agree across formats and requested features");
        Check(memory.Json==raw.ToString()&&memory.Version==disk.Version&&memory.Bpm==disk.Bpm,"Document parsing preserves exact source/version/BPM");
        Check(memory.Input.Duration==disk.Input.Duration,"Document duration respects hidden/visible timeline features");
    }
}
var missingAudioDocs=MapDocuments.Bundled("""{"version":"4.1.0","colorNotes":[],"colorNotesData":[]}""",null,120,10,"Standard","Expert");
bool documentAudioRejected=false;try{LocalMap.LoadDocuments(missingAudioDocs,"Standard","Expert");}catch(NotSupportedException){documentAudioRejected=true;}
Check(documentAudioRejected,"V4 document data cannot silently fall back to filesystem AudioData");
var missingDurationInfo=JObject.Parse("""{"version":"4.0.1","audio":{"bpm":120},"difficultyBeatmaps":[{"characteristic":"Standard","difficulty":"Expert","beatmapDataFilename":"beatmap.dat"}]}""");
var missingDurationDocs=new MapDocuments(missingDurationInfo.ToString(),"""{"version":"3.3.0","colorNotes":[{"b":2,"c":0,"d":1}]}""");
Check(LocalMap.LoadDocuments(missingDurationDocs,"Standard","Expert",AnalysisFeatures.Average).Input.Duration==0,"Hidden document timeline does not request unavailable audio duration");
bool durationRejected=false;try{LocalMap.LoadDocuments(missingDurationDocs,"Standard","Expert",AnalysisFeatures.Density);}catch(NotSupportedException){durationRejected=true;}
Check(durationRejected,"A visible document timeline requires a declared duration, without disk fallback");
Equal(legacyBpm.Input.Notes[0].Time,30);
var chainArc=LoadSynthetic(JObject.Parse("""{"version":"3.3.0","colorNotes":[{"b":4,"x":1,"y":0,"c":0,"d":1}],"sliders":[{"b":0,"x":3,"y":0,"c":0,"tb":4,"tx":3,"ty":0},{"b":4,"x":1,"y":0,"c":0,"tb":8,"tx":2,"ty":0}],"burstSliders":[{"b":4,"x":1,"y":0,"c":0,"d":1,"tb":4.5,"tx":1,"ty":1,"sc":4,"s":1}]}"""));
Equal(chainArc.Input.Notes.Length,2); // Later arc head annotation preserves both raw head and synthetic chain head.
Check(chainArc.Input.Notes.Any(n=>n.SliceCount==4)&&chainArc.Input.Notes.Any(n=>n.ScoringType==2),"Preserve chain/arc multiplicity");
var bombArc=LoadSynthetic(JObject.Parse("""{"version":"3.3.0","colorNotes":[],"bombNotes":[{"b":4,"x":1,"y":0}],"sliders":[{"b":0,"x":3,"y":0,"c":0,"tb":4,"tx":1,"ty":0}]}"""));
Equal(bombArc.Input.Notes.Length,1);
Check(bombArc.Input.Notes[0].Color==-1&&bombArc.Input.Notes[0].ScoringType==3,"Reproduce original arc annotation on bombs");
var timing=LoadSynthetic(JObject.Parse("""{"version":"4.1.0","colorNotes":[{"b":1,"i":0}],"colorNotesData":[{"x":1,"y":0,"c":0,"d":1}]}"""),JObject.Parse("""{"songFrequency":44100,"bpmData":[{"si":0,"ei":88095,"sb":0,"eb":1},{"si":88095,"ei":125161,"sb":1,"eb":2}]}"""));
float firstSegmentBpm=1f/(88095f/44100f)*60f;
Equal(timing.Input.Notes[0].Time,1f/firstSegmentBpm*60f);
bool invalidTimeline=false;
try {LoadSynthetic(JObject.Parse("""{"_version":"2.0.0","_notes":[{"_time":2,"_type":0,"_cutDirection":1}],"_events":[{"_time":1,"_type":10,"_value":0,"_floatValue":0}]}"""));}
catch(NotSupportedException){invalidTimeline=true;}
Check(invalidTimeline,"Reject legacy zero-BPM timeline explicitly instead of overflowing reference arrays");
var zeroWall=LoadSynthetic(JObject.Parse("""{"version":"3.3.0","colorNotes":[{"b":2,"x":0,"y":0,"c":0,"d":1}],"obstacles":[{"b":180,"d":0,"x":1,"y":0,"w":1,"h":5}]}"""));
Equal(CompatibleAnalysis.Analyze(zeroWall.Input).LeftBins.Length,2);
var unsortedTies=LoadSynthetic(JObject.Parse("""{"version":"3.3.0","colorNotes":[{"b":4,"x":0,"c":0,"d":8},{"b":6,"x":3,"c":0,"d":1},{"b":4,"x":1,"c":0,"d":2}]}"""));
Check(unsortedTies.Input.Notes[0].X==1&&unsortedTies.Input.Notes[1].X==0,"Backward insertion precedes existing equal-time notes");
var v4ChainArc=LoadSynthetic(JObject.Parse("""{"version":"4.1.0","colorNotes":[{"b":4,"i":0}],"colorNotesData":[{"x":1,"y":0,"c":0,"d":1},{"x":2,"y":0,"c":0,"d":1}],"arcs":[{"hb":4,"tb":8,"hi":0,"ti":1,"ai":0}],"arcsData":[{}],"chains":[{"hb":4,"tb":4.5,"i":0,"ci":0}],"chainsData":[{"tx":1,"ty":1,"c":4}]}"""),JObject.Parse("""{"songFrequency":44100,"bpmData":[{"si":0,"ei":4410000,"sb":0,"eb":200}]}"""));
Check(v4ChainArc.Input.Notes.Length==2&&v4ChainArc.Input.Notes[0].ScoringType==2,"v4 chains precede arcs during row annotation");
var captureModifiers=new HistoryModifiers {NoBombs=true,SpeedPreset="faster",SpeedMultiplier=1.2,Additional=new(){{"noFailOn0Energy","false"}}};
var attempt=new DeathAttempt("attempt___3___Standard",captureModifiers,false);
captureModifiers.NoBombs=false;captureModifiers.Additional["noFailOn0Energy"]="true";
Check(attempt.CaptureFailure(25,1000),"Capture first failure event");
Check(!attempt.CaptureFailure(26,1100),"Duplicate failure event cannot move death time");
var captured=attempt.Complete(AttemptOutcome.Failed,30,2000)!;
Equal(captured.SongTimeSeconds,25);Equal(captured.Date,1000);
Check(captured.TimingSource=="failure-song-time"&&captured.Modifiers!.NoBombs==true&&captured.Modifiers.Additional["noFailOn0Energy"]=="false","Capture precision and immutable attempt settings");
Check(attempt.Complete(AttemptOutcome.Failed,31,3000)==null&&!attempt.CaptureFailure(32,4000),"Complete once and ignore late events");
captureModifiers.Additional["noFailOn0Energy"]="false";
var unobserved=new DeathAttempt("attempt___3___Standard",captureModifiers,false);
Check(unobserved.Complete(AttemptOutcome.Failed,12,4000)==null&&unobserved.ExclusionReason=="no-observed-failure","Changed completion flags alone cannot create a death");
var fallback=new DeathRecord {MapKey="attempt___3___Standard",SongTimeSeconds=12,TimingSource="completion-song-time-fallback"};
var fallbackMarker=DeathHistory.OwnMarkers([fallback],fallback.MapKey,100).Single();
Check(fallbackMarker.TimingSource=="completion-song-time-fallback"&&fallbackMarker.PositionStatus=="completion-song-time-estimate","Fallback remains visibly distinct from early capture");
foreach(var outcome in new[]{AttemptOutcome.Incomplete,AttemptOutcome.Cleared})
{
    var nonFailure=new DeathAttempt("attempt___3___Standard",captureModifiers,false);
    Check(nonFailure.Complete(outcome,2,2000)==null,"Successful or aborted attempt is not a death");
}
var failedRestart=new DeathAttempt("attempt___3___Standard",captureModifiers,false);
failedRestart.CaptureFailure(3,4000);
Check(failedRestart.Complete(AttemptOutcome.Incomplete,4,5000)!.SongTimeSeconds==3,"An observed failure is preserved when followed by restart/quit");
var completedAfterEvent=new DeathAttempt("attempt___3___Standard",captureModifiers,false);
completedAfterEvent.CaptureFailure(3,4000);
Check(completedAfterEvent.Complete(AttemptOutcome.Cleared,4,5000)==null,"Cleared result does not create a death even after a conflicting event");
var practiceAttempt=new DeathAttempt("attempt___3___Standard",captureModifiers,true);
Check(!practiceAttempt.CaptureFailure(10,1000)&&practiceAttempt.Complete(AttemptOutcome.Failed,12,2000)==null,"Practice is excluded at both lifecycle stages");
var invalidAttempt=new DeathAttempt("attempt___3___Standard",captureModifiers,false);
Check(!invalidAttempt.CaptureFailure(double.NaN,0)&&!invalidAttempt.CaptureFailure(-1,0)&&!invalidAttempt.CaptureFailure(double.PositiveInfinity,0),"Invalid early times are ignored");
Check(invalidAttempt.Complete(AttemptOutcome.Failed,double.NaN,0)==null,"No fake zero time from invalid completion");
var retry=new DeathAttempt("attempt___3___Standard",captureModifiers,false);
Check(retry.CaptureFailure(2,5000)&&retry.Complete(AttemptOutcome.Failed,4,6000)!.Id!=captured.Id,"A new attempt on the same map has an independent identity");
var noFailAttempt=new DeathAttempt("attempt___3___Standard",captureModifiers,false);
var recordingOff=new DeathAttempt("attempt___3___Standard",captureModifiers,false);
recordingOff.CaptureFailure(20,1000);recordingOff.SuppressRecording();
Check(!recordingOff.CaptureFailure(21,1100)&&recordingOff.Complete(AttemptOutcome.Failed,30,2000)==null&&recordingOff.ExclusionReason=="recording-disabled",
    "Disabling recording invalidates the current attempt without producing a record");
Check(noFailAttempt.Complete(AttemptOutcome.Cleared,100,7000)==null,"NoFail completion without a failure event produces no death");
var noFailModifiers=new HistoryModifiers {Additional=new(){{"noFailOn0Energy","True"}}};
var zeroAttempt=new DeathAttempt("no-fail___3___Standard",noFailModifiers,false,"map-hash");
Check(!zeroAttempt.CaptureZeroEnergy(double.NaN,1)&&!zeroAttempt.CaptureZeroEnergy(-1,1)&&!zeroAttempt.CaptureZeroEnergy(double.PositiveInfinity,1),"No Fail rejects invalid zero-energy times");
Check(zeroAttempt.CaptureZeroEnergy(12.5,2000)&&!zeroAttempt.CaptureZeroEnergy(25,3000),"No Fail captures only the first energy depletion");
Check(!zeroAttempt.PrepareBeatmapHash("later-hash"),"Zero energy freezes the attempt fingerprint");
var zeroRecord=zeroAttempt.Complete(AttemptOutcome.Cleared,100,4000)!;
Check(zeroRecord.SongTimeSeconds==12.5&&zeroRecord.Date==2000&&zeroRecord.TimingSource=="zero-energy-song-time"&&zeroRecord.BeatmapSha256=="map-hash","Cleared No Fail keeps exact first-zero position/date/hash");
Check(zeroRecord.Modifiers!.Additional["noFailOn0Energy"]=="True","No Fail modifier survives capture");
Check(zeroAttempt.Complete(AttemptOutcome.Cleared,100,4000)==null&&!zeroAttempt.CaptureZeroEnergy(40,5000),"Completed No Fail attempt cannot produce duplicate or late records");
var noZeroAttempt=new DeathAttempt("no-fail___3___Standard",noFailModifiers,false);
Check(noZeroAttempt.Complete(AttemptOutcome.Cleared,100,4000)==null,"No Fail without depletion produces no cross");
foreach(var finish in new[]{AttemptOutcome.Incomplete,AttemptOutcome.Failed})
{
    var abortedZero=new DeathAttempt("no-fail___3___Standard",noFailModifiers,false);
    abortedZero.CaptureZeroEnergy(10,1000);
    Check(abortedZero.Complete(finish,20,2000)==null&&abortedZero.ExclusionReason=="no-fail-not-cleared","No Fail depletion is discarded unless the map is cleared");
}
var soughtZero=new DeathAttempt("no-fail___3___Standard",noFailModifiers,false);
soughtZero.CaptureZeroEnergy(10,1000);soughtZero.ExcludeAfterSeek();
Check(soughtZero.Complete(AttemptOutcome.Cleared,100,4000)==null&&soughtZero.ExclusionReason=="song-seek","Seek invalidates an earlier No Fail depletion");
var soughtBeforeZero=new DeathAttempt("no-fail___3___Standard",noFailModifiers,false);
soughtBeforeZero.ExcludeAfterSeek();
Check(!soughtBeforeZero.CaptureZeroEnergy(10,1000)&&soughtBeforeZero.Complete(AttemptOutcome.Cleared,100,4000)==null,"Seek also excludes subsequent No Fail depletion");
var practiceZero=new DeathAttempt("no-fail___3___Standard",noFailModifiers,true);
Check(!practiceZero.CaptureZeroEnergy(10,1000)&&practiceZero.Complete(AttemptOutcome.Cleared,100,4000)==null,"Practice No Fail creates no cross");
var disabledZero=new DeathAttempt("no-fail___3___Standard",noFailModifiers,false);
disabledZero.CaptureZeroEnergy(10,1000);disabledZero.SuppressRecording();
Check(!disabledZero.CaptureZeroEnergy(12,2000)&&disabledZero.Complete(AttemptOutcome.Cleared,100,4000)==null,"Disabling recording invalidates No Fail depletion");
var ordinaryZero=new DeathAttempt("ordinary___3___Standard",new HistoryModifiers(),false);
Check(!ordinaryZero.CaptureZeroEnergy(10,1000)&&ordinaryZero.Complete(AttemptOutcome.Cleared,100,4000)==null,"Zero-energy callback alone cannot change ordinary clear policy");
string zeroFile=Path.Combine(scratch,"no-fail-capture",DeathHistoryStore.FileName);
DeathHistoryStore.Append(zeroFile,zeroRecord);
var zeroRestored=DeathHistoryStore.Read(zeroFile).Records.Single();
var zeroMarker=DeathHistory.OwnMarkers([zeroRestored],zeroRecord.MapKey,100,"map-hash").Single();
Check(zeroMarker.ColorHex=="#FF0000"&&zeroMarker.SongTimeSeconds==12.5&&zeroMarker.TimingSource=="zero-energy-song-time","No Fail persists as an own red cross at the depletion time");
var rewound=new DeathAttempt("attempt___3___Standard",captureModifiers,false);
Check(rewound.ExcludeAfterSeek()&&!rewound.CaptureFailure(25,1000),"Seeking excludes subsequent failure events");
Check(rewound.Complete(AttemptOutcome.Failed,100,2000)==null&&rewound.ExclusionReason=="song-seek","Rewind plus a modified Failed result cannot create a death");
var rewindAfterFailure=new DeathAttempt("attempt___3___Standard",captureModifiers,false);
rewindAfterFailure.CaptureFailure(25,1000);rewindAfterFailure.ExcludeAfterSeek();
Check(rewindAfterFailure.Complete(AttemptOutcome.Failed,100,2000)==null,"Seeking also invalidates an earlier captured failure in the same attempt");
Check(!rewound.ExcludeAfterSeek(),"Repeated seek notifications are idempotent");
var freshAfterSeek=new DeathAttempt("attempt___3___Standard",captureModifiers,false);
freshAfterSeek.CaptureFailure(30,3000);
Check(freshAfterSeek.Complete(AttemptOutcome.Failed,31,4000)!.SongTimeSeconds==30,"A new attempt after seeking can record a genuine failure");
string attemptFile=Path.Combine(scratch,"attempt-capture",DeathHistoryStore.FileName);
DeathHistoryStore.Append(attemptFile,captured);
Check(DeathHistoryStore.Read(attemptFile).Records.Single().TimingSource=="failure-song-time","Timing source survives own-history persistence");
Check(Newtonsoft.Json.JsonConvert.DeserializeObject<DeathRecord>("{\"MapKey\":\"old\",\"SongTimeSeconds\":1}")!.TimingSource==null,"Old own records do not invent a precise timing source");
string extendedWalls="""{"version":"4.1.0","obstacles":[{"b":2},{"b":4,"i":1}],"obstaclesData":[{"x":1,"y":4,"w":2,"h":1,"d":1},{"x":1,"y":4,"w":2,"h":1,"d":1}]}""";
Check(WallAlertAdapter.Count(extendedWalls,120,preserveExtendedLayers:true)==2,"Preserved extended layers use ordinary Top spawn height, including implicit v4 index zero");
Check(WallAlertAdapter.Count(extendedWalls,120)==0,"Unpatched JSON conversion maps extended layer to Base");
Check(WallAlertAdapter.Count(extendedWalls.Replace("\"y\":4","\"y\":2"),120)==2,"Implicit and explicit zero indices produce the same v4 wall result");
var extendedSequence=new SquatWarningSequence();
Check(extendedSequence.Observe(2,3,1,1,4,2,1)&&extendedSequence.Observe(4,5,1,1,4,2,1),"Loaded extended vanilla wall enums follow ordinary Top spawn height");
Check(SelectedMapRequest.SelectionIdentity("map",3,"Standard",AnalysisFeatures.Squats,null,true)!=SelectedMapRequest.SelectionIdentity("map",3,"Standard",AnalysisFeatures.Squats,null,false),"Wall conversion context participates in menu request identity");
Check(SelectedMapRequest.SelectionIdentity("map",3,"Standard",AnalysisFeatures.Average,null,true)==SelectedMapRequest.SelectionIdentity("map",3,"Standard",AnalysisFeatures.Average,null,false),"Hidden squat calculation ignores wall conversion context");
var warnings=new BeatInsight.WallAlert.SquatWarningSequence();
Check(!warnings.Observe(1,2,1,0,0,4,3),"Full-height wall alone does not start a squat warning");
warnings.Reset();
Check(!warnings.Observe(1,2,1,0,1,1,3),"Side-only upper wall does not warn");
Check(!warnings.Observe(1,2,1,1,1,1,3),"Single center lane waits for combined coverage");
Check(warnings.Observe(1,2,1,2,1,1,3),"Split upper walls covering both center lanes warn once");
Check(!warnings.Observe(1,2,1,2,1,1,3),"Duplicate wall does not duplicate warning");
Check(!warnings.Observe(1.5f,2.5f,1,1,1,2,3),"Half-beat boundary continues the same sequence");
Check(!warnings.Observe(2,3,1,1,1,2,3),"Adjacent chain extends sequence suppression");
Check(warnings.Observe(2.501f,3.501f,1,1,1,2,3),"Gap greater than half a beat starts a new warning");
warnings.Reset();
Check(warnings.Observe(1,2,1,1,1,2,3),"Restart or rewind resets sequence suppression");
warnings.Reset();
Check(!warnings.Observe(1,1.1f,.1f,1,1,1,3)
    &&!warnings.Observe(1.3f,2,0.7f,2,1,1,3),"Expired lane coverage cannot complete a later squat");
warnings.Reset();
Check(!warnings.Observe(1,2,2,1,1,1,3)&&!warnings.Observe(2.3f,3.3f,2,2,1,1,3),
    "Slow-tempo duration seconds cannot extend beat-domain lane coverage");
warnings.Reset();
Check(!warnings.Observe(1,2,2,1,1,1,3)&&warnings.Observe(1.5f,2.5f,2,2,1,1,3),
    "A genuine beat-domain overlap still emits a squat with long duration seconds");
warnings.Reset();
Check(warnings.Observe(1,2,1,1,3,2,1),"Out-of-range loaded layer uses ordinary Top spawn height");
Check(!warnings.Observe(1,2,1,1,1,0,3),"Zero width wall does not warn");
Check(!warnings.Observe(1,2,1,1,1,2,0),"Zero height wall does not warn");
const string fingerprintKey="fingerprint___3___Standard";
var fingerprint=new AttemptFingerprint(fingerprintKey);
var sourceA=new object();var sourceB=new object();int reads=0;
string loadedJson="{\"version\":\"3.3.0\",\"label\":\"карта\"}";
string ReadSnapshot(){reads++;return loadedJson;}
Check(!fingerprint.Prepare("another-map",sourceA,ReadSnapshot)&&reads==0&&fingerprint.Hash==null,
    "A different difficulty or level cannot supply this attempt's fingerprint");
Check(fingerprint.Prepare(fingerprintKey,sourceA,ReadSnapshot)&&reads==1&&fingerprint.Hash?.Length==64,
    "Loaded source produces a SHA256 fingerprint before gameplay");
string hashA=fingerprint.Hash!;
Check(hashA==Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(loadedJson))),
    "Gameplay fingerprint preserves the existing menu/CLI UTF8 hashing protocol");
loadedJson="{\"version\":\"3.3.0\",\"label\":\"updated map\"}";
Check(fingerprint.Prepare(fingerprintKey,sourceA,ReadSnapshot)&&reads==1&&fingerprint.Hash==hashA,
    "Restart with the same loaded source keeps its snapshot and performs no file read");
var fingerprintAttempt=new DeathAttempt(fingerprintKey,captureModifiers,false);
Check(fingerprintAttempt.PrepareBeatmapHash(fingerprint.Hash),"Fingerprint is assigned to the existing attempt during loading");
fingerprintAttempt.FreezeBeatmapHash();
Check(!fingerprintAttempt.PrepareBeatmapHash("changed-during-song"),"Gameplay cannot change a sealed fingerprint before a failure");
fingerprintAttempt.CaptureFailure(15,1000);
var fingerprintRecord=fingerprintAttempt.Complete(AttemptOutcome.Failed,16,2000)!;
Check(fingerprintRecord.BeatmapSha256==hashA&&reads==1,"Failure saves the prepared fingerprint without reading or hashing the map");
Check(fingerprint.Prepare(fingerprintKey,sourceB,ReadSnapshot)&&reads==2&&fingerprint.Hash!=hashA,
    "A newly loaded source of the same map gets a new fingerprint");
Check(DeathHistory.OwnMarkers([fingerprintRecord],fingerprintKey,100,fingerprint.Hash).Single().PositionStatus=="beatmap-changed",
    "A death recorded before the update is recognized as stale afterwards");
Check(fingerprintRecord.BeatmapSha256==hashA,"A later load cannot mutate the earlier death record");
bool hashReadFailed=false;
try{fingerprint.Prepare(fingerprintKey,new object(),()=>throw new IOException("synthetic read failure"));}
catch(IOException){hashReadFailed=true;}
Check(hashReadFailed&&fingerprint.Hash==null,"Failed fingerprint reads never reuse another source's hash");
Check(fingerprint.Prepare(fingerprintKey,sourceB,ReadSnapshot)&&reads==3,"A failed source can be retried during a later load");
fingerprint.Invalidate();
Check(fingerprint.Hash==null&&new AttemptFingerprint(fingerprintKey).Hash==null,"Level teardown and new attempt series release fingerprint state");
var stoppedAttempt=new DeathAttempt(fingerprintKey,captureModifiers,false);
stoppedAttempt.SuppressRecording();stoppedAttempt.PrepareBeatmapHash(hashA);
Check(!stoppedAttempt.CaptureFailure(15,1000)&&stoppedAttempt.Complete(AttemptOutcome.Failed,16,2000)==null,
    "Preparing a hash cannot re-enable an attempt excluded by the recording setting");
var freezeOnFailure=new DeathAttempt(fingerprintKey,captureModifiers,false,hashA);
freezeOnFailure.CaptureFailure(15,1000);
Check(!freezeOnFailure.PrepareBeatmapHash("late-hash"),"Observed failure also seals the hash without relying on the game adapter");
string fingerprintFile=Path.Combine(scratch,"fingerprint-history",DeathHistoryStore.FileName);
DeathHistoryStore.Append(fingerprintFile,fingerprintRecord);
Check(DeathHistoryStore.Read(fingerprintFile).Records.Single().BeatmapSha256==hashA,"Prepared fingerprint survives own-history storage");
Check(Newtonsoft.Json.JsonConvert.DeserializeObject<DeathRecord>("{\"MapKey\":\"old\",\"SongTimeSeconds\":1}")!.BeatmapSha256==null,
    "An older record without a hash remains unknown rather than inheriting the current map version");
foreach(var status in new[]{"source-missing","source-unreadable","invalid-data","unsupported-schema","not-requested","unknown"})
{
    var unavailable=HistoryOverview.Create("beatinsight",status,[]);
    Check(!unavailable.Tooltip.Contains("0 shown")&&!unavailable.Tooltip.Contains("No records"),"Unavailable history never masquerades as zero deaths: "+status);
}
Check(HistoryOverview.Create("legacy","key-absent",[]).Tooltip.Contains("No records for this difficulty"),"Missing difficulty has a specific empty-state message");
Check(HistoryOverview.Create("beatinsight","available",[]).Tooltip.Contains("No records for this difficulty"),"Readable empty history differs from unavailable history");
var overviewRecords=DeathHistory.OwnMarkers([
    new(){MapKey=fingerprintKey,SongTimeSeconds=10,BeatmapSha256=hashA,TimingSource="failure-song-time"},
    new(){MapKey=fingerprintKey,SongTimeSeconds=20,TimingSource="completion-song-time-fallback"},
    new(){MapKey=fingerprintKey,SongTimeSeconds=30},
    new(){MapKey=fingerprintKey,SongTimeSeconds=40,BeatmapSha256="another-version"},
    new(){MapKey=fingerprintKey,SongTimeSeconds=200}],fingerprintKey,100,hashA);
var overview=HistoryOverview.Create("beatinsight","available",overviewRecords);
Check(overview.Records==5&&overview.Shown==3&&overview.Changed==1&&overview.Invalid==1&&overview.Unverified==2,
    "Overview distinguishes visible, changed, invalid and version-unverified positions");
Check(overview.Tooltip.Contains("Recorded failure time: 1")&&overview.Tooltip.Contains("Completion-time estimates: 1")&&
    overview.Tooltip.Contains("Timing accuracy unspecified: 1")&&overview.Tooltip.Contains("Map version verified: 1"),
    "Recorded failure time, completion estimate, unspecified timing and verified version remain distinct");
Check(overview.Tooltip.Contains("Current modifiers do not filter")&&overview.Tooltip.Contains("Speed presets: Unknown"),
    "Unknown modifiers remain unknown and history is not presented as filtered by current settings");
var legacyOverview=HistoryOverview.Create("legacy","available",legacy);
Check(legacyOverview.Shown==2&&legacyOverview.Invalid==1&&legacyOverview.Tooltip.Contains("note indices")&&legacyOverview.Tooltip.Contains("unverified"),
    "Imported positions retain their index estimate and unverified map version");
var emptyHashMarker=DeathHistory.OwnMarkers([new(){MapKey=fingerprintKey,SongTimeSeconds=1,BeatmapSha256=" "}],fingerprintKey,100,hashA).Single();
Check(emptyHashMarker.MapVersionStatus=="not-recorded"&&HistoryOverview.CanDraw(emptyHashMarker),"Empty stored hash is unknown, not evidence of a changed map");
var notChecked=DeathHistory.OwnMarkers([new(){MapKey=fingerprintKey,SongTimeSeconds=1,BeatmapSha256=hashA}],fingerprintKey,100).Single();
Check(notChecked.MapVersionStatus=="not-checked","A stored hash without a current hash is not marked verified");
Check(!HistoryOverview.CanDraw(new DeathMarker {SongTimeSeconds=double.NaN,NormalizedPosition=.5})&&
    !HistoryOverview.CanDraw(new DeathMarker {SongTimeSeconds=1,NormalizedPosition=double.PositiveInfinity}),"Nonfinite positions cannot be counted or drawn");
foreach(var flags in new[]{(false,false),(true,false),(false,true),(true,true)})
{
    var importedRegion=HistoryHoverLayout.Region(flags.Item1,flags.Item2,false);
    var ownRegion=HistoryHoverLayout.Region(flags.Item1,flags.Item2,true);
    Check(importedRegion.Enabled==flags.Item1&&ownRegion.Enabled==flags.Item2&&
        (!flags.Item1||importedRegion.Left==0)&&(!flags.Item2||ownRegion.Right==1)&&
        (!flags.Item1||!flags.Item2||importedRegion.Right==ownRegion.Left)&&
        (flags.Item1==flags.Item2||(flags.Item1?importedRegion.Right==1:ownRegion.Left==0)),
        "Source hover zones remain accessible without markers and never overlap: "+flags);
}
var oldArcs=JObject.Parse("""{"_version":"2.6.0","_notes":[{"_time":2,"_lineIndex":0,"_type":0,"_cutDirection":1},{"_time":4,"_lineIndex":1,"_type":0,"_cutDirection":1},{"_time":6,"_lineIndex":2,"_type":0,"_cutDirection":1}],"_sliders":[{"_headTime":2,"_tailTime":4,"_headLineIndex":0,"_tailLineIndex":1,"_colorType":0},{"_headTime":4,"_tailTime":6,"_headLineIndex":1,"_tailLineIndex":2,"_colorType":0}]}""");
var preparedOldArcs=LoadSynthetic(oldArcs);
Check(preparedOldArcs.Features.Contains("arcs")&&preparedOldArcs.Input.Notes.Single(n=>n.Beat==4).ScoringType==6,
    "Old v2 sliders are loaded and joined heads/tails retain their combined scoring type");
var reversedOldArcs=(JObject)oldArcs.DeepClone();reversedOldArcs["_sliders"]=new JArray(((JArray)reversedOldArcs["_sliders"]!).Reverse());
Check(LoadSynthetic(reversedOldArcs).Input.Notes.Select(n=>(n.Time,n.ScoringType)).SequenceEqual(preparedOldArcs.Input.Notes.Select(n=>(n.Time,n.ScoringType))),
    "Unsorted v2 sliders are ordered before annotation as in the game loader");
foreach(var feature in new[]{AnalysisFeatures.Average,AnalysisFeatures.Median,AnalysisFeatures.FirstNotes,AnalysisFeatures.Density,AnalysisFeatures.ImportedDeaths})
    Check(LoadSynthetic(oldArcs,features:feature).Input.Notes.Select(n=>(n.Time,n.ScoringType)).SequenceEqual(preparedOldArcs.Input.Notes.Select(n=>(n.Time,n.ScoringType))),
        "Selective loading preserves v2 arc preparation: "+feature);
foreach(string version in new[]{"1.0.0","5.0.0","invalid"})
{
    bool rejected=false;
    try{LoadSynthetic(JObject.Parse("{\"_version\":\""+version+"\"}"),features:AnalysisFeatures.Squats);}catch(NotSupportedException){rejected=true;}
    Check(rejected,"Unsupported versions are explicit even when note analysis is hidden: "+version);
}
foreach(float initialBpm in new[]{0f,-1f,float.NaN,float.PositiveInfinity})
{
    bool rejected=false;
    try{LoadSynthetic(JObject.Parse("{\"_version\":\"2.6.0\"}"),initialBpm:initialBpm);}catch(NotSupportedException){rejected=true;}
    Check(rejected,"Invalid initial BPM is explicitly unsupported: "+initialBpm);
}
var unusedBadBpm=LoadSynthetic(JObject.Parse("""{"_version":"2.0.0","_notes":[],"_events":[{"_time":1,"_type":10,"_value":0,"_floatValue":0}]}"""));
Check(unusedBadBpm.Input.Notes.Length==0,"Invalid lighting-only BPM does not reject a map without gameplay objects");
var lateBadBpm=LoadSynthetic(JObject.Parse("""{"_version":"2.6.0","_notes":[{"_time":2,"_type":0,"_cutDirection":1}],"_events":[{"_time":20,"_type":100,"_value":0,"_floatValue":0}]}"""));
Check(lateBadBpm.Input.Notes.Single().Time==1,"Unused BPM after the final object does not change valid note times");
var v4Note=JObject.Parse("""{"version":"4.1.0","colorNotes":[{"b":1,"i":0}],"colorNotesData":[{"x":0,"y":0,"c":0,"d":1}]}""");
var v4Audio=JObject.Parse("""{"songFrequency":44100,"bpmData":[{"si":0,"ei":88200,"sb":0,"eb":4}]}""");
var instantAudio=JObject.Parse("""{"songFrequency":44100,"bpmData":[{"si":0,"ei":0,"sb":0,"eb":4}]}""");
Check(LoadSynthetic(v4Note,instantAudio).Input.Notes.Single().Time==0,"A zero-sample v4 range uses the native instantaneous timeline");
var afterUnusedRange=(JObject)v4Note.DeepClone();afterUnusedRange["colorNotes"]![0]!["b"]=5;
var unusedZeroBeat=JObject.Parse("""{"songFrequency":44100,"bpmData":[{"si":0,"ei":88200,"sb":0,"eb":0},{"si":88200,"ei":176400,"sb":4,"eb":8}]}""");
Check(LoadSynthetic(afterUnusedRange,unusedZeroBeat).Input.Notes.Single().Time==2.5f,"A v4 object's sample-anchored range does not inherit an unused earlier range's invalid tempo");
var emptyV4=JObject.Parse("{\"version\":\"4.1.0\"}");
Check(LoadSynthetic(emptyV4,JObject.Parse("{\"songFrequency\":44100,\"bpmData\":[]}")).Input.Notes.Length==0,"Empty v4 gameplay does not invent a timing range");
foreach(var audioData in new[]{JObject.Parse("{\"songFrequency\":44100,\"bpmData\":[]}"),JObject.Parse("{\"songFrequency\":44100}")})
{
    bool rejected=false;try{LoadSynthetic(v4Note,audioData);}catch(NotSupportedException){rejected=true;}
    Check(rejected,"Missing v4 timing for a gameplay object is explicitly unsupported");
}
bool missingAudioRejected=false;try{LoadSynthetic(v4Note);}catch(NotSupportedException){missingAudioRejected=true;}
Check(missingAudioRejected,"A missing AudioData file is a specific unsupported state rather than a generic file exception");
foreach(int index in new[]{-1,99})
{
    var invalid=(JObject)v4Note.DeepClone();invalid["colorNotes"]![0]!["i"]=index;
    bool rejected=false;try{LoadSynthetic(invalid,v4Audio);}catch(NotSupportedException){rejected=true;}
    Check(rejected,"Invalid v4 data references are explicitly unsupported: "+index);
}
var noV4Data=(JObject)v4Note.DeepClone();noV4Data.Remove("colorNotesData");
bool noDataRejected=false;try{LoadSynthetic(noV4Data,v4Audio);}catch(NotSupportedException){noDataRejected=true;}
Check(noDataRejected,"Referenced v4 note data cannot silently disappear");
var defaultIndex=(JObject)v4Note.DeepClone();((JObject)defaultIndex["colorNotes"]![0]!).Remove("i");
Check(LoadSynthetic(defaultIndex,v4Audio).Input.Notes.Single().X==0,"An omitted zero index retains native deserialization semantics");
var implicitSample=(JObject)v4Audio.DeepClone();((JObject)implicitSample["bpmData"]![0]!).Remove("si");
Check(LoadSynthetic(v4Note,implicitSample).Input.Notes.Single().Time==.5f,"An omitted zero sample index retains native deserialization semantics");
var onlyOwnV4=LoadSynthetic(noV4Data,features:AnalysisFeatures.OwnDeaths);
Check(onlyOwnV4.Input.Duration==100&&onlyOwnV4.AudioJson==null,"Hidden note/timing analysis does not read AudioData or referenced note arrays for own markers");

JObject MappingWall(int x,int width,int y=3000,int height=2000,float beat=2,float duration=1)=>new() {["b"]=beat,["x"]=x,["w"]=width,["y"]=y,["h"]=height,["d"]=duration};
int MappingSquats(params JObject[] walls)=>BeatInsight.WallAlert.WallAlertAdapter.Count(new JObject {["version"]="3.3.0",["obstacles"]=new JArray(walls)}.ToString(),120,true);
Check(MappingSquats(MappingWall(2000,3000))==1,"Mapping wall precision coordinates cover both central lanes");
Check(MappingSquats(MappingWall(2000,1500),MappingWall(2500,2500))==1,"Fractional Mapping wall fragments combine into continuous center coverage");
Check(MappingSquats(MappingWall(2000,1499),MappingWall(2500,2500))==0,"A fractional gap is not rounded away");
Check(MappingSquats(MappingWall(2000,3000,5000))==0,"Elevated Mapping walls do not obstruct the playable head row");
Check(MappingSquats(MappingWall(2000,3000,1000,6000))==0,"Floor walls alone do not create a squat trigger");
Check(MappingSquats(MappingWall(-2000,5000))==1,"Negative precision wall coordinates retain their asymmetric horizontal encoding");
Check(MappingSquats(MappingWall(2000,1000))==0,"Zero-width encoded walls do not become oversized walls");
Check(MappingSquats(MappingWall(2000,3000,height:1000))==0,"Zero-height encoded walls do not obstruct the head");
Check(MappingSquats(MappingWall(2000,3000),MappingWall(2000,3000,beat:2.25f),MappingWall(2000,3000,beat:4))==2,"Mapping squat sequence grouping retains the beat-gap policy");
Check(BeatInsight.WallAlert.WallAlertAdapter.Count("{\"_version\":\"2.6.0\",\"_obstacles\":[{\"_time\":2,\"_duration\":1,\"_lineIndex\":1,\"_width\":2,\"_type\":1004251}]}",120,true)==1,"Legacy packed Mapping walls are decoded before crouch classification");

foreach(int d in new[]{1000,1090,1180,1270,1360,2000,2090,2360})
{
    var mapped=new MapNote {MappingExtensions=true,Direction=d,CutDirectionAngleOffset=0};
    var visual=FirstNoteVisual.From(mapped,DrawColor.White);
    Check(visual!=null&&visual.RotationZ==MappingNotes.Angle(d)&&visual.Direction==(MappingNotes.IsRotatedDot(d)?8:1),"Extended arrow/dot preserves the declared physical angle: "+d);
}
Check(FirstNoteVisual.From(new MapNote {MappingExtensions=true,Direction=1090,CutDirectionAngleOffset=0},DrawColor.White)!.ShouldBlink,"Sideways extended first arrow blinks");
Check(!FirstNoteVisual.From(new MapNote {MappingExtensions=true,Direction=2090,CutDirectionAngleOffset=0},DrawColor.White)!.ShouldBlink,"Rotated dot does not blink as a directional warning");
var extendedSwings=new MapInput {Notes=new[]{new MapNote {Time=1,Direction=1000,MappingExtensions=true},new MapNote {Time=1.02f,Direction=1135,MappingExtensions=true}}};
Check(CompatibleAnalysis.Analyze(extendedSwings).LeftBins[1]==2,"Extended cut angles participate in the compatible swing threshold");
Check(CompatibleAnalysis.NoteAngle(new MapNote {Direction=1090})==0,"Extension angles are not activated without the map capability");

var fullTurn=new MapInput {Notes=new[]{new MapNote {Time=1,Direction=1000,MappingExtensions=true},new MapNote {Time=1.02f,Direction=1360,MappingExtensions=true}}};
Check(CompatibleAnalysis.Analyze(fullTurn).LeftBins[1]==2&&CompatibleAnalysis.Analyze(fullTurn,reproduceKnownDefects:false).LeftBins[1]==1,"REF-002 preserves the compatible extra swing but corrects equivalent full-turn angles in UI statistics");

BeatInsight.WallAlert.SquatWallData ScheduledWall(int x,int width,float beat=2,float time=1,float end=4,int layer=3000,int height=2000)=>new(time,beat,end,x,layer,width,height);
var splitSchedule=new[]{ScheduledWall(2000,1500),ScheduledWall(2500,2500)};
Check(BeatInsight.WallAlert.MappingSquatSchedule.Build(splitSchedule).Length==1,"Prepared fractional fragments produce exactly one signal");
Check(BeatInsight.WallAlert.MappingSquatSchedule.Build(splitSchedule.Reverse().ToArray()).Length==1,"Same-beat signal grouping is independent of fragment order");
Check(BeatInsight.WallAlert.MappingSquatSchedule.Build(new[]{ScheduledWall(2000,1499),ScheduledWall(2500,2500)}).Length==0,"Prepared warning preserves a real fractional gap");
Check(BeatInsight.WallAlert.MappingSquatSchedule.Build(new[]{ScheduledWall(2000,1500,beat:1,time:.5f),ScheduledWall(2500,2500)},1).Length==0,"Initial callback time filter excludes earlier coverage");
Check(BeatInsight.WallAlert.MappingSquatSchedule.Build(new[]{ScheduledWall(2000,3000)},1).Length==1,"Obstacle exactly at the callback filter boundary is retained");
Check(BeatInsight.WallAlert.MappingSquatSchedule.Build(new[]{ScheduledWall(2000,3000,time:.5f)},1).Length==0,"Earlier native song-time obstacle is excluded independently of beat");
Check(BeatInsight.WallAlert.MappingSquatSchedule.Build(Array.Empty<BeatInsight.WallAlert.SquatWallData>()).Length==0,"No-obstacles data produces no prepared warning");
Check(BeatInsight.WallAlert.MappingSquatSchedule.Build(new[]{ScheduledWall(2000,3000,layer:0,height:5)}).Length==0,"Full-height-only transformed data does not create an upper-wall trigger");
Check(BeatInsight.WallAlert.MappingSquatSchedule.Build(new[]{ScheduledWall(2000,3000,height:1000)}).Length==0,"Zero physical height after transformation produces no warning");
Check(BeatInsight.WallAlert.MappingSquatSchedule.Build(new[]{ScheduledWall(2000,3000),ScheduledWall(2000,3000,beat:2.25f,time:1.125f),ScheduledWall(2000,3000,beat:5,time:2.5f,end:6)}).Length==2,"Prepared warning groups retain the half-beat suppression policy");
Check(BeatInsight.WallAlert.MappingSquatSchedule.Build(new[]{ScheduledWall(2000,3000,time:float.NaN),ScheduledWall(2000,3000,end:float.NaN)}).Length==0,"Nonfinite loaded obstacle timing cannot produce a warning");
var playback=new BeatInsight.WallAlert.SquatWarningPlayback(2);
Check(playback.Observe(0)&&!playback.Observe(0)&&playback.Observe(1),"Prepared playback suppresses duplicate callbacks per trigger");
playback.Reset();
Check(playback.Observe(0)&&playback.Observe(1),"Restart or rewind allows the prepared triggers again");
Check(!playback.Observe(-1)&&!playback.Observe(2),"Unknown trigger indices cannot create a warning");
for(int i=0;i<1000;i++){playback.Reset();playback.Observe(0);playback.Observe(1);}
long beforePlayback=GC.GetAllocatedBytesForCurrentThread();
for(int i=0;i<10000;i++){playback.Reset();playback.Observe(0);playback.Observe(1);}
Check(GC.GetAllocatedBytesForCurrentThread()==beforePlayback,"Prepared playback and rewind reset allocate zero managed bytes after warmup");

var plainNoodle=JObject.Parse("""{"version":"3.3.0","colorNotes":[{"b":1,"x":0,"y":0,"c":0,"d":1}]}""");
var noodleCapability=new[]{"Noodle Extensions"};
Check(LoadSynthetic(plainNoodle,requiredExtensions:noodleCapability).NoodleExtensions,"Noodle declaration is accepted for nominal gameplay objects");
foreach(string key in new[]{"coordinates","track","animation","uninteractable","unknownGameplayProperty"})
{
    var changed=(JObject)plainNoodle.DeepClone();changed["colorNotes"]![0]!["customData"]=new JObject {[key]=true};
    bool noodleRefused=false;try{LoadSynthetic(changed,requiredExtensions:noodleCapability);}catch(NotSupportedException e){noodleRefused=e.Message.Contains(key);}
    Check(noodleRefused,"Unvalidated Noodle property has an explicit diagnostic: "+key);
}
var noodleFalse=(JObject)plainNoodle.DeepClone();noodleFalse["colorNotes"]![0]!["customData"]=new JObject {["NE_fake"]=false};
Check(LoadSynthetic(noodleFalse,requiredExtensions:noodleCapability).Input.Notes.Length==1,"Explicitly false Noodle fake flag does not remove a scoring note");
var noodleFake=(JObject)noodleFalse.DeepClone();noodleFake["colorNotes"]![0]!["customData"]!["NE_fake"]=true;
var loadedFake=LoadSynthetic(noodleFake,requiredExtensions:noodleCapability);
Check(loadedFake.Input.Notes.Single().IsFake&&CompatibleAnalysis.Analyze(loadedFake.Input,reproduceKnownDefects:false).LeftBins.Length==0,"True fake identity is retained for compatibility and excluded from corrected counts");
var ownNoodle=LoadSynthetic(noodleFake,features:AnalysisFeatures.OwnDeaths,requiredExtensions:noodleCapability);
Check(ownNoodle.Input.Notes.Length==0&&ownNoodle.Input.Duration==100,"Own marker metadata bypasses hidden Noodle gameplay analysis");
var noodleWall=(JObject)plainNoodle.DeepClone();noodleWall["obstacles"]=new JArray(new JObject {["b"]=2,["x"]=0,["y"]=2,["w"]=4,["h"]=3,["d"]=1,["customData"]=new JObject {["size"]=new JArray(1,1,1)}});
Check(LoadSynthetic(noodleWall,features:AnalysisFeatures.Density,requiredExtensions:noodleCapability).Input.Notes.Length==1,"Hidden wall geometry is not validated for note density");
bool wallRefused=false;try{LoadSynthetic(noodleWall,features:AnalysisFeatures.Squats,requiredExtensions:noodleCapability);}catch(NotSupportedException){wallRefused=true;}
Check(wallRefused,"Visible squat data rejects unvalidated Noodle wall size");

var fakeSequence=new MapInput {Duration=10,Notes=new[]{new MapNote {Time=.5f,Color=0,Direction=0,IsFake=true},new MapNote {Time=1,Color=0,Direction=1},new MapNote {Time=1.5f,Color=0,Direction=3,IsFake=true},new MapNote {Time=2,Color=0,Direction=1}}};
var sequenceRecords=new[]{new PlayRecord {Date=1,LastNote=1}};
var fakeCompatible=CompatibleAnalysis.Analyze(fakeSequence,history:sequenceRecords);
var fakeCorrected=CompatibleAnalysis.Analyze(fakeSequence,reproduceKnownDefects:false,history:sequenceRecords);
Check(fakeCompatible.LeftBins.Sum()==4&&fakeCorrected.LeftBins.Sum()==2,"raw-loader-fake-diagnostic preserves compatible fake swings but excludes them from displayed statistics");
Check(fakeCompatible.FirstLeft==-180&&fakeCorrected.FirstLeft==0,"A fake first cube is not the first displayed gameplay direction");
Check(fakeCompatible.HistorySeconds.Single()==2&&fakeCorrected.HistorySeconds.Single()==2,"Fake notes do not shift compatible history indices in either analysis mode");
Check(fakeCompatible.Segments.Sum()==fakeCorrected.Segments.Sum(),"Both graph variants omit fake notes independently of swing compatibility");
Check(DeathHistory.LegacyMarkers(sequenceRecords,fakeSequence).Single().SongTimeSeconds==2,"Legacy cross uses the non-fake note sequence");
Check(FirstNoteVisual.From(fakeSequence.Notes[0],DrawColor.White)==null,"A fake note cannot become a first-cube visual");
var fakeWallNoodle=(JObject)plainNoodle.DeepClone();fakeWallNoodle["obstacles"]=new JArray(new JObject {["b"]=2,["x"]=0,["y"]=2,["w"]=4,["h"]=3,["d"]=1,["customData"]=new JObject {["NE_fake"]=true,["coordinates"]=new JArray(50,30),["animation"]=new JObject()}});
var fakeWallLoaded=LoadSynthetic(fakeWallNoodle,requiredExtensions:noodleCapability);
Check(fakeWallLoaded.Input.Walls.Single().IsFake&&BeatInsight.WallAlert.WallAlertAdapter.Count(fakeWallLoaded.SquatJson!,120)==0,"Fake decorative wall retains compatibility metadata without becoming a squat");
var fakeArrayNoodle=(JObject)plainNoodle.DeepClone();fakeArrayNoodle["customData"]=new JObject {["fakeColorNotes"]=new JArray(new JObject {["b"]=2,["x"]=1,["y"]=0,["c"]=0,["d"]=1})};
var fakeArrayLoaded=LoadSynthetic(fakeArrayNoodle,requiredExtensions:noodleCapability);
Check(fakeArrayLoaded.Input.Notes.Count(n=>n.IsFake)==1&&fakeArrayLoaded.Json==fakeArrayNoodle.ToString(),"Separate fake array is expanded only in analysis, preserving the original JSON/hash source");
var independentHistory=new MapInput {Notes=new[]{new MapNote {Time=1,ScoringType=1},new MapNote {Time=2,ScoringType=6},new MapNote {Time=3,ScoringType=1}}};
Check(CompatibleAnalysis.Analyze(independentHistory,reproduceKnownDefects:false,history:sequenceRecords).HistorySeconds.Single()==3,"Corrected arc annotations do not change the compatible history sequence");

// Hand-calculated geometry fixtures; no reference implementation is used.
foreach(int major in new[]{2,3})
{
    JObject GeometryMap(JToken coords)=>major==2?
        new JObject {["_version"]="2.6.0",["_notes"]=new JArray(new JObject {["_time"]=1,["_lineIndex"]=3,["_lineLayer"]=2,["_type"]=0,["_cutDirection"]=1,["_customData"]=new JObject {["_position"]=coords}})}:
        new JObject {["version"]="3.3.0",["colorNotes"]=new JArray(new JObject {["b"]=1,["x"]=3,["y"]=2,["c"]=0,["d"]=1,["customData"]=new JObject {["coordinates"]=coords}})};
    foreach(var sample in new[]{(new JArray(-1,1.5f),(float?)1,(float?)1.5f),(new JArray(JValue.CreateNull(),.5f),(float?)null,(float?).5f),(new JArray(-.5f),(float?)1.5f,(float?)null),(new JArray(),(float?)null,(float?)null)})
    {
        var input=GeometryMap(sample.Item1);var geometryLoaded=LoadSynthetic(input,requiredExtensions:noodleCapability);var note=geometryLoaded.Input.Notes.Single();
        Check(note.NoodleX==sample.Item2&&note.NoodleY==sample.Item3&&note.X==3&&note.Y==2,"Noodle partial coordinate fallback preserves base fields v"+major);
        Check(geometryLoaded.Json==input.ToString(),"Noodle geometry preserves source JSON/hash v"+major);
    }
    foreach(var invalid in new JToken[]{new JValue(true),new JValue("1"),new JArray(0,1,2),new JArray("x"),new JArray(double.PositiveInfinity),new JArray(1e30)})
    {
        bool coordinateRefused=false;try{LoadSynthetic(GeometryMap(invalid),requiredExtensions:noodleCapability);}catch(NotSupportedException){coordinateRefused=true;}
        Check(coordinateRefused,"Malformed or unsupported coordinate is rejected v"+major);
    }
}
JObject geometryArc=JObject.Parse("""{"version":"3.3.0","colorNotes":[{"b":1,"x":0,"y":0,"c":0,"d":1,"customData":{"coordinates":[-1,1.5]}},{"b":2,"x":0,"y":0,"c":0,"d":1,"customData":{"coordinates":[0.5,0.5]}}],"sliders":[{"b":1,"x":3,"y":2,"c":0,"d":1,"tb":2,"tx":3,"ty":2,"tc":1,"customData":{"coordinates":[-1,1.5],"tailCoordinates":[0.5,0.5]}}]}""");
var geometryNotes=LoadSynthetic(geometryArc,requiredExtensions:noodleCapability).Input.Notes;
Check(geometryNotes[0].ScoringType==2&&geometryNotes[1].ScoringType==3,"Arc head and tail use effective fractional coordinates rather than base cells");
geometryArc["sliders"]![0]!["customData"]!["coordinates"]=new JArray(1,0);
Check(LoadSynthetic(geometryArc,requiredExtensions:noodleCapability).Input.Notes[0].ScoringType==1,"Different effective arc positions do not match");
geometryArc["sliders"]![0]!["customData"]!["coordinates"]=new JArray(-.9999999,1.5);
Check(LoadSynthetic(geometryArc,requiredExtensions:noodleCapability).Input.Notes[0].ScoringType==2,"Arc coordinates use extension floating-point tolerance");
geometryArc["sliders"]![0]!["customData"]!["coordinates"]=new JArray(-.9999,1.5);
Check(LoadSynthetic(geometryArc,requiredExtensions:noodleCapability).Input.Notes[0].ScoringType==1,"Arc coordinate tolerance does not merge visibly distinct points");

var positionedPair=JObject.Parse("""{"version":"3.3.0","colorNotes":[{"b":1,"x":0,"y":0,"c":0,"d":1,"customData":{"coordinates":[-2,0]}},{"b":1,"x":1,"y":0,"c":0,"d":1,"customData":{"coordinates":[-1,2]}}]}""");
var pairNotes=LoadSynthetic(positionedPair,requiredExtensions:noodleCapability).Input.Notes;
Check(pairNotes.All(n=>n.CutDirectionAngleOffset==0),"Static positions do not invent a new tilt: normal same-color row processing still uses base note fields");
var geometryRequest=SelectedMapAnalyzer.Load(new SelectedMapRequest("geometry-test","Standard","Expert",3,Directory.GetFiles(scratch,"map.dat",SearchOption.AllDirectories).Single(path=>File.ReadAllText(path)==positionedPair.ToString()),legacyHistoryFile:legacyFile,features:AnalysisFeatures.ImportedDeaths));
Check(geometryRequest.LegacyHistoryStatus=="extension-positions-unverified"&&geometryRequest.DeathMarkers.Length==0,"Unverified imported indices are not guessed on custom geometry");
Check(HistoryOverview.Create("legacy","extension-positions-unverified",Array.Empty<DeathMarker>()).Tooltip.Contains("custom geometry"),"Custom geometry history restriction has an English explanation");

// Independent axis-aligned obstacle fixtures. Coverage is the full [1,3] interval
// at nominal upper layer 2, not rounded cells or a reference counter.
foreach(int wallMajor in new[]{2,3})
{
    JObject StaticWall(float x,float y,float width,float height,float beat=1,float duration=1,bool fake=false)
    {
        var custom=new JObject {[wallMajor==2?"_position":"coordinates"]=new JArray(x,y),[wallMajor==2?"_scale":"size"]=new JArray(width,height,JValue.CreateNull()),[wallMajor==2?"_fake":"NE_fake"]=fake};
        return wallMajor==2?new JObject {["_time"]=beat,["_lineIndex"]=1,["_width"]=4,["_type"]=1,["_duration"]=duration,["_customData"]=custom}:
            new JObject {["b"]=beat,["x"]=1,["y"]=2,["w"]=4,["h"]=3,["d"]=duration,["customData"]=custom};
    }
    JObject WallMap(params JObject[] walls)=>new JObject {[wallMajor==2?"_version":"version"]=wallMajor==2?"2.6.0":"3.3.0",[wallMajor==2?"_obstacles":"obstacles"]=new JArray(walls)};
    int WallCount(JObject root)
    {
        var parsed=LoadSynthetic(root,features:AnalysisFeatures.Squats,requiredExtensions:noodleCapability);
        Check(parsed.Json==root.ToString(),"Static wall analysis leaves source JSON unchanged v"+wallMajor);
        return BeatInsight.WallAlert.WallAlertAdapter.Count(parsed.SquatJson??parsed.Json,120,false,true);
    }
    foreach(var fixture in new[]{
        (WallMap(StaticWall(-1,1.5f,2,1)),1),
        (WallMap(StaticWall(-4,1.5f,2,1)),0),
        (WallMap(StaticWall(-1,1.5f,1,1)),0),
        (WallMap(StaticWall(-1,1.5f,1,1),StaticWall(0,1.5f,1,1)),1),
        (WallMap(StaticWall(-1,1.5f,1,1),StaticWall(.01f,1.5f,1,1)),0),
        (WallMap(StaticWall(-2,0,4,5)),0),
        (WallMap(StaticWall(-1,.5f,2,1)),0),
        (WallMap(StaticWall(-1,3,2,1)),0),
        (WallMap(StaticWall(-1,1.5f,0,1)),0),
        (WallMap(StaticWall(-1,1.5f,2,0)),0),
        (WallMap(StaticWall(-1,1.5f,2,1,fake:true)),0),
        (WallMap(StaticWall(-1,1.5f,2,1,beat:1),StaticWall(-1,1.5f,2,1,beat:1.5f)),1),
        (WallMap(StaticWall(-1,1.5f,2,1,beat:1),StaticWall(-1,1.5f,2,1,beat:2)),2)
    })Check(WallCount(fixture.Item1)==fixture.Item2,"Independent Noodle wall coverage/count v"+wallMajor);
    var partial=StaticWall(-1,1.5f,2,1);partial[wallMajor==2?"_customData":"customData"]![wallMajor==2?"_position":"coordinates"]=new JArray(JValue.CreateNull(),1.5);
    Check(WallCount(WallMap(partial))==1,"Wall null horizontal component uses original lane v"+wallMajor);
    foreach(var size in new[]{new JArray(-1,1),new JArray(2,-1),new JArray(2,1,1),new JArray("bad"),new JArray(double.NaN),new JArray(1,2,3,4)})
    {
        var invalidWall=StaticWall(-1,1.5f,2,1);invalidWall[wallMajor==2?"_customData":"customData"]![wallMajor==2?"_scale":"size"]=size;
        bool rejectedSize=false;try{WallCount(WallMap(invalidWall));}catch(NotSupportedException){rejectedSize=true;}
        Check(rejectedSize,"Unsupported wall dimensions are rejected v"+wallMajor);
    }
    var menuWallMap=LoadSynthetic(WallMap(StaticWall(-1,1.5f,2,1)),requiredExtensions:noodleCapability);
    string wallFile=Directory.GetFiles(scratch,"map.dat",SearchOption.AllDirectories).First(path=>File.ReadAllText(path)==menuWallMap.Json);
    var wallMenu=SelectedMapAnalyzer.Load(new SelectedMapRequest("wall-geometry","Standard","Expert",3,wallFile,features:AnalysisFeatures.Squats));
    Check(wallMenu.SquatCount==1&&wallMenu.SquatError==null,"Menu uses Noodle wall geometry v"+wallMajor);
}

var movedWallMap=(JObject)plainNoodle.DeepClone();movedWallMap["obstacles"]=new JArray(new JObject {["b"]=20,["d"]=1,["x"]=1,["y"]=2,["w"]=2,["h"]=3,["customData"]=new JObject {["coordinates"]=new JArray(10,1.5),["size"]=new JArray(2,1)}});
var movedWallInput=LoadSynthetic(movedWallMap,requiredExtensions:noodleCapability).Input;
Check(movedWallInput.Walls.Single().InfluencesWindowEnd==false,"Off-center custom wall does not extend corrected analysis windows");
Check(CompatibleAnalysis.Analyze(movedWallInput,reproduceKnownDefects:false).LeftBins.Length==1,"Corrected window extent uses static custom wall position");
Check(CompatibleAnalysis.Analyze(movedWallInput).LeftBins.Length==11,"Raw compatibility window extent remains unchanged");

Equal(BeatInsight.WallAlert.NoodleWallAnalysis.Duration(2,10,12),.5f);
Equal(BeatInsight.WallAlert.NoodleWallAnalysis.Duration(2,null,0),2);
Equal(BeatInsight.WallAlert.NoodleWallAnalysis.Duration(2,0,12),0);
foreach(var badLength in new[]{(3f,0f),(3f,float.NaN),(-1f,12f),(100f,12f)})
{
    bool invalidLength=false;try{BeatInsight.WallAlert.NoodleWallAnalysis.Duration(2,badLength.Item1,badLength.Item2);}catch(NotSupportedException){invalidLength=true;}
    Check(invalidLength,"Unknown speed, negative or lifetime-exceeding length is rejected");
}
var shortWall=(JObject)plainNoodle.DeepClone();shortWall["obstacles"]=new JArray(new JObject {["b"]=1,["d"]=4,["x"]=1,["y"]=2,["w"]=2,["h"]=3,["customData"]=new JObject {["size"]=new JArray(2,1,10)}});
var shortLoaded=LoadSynthetic(shortWall,requiredExtensions:noodleCapability,movementSpeed:12);
Check(shortLoaded.Input.Walls.Single().CorrectedDuration==.5f&&shortLoaded.Input.Walls.Single().Duration==2,"Physical length affects corrected duration without rewriting raw duration");
Check(shortLoaded.Json==shortWall.ToString()&&BeatInsight.WallAlert.WallAlertAdapter.Count(shortLoaded.SquatJson!,120,false,true)==1,"Short wall projection is accepted without changing JSON/hash");
var zeroLengthWall=(JObject)shortWall.DeepClone();zeroLengthWall["obstacles"]![0]!["customData"]!["size"]![2]=0;
var zeroLoaded=LoadSynthetic(zeroLengthWall,features:AnalysisFeatures.Squats,requiredExtensions:noodleCapability,movementSpeed:12);
Check(zeroLoaded.Input.Notes.Length==0&&BeatInsight.WallAlert.WallAlertAdapter.Count(zeroLoaded.SquatJson!,120,false,true)==0,"Zero physical length creates no squat and does not trigger hidden note analysis");
var bpmLength=(JObject)shortWall.DeepClone();bpmLength["obstacles"]![0]!["customData"]!["size"]![2]=20;bpmLength["bpmEvents"]=new JArray(new JObject {["b"]=2,["m"]=60});
var bpmLengthLoaded=LoadSynthetic(bpmLength,requiredExtensions:noodleCapability,movementSpeed:12);
var projectedWall=JObject.Parse(bpmLengthLoaded.SquatJson!)["obstacles"]![0]!;
Check(Math.Abs((float)projectedWall["d"]!-1.5f)<1e-5,"Physical seconds are converted through BPM segments rather than a fixed beat ratio");
foreach(string unsupportedMotion in new[]{"unknown-speed","variable-speed","object-speed","long-lifetime"})
{
    var invalidWall=(JObject)shortWall.DeepClone();
    if(unsupportedMotion=="variable-speed")invalidWall["njsEvents"]=new JArray(new JObject {["b"]=1});
    if(unsupportedMotion=="object-speed")invalidWall["obstacles"]![0]!["customData"]!["noteJumpMovementSpeed"]=12;
    if(unsupportedMotion=="long-lifetime")invalidWall["obstacles"]![0]!["customData"]!["size"]![2]=1000;
    bool rejectedMotion=false;try{LoadSynthetic(invalidWall,requiredExtensions:noodleCapability,movementSpeed:unsupportedMotion=="unknown-speed"?0:12);}catch(NotSupportedException){rejectedMotion=true;}
    Check(rejectedMotion,"Unverified wall timeline rejected: "+unsupportedMotion);
}
var preparedWalls=new[]{new BeatInsight.WallAlert.NoodleWallSnapshot(1,2,3,new(1,1.5f,1,1)),new BeatInsight.WallAlert.NoodleWallSnapshot(1,2,3,new(2,1.5f,1,1)),new BeatInsight.WallAlert.NoodleWallSnapshot(3,6,7,new(1,1.5f,2,1))};
Check(BeatInsight.WallAlert.NoodleWallAnalysis.Build(preparedWalls).SequenceEqual(new[]{0,2}),"Prepared custom warning indices point to original snapshot objects");
Check(BeatInsight.WallAlert.NoodleWallAnalysis.Build(preparedWalls,2).SequenceEqual(new[]{2}),"Custom warning schedule respects the initial callback filter");

var shortLegacy=JObject.Parse("""{"_version":"2.6.0","_notes":[],"_obstacles":[{"_time":1,"_duration":4,"_lineIndex":1,"_width":2,"_type":1,"_customData":{"_scale":[2,1,10]}}]}""");
var shortLegacyLoaded=LoadSynthetic(shortLegacy,features:AnalysisFeatures.Squats,requiredExtensions:noodleCapability,movementSpeed:12);
Check(Math.Abs((float)JObject.Parse(shortLegacyLoaded.SquatJson!)["_obstacles"]![0]!["_duration"]!-1)<1e-5,"Legacy physical wall length uses seconds rather than raw beat duration");
Check(BeatInsight.WallAlert.WallAlertAdapter.Count(shortLegacyLoaded.SquatJson!,120,false,true)==1,"Legacy shortened wall reaches menu squat analysis");

foreach(int rollMajor in new[]{2,3})
{
    string customKey=rollMajor==2?"_customData":"customData",rotationKey=rollMajor==2?"_localRotation":"localRotation";
    JObject RolledWall(float x,float y,float width,float height,JToken rotation)
    {
        var custom=new JObject {[rollMajor==2?"_position":"coordinates"]=new JArray(x,y),[rollMajor==2?"_scale":"size"]=new JArray(width,height),[rotationKey]=rotation};
        return rollMajor==2?new JObject {["_time"]=1,["_duration"]=2,["_lineIndex"]=0,["_width"]=4,["_type"]=1,[customKey]=custom}:
            new JObject {["b"]=1,["d"]=2,["x"]=0,["y"]=2,["w"]=4,["h"]=3,[customKey]=custom};
    }
    JObject RollMap(JObject wall)=>new JObject {[rollMajor==2?"_version":"version"]=rollMajor==2?"2.6.0":"3.3.0",[rollMajor==2?"_obstacles":"obstacles"]=new JArray(wall)};
    foreach(var fixture in new[]{
        (RolledWall(0,3,4,4,new JArray(0,0,90)),1),
        (RolledWall(-2,3,4,2,new JArray(0,0,180)),1),
        (RolledWall(-4,3,4,4,new JArray(0,0,-90)),1),
        (RolledWall(-4,3,4,4,new JArray(0,0,270)),1),
        (RolledWall(-1,1.5f,2,1,new JArray(0,0,45)),0),
        (RolledWall(-1,1.5f,2,1,new JArray(360,-720,360)),1),
        (RolledWall(-1.5f,3,10,4,new JArray(0,0,30)),1)
    })
    {
        var data=RollMap(fixture.Item1);var parsed=LoadSynthetic(data,features:AnalysisFeatures.Squats,requiredExtensions:noodleCapability);
        Check(BeatInsight.WallAlert.WallAlertAdapter.Count(parsed.SquatJson!,120,false,true)==fixture.Item2,"Independent local-roll squat fixture v"+rollMajor);
        Check(parsed.Json==data.ToString(),"Local roll preserves source JSON/hash v"+rollMajor);
    }
    var thirty=BeatInsight.WallAlert.NoodleWallAnalysis.Read(RolledWall(-1.5f,3,10,4,new JArray(0,0,30)),rollMajor);
    Check(Math.Abs(thirty.X-(5.5-3*Math.Sqrt(3)))<1e-5&&Math.Abs(thirty.X+thirty.Width-(5.5-Math.Sqrt(3)))<1e-5,"Thirty-degree cross section matches independent analytic endpoints v"+rollMajor);
    // Bounding rectangle covers both center lanes, actual 45-degree slice does not.
    var diagonal=BeatInsight.WallAlert.NoodleWallAnalysis.Read(RolledWall(-1,1.5f,2,1,new JArray(0,0,45)),rollMajor);
    Check(diagonal.X>1&&diagonal.X+diagonal.Width<3,"Head-height section avoids bounding-box overcount v"+rollMajor);
    foreach(var rotation in new JToken[]{new JArray(1,0,0),new JArray(0,30,0),new JArray(0,0),new JArray(0,0,"bad"),new JArray(0,0,double.NaN),new JArray(0,0,1000001),new JValue(30)})
    {
        bool badRoll=false;try{LoadSynthetic(RollMap(RolledWall(-1,1.5f,2,1,rotation)),requiredExtensions:noodleCapability);}catch(NotSupportedException){badRoll=true;}
        Check(badRoll,"Unsupported static rotation rejected v"+rollMajor);
    }
    var missingRollHeight=RolledWall(-1,1.5f,2,1,new JArray(0,0,30));missingRollHeight[customKey]![rollMajor==2?"_scale":"size"]![1]=JValue.CreateNull();
    bool missingHeightRefused=false;try{LoadSynthetic(RollMap(missingRollHeight),requiredExtensions:noodleCapability);}catch(NotSupportedException){missingHeightRefused=true;}
    Check(missingHeightRefused,"Rolled wall with game-clipped default height is rejected v"+rollMajor);
    var nullableCustom=RolledWall(-1,1.5f,2,1,JValue.CreateNull());nullableCustom[customKey]=JValue.CreateNull();
    var nullCustomMap=LoadSynthetic(RollMap(nullableCustom),features:AnalysisFeatures.Squats,requiredExtensions:noodleCapability);
    Check(BeatInsight.WallAlert.WallAlertAdapter.Count(nullCustomMap.SquatJson!,120,false,true)==1,"Explicit null wall customData retains ordinary coverage v"+rollMajor);
    var world=RolledWall(-1,1.5f,2,1,new JArray(0,0,0));world[customKey]![rollMajor==2?"_rotation":"worldRotation"]=90;
    bool worldRejected=false;try{LoadSynthetic(RollMap(world),requiredExtensions:noodleCapability);}catch(NotSupportedException){worldRejected=true;}
    Check(worldRejected,"Non-identity world rotation remains unsupported v"+rollMajor);
}

// Runtime base lanes are already mirrored; custom coordinates are not.
foreach(int mirrorMajor in new[]{2,3})foreach(int roll in new[]{0,30,45,90,135,180,270,325})
{
    bool mirrorLegacy=mirrorMajor==2;
    var wall=new JObject {[mirrorLegacy?"_lineIndex":"x"]=0,[mirrorLegacy?"_lineLayer":"y"]=2,[mirrorLegacy?"_width":"w"]=1,["h"]=3,["_type"]=1,
        [mirrorLegacy?"_customData":"customData"]=new JObject {[mirrorLegacy?"_position":"coordinates"]=new JArray(-1,1.5f),[mirrorLegacy?"_scale":"size"]=new JArray(2,1),[mirrorLegacy?"_localRotation":"localRotation"]=new JArray(0,0,roll)}};
    var original=BeatInsight.WallAlert.NoodleWallAnalysis.Read(wall,mirrorMajor);
    wall[mirrorLegacy?"_lineIndex":"x"]=3;
    var reflected=BeatInsight.WallAlert.NoodleWallAnalysis.Read(wall,mirrorMajor,leftHanded:true);
    Check(Math.Abs(original.Width-reflected.Width)<1e-5&&Math.Abs(original.Y-reflected.Y)<1e-5&&Math.Abs(original.Height-reflected.Height)<1e-5,"Roll reflection preserves coverage size and vertical bounds v"+mirrorMajor);
    if(original.Width>0)Check(Math.Abs(reflected.X-(4-original.X-original.Width))<1e-5,"Custom positioned roll reflects the analytic section about center v"+mirrorMajor);
}

var v2ArcGeometry=JObject.Parse("""{"_version":"2.6.0","_notes":[{"_time":1,"_lineIndex":0,"_lineLayer":0,"_type":0,"_cutDirection":1,"_customData":{"_position":[-1,1.5]}},{"_time":2,"_lineIndex":0,"_lineLayer":0,"_type":0,"_cutDirection":1,"_customData":{"_position":[0.5,0.5]}}],"_sliders":[{"_headTime":1,"_headLineIndex":3,"_headLineLayer":2,"_headCutDirection":1,"_tailTime":2,"_tailLineIndex":3,"_tailLineLayer":2,"_tailCutDirection":1,"_colorType":0,"_customData":{"_position":[-1,1.5],"tailCoordinates":[0.5,0.5]}}]}""");
var v2ArcLoaded=LoadSynthetic(v2ArcGeometry,requiredExtensions:noodleCapability);
Check(v2ArcLoaded.Input.Notes.Select(n=>n.ScoringType).SequenceEqual(new[]{2,3}),"Legacy arc head/tail match fractional effective coordinates instead of different raw cells");
Check(v2ArcLoaded.HasNoodleGeometry&&v2ArcLoaded.Json==v2ArcGeometry.ToString(),"Legacy arc overrides are tracked without changing JSON/hash");
Check(v2ArcLoaded.Input.Notes.All(n=>n.CutDirectionAngleOffset==0),"Ordinary legacy arc does not invent a different physical tilt");
foreach(var variant in new[]{("near",-.9999999f,2),("outside",-.9999f,1),("different",1f,1)})
{
    var v2Changed=(JObject)v2ArcGeometry.DeepClone();v2Changed["_sliders"]![0]!["_customData"]!["_position"]![0]=variant.Item2;
    Check(LoadSynthetic(v2Changed,requiredExtensions:noodleCapability).Input.Notes[0].ScoringType==variant.Item3,"Legacy arc floating-point matching: "+variant.Item1);
}
var v2PartialArc=(JObject)v2ArcGeometry.DeepClone();
v2PartialArc["_notes"]![0]!["_lineIndex"]=3;v2PartialArc["_notes"]![0]!["_customData"]!["_position"]=new JArray(JValue.CreateNull(),1.5);
v2PartialArc["_sliders"]![0]!["_customData"]!["_position"]=new JArray(JValue.CreateNull(),1.5);
v2PartialArc["_notes"]![1]!["_lineLayer"]=2;v2PartialArc["_notes"]![1]!["_customData"]!["_position"]=new JArray(.5);
v2PartialArc["_sliders"]![0]!["_customData"]!["tailCoordinates"]=new JArray(.5);
Check(LoadSynthetic(v2PartialArc,requiredExtensions:noodleCapability).Input.Notes.Select(n=>n.ScoringType).SequenceEqual(new[]{2,3}),"Legacy arc missing components use each object's original base fields");
foreach(var badTail in new JToken[]{new JValue(true),new JArray("bad"),new JArray(0,0,0),new JArray(double.PositiveInfinity)})
{
    var v2BadArc=(JObject)v2ArcGeometry.DeepClone();v2BadArc["_sliders"]![0]!["_customData"]!["tailCoordinates"]=badTail;
    bool v2BadTailRefused=false;try{LoadSynthetic(v2BadArc,requiredExtensions:noodleCapability);}catch(NotSupportedException){v2BadTailRefused=true;}
    Check(v2BadTailRefused,"Malformed legacy arc tail is explicitly rejected");
}
var v2AliasArc=(JObject)v2ArcGeometry.DeepClone();((JObject)v2AliasArc["_sliders"]![0]!["_customData"]!).Remove("tailCoordinates");v2AliasArc["_sliders"]![0]!["_customData"]!["_tailPosition"]=new JArray(.5,.5);
bool v2AliasRefused=false;try{LoadSynthetic(v2AliasArc,requiredExtensions:noodleCapability);}catch(NotSupportedException){v2AliasRefused=true;}
Check(v2AliasRefused,"Unrecognized legacy tail aliases are not guessed");
var v2HiddenArc=(JObject)v2ArcGeometry.DeepClone();v2HiddenArc["_sliders"]="unrequested";
Check(LoadSynthetic(v2HiddenArc,features:AnalysisFeatures.Squats,requiredExtensions:noodleCapability).Input.Notes.Length==0,"Hidden legacy arc data is not parsed for a squat-only request");
var v2PlainArc=LoadSynthetic(v2ArcGeometry);
Check(!v2PlainArc.HasNoodleGeometry&&v2PlainArc.Input.Notes.All(n=>n.ScoringType==1),"Legacy custom arc positions are not applied without the required extension");
var v2NullArc=(JObject)v2ArcGeometry.DeepClone();v2NullArc["_sliders"]![0]!["_customData"]=JValue.CreateNull();
Check(LoadSynthetic(v2NullArc,requiredExtensions:noodleCapability).Input.Notes.All(n=>n.ScoringType==1),"Explicit null legacy arc customData uses base positions");
var v2ArcRequest=SelectedMapAnalyzer.Load(new SelectedMapRequest("legacy-arc-test","Standard","Expert",3,Directory.GetFiles(scratch,"map.dat",SearchOption.AllDirectories).First(path=>File.ReadAllText(path)==v2ArcGeometry.ToString()&&File.ReadAllText(Path.Combine(Path.GetDirectoryName(path)!,"Info.dat")).Contains("Noodle Extensions")),legacyHistoryFile:legacyFile,features:AnalysisFeatures.ImportedDeaths));
Check(v2ArcRequest.LegacyHistoryStatus=="extension-positions-unverified"&&v2ArcRequest.DeathMarkers.Length==0,"Legacy arc geometry does not guess imported history positions");

var noodleBurstMap=JObject.Parse("""{"version":"3.3.0","colorNotes":[{"b":2,"x":0,"y":0,"c":0,"d":1},{"b":4,"x":1,"y":0,"c":0,"d":1}],"burstSliders":[{"b":2,"x":0,"y":0,"c":0,"d":1,"tb":3,"tx":1,"ty":2,"sc":4,"s":1}]}""");
var noodleBurstLoaded=LoadSynthetic(noodleBurstMap,requiredExtensions:noodleCapability);
var noodleBurstHead=noodleBurstLoaded.Input.Notes.First();
Check(noodleBurstLoaded.HasNoodleChains&&noodleBurstLoaded.Input.Notes.Length==2&&noodleBurstHead.SliceCount==4,"Supported Noodle burst is one synthetic swing head plus the later note");
Check(noodleBurstHead.CutDirectionAngleOffset==0,"Arrow burst head retains its original angle because native tail direction is Any");
Check(CompatibleAnalysis.Analyze(noodleBurstLoaded.Input,reproduceKnownDefects:false).LeftBins.Sum()==2,"Burst slices do not inflate swing counts");
Check(DeathHistory.LegacyMarkers(new[]{new PlayRecord {LastNote=4,Date=1}},noodleBurstLoaded.Input).Single().SongTimeSeconds==2,"Internal compatible chain sequence repeats the head per slice without shifting the next note");
Check(noodleBurstLoaded.Json==noodleBurstMap.ToString(),"Burst analysis preserves original JSON/hash");
var noodleBurstHidden=LoadSynthetic(noodleBurstMap,features:AnalysisFeatures.Average,requiredExtensions:noodleCapability);
Check(noodleBurstHidden.Input.Notes.First().CutDirectionAngleOffset==null,"Hidden first-note metric skips burst angle calculations");
var noodleBurstSteep=(JObject)noodleBurstMap.DeepClone();noodleBurstSteep["colorNotes"]![0]!["a"]=12;noodleBurstSteep["burstSliders"]![0]!["tx"]=2;noodleBurstSteep["burstSliders"]![0]!["ty"]=0;
Check(LoadSynthetic(noodleBurstSteep,requiredExtensions:noodleCapability).Input.Notes.First().CutDirectionAngleOffset==12,"Burst slope outside the 40-degree policy preserves the original note offset");
var noodleBurstDot=(JObject)noodleBurstMap.DeepClone();noodleBurstDot["colorNotes"]![0]!["d"]=8;noodleBurstDot["colorNotes"]![0]!["a"]=12;noodleBurstDot["burstSliders"]![0]!["d"]=8;
Check(LoadSynthetic(noodleBurstDot,requiredExtensions:noodleCapability).Input.Notes.First().CutDirectionAngleOffset==0,"Dot burst head uses the extension's zero direction vector");
var noodleBurstSpatial=(JObject)noodleBurstMap.DeepClone();noodleBurstSpatial["colorNotes"]![0]!["customData"]=new JObject {["coordinates"]=new JArray(-1,0)};noodleBurstSpatial["burstSliders"]![0]!["customData"]=new JObject {["coordinates"]=new JArray(-1,0),["tailCoordinates"]=new JArray(0,2)};
var noodleBurstSpatialHead=LoadSynthetic(noodleBurstSpatial,requiredExtensions:noodleCapability).Input.Notes.First();
Check(noodleBurstSpatialHead.NoodleX==1&&noodleBurstSpatialHead.CutDirectionAngleOffset==0,"Spatial burst overrides preserve the arrow head angle instead of inventing a slope tilt");
foreach(string scenario in new[]{"orphan","fake-head","direction","color","duplicates","fake-head-overlap","tail-anchor","nearby-arc-row","invalid-count","fractional-count","invalid-time","fake-burst","high-count","overflow-count","overflow-color","overlap-chains","head-position"})
{
    var invalidBurst=(JObject)noodleBurstMap.DeepClone();var burstNotes=(JArray)invalidBurst["colorNotes"]!;var burstData=invalidBurst["burstSliders"]![0]!;
    switch(scenario)
    {
        case "orphan":burstNotes[0]!["x"]=3;break;
        case "fake-head":burstNotes[0]!["customData"]=new JObject {["NE_fake"]=true};break;
        case "direction":burstNotes[0]!["d"]=0;break;
        case "color":burstNotes[0]!["c"]=1;break;
        case "duplicates":burstNotes.Add(burstNotes[0]!.DeepClone());break;
        case "fake-head-overlap":var other=burstNotes[0]!.DeepClone();other["customData"]=new JObject {["NE_fake"]=true};burstNotes.Add(other);break;
        case "tail-anchor":burstNotes.Add(new JObject {["b"]=3,["x"]=1,["y"]=2,["c"]=0,["d"]=1,["customData"]=new JObject {["NE_fake"]=true}});break;
        case "nearby-arc-row":invalidBurst["sliders"]=new JArray(new JObject {["b"]=2.001,["x"]=0,["y"]=0,["c"]=0,["d"]=1,["tb"]=5,["tx"]=3,["ty"]=2,["tc"]=1});break;
        case "invalid-count":burstData["sc"]=0;break;
        case "fractional-count":burstData["sc"]=2.5;break;
        case "invalid-time":burstData["tb"]=1;break;
        case "fake-burst":burstData["customData"]=new JObject {["NE_fake"]=true};break;
        case "high-count":burstData["sc"]=4097;break;
        case "overflow-count":burstData["sc"]=long.MaxValue;break;
        case "overflow-color":burstData["c"]=long.MaxValue;break;
        case "overlap-chains":((JArray)invalidBurst["burstSliders"]!).Add(burstData.DeepClone());break;
        case "head-position":burstData["customData"]=new JObject {["coordinates"]=new JArray(-1,0)};break;
    }
    bool burstRefused=false;try{LoadSynthetic(invalidBurst,requiredExtensions:noodleCapability);}catch(NotSupportedException){burstRefused=true;}
    Check(burstRefused,"Unsupported Noodle burst is explicitly rejected: "+scenario);
}
var noodleBurstFile=Directory.GetFiles(scratch,"map.dat",SearchOption.AllDirectories).First(path=>File.ReadAllText(path)==noodleBurstMap.ToString());
var noodleBurstMenu=SelectedMapAnalyzer.Load(new SelectedMapRequest("burst-test","Standard","Expert",3,noodleBurstFile,legacyHistoryFile:legacyFile,features:AnalysisFeatures.FirstNotes|AnalysisFeatures.ImportedDeaths));
Check(noodleBurstMenu.LegacyHistoryStatus=="extension-positions-unverified"&&noodleBurstMenu.DeathMarkers.Length==0&&noodleBurstMenu.FirstLeftNote?.SliceCount==4,"Menu renders the real burst head and refuses unverified imported indices");

Check(LoadSynthetic(noodleBurstDot,features:AnalysisFeatures.Average,requiredExtensions:noodleCapability).Input.Notes.First().CutDirectionAngleOffset==null,"Hidden first-note metric skips the reachable dot-head angle reset");
var noodleBurstRawTail=(JObject)noodleBurstMap.DeepClone();noodleBurstRawTail["burstSliders"]![0]!["customData"]=new JObject {["tailCoordinates"]=new JArray(0,2)};
((JArray)noodleBurstRawTail["colorNotes"]!).Add(new JObject {["b"]=3,["x"]=1,["y"]=2,["c"]=0,["d"]=1});
Check(LoadSynthetic(noodleBurstRawTail,requiredExtensions:noodleCapability).Input.Notes.Single(n=>n.Beat==3).ScoringType==1,"Burst tail validation uses overridden coordinates rather than raw cells");

var arrowDoubleBurst=(JObject)noodleBurstMap.DeepClone();((JArray)arrowDoubleBurst["colorNotes"]!).Add(new JObject {["b"]=2,["x"]=1,["y"]=2,["c"]=0,["d"]=1});
var arrowDoubleLoaded=LoadSynthetic(arrowDoubleBurst,requiredExtensions:noodleCapability);
Check(arrowDoubleLoaded.Input.Notes.Length==3&&arrowDoubleLoaded.Input.Notes[0].SliceCount==4,"Two-arrow row retains one physical burst head in its original input position");
Check(arrowDoubleLoaded.Input.Notes.Where(n=>n.Time==1).All(n=>Math.Abs(n.CutDirectionAngleOffset!.Value+26.565051f)<1e-4),"Two-arrow burst row uses the independently verified native pair angle");
Check(CompatibleAnalysis.Analyze(arrowDoubleLoaded.Input,reproduceKnownDefects:false).LeftBins.Sum()==2,"Simultaneous same-direction arrows remain one swing followed by the later note");
var directionDoubleBurst=(JObject)arrowDoubleBurst.DeepClone();directionDoubleBurst["colorNotes"]![2]!["d"]=3;
var directionDoubleLoaded=LoadSynthetic(directionDoubleBurst,requiredExtensions:noodleCapability);
Check(directionDoubleLoaded.Input.Notes.Where(n=>n.Time==1).Select(n=>n.Direction).SequenceEqual(new[]{1,3}),"Noodle burst replacement preserves first-note direction instead of moving the head behind its row partner");
foreach(string arcKind in new[]{"head","tail","both"})
{
    var overlapBurst=(JObject)noodleBurstMap.DeepClone();var overlapArcs=new JArray();overlapBurst["sliders"]=overlapArcs;
    if(arcKind!="tail")overlapArcs.Add(new JObject {["b"]=2,["x"]=0,["y"]=0,["c"]=0,["d"]=1,["tb"]=5,["tx"]=3,["ty"]=2,["tc"]=1});
    if(arcKind!="head")overlapArcs.Add(new JObject {["b"]=0,["x"]=3,["y"]=2,["c"]=0,["d"]=1,["tb"]=2,["tx"]=0,["ty"]=0,["tc"]=1});
    var overlapLoaded=LoadSynthetic(overlapBurst,requiredExtensions:noodleCapability);
    Check(overlapLoaded.Input.Notes.Length==2&&overlapLoaded.Input.Notes[0].SliceCount==4,"Shared burst/arc "+arcKind+" is represented once by source identity");
    Check(CompatibleAnalysis.Analyze(overlapLoaded.Input,reproduceKnownDefects:false).LeftBins.Sum()==2,"Shared burst/arc "+arcKind+" does not add an extra swing");
}
var tripleBurst=(JObject)arrowDoubleBurst.DeepClone();((JArray)tripleBurst["colorNotes"]!).Add(new JObject {["b"]=2,["x"]=3,["y"]=0,["c"]=0,["d"]=1});
var triplePrepared=LoadSynthetic(tripleBurst,requiredExtensions:noodleCapability);
Check(triplePrepared.Input.Notes.Length==4&&triplePrepared.Input.Notes.Count(n=>n.Time==1)==3,"Three-note same-hand burst row retains one physical note per source");
Check(triplePrepared.Input.Notes.Where(n=>n.Time==1).All(n=>n.CutDirectionAngleOffset==0),"Three-note row does not apply the native two-note tilt override");
var epsilonBurst=(JObject)noodleBurstMap.DeepClone();epsilonBurst["colorNotes"]![0]!["x"]=1;epsilonBurst["burstSliders"]![0]!["x"]=1;
epsilonBurst["burstSliders"]![0]!["customData"]=new JObject {["coordinates"]=new JArray(-1.0000008,0)};
var secondEpsilonHead=epsilonBurst["burstSliders"]![0]!.DeepClone();secondEpsilonHead["customData"]!["coordinates"]![0]=-.9999992;((JArray)epsilonBurst["burstSliders"]!).Add(secondEpsilonHead);
bool epsilonBurstRefused=false;try{LoadSynthetic(epsilonBurst,requiredExtensions:noodleCapability);}catch(NotSupportedException){epsilonBurstRefused=true;}
Check(epsilonBurstRefused,"Distinct approximate burst positions cannot silently share one physical note or crash replacement lookup");

var reorderedOverlap=(JObject)directionDoubleBurst.DeepClone();reorderedOverlap["colorNotes"]![2]!["d"]=5;
reorderedOverlap["sliders"]=new JArray(
    new JObject {["b"]=2,["x"]=3,["y"]=2,["c"]=0,["d"]=1,["tb"]=5,["tx"]=3,["ty"]=0,["tc"]=1},
    new JObject {["b"]=2,["x"]=0,["y"]=0,["c"]=0,["d"]=1,["tb"]=5,["tx"]=3,["ty"]=2,["tc"]=1});
var reorderedLoaded=LoadSynthetic(reorderedOverlap,requiredExtensions:noodleCapability);
Check(reorderedLoaded.Input.Notes.Length==3&&reorderedLoaded.Input.Notes.First().SliceCount==4,"Arc-after-burst role change does not duplicate the physical head or reorder it");
Check(CompatibleAnalysis.Analyze(reorderedLoaded.Input,reproduceKnownDefects:false).LeftBins.Sum()==3,"Shared head between different-direction arrows cannot introduce a fourth phantom swing");

var uncertainDotRow=(JObject)noodleBurstDot.DeepClone();((JArray)uncertainDotRow["colorNotes"]!).Add(new JObject {["b"]=2,["x"]=1,["y"]=2,["c"]=0,["d"]=1});
var uncertainDotLoaded=LoadSynthetic(uncertainDotRow,requiredExtensions:noodleCapability);
Check(uncertainDotLoaded.Input.Notes.First().Direction==8&&uncertainDotLoaded.Input.Notes.First().CutDirectionAngleOffset==null,"Order-dependent burst dot retains its point and exposes unknown tilt");
Check(uncertainDotLoaded.Input.Notes.Length==3&&CompatibleAnalysis.Analyze(uncertainDotLoaded.Input,reproduceKnownDefects:false).LeftBins.Sum()==2,"Unknown dot tilt does not disable or inflate swing statistics");
var uncertainDotVisual=FirstNoteVisual.From(uncertainDotLoaded.Input.Notes.First(),DrawColor.White)!;
Check(uncertainDotVisual.Direction==8&&uncertainDotVisual.RotationZ==null&&!uncertainDotVisual.ShouldBlink,"Uncertain dot uses existing dashed-cube contract without directional flashing");
Check(Math.Abs(uncertainDotLoaded.Input.Notes.Single(n=>n.Time==1&&n.Direction==1).CutDirectionAngleOffset!.Value+26.565051f)<1e-4,"Burst dot ambiguity does not erase the known partner-arrow angle");
var verticalDotRow=(JObject)uncertainDotRow.DeepClone();verticalDotRow["colorNotes"]![2]!["x"]=0;
Check(LoadSynthetic(verticalDotRow,requiredExtensions:noodleCapability).Input.Notes.First().CutDirectionAngleOffset==0,"Dot angle stays known when pair and extension reset both produce zero");
var noPairCorrectionDot=(JObject)uncertainDotRow.DeepClone();noPairCorrectionDot["colorNotes"]![2]!["x"]=2;noPairCorrectionDot["colorNotes"]![2]!["y"]=0;
Check(LoadSynthetic(noPairCorrectionDot,requiredExtensions:noodleCapability).Input.Notes.First().CutDirectionAngleOffset==0,"Pair beyond forty degrees cannot reintroduce the original nonzero dot offset");
var bothDotRow=(JObject)uncertainDotRow.DeepClone();bothDotRow["colorNotes"]![2]!["d"]=8;
var bothDotLoaded=LoadSynthetic(bothDotRow,requiredExtensions:noodleCapability);
Check(bothDotLoaded.Input.Notes.First().CutDirectionAngleOffset==null&&Math.Abs(bothDotLoaded.Input.Notes.Single(n=>n.Time==1&&n.SliceCount==1).CutDirectionAngleOffset!.Value+26.565051f)<1e-4,"Only the burst dot loses certainty; its ordinary dot partner keeps the native pair angle");
var arrowWithDot=(JObject)arrowDoubleBurst.DeepClone();arrowWithDot["colorNotes"]![2]!["d"]=8;
Check(LoadSynthetic(arrowWithDot,requiredExtensions:noodleCapability).Input.Notes.Where(n=>n.Time==1).All(n=>Math.Abs(n.CutDirectionAngleOffset!.Value+26.565051f)<1e-4),"An arrow burst with an ordinary dot partner has no burst-dot reset ambiguity");
var hiddenUncertain=LoadSynthetic(uncertainDotRow,features:AnalysisFeatures.Average,requiredExtensions:noodleCapability);
Check(hiddenUncertain.Input.Notes.All(n=>n.CutDirectionAngleOffset==null),"Hidden first-note metric does not calculate dot-pair angle certainty");
var uncertainDotFile=Directory.GetFiles(scratch,"map.dat",SearchOption.AllDirectories).First(path=>File.ReadAllText(path)==uncertainDotRow.ToString());
var uncertainDotMenu=SelectedMapAnalyzer.Load(new SelectedMapRequest("dot-row","Standard","Expert",3,uncertainDotFile,features:AnalysisFeatures.FirstNotes|AnalysisFeatures.Average));
Check(uncertainDotMenu.HasLeftNote&&uncertainDotMenu.FirstLeftNote?.CutDirectionAngleOffset==null&&uncertainDotMenu.Corrected.LeftBins.Sum()==2,"Menu retains a ready first dot and average result with unknown tilt");
var burstTailAnchor=(JObject)noodleBurstMap.DeepClone();((JArray)burstTailAnchor["colorNotes"]!).Add(new JObject {["b"]=3,["x"]=1,["y"]=2,["c"]=1,["d"]=8,["a"]=17});
var burstTailLoaded=LoadSynthetic(burstTailAnchor,requiredExtensions:noodleCapability);
var tailAnchorNote=burstTailLoaded.Input.Notes.Single(n=>n.Time==1.5f);
Check(tailAnchorNote.ScoringType==3&&tailAnchorNote.Color==1&&tailAnchorNote.Direction==8&&tailAnchorNote.CutDirectionAngleOffset==17,"Real opposite-color burst tail anchor preserves dot direction and angle with native tail role");
Check(burstTailLoaded.Input.Notes.Length==3&&CompatibleAnalysis.Analyze(burstTailLoaded.Input,reproduceKnownDefects:false).RightBins.Sum()==1,"Burst tail anchor remains a physical swing, not a generated slice");
Check(burstTailLoaded.Json==burstTailAnchor.ToString(),"Tail anchor support leaves original JSON/hash unchanged");
var spatialTailAnchor=(JObject)burstTailAnchor.DeepClone();spatialTailAnchor["colorNotes"]![2]!["customData"]=new JObject {["coordinates"]=new JArray(-.75f,1.25f)};spatialTailAnchor["burstSliders"]![0]!["customData"]=new JObject {["tailCoordinates"]=new JArray(-.75f,1.25f)};
Check(LoadSynthetic(spatialTailAnchor,requiredExtensions:noodleCapability).Input.Notes.Single(n=>n.Time==1.5f).ScoringType==3,"Burst tail anchor matches effective static coordinates");
Check(LoadSynthetic(burstTailAnchor,features:AnalysisFeatures.Average,requiredExtensions:noodleCapability).Input.Notes.All(n=>n.CutDirectionAngleOffset==null),"Tail role preparation does not calculate hidden orientations");
foreach(string kind in new[]{"nearby","duplicate","bomb"})
{
    var invalid=(JObject)burstTailAnchor.DeepClone();var anchor=invalid["colorNotes"]![2]!;
    if(kind=="nearby")anchor["b"]=3.0005f;
    if(kind=="duplicate")((JArray)invalid["colorNotes"]!).Add(anchor.DeepClone());
    if(kind=="bomb"){((JArray)invalid["colorNotes"]!).RemoveAt(2);invalid["bombNotes"]=new JArray(new JObject {["b"]=3,["x"]=1,["y"]=2});}
    if(kind=="chain-head")((JArray)invalid["burstSliders"]!).Add(new JObject {["b"]=3,["x"]=1,["y"]=2,["c"]=1,["d"]=8,["tb"]=5,["tx"]=2,["ty"]=0,["sc"]=4,["s"]=1});
    bool rejected=false;try{LoadSynthetic(invalid,requiredExtensions:noodleCapability);}catch(NotSupportedException){rejected=true;}
    Check(rejected,$"Unverified burst tail {kind} remains explicitly unsupported");
}
foreach(string kind in new[]{"arc-head","arc-tail","chain-head"})
{
    var junction=(JObject)burstTailAnchor.DeepClone();
    var next=new JObject {["b"]=3,["x"]=1,["y"]=2,["c"]=1,["d"]=8,["tb"]=5,["tx"]=2,["ty"]=0,["tc"]=1,["sc"]=4,["s"]=1};
    if(kind=="chain-head")((JArray)junction["burstSliders"]!).Add(next);
    else {if(kind=="arc-tail"){next["b"]=0;next["x"]=2;next["tb"]=3;next["tx"]=1;next["ty"]=2;}junction["sliders"]=new JArray(next);}
    var junctionLoaded=LoadSynthetic(junction,requiredExtensions:noodleCapability);
    var anchor=junctionLoaded.Input.Notes.Single(n=>n.Time==1.5f);
    Check(junctionLoaded.Input.Notes.Length==3&&anchor.ScoringType==(kind=="chain-head"?1:kind=="arc-head"?6:3),$"Tail junction {kind} keeps one physical note with the correct logical role");
    Check(anchor.CutDirectionAngleOffset==(kind=="chain-head"?0:17)&&anchor.Direction==8,$"Tail junction {kind} preserves the native dot angle policy");
    Check(CompatibleAnalysis.Analyze(junctionLoaded.Input,reproduceKnownDefects:false).RightBins.Sum()==1,$"Tail junction {kind} does not duplicate its shared swing");
    Check(junctionLoaded.Json==junction.ToString(),$"Tail junction {kind} preserves source JSON/hash");
    if(kind!="arc-tail")
    {
        next["b"]=3.0005f;
        bool rejected=false;try{LoadSynthetic(junction,requiredExtensions:noodleCapability);}catch(NotSupportedException){rejected=true;}
        Check(rejected,$"Nearby {kind} on a burst tail remains unsupported");
    }
}
var fakeBurstPartner=(JObject)arrowDoubleBurst.DeepClone();fakeBurstPartner["colorNotes"]![2]!["customData"]=new JObject {["NE_fake"]=true};
var fakePartnerLoaded=LoadSynthetic(fakeBurstPartner,requiredExtensions:noodleCapability);
Check(fakePartnerLoaded.Input.Notes.Count(n=>n.IsFake)==1&&Math.Abs(fakePartnerLoaded.Input.Notes.First().CutDirectionAngleOffset!.Value+26.565051f)<1e-4,"Fake burst partner participates in native tilt preparation");
Check(CompatibleAnalysis.Analyze(fakePartnerLoaded.Input,reproduceKnownDefects:false).LeftBins.Sum()==2,"Fake burst partner is excluded from corrected swings");
var fakeDotPartner=(JObject)uncertainDotRow.DeepClone();fakeDotPartner["colorNotes"]![2]!["customData"]=new JObject {["NE_fake"]=true};
Check(LoadSynthetic(fakeDotPartner,requiredExtensions:noodleCapability).Input.Notes.First().CutDirectionAngleOffset==null,"Fake partner can make the real burst dot tilt order-dependent");
var fakeArrayPartner=(JObject)fakeBurstPartner.DeepClone();var fakeArrayNote=fakeArrayPartner["colorNotes"]![2]!;fakeArrayNote.Remove();fakeArrayPartner.AddFirst(new JProperty("customData",new JObject {["fakeColorNotes"]=new JArray(fakeArrayNote)}));
var fakeBurstArrayLoaded=LoadSynthetic(fakeArrayPartner,requiredExtensions:noodleCapability);
Check(fakeBurstArrayLoaded.Input.Notes.First().IsFake&&Math.Abs(fakeBurstArrayLoaded.Input.Notes.Single(n=>n.SliceCount==4).CutDirectionAngleOffset!.Value+26.565051f)<1e-4,"Prepended fakeColorNotes retains fake identity and real head tilt");
var fakePartnerFile=Directory.GetFiles(scratch,"map.dat",SearchOption.AllDirectories).First(path=>File.ReadAllText(path)==fakeArrayPartner.ToString());
var fakePartnerMenu=SelectedMapAnalyzer.Load(new SelectedMapRequest("fake-partner","Standard","Expert",3,fakePartnerFile,features:AnalysisFeatures.FirstNotes));
Check(fakePartnerMenu.FirstLeftNote?.SliceCount==4&&!fakePartnerMenu.FirstLeftNote.IsFake,"Prepended fake partner is not chosen as the first real note");
Check(LoadSynthetic(fakeBurstPartner,features:AnalysisFeatures.Average,requiredExtensions:noodleCapability).Input.Notes.All(n=>n.CutDirectionAngleOffset==null),"Hidden first notes skip fake-partner tilt calculations");
var fakeTripleBurst=(JObject)tripleBurst.DeepClone();fakeTripleBurst["colorNotes"]![3]!["customData"]=new JObject {["NE_fake"]=true};
Check(LoadSynthetic(fakeTripleBurst,requiredExtensions:noodleCapability).Input.Notes.Where(n=>n.Time==1).All(n=>n.CutDirectionAngleOffset==0),"Fake note still counts in the native row size and prevents pair tilt for three objects");
Check(fakePartnerLoaded.Json==fakeBurstPartner.ToString(),"Fake partner support preserves original source JSON/hash");
var orphanOnly=(JObject)noodleBurstMap.DeepClone();((JArray)orphanOnly["colorNotes"]!).RemoveAt(0);
var orphanPrepared=LoadSynthetic(orphanOnly,requiredExtensions:noodleCapability);
Check(orphanPrepared.Input.Notes.Length==2&&orphanPrepared.Input.Notes.First().Direction==8&&Math.Abs(orphanPrepared.Input.Notes.First().Time-(1f+.5f/3))<1e-5,"Isolated orphan starts at its first real slice, not at an invented head");
Check(orphanPrepared.Input.Notes.First().SliceCount==3&&orphanPrepared.Input.Notes.First().CutDirectionAngleOffset==null,"Orphan preserves actual slice count and explicitly unknown dot tilt");
Check(CompatibleAnalysis.Analyze(orphanPrepared.Input,reproduceKnownDefects:false).LeftBins.Sum()==2,"Isolated orphan contributes one swing before the later normal note");
Check(orphanPrepared.HasNoodleChains&&orphanPrepared.Json==orphanOnly.ToString(),"Orphan retains history guard and original JSON/hash");
var orphanFile=Directory.GetFiles(scratch,"map.dat",SearchOption.AllDirectories).First(path=>File.ReadAllText(path)==orphanOnly.ToString());
var orphanMenu=SelectedMapAnalyzer.Load(new SelectedMapRequest("orphan-isolated","Standard","Expert",3,orphanFile,legacyHistoryFile:legacyFile,features:AnalysisFeatures.FirstNotes|AnalysisFeatures.Average|AnalysisFeatures.Density|AnalysisFeatures.ImportedDeaths));
Check(orphanMenu.HasLeftNote&&orphanMenu.FirstLeftNote?.Direction==8&&Math.Abs(orphanMenu.FirstLeftNote.Time-(1+.5f/3))<1e-5&&orphanMenu.FirstLeftNote.CutDirectionAngleOffset==null,"Menu shows the first actual orphan slice as a dot with unknown tilt");
Check(orphanMenu.LegacyHistoryStatus=="extension-positions-unverified"&&orphanMenu.DeathMarkers.Length==0,"Orphan slice indices never expose unverified imported crosses");
Check(Math.Abs(orphanMenu.Corrected.Segments.Sum()*orphanMenu.Duration/90-2)<1e-4,"Orphan density uses one logical chain event plus the later note");
var twoSliceOrphan=(JObject)orphanOnly.DeepClone();twoSliceOrphan["burstSliders"]![0]!["sc"]=2;
var twoSliceNote=LoadSynthetic(twoSliceOrphan,requiredExtensions:noodleCapability).Input.Notes.First();
Check(twoSliceNote.Time==1.5f&&twoSliceNote.X==1&&twoSliceNote.Y==2&&twoSliceNote.SliceCount==1,"Two-slice orphan has one actual element at the tail time and raw tail position");
foreach(string kind in new[]{"note","arc","chain"})
{
    var unsupported=(JObject)orphanOnly.DeepClone();
    if(kind=="note")((JArray)unsupported["colorNotes"]!).Add(new JObject {["b"]=2.5f,["x"]=3,["y"]=0,["c"]=1,["d"]=1});
    if(kind=="arc")unsupported["sliders"]=new JArray(new JObject {["b"]=1,["x"]=3,["y"]=0,["c"]=1,["d"]=1,["tb"]=4,["tx"]=3,["ty"]=2,["tc"]=1});
    if(kind=="chain")((JArray)unsupported["burstSliders"]!).Add(new JObject {["b"]=1,["x"]=3,["y"]=0,["c"]=1,["d"]=1,["tb"]=4,["tx"]=3,["ty"]=2,["sc"]=4,["s"]=1});
    bool rejected=false;try{LoadSynthetic(unsupported,requiredExtensions:noodleCapability);}catch(NotSupportedException){rejected=true;}
    Check(rejected,$"Orphan {kind} overlap remains explicitly unsupported");
}
foreach(string geometry in new[]{"head","tail","both","partial"})
{
    var spatialOrphan=(JObject)orphanOnly.DeepClone();var data=new JObject();
    if(geometry=="head"||geometry=="both")data["coordinates"]=new JArray(-2.5f,.75f);
    if(geometry=="tail"||geometry=="both")data["tailCoordinates"]=new JArray(3.25f,4.5f);
    if(geometry=="partial"){data["coordinates"]=new JArray(JValue.CreateNull(),.75f);data["tailCoordinates"]=new JArray(3.25f,JValue.CreateNull());}
    spatialOrphan["burstSliders"]![0]!["customData"]=data;
    var spatialOrphanLoaded=LoadSynthetic(spatialOrphan,requiredExtensions:noodleCapability);
    Check(spatialOrphanLoaded.HasNoodleGeometry&&Math.Abs(spatialOrphanLoaded.Input.Notes.First().Time-(1+.5f/3))<1e-5&&spatialOrphanLoaded.Input.Notes.First().Direction==8,$"Static orphan {geometry} geometry preserves first slice timing and point");
    Check(spatialOrphanLoaded.Input.Notes.First().CutDirectionAngleOffset==null&&CompatibleAnalysis.Analyze(spatialOrphanLoaded.Input,reproduceKnownDefects:false).LeftBins.Sum()==2,$"Static orphan {geometry} geometry does not invent tilt or extra swings");
    Check(spatialOrphanLoaded.Json==spatialOrphan.ToString(),$"Static orphan {geometry} preserves source JSON/hash");
}
foreach(int major in new[]{2,3})foreach(var anchorFlag in new JToken[]{new JValue(false),new JValue(true),JValue.CreateNull()})
{
    var anchorMap=major==2?JObject.Parse("""{"_version":"2.6.0","_notes":[{"_time":2,"_lineIndex":0,"_lineLayer":0,"_type":0,"_cutDirection":1},{"_time":4,"_lineIndex":1,"_lineLayer":0,"_type":0,"_cutDirection":1}],"_obstacles":[{"_time":6,"_duration":1,"_lineIndex":1,"_width":2,"_type":1}]}"""):
        JObject.Parse("""{"version":"3.3.0","colorNotes":[{"b":2,"x":0,"y":0,"c":0,"d":1},{"b":4,"x":1,"y":0,"c":0,"d":1}],"obstacles":[{"b":6,"d":1,"x":1,"y":2,"w":2,"h":3}]}""");
    var anchorBaseline=LoadSynthetic(anchorMap,requiredExtensions:noodleCapability);
    foreach(var obj in ((JArray)anchorMap[major==2?"_notes":"colorNotes"]!).Concat((JArray)anchorMap[major==2?"_obstacles":"obstacles"]!))obj[major==2?"_customData":"customData"]=new JObject {["_isAnchor"]=anchorFlag.DeepClone()};
    var anchorLoaded=LoadSynthetic(anchorMap,requiredExtensions:noodleCapability);
    Check(CompatibleAnalysis.Analyze(anchorLoaded.Input,reproduceKnownDefects:false).LeftBins.Sum()==2&&anchorLoaded.Input.Notes.All(n=>n.CutDirectionAngleOffset==0)&&anchorLoaded.Json==anchorMap.ToString(),$"v{major} neutral _isAnchor {anchorFlag.Type} preserves swings, tilt and source JSON");
    Check(WallAlertAdapter.Count(anchorLoaded.SquatJson??anchorLoaded.Json,anchorLoaded.Bpm,false,true)==WallAlertAdapter.Count(anchorBaseline.SquatJson??anchorBaseline.Json,anchorBaseline.Bpm,false,true),$"v{major} _isAnchor does not change wall coverage");
}
foreach(var badAnchor in new JToken[]{new JValue(1),new JValue("true")})
{
    var invalidAnchor=(JObject)noodleBurstMap.DeepClone();invalidAnchor["colorNotes"]![0]!["customData"]=new JObject {["_isAnchor"]=badAnchor};
    bool rejected=false;try{LoadSynthetic(invalidAnchor,requiredExtensions:noodleCapability);}catch(NotSupportedException){rejected=true;}
    Check(rejected,"Malformed anchor metadata remains unsupported");
}
var animatedAnchor=(JObject)noodleBurstMap.DeepClone();animatedAnchor["colorNotes"]![0]!["customData"]=new JObject {["_isAnchor"]=true,["animation"]=new JObject {["offsetPosition"]=new JArray(1,0,0)}};
bool animatedAnchorRejected=false;try{LoadSynthetic(animatedAnchor,requiredExtensions:noodleCapability);}catch(NotSupportedException){animatedAnchorRejected=true;}
Check(animatedAnchorRejected,"Neutral anchor metadata does not bypass unsupported animation");
foreach(string axis in new[]{"x","y","both"})
{
    var extendedWallMap=JObject.Parse("""{"version":"3.3.0","colorNotes":[{"b":1,"x":0,"y":0,"c":0,"d":1}],"obstacles":[{"b":2,"d":2,"x":0,"y":2,"w":1,"h":3,"customData":{"coordinates":[-1,1.5],"size":[2,1]}}]}""");
    if(axis!="y")extendedWallMap["obstacles"]![0]!["x"]=6;
    if(axis!="x")extendedWallMap["obstacles"]![0]!["y"]=9;
    var extendedWallLoaded=LoadSynthetic(extendedWallMap,requiredExtensions:noodleCapability);
    Check(WallAlertAdapter.Count(extendedWallLoaded.SquatJson!,120,false,true)==1,$"Extended wall {axis} uses explicit geometry for squat coverage");
    Check(extendedWallLoaded.Input.Walls.Single().InfluencesWindowEnd==true&&extendedWallLoaded.Json==extendedWallMap.ToString(),$"Extended wall {axis} preserves window coverage and source JSON");
}
var extendedLegacyWall=JObject.Parse("""{"_version":"2.6.0","_notes":[],"_obstacles":[{"_time":2,"_duration":2,"_lineIndex":6,"_width":1,"_type":1,"_customData":{"_position":[-1,1.5],"_scale":[2,1]}}]}""");
Check(WallAlertAdapter.Count(LoadSynthetic(extendedLegacyWall,requiredExtensions:noodleCapability).SquatJson!,120,false,true)==1,"Legacy extended lane is replaced by explicit Noodle position");
foreach(string invalidKind in new[]{"bare-negative-width","y-without-height","null-y","range","note"})
{
    var badWallMap=JObject.Parse("""{"version":"3.3.0","colorNotes":[{"b":1,"x":0,"y":0,"c":0,"d":1}],"obstacles":[{"b":2,"d":2,"x":6,"y":9,"w":1,"h":3,"customData":{"coordinates":[-1,1.5],"size":[2,1]}}]}""");
    var wall=badWallMap["obstacles"]![0]!;
    if(invalidKind=="bare-negative-width"){wall["customData"]!["coordinates"]![0]=JValue.CreateNull();wall["w"]=-2;}
    if(invalidKind=="y-without-height")wall["customData"]!["size"]![1]=JValue.CreateNull();
    if(invalidKind=="null-y")wall["customData"]!["coordinates"]![1]=JValue.CreateNull();
    if(invalidKind=="range")wall["x"]=1000001;
    if(invalidKind=="note"){badWallMap["colorNotes"]![0]!["x"]=6;badWallMap["colorNotes"]![0]!["customData"]=new JObject {["coordinates"]=new JArray(-1,0)};}
    bool rejected=false;try{LoadSynthetic(badWallMap,requiredExtensions:noodleCapability);}catch(NotSupportedException){rejected=true;}
    Check(rejected,$"Unvalidated extended coordinate {invalidKind} remains unsupported");
}
foreach(int major in new[]{2,3})foreach(var indexMetadata in new JToken[]{new JValue(0),new JValue(9),new JValue(-1),new JValue(105.75),new JValue(false),new JValue(true),new JValue("29"),new JValue("marker"),new JValue(new DateTime(2020,1,1,0,0,0,DateTimeKind.Utc)),JValue.CreateNull()})
{
    var indexedMap=major==2?JObject.Parse("""{"_version":"2.6.0","_notes":[{"_time":2,"_lineIndex":0,"_lineLayer":0,"_type":0,"_cutDirection":1},{"_time":4,"_lineIndex":1,"_lineLayer":0,"_type":0,"_cutDirection":1}],"_obstacles":[{"_time":6,"_duration":1,"_lineIndex":1,"_width":2,"_type":1}]}"""):(JObject)noodleBurstMap.DeepClone();
    var objects=((JArray)indexedMap[major==2?"_notes":"colorNotes"]!).AsEnumerable();
    objects=objects.Concat((JArray)indexedMap[major==2?"_obstacles":"burstSliders"]!);
    foreach(var obj in objects)obj[major==2?"_customData":"customData"]=new JObject {["_paul"]=indexMetadata.DeepClone()};
    var indexedLoaded=LoadSynthetic(indexedMap,requiredExtensions:noodleCapability);
    Check(CompatibleAnalysis.Analyze(indexedLoaded.Input,reproduceKnownDefects:false).LeftBins.Sum()==2&&indexedLoaded.Input.Notes.All(n=>!n.IsFake),$"v{major} scalar metadata does not hide real notes or alter swings");
    Check(indexedLoaded.Input.Notes.First().CutDirectionAngleOffset==0&&indexedLoaded.Json==indexedMap.ToString(),$"v{major} scalar metadata preserves tilt and source JSON");
}
foreach(var badIndexMetadata in new JToken[]{new JObject(),new JArray(),new JArray(1,2)})
{
    var badIndexMap=(JObject)noodleBurstMap.DeepClone();badIndexMap["colorNotes"]![0]!["customData"]=new JObject {["_paul"]=badIndexMetadata};
    bool rejected=false;try{LoadSynthetic(badIndexMap,requiredExtensions:noodleCapability);}catch(NotSupportedException){rejected=true;}
    Check(rejected,"Structured unused metadata remains unsupported");
}
var animatedIndexMap=(JObject)noodleBurstMap.DeepClone();animatedIndexMap["colorNotes"]![0]!["customData"]=new JObject {["_paul"]=9,["animation"]=new JObject {["offsetPosition"]=new JArray(1,0,0)}};
bool animatedIndexRejected=false;try{LoadSynthetic(animatedIndexMap,requiredExtensions:noodleCapability);}catch(NotSupportedException){animatedIndexRejected=true;}
Check(animatedIndexRejected,"Integer metadata does not bypass unsupported animation");
foreach(var flagSpec in new[]{(2,"_disableNoteGravity"),(2,"_disableNoteLook"),(3,"disableNoteGravity"),(3,"disableNoteLook"),(3,"disableBadCutDirection"),(3,"disableBadCutSpeed"),(3,"disableBadCutSaberType")})
{
    int major=flagSpec.Item1;string field=flagSpec.Item2;
    JObject FlagMap(JToken value)
    {
        var root=major==2?JObject.Parse("""{"_version":"2.6.0","_notes":[{"_time":2,"_lineIndex":0,"_lineLayer":0,"_type":0,"_cutDirection":1},{"_time":4,"_lineIndex":1,"_lineLayer":0,"_type":0,"_cutDirection":1}],"_sliders":[{"_headTime":2,"_headLineIndex":0,"_headLineLayer":0,"_headCutDirection":1,"_tailTime":4,"_tailLineIndex":1,"_tailLineLayer":0,"_tailCutDirection":1,"_colorType":0}]}"""):(JObject)noodleBurstMap.DeepClone();
        foreach(var obj in ((JArray)root[major==2?"_notes":"colorNotes"]!).Concat((JArray)root[major==2?"_sliders":"burstSliders"]!))obj[major==2?"_customData":"customData"]=new JObject {[field]=value.DeepClone()};
        return root;
    }
    foreach(var defaultValue in new JToken[]{new JValue(false),JValue.CreateNull()})
    {
        var defaultMap=FlagMap(defaultValue);var defaultLoaded=LoadSynthetic(defaultMap,requiredExtensions:noodleCapability);
        Check(CompatibleAnalysis.Analyze(defaultLoaded.Input,reproduceKnownDefects:false).LeftBins.Sum()==2&&defaultLoaded.Input.Notes.All(n=>n.CutDirectionAngleOffset==0)&&defaultLoaded.Json==defaultMap.ToString(),$"Default {field} preserves swings, tilt and source JSON");
    }
    foreach(var unsupportedValue in new JToken[]{new JValue(true),new JValue(0),new JValue("false")})
    {
        bool rejected=false;try{LoadSynthetic(FlagMap(unsupportedValue),requiredExtensions:noodleCapability);}catch(NotSupportedException){rejected=true;}
        Check(rejected,$"Active or malformed {field} remains unsupported");
    }
}
foreach(int cutMask in Enumerable.Range(1,7))
{
    var baseline=(JObject)noodleBurstMap.DeepClone();baseline.Remove("burstSliders");
    var flagged=(JObject)baseline.DeepClone();var metadata=new JObject();
    string[] cutFields={"disableBadCutDirection","disableBadCutSpeed","disableBadCutSaberType"};
    for(int bit=0;bit<3;bit++)metadata[cutFields[bit]]=(cutMask&(1<<bit))!=0;
    foreach(var note in (JArray)flagged["colorNotes"]!)note["customData"]=metadata.DeepClone();
    var plain=LoadSynthetic(baseline,requiredExtensions:noodleCapability);var cutLoaded=LoadSynthetic(flagged,requiredExtensions:noodleCapability);
    Check(JToken.DeepEquals(JArray.FromObject(cutLoaded.Input.Notes),JArray.FromObject(plain.Input.Notes))&&cutLoaded.HasNoodleGeometry==plain.HasNoodleGeometry&&cutLoaded.Json==flagged.ToString(),"Cut flags preserve nominal note data, geometry status and source JSON");
}
foreach(string alias in new[]{"_disableNoteGravity","_disableNoteLook"})
{
    var wrongVersionFlag=(JObject)noodleBurstMap.DeepClone();wrongVersionFlag["colorNotes"]![0]!["customData"]=new JObject {[alias]=false};
    bool rejected=false;try{LoadSynthetic(wrongVersionFlag,requiredExtensions:noodleCapability);}catch(NotSupportedException){rejected=true;}
    Check(rejected,"Default flag support does not silently accept a different format version alias");
}
var combinedDefaultFlags=(JObject)noodleBurstMap.DeepClone();combinedDefaultFlags["colorNotes"]![0]!["customData"]=new JObject {["disableNoteGravity"]=false,["disableNoteLook"]=false,["disableBadCutDirection"]=false,["disableBadCutSpeed"]=false,["disableBadCutSaberType"]=false};
Check(CompatibleAnalysis.Analyze(LoadSynthetic(combinedDefaultFlags,requiredExtensions:noodleCapability).Input,reproduceKnownDefects:false).LeftBins.Sum()==2,"Combined neutral note flags retain the expected swings");
combinedDefaultFlags["colorNotes"]![0]!["customData"]!["animation"]=new JObject {["offsetPosition"]=new JArray(1,0,0)};
bool defaultFlagsAnimationRejected=false;try{LoadSynthetic(combinedDefaultFlags,requiredExtensions:noodleCapability);}catch(NotSupportedException){defaultFlagsAnimationRejected=true;}
Check(defaultFlagsAnimationRejected,"Default flags do not bypass unsupported animation");
foreach(string nonFiniteMetadata in new[]{"NaN","Infinity","1e999"})
{
    var nonFiniteMap=(JObject)noodleBurstMap.DeepClone();nonFiniteMap["colorNotes"]![0]!["customData"]=new JObject {["_paul"]=105.75};
    LoadSynthetic(nonFiniteMap,requiredExtensions:noodleCapability);
    var nonFiniteFile=Directory.GetFiles(scratch,"map.dat",SearchOption.AllDirectories).First(path=>File.ReadAllText(path)==nonFiniteMap.ToString());
    File.WriteAllText(nonFiniteFile,nonFiniteMap.ToString().Replace("105.75",nonFiniteMetadata));
    bool rejected=false;try{LocalMap.Load(new MapCase {Folder=Path.GetDirectoryName(nonFiniteFile)!,File="map.dat",Characteristic="Standard",Difficulty="Expert"});}catch(NotSupportedException){rejected=true;}
    Check(rejected,$"Non-finite {nonFiniteMetadata} metadata remains unsupported");
}
var legacyAngleMap=JObject.Parse("""{"_version":"2.6.0","_notes":[{"_time":2,"_lineIndex":0,"_lineLayer":0,"_type":0,"_cutDirection":4,"_customData":{"_cutDirection":45}},{"_time":2.04,"_lineIndex":1,"_lineLayer":0,"_type":0,"_cutDirection":1}]}""");
var legacyAngleLoaded=LoadSynthetic(legacyAngleMap,requiredExtensions:noodleCapability);
Check(legacyAngleLoaded.Input.Notes.First().Direction==1&&legacyAngleLoaded.Input.Notes.First().CutDirectionAngleOffset==45,"Legacy angle override changes arrows to Down with the declared physical offset");
Check(CompatibleAnalysis.Analyze(legacyAngleLoaded.Input,reproduceKnownDefects:false).LeftBins.Sum()==1,"Legacy angle direction override participates in close-note swing grouping");
Check(FirstNoteVisual.From(legacyAngleLoaded.Input.Notes.First(),DrawColor.White)!.RotationZ==45,"Menu first cube shows the legacy custom physical angle");
var hiddenLegacyAngle=LoadSynthetic(legacyAngleMap,features:AnalysisFeatures.Average,requiredExtensions:noodleCapability);
Check(hiddenLegacyAngle.Input.Notes.First().Direction==1&&hiddenLegacyAngle.Input.Notes.All(n=>n.CutDirectionAngleOffset==null)&&CompatibleAnalysis.Analyze(hiddenLegacyAngle.Input,reproduceKnownDefects:false).LeftBins.Sum()==1,"Hidden first notes skip legacy tilt but retain the direction needed for swings");
var pairedLegacyAngle=(JObject)legacyAngleMap.DeepClone();pairedLegacyAngle["_notes"]![0]!["_cutDirection"]=1;pairedLegacyAngle["_notes"]![0]!["_customData"]!["_cutDirection"]=120;pairedLegacyAngle["_notes"]![1]!["_time"]=2;pairedLegacyAngle["_notes"]![1]!["_lineLayer"]=2;
var pairedLegacyLoaded=LoadSynthetic(pairedLegacyAngle,requiredExtensions:noodleCapability);
Check(pairedLegacyLoaded.Input.Notes.First().CutDirectionAngleOffset==120&&Math.Abs(pairedLegacyLoaded.Input.Notes[1].CutDirectionAngleOffset!.Value+26.565051f)<1e-4,"Legacy override applies after native pair tilt and leaves the ordinary partner angle intact");
var legacyAngleDot=(JObject)legacyAngleMap.DeepClone();legacyAngleDot["_notes"]![0]!["_cutDirection"]=8;legacyAngleDot["_notes"]![0]!["_customData"]!["_cutDirection"]=-45;
var legacyAngleDotLoaded=LoadSynthetic(legacyAngleDot,requiredExtensions:noodleCapability);
Check(legacyAngleDotLoaded.Input.Notes.First().Direction==8&&legacyAngleDotLoaded.Input.Notes.First().CutDirectionAngleOffset==-45,"Legacy angle preserves a dot instead of inventing an arrow");
var nullLegacyAngle=(JObject)legacyAngleMap.DeepClone();nullLegacyAngle["_notes"]![0]!["_customData"]!["_cutDirection"]=JValue.CreateNull();
Check(LoadSynthetic(nullLegacyAngle,requiredExtensions:noodleCapability).Input.Notes.First().Direction==4&&LoadSynthetic(nullLegacyAngle,requiredExtensions:noodleCapability).Input.Notes.First().CutDirectionAngleOffset==0,"Null legacy angle leaves the original direction and known default tilt");
Check(legacyAngleLoaded.Json==legacyAngleMap.ToString(),"Legacy angle support preserves original JSON/hash");
foreach(var invalidAngle in new JToken[]{new JValue(true),new JValue("45"),new JObject(),new JValue(1000001)})
{
    var invalid=(JObject)legacyAngleMap.DeepClone();invalid["_notes"]![0]!["_customData"]!["_cutDirection"]=invalidAngle;
    bool rejected=false;try{LoadSynthetic(invalid,requiredExtensions:noodleCapability);}catch(NotSupportedException){rejected=true;}
    Check(rejected,"Unverified legacy angle type or range remains unsupported");
}
var wrongAngleVersion=(JObject)noodleBurstMap.DeepClone();wrongAngleVersion["colorNotes"]![0]!["customData"]=new JObject {["_cutDirection"]=45};
bool wrongAngleVersionRejected=false;try{LoadSynthetic(wrongAngleVersion,requiredExtensions:noodleCapability);}catch(NotSupportedException){wrongAngleVersionRejected=true;}
Check(wrongAngleVersionRejected,"Legacy angle support does not enable a v3 alias");
foreach(int major in new[]{2,3})foreach(var noteTail in new JToken[]{new JArray(-2,1.5f),new JArray(-2),new JArray(JValue.CreateNull(),1.5f),new JArray(),JValue.CreateNull()})
{
    var noteTailMap=major==2?JObject.Parse("""{"_version":"2.6.0","_notes":[{"_time":2,"_lineIndex":0,"_lineLayer":0,"_type":0,"_cutDirection":1},{"_time":4,"_lineIndex":1,"_lineLayer":0,"_type":0,"_cutDirection":1}]}"""):
        JObject.Parse("""{"version":"3.3.0","colorNotes":[{"b":2,"x":0,"y":0,"c":0,"d":1},{"b":4,"x":1,"y":0,"c":0,"d":1}]}""");
    noteTailMap[major==2?"_notes":"colorNotes"]![0]![major==2?"_customData":"customData"]=new JObject {["tailCoordinates"]=noteTail.DeepClone()};
    var noteTailLoaded=LoadSynthetic(noteTailMap,requiredExtensions:noodleCapability);
    Check(!noteTailLoaded.HasNoodleGeometry&&noteTailLoaded.Input.Notes.All(n=>n.NoodleX==null&&n.NoodleY==null&&n.CutDirectionAngleOffset==0)&&CompatibleAnalysis.Analyze(noteTailLoaded.Input,reproduceKnownDefects:false).LeftBins.Sum()==2,$"v{major} note tail metadata does not move the note, alter tilt or change swings");
    Check(noteTailLoaded.Json==noteTailMap.ToString(),$"v{major} note tail metadata preserves original JSON/hash");
}
foreach(var invalidNoteTail in new JToken[]{new JArray(1,2,3),new JValue("1,2"),new JArray(true,0),new JArray(1000001,0)})
{
    var invalid=(JObject)noodleBurstMap.DeepClone();invalid["colorNotes"]![0]!["customData"]=new JObject {["tailCoordinates"]=invalidNoteTail};
    bool rejected=false;try{LoadSynthetic(invalid,requiredExtensions:noodleCapability);}catch(NotSupportedException){rejected=true;}
    Check(rejected,"Malformed or unbounded note tail metadata remains unsupported");
}
var noteTailAnimated=(JObject)noodleBurstMap.DeepClone();noteTailAnimated["colorNotes"]![0]!["customData"]=new JObject {["tailCoordinates"]=new JArray(-2,1.5f),["animation"]=new JObject {["offsetPosition"]=new JArray(1,0,0)}};
bool noteTailAnimationRejected=false;try{LoadSynthetic(noteTailAnimated,requiredExtensions:noodleCapability);}catch(NotSupportedException){noteTailAnimationRejected=true;}
Check(noteTailAnimationRejected,"Unused note tail metadata does not bypass animation validation");
var noteTailBomb=JObject.Parse("""{"version":"3.3.0","colorNotes":[{"b":2,"x":0,"y":0,"c":0,"d":1}],"bombNotes":[{"b":3,"x":2,"y":2,"customData":{"tailCoordinates":[-2,1.5]}}]}""");
var noteTailBombLoaded=LoadSynthetic(noteTailBomb,requiredExtensions:noodleCapability);
Check(noteTailBombLoaded.Input.Notes.Length==1&&!noteTailBombLoaded.HasNoodleGeometry,"Bomb tail metadata does not create a colored note or move the bomb");
foreach(bool aliasArc in new[]{false,true})foreach(int aliasColor in new[]{0,1})foreach(int aliasDirection in new[]{0,3,8})foreach(bool aliasCoordinates in new[]{false,true})
foreach(string aliasVariant in new[]{"_position","_scale","_cutDirection","size","all"})
{
    var aliasBase=JObject.Parse("""{"version":"3.3.0","colorNotes":[{"b":2,"x":0,"y":0,"c":0,"d":1,"a":15},{"b":4,"x":1,"y":1,"c":0,"d":1,"a":15}]}""");
    for(int n=0;n<2;n++)
    {
        var note=(JObject)aliasBase["colorNotes"]![n]!;note["c"]=aliasColor;note["d"]=aliasDirection;
        note["customData"]=aliasCoordinates?new JObject {["coordinates"]=new JArray(n-.75,n+.25)}:new JObject();
    }
    if(aliasArc)
    {
        var arc=new JObject {["b"]=2,["c"]=aliasColor,["x"]=0,["y"]=0,["d"]=aliasDirection,["tb"]=4,["tx"]=1,["ty"]=1,["tc"]=aliasDirection};
        if(aliasCoordinates)arc["customData"]=new JObject {["coordinates"]=new JArray(-.75,.25),["tailCoordinates"]=new JArray(.25,1.25)};
        aliasBase["sliders"]=new JArray(arc);
    }
    var baselineAliases=LoadSynthetic(aliasBase,requiredExtensions:noodleCapability);
    var extraAliases=(JObject)aliasBase.DeepClone();
    for(int n=0;n<2;n++)
    {
        var metadata=(JObject)extraAliases["colorNotes"]![n]!["customData"]!;
        if(aliasVariant is "_position" or "all")metadata["_position"]=new JArray(3-n,-3+n);
        if(aliasVariant is "_scale" or "all")metadata["_scale"]=new JArray(1,1,1);
        if(aliasVariant is "_cutDirection" or "all")metadata["_cutDirection"]=n==0?-45:225;
        if(aliasVariant is "size" or "all")metadata["size"]=new JArray(1,1,1);
    }
    var loadedAliases=LoadSynthetic(extraAliases,requiredExtensions:noodleCapability);
    Check(JToken.DeepEquals(JToken.FromObject(baselineAliases.Input),JToken.FromObject(loadedAliases.Input)),"Proven v3 legacy note metadata and unit size preserve prepared input, including arc roles and tilt");
    Check(loadedAliases.Json==extraAliases.ToString()&&loadedAliases.HasNoodleGeometry==baselineAliases.HasNoodleGeometry,"Ignored v3 aliases do not rewrite JSON or introduce geometry");
}
foreach(string aliasName in new[]{"_position","_scale","_cutDirection"})
foreach(var invalidAlias in new JToken[]{JValue.CreateNull(),new JValue(true),new JValue("1"),new JObject(),new JArray(),new JArray(1),new JValue(double.PositiveInfinity)})
{
    var badAlias=JObject.Parse("""{"version":"3.3.0","colorNotes":[{"b":2,"x":0,"y":0,"c":0,"d":1,"customData":{}}]}""");
    badAlias["colorNotes"]![0]!["customData"]![aliasName]=invalidAlias;
    bool aliasRejected=false;try{LoadSynthetic(badAlias,requiredExtensions:noodleCapability);}catch(NotSupportedException){aliasRejected=true;}
    Check(aliasRejected,"Ignored legacy alias admission still requires proven bounded syntax");
}
foreach(string aliasContext in new[]{"fake","bomb","wrong-container","nonunit-legacy-scale","active-scale","animation","world-rotation","chain"})
{
    var guardedAlias=JObject.Parse("""{"version":"3.3.0","colorNotes":[{"b":2,"x":0,"y":0,"c":0,"d":1,"customData":{"_position":[3,-3],"_scale":[1,1,1],"_cutDirection":45}}]}""");
    var metadata=(JObject)guardedAlias["colorNotes"]![0]!["customData"]!;
    if(aliasContext=="fake")metadata["NE_fake"]=true;
    if(aliasContext=="bomb"){guardedAlias["bombNotes"]=guardedAlias["colorNotes"]!.DeepClone();guardedAlias["colorNotes"]=new JArray();}
    if(aliasContext=="wrong-container"){guardedAlias["colorNotes"]![0]!["_customData"]=metadata.DeepClone();guardedAlias["colorNotes"]![0]!["customData"]=new JObject();}
    if(aliasContext=="nonunit-legacy-scale")metadata["_scale"]=new JArray(2,1,1);
    if(aliasContext=="active-scale")metadata["scale"]=new JArray(2,1,1);
    if(aliasContext=="animation")metadata["animation"]=new JObject {["offsetPosition"]=new JArray(1,0,0)};
    if(aliasContext=="world-rotation")metadata["worldRotation"]=new JArray(0,45,0);
    if(aliasContext=="chain")guardedAlias["burstSliders"]=new JArray(new JObject {["b"]=2,["c"]=0,["x"]=0,["y"]=0,["d"]=1,["tb"]=3,["tx"]=1,["ty"]=0,["sc"]=2});
    bool aliasRejected=false;try{LoadSynthetic(guardedAlias,requiredExtensions:noodleCapability);}catch(NotSupportedException){aliasRejected=true;}
    Check(aliasRejected,"Ignored aliases do not bypass independent restrictions: "+aliasContext);
}
foreach(float? bombAngle in new float?[]{null,-450,-63.806f,-45,0,26.567f,180,450})foreach(bool positionedBomb in new[]{false,true})
{
    var bombAngleMap=JObject.Parse("""{"_version":"2.6.0","_notes":[{"_time":2,"_lineIndex":1,"_lineLayer":0,"_type":0,"_cutDirection":1},{"_time":2,"_lineIndex":2,"_lineLayer":0,"_type":3,"_cutDirection":1,"_customData":{}},{"_time":4,"_lineIndex":2,"_lineLayer":1,"_type":1,"_cutDirection":0}]}""");
    var bombMetadata=(JObject)bombAngleMap["_notes"]![1]!["_customData"]!;
    if(positionedBomb)bombMetadata["_position"]=new JArray(1,.5);
    var baselineBomb=LoadSynthetic(bombAngleMap,requiredExtensions:noodleCapability);
    bombMetadata["_cutDirection"]=bombAngle.HasValue?new JValue(bombAngle.Value):JValue.CreateNull();
    var parsedBomb=LoadSynthetic(bombAngleMap,requiredExtensions:noodleCapability);
    Check(JToken.DeepEquals(JToken.FromObject(baselineBomb.Input),JToken.FromObject(parsedBomb.Input)),"Legacy real-bomb angle preserves prepared notes, positions and analysis input");
    Check(parsedBomb.Input.Notes.Length==2&&parsedBomb.Json==bombAngleMap.ToString(),"Bomb custom angle does not create a colored note or rewrite JSON");
}
foreach(var invalidBombAngle in new JToken[]{new JArray(0),new JObject(),new JValue("45"),new JValue(true),new JValue(1000001),new JValue(double.NaN),new JValue(double.PositiveInfinity)})
{
    var invalidAngleMap=JObject.Parse("""{"_version":"2.6.0","_notes":[{"_time":2,"_lineIndex":1,"_lineLayer":0,"_type":3,"_cutDirection":1,"_customData":{}}]}""");
    invalidAngleMap["_notes"]![0]!["_customData"]!["_cutDirection"]=invalidBombAngle;
    bool bombAngleRefused=false;try{LoadSynthetic(invalidAngleMap,requiredExtensions:noodleCapability);}catch(NotSupportedException){bombAngleRefused=true;}
    Check(bombAngleRefused,"Invalid or out-of-range legacy bomb angle stays rejected");
}
foreach(string unsupportedBombContext in new[]{"fake","v3-alias","animation","scale"})
{
    var angleContext=JObject.Parse("""{"_version":"2.6.0","_notes":[{"_time":2,"_lineIndex":1,"_lineLayer":0,"_type":3,"_cutDirection":1,"_customData":{"_cutDirection":45}}]}""");
    var angleMetadata=(JObject)angleContext["_notes"]![0]!["_customData"]!;
    if(unsupportedBombContext=="fake")angleMetadata["_fake"]=true;
    if(unsupportedBombContext=="v3-alias")angleContext=JObject.Parse("""{"version":"3.3.0","bombNotes":[{"b":2,"x":1,"y":0,"customData":{"_cutDirection":45}}]}""");
    if(unsupportedBombContext=="animation")angleMetadata["_animation"]=new JObject {["_position"]=new JArray(1,0,0)};
    if(unsupportedBombContext=="scale")angleMetadata["_scale"]=new JArray(2,1,1);
    bool contextRefused=false;try{LoadSynthetic(angleContext,requiredExtensions:noodleCapability);}catch(NotSupportedException){contextRefused=true;}
    Check(contextRefused,"Legacy bomb cut angle does not bypass unvalidated context: "+unsupportedBombContext);
}
foreach(int color in new[]{0,1})foreach(int direction in new[]{0,1,8})foreach(var unitSize in new[]{new JArray(1,1,1),new JArray(1.0,1.0,1.0)})
{
    var baseSizeMap=JObject.Parse("""{"version":"3.3.0","colorNotes":[{"b":2,"x":1,"y":0,"c":0,"d":1}]}""");
    baseSizeMap["colorNotes"]![0]!["c"]=color;baseSizeMap["colorNotes"]![0]!["d"]=direction;
    var baseSizeInput=LoadSynthetic(baseSizeMap,requiredExtensions:noodleCapability).Input;
    var sizeMap=(JObject)baseSizeMap.DeepClone();sizeMap["colorNotes"]![0]!["customData"]=new JObject {["size"]=unitSize};
    var loadedSize=LoadSynthetic(sizeMap,requiredExtensions:noodleCapability);
    Check(JToken.DeepEquals(JToken.FromObject(baseSizeInput),JToken.FromObject(loadedSize.Input)),"Canonical v3 note size preserves prepared input for both hands, arrows and dots");
    Check(loadedSize.Json==sizeMap.ToString()&&!loadedSize.HasNoodleGeometry,"Unit size does not rewrite chart JSON or mark custom geometry");
}
foreach(var invalidSize in new JToken[]{JValue.CreateNull(),new JArray(),new JArray(1),new JArray(1,1),new JArray(1,1,1,1),new JArray(2,1,1),new JArray(0,1,1),new JArray(-1,1,1),new JArray(1,JValue.CreateNull(),1),new JArray("1",1,1),new JObject(),new JValue(true)})
{
    var badSize=JObject.Parse("""{"version":"3.3.0","colorNotes":[{"b":2,"x":1,"y":0,"c":0,"d":1,"customData":{}}]}""");
    badSize["colorNotes"]![0]!["customData"]!["size"]=invalidSize;
    bool sizeRefused=false;try{LoadSynthetic(badSize,requiredExtensions:noodleCapability);}catch(NotSupportedException){sizeRefused=true;}
    Check(sizeRefused,"Non-canonical note size stays unsupported");
}
foreach(string context in new[]{"bomb","fake","legacy","scale","animation","chain"})
{
    var sizeContext=JObject.Parse("""{"version":"3.3.0","colorNotes":[{"b":2,"x":1,"y":0,"c":0,"d":1,"customData":{"size":[1,1,1]}}]}""");
    var metadata=(JObject)sizeContext["colorNotes"]![0]!["customData"]!;
    if(context=="bomb"){sizeContext["bombNotes"]=sizeContext["colorNotes"]!.DeepClone();sizeContext["colorNotes"]=new JArray();}
    if(context=="fake")metadata["NE_fake"]=true;
    if(context=="legacy")sizeContext=JObject.Parse("""{"_version":"2.6.0","_notes":[{"_time":2,"_lineIndex":1,"_lineLayer":0,"_type":0,"_cutDirection":1,"_customData":{"size":[1,1,1]}}]}""");
    if(context=="scale")metadata["scale"]=new JArray(2,1,1);
    if(context=="animation")metadata["animation"]=new JObject {["offsetPosition"]=new JArray(1,0,0)};
    if(context=="arc")sizeContext["sliders"]=new JArray(new JObject {["b"]=2,["c"]=0,["x"]=1,["y"]=0,["d"]=1,["tb"]=3,["tx"]=2,["ty"]=0});
    if(context=="chain")sizeContext["burstSliders"]=new JArray(new JObject {["b"]=2,["c"]=0,["x"]=1,["y"]=0,["d"]=1,["tb"]=3,["tx"]=2,["ty"]=0,["sc"]=2});
    bool sizeRefused=false;try{LoadSynthetic(sizeContext,requiredExtensions:noodleCapability);}catch(NotSupportedException){sizeRefused=true;}
    Check(sizeRefused,"Unit note size does not admit unvalidated context: "+context);
}
foreach(int major in new[]{2,3})foreach(var identityScale in new JToken[]{new JArray(1,1,1),new JArray(1),new JArray(JValue.CreateNull(),1,JValue.CreateNull()),new JArray(),JValue.CreateNull()})
{
    var neutralScaleMap=major==2?JObject.Parse("""{"_version":"2.6.0","_notes":[{"_time":2,"_lineIndex":0,"_lineLayer":0,"_type":0,"_cutDirection":1},{"_time":4,"_lineIndex":1,"_lineLayer":0,"_type":0,"_cutDirection":1}]}"""):
        JObject.Parse("""{"version":"3.3.0","colorNotes":[{"b":2,"x":0,"y":0,"c":0,"d":1},{"b":4,"x":1,"y":0,"c":0,"d":1}]}""");
    neutralScaleMap[major==2?"_notes":"colorNotes"]![0]![major==2?"_customData":"customData"]=new JObject {[major==2?"_scale":"scale"]=identityScale.DeepClone()};
    var neutralScaleLoaded=LoadSynthetic(neutralScaleMap,requiredExtensions:noodleCapability);
    Check(neutralScaleLoaded.Input.Notes.All(n=>!n.IsFake&&n.CutDirectionAngleOffset==0)&&CompatibleAnalysis.Analyze(neutralScaleLoaded.Input,reproduceKnownDefects:false).LeftBins.Sum()==2,$"v{major} identity scale preserves tilt, real notes and swings");
    Check(neutralScaleLoaded.Json==neutralScaleMap.ToString()&&!neutralScaleLoaded.HasNoodleGeometry,$"v{major} identity scale preserves source JSON and geometry guard");
}
foreach(var badNoteScale in new JToken[]{new JArray(2,1,1),new JArray(0,1,1),new JArray(-1,1,1),new JArray(true,1,1),new JArray(1,1,1,1),new JValue(1)})
{
    var badScaleMap=(JObject)noodleBurstMap.DeepClone();badScaleMap["colorNotes"]![0]!["customData"]=new JObject {["scale"]=badNoteScale};
    bool rejected=false;try{LoadSynthetic(badScaleMap,requiredExtensions:noodleCapability);}catch(NotSupportedException){rejected=true;}
    Check(rejected,"Non-default or malformed note scale remains unsupported");
}
var neutralScaleAlias=(JObject)noodleBurstMap.DeepClone();neutralScaleAlias["colorNotes"]![0]!["customData"]=new JObject {["_scale"]=new JArray(1,1,1)};
bool neutralScaleAliasRejected=false;try{LoadSynthetic(neutralScaleAlias,requiredExtensions:noodleCapability);}catch(NotSupportedException){neutralScaleAliasRejected=true;}
Check(neutralScaleAliasRejected,"Identity scale does not silently enable a different format version alias");
var animatedNeutralScale=(JObject)noodleBurstMap.DeepClone();animatedNeutralScale["colorNotes"]![0]!["customData"]=new JObject {["scale"]=new JArray(1,1,1),["animation"]=new JObject {["offsetPosition"]=new JArray(1,0,0)}};
bool animatedNeutralScaleRejected=false;try{LoadSynthetic(animatedNeutralScale,requiredExtensions:noodleCapability);}catch(NotSupportedException){animatedNeutralScaleRejected=true;}
Check(animatedNeutralScaleRejected,"Identity scale does not bypass animation validation");
var scaledLegacyAngle=(JObject)legacyAngleMap.DeepClone();scaledLegacyAngle["_notes"]![0]!["_customData"]!["_scale"]=new JArray(1,1,1);
Check(LoadSynthetic(scaledLegacyAngle,requiredExtensions:noodleCapability).Input.Notes.First().CutDirectionAngleOffset==45,"Legacy identity scale composes with the validated custom cut angle");
foreach(var bareWallSpec in new[]{(-1,4,1),(4,1,0),(-1,1,0),(1000,1,0)})
{
    var bareWallMap=new JObject {["version"]="3.3.0",["colorNotes"]=new JArray(),["obstacles"]=new JArray(new JObject {["b"]=2,["d"]=2,["x"]=bareWallSpec.Item1,["y"]=2,["w"]=bareWallSpec.Item2,["h"]=3})};
    var bareWallLoaded=LoadSynthetic(bareWallMap,requiredExtensions:noodleCapability);
    Check(WallAlertAdapter.Count(bareWallLoaded.SquatJson!,120,false,true)==bareWallSpec.Item3,"Bare extended wall lane uses literal geometry for squat coverage");
    Check(bareWallLoaded.Json==bareWallMap.ToString(),"Bare extended wall preserves source JSON/hash");
}
var bareLegacyWall=JObject.Parse("""{"_version":"2.6.0","_notes":[],"_obstacles":[{"_time":2,"_duration":2,"_lineIndex":-1,"_width":4,"_type":1}]}""");
Check(WallAlertAdapter.Count(LoadSynthetic(bareLegacyWall,requiredExtensions:noodleCapability).SquatJson!,120,false,true)==1,"Bare legacy lane outside the grid can still cover the crouch zone");
foreach(string unsupportedBare in new[]{"negative-width","negative-height","fractional-lane","fractional-width","extended-layer","range"})
{
    var badBare=JObject.Parse("""{"version":"3.3.0","colorNotes":[],"obstacles":[{"b":2,"d":2,"x":-1,"y":2,"w":4,"h":3}]}""");var wall=badBare["obstacles"]![0]!;
    if(unsupportedBare=="negative-width")wall["w"]=-2;
    if(unsupportedBare=="negative-height")wall["h"]=-1;
    if(unsupportedBare=="fractional-lane")wall["x"]=-1.5;
    if(unsupportedBare=="fractional-width")wall["w"]=4.5;
    if(unsupportedBare=="extended-layer")wall["y"]=7;
    if(unsupportedBare=="range")wall["x"]=1000001;
    bool rejected=false;try{LoadSynthetic(badBare,requiredExtensions:noodleCapability);}catch(NotSupportedException){rejected=true;}
    Check(rejected,$"Unverified bare wall {unsupportedBare} remains unsupported");
}
JObject ReplacedSliderMap(string kind,string replacement)
{
    bool old=kind=="v2-arc";bool burst=kind=="v3-burst";
    var root=old?JObject.Parse("""{"_version":"2.6.0","_notes":[{"_time":2,"_lineIndex":0,"_lineLayer":0,"_type":0,"_cutDirection":1},{"_time":4,"_lineIndex":1,"_lineLayer":2,"_type":0,"_cutDirection":1}],"_sliders":[{"_headTime":2,"_headLineIndex":0,"_headLineLayer":0,"_headCutDirection":1,"_tailTime":4,"_tailLineIndex":1,"_tailLineLayer":2,"_tailCutDirection":1,"_colorType":0}]}"""):
        JObject.Parse("""{"version":"3.3.0","colorNotes":[{"b":2,"x":0,"y":0,"c":0,"d":1},{"b":4,"x":1,"y":2,"c":0,"d":1}],"sliders":[{"b":2,"x":0,"y":0,"c":0,"d":1,"tb":4,"tx":1,"ty":2,"tc":1,"sc":4,"s":1}]}""");
    if(burst){root["burstSliders"]=root["sliders"];root.Remove("sliders");}
    var slider=root[old?"_sliders":burst?"burstSliders":"sliders"]![0]!;
    slider[old?"_customData":"customData"]=new JObject {[old?"_position":"coordinates"]=new JArray(-2,0),["tailCoordinates"]=new JArray(-1,2)};
    if(replacement=="head"||replacement=="both"){slider[old?"_headLineIndex":"x"]=6;slider[old?"_headLineLayer":"y"]=-2;}
    if(replacement=="tail"||replacement=="both"){slider[old?"_tailLineIndex":"tx"]=-4;slider[old?"_tailLineLayer":"ty"]=7;}
    return root;
}
foreach(string kind in new[]{"v2-arc","v3-arc","v3-burst"})foreach(string replacement in new[]{"head","tail","both"})
{
    var replacedMap=ReplacedSliderMap(kind,replacement);var replacedLoaded=LoadSynthetic(replacedMap,requiredExtensions:noodleCapability);
    Check(replacedLoaded.Input.Notes.Length==2&&replacedLoaded.Input.Notes.First().ScoringType==(kind=="v3-burst"?1:2)&&replacedLoaded.Input.Notes.Last().ScoringType==3,$"Replaced {kind} {replacement} base matches physical head and tail exactly once");
    Check(replacedLoaded.Input.Notes.All(n=>n.CutDirectionAngleOffset==0)&&CompatibleAnalysis.Analyze(replacedLoaded.Input,reproduceKnownDefects:false).LeftBins.Sum()==2,$"Replaced {kind} {replacement} base preserves tilt and swing count");
    Check(replacedLoaded.HasNoodleGeometry&&replacedLoaded.Json==replacedMap.ToString(),$"Replaced {kind} {replacement} base retains history guard and source JSON");
}
foreach(string badReplacement in new[]{"head-x","head-y","tail-x","tail-y","missing","range"})
{
    var badSliderMap=ReplacedSliderMap("v3-arc","both");var slider=badSliderMap["sliders"]![0]!;
    if(badReplacement=="head-x")slider["customData"]!["coordinates"]![0]=JValue.CreateNull();
    if(badReplacement=="head-y")slider["customData"]!["coordinates"]![1]=JValue.CreateNull();
    if(badReplacement=="tail-x")slider["customData"]!["tailCoordinates"]![0]=JValue.CreateNull();
    if(badReplacement=="tail-y")slider["customData"]!["tailCoordinates"]![1]=JValue.CreateNull();
    if(badReplacement=="missing")slider["customData"]=new JObject();
    if(badReplacement=="range")slider["x"]=1000001;
    bool rejected=false;try{LoadSynthetic(badSliderMap,requiredExtensions:noodleCapability);}catch(NotSupportedException){rejected=true;}
    Check(rejected,$"Unverified slider replacement {badReplacement} remains unsupported");
}
foreach(int major in new[]{2,3})foreach(var emptyAssignment in new JToken[]{JValue.CreateNull(),new JArray()})
{
    var emptyTrackMap=major==2?JObject.Parse("""{"_version":"2.6.0","_notes":[{"_time":2,"_lineIndex":0,"_lineLayer":0,"_type":0,"_cutDirection":1},{"_time":4,"_lineIndex":1,"_lineLayer":0,"_type":0,"_cutDirection":1}],"_sliders":[{"_headTime":2,"_headLineIndex":0,"_headLineLayer":0,"_headCutDirection":1,"_tailTime":4,"_tailLineIndex":1,"_tailLineLayer":0,"_tailCutDirection":1,"_colorType":0}],"_obstacles":[{"_time":6,"_duration":1,"_lineIndex":1,"_width":2,"_type":1}]}"""):
        JObject.Parse("""{"version":"3.3.0","colorNotes":[{"b":2,"x":0,"y":0,"c":0,"d":1},{"b":4,"x":1,"y":0,"c":0,"d":1}],"sliders":[{"b":2,"x":0,"y":0,"c":0,"d":1,"tb":4,"tx":1,"ty":0,"tc":1}],"obstacles":[{"b":6,"d":1,"x":1,"y":2,"w":2,"h":3}]}""");
    foreach(string collection in major==2?new[]{"_notes","_sliders","_obstacles"}:new[]{"colorNotes","sliders","obstacles"})foreach(var obj in emptyTrackMap[collection]!)obj[major==2?"_customData":"customData"]=new JObject {[major==2?"_track":"track"]=emptyAssignment.DeepClone()};
    var emptyTrackLoaded=LoadSynthetic(emptyTrackMap,requiredExtensions:noodleCapability);
    Check(CompatibleAnalysis.Analyze(emptyTrackLoaded.Input,reproduceKnownDefects:false).LeftBins.Sum()==2&&emptyTrackLoaded.Input.Notes.All(n=>n.CutDirectionAngleOffset==0),$"v{major} empty tracks preserve swings and tilt");
    Check(WallAlertAdapter.Count(emptyTrackLoaded.SquatJson!,120,false,true)==1&&!emptyTrackLoaded.HasNoodleGeometry&&emptyTrackLoaded.Json==emptyTrackMap.ToString(),$"v{major} empty tracks preserve squats, geometry guard and source JSON");
}
foreach(var nonemptyAssignment in new JToken[]{new JValue(""),new JArray(JValue.CreateNull()),new JObject(),new JValue(false)})
{
    var invalid=(JObject)noodleBurstMap.DeepClone();invalid["colorNotes"]![0]!["customData"]=new JObject {["track"]=nonemptyAssignment};
    bool rejected=false;try{LoadSynthetic(invalid,requiredExtensions:noodleCapability);}catch(NotSupportedException){rejected=true;}
    Check(rejected,"Nonempty or malformed track assignment remains unsupported");
}
var emptyTrackAlias=(JObject)noodleBurstMap.DeepClone();emptyTrackAlias["colorNotes"]![0]!["customData"]=new JObject {["_track"]=new JArray()};
bool emptyTrackAliasRejected=false;try{LoadSynthetic(emptyTrackAlias,requiredExtensions:noodleCapability);}catch(NotSupportedException){emptyTrackAliasRejected=true;}
Check(emptyTrackAliasRejected,"Empty tracks do not enable a different format version alias");
var emptyTrackAnimated=(JObject)noodleBurstMap.DeepClone();emptyTrackAnimated["colorNotes"]![0]!["customData"]=new JObject {["track"]=new JArray(),["animation"]=new JObject {["offsetPosition"]=new JArray(1,0,0)}};
bool emptyTrackAnimatedRejected=false;try{LoadSynthetic(emptyTrackAnimated,requiredExtensions:noodleCapability);}catch(NotSupportedException){emptyTrackAnimatedRejected=true;}
Check(emptyTrackAnimatedRejected,"Empty tracks do not bypass animation validation");
foreach(int major in new[]{2,3})foreach(var namedAssignment in new JToken[]{new JValue("fixture"),new JArray("fixture","other")})
{
    var staticTrackMap=major==2?JObject.Parse("""{"_version":"2.6.0","_notes":[{"_time":2,"_lineIndex":0,"_lineLayer":0,"_type":0,"_cutDirection":1},{"_time":4,"_lineIndex":1,"_lineLayer":0,"_type":0,"_cutDirection":1}],"_sliders":[{"_headTime":2,"_headLineIndex":0,"_headLineLayer":0,"_headCutDirection":1,"_tailTime":4,"_tailLineIndex":1,"_tailLineLayer":0,"_tailCutDirection":1,"_colorType":0}],"_obstacles":[{"_time":6,"_duration":1,"_lineIndex":1,"_width":2,"_type":1}]}"""):
        JObject.Parse("""{"version":"3.3.0","colorNotes":[{"b":2,"x":0,"y":0,"c":0,"d":1},{"b":4,"x":1,"y":0,"c":0,"d":1}],"sliders":[{"b":2,"x":0,"y":0,"c":0,"d":1,"tb":4,"tx":1,"ty":0,"tc":1}],"obstacles":[{"b":6,"d":1,"x":1,"y":2,"w":2,"h":3}]}""");
    foreach(string collection in major==2?new[]{"_notes","_sliders","_obstacles"}:new[]{"colorNotes","sliders","obstacles"})foreach(var obj in staticTrackMap[collection]!)obj[major==2?"_customData":"customData"]=new JObject {[major==2?"_track":"track"]=namedAssignment.DeepClone()};
    var staticTrackLoaded=LoadSynthetic(staticTrackMap,requiredExtensions:noodleCapability);
    Check(CompatibleAnalysis.Analyze(staticTrackLoaded.Input,reproduceKnownDefects:false).LeftBins.Sum()==2&&staticTrackLoaded.Input.Notes.All(n=>n.CutDirectionAngleOffset==0),$"v{major} event-free static tracks preserve swings and tilt");
    Check(WallAlertAdapter.Count(staticTrackLoaded.SquatJson!,120,false,true)==1&&!staticTrackLoaded.HasNoodleGeometry&&staticTrackLoaded.Json==staticTrackMap.ToString(),$"v{major} event-free static tracks preserve wall coverage, geometry guard and JSON");
    var visual=(JObject)staticTrackMap.DeepClone();
    string visualCustom=major==2?"_customData":"customData",visualData=major==2?"_data":"d";
    var visualFields=new JObject {[major==2?"_track":"track"]=namedAssignment.DeepClone(),[major==2?"_dissolve":"dissolve"]=new JArray(new JArray(0,0),new JArray(1,1)),[major==2?"_duration":"duration"]=8,[major==2?"_easing":"easing"]="easeOutQuad"};
    visual[visualCustom]=new JObject {[major==2?"_customEvents":"customEvents"]=new JArray(new JObject {[major==2?"_type":"t"]="AnimateTrack",[major==2?"_time":"b"]=0,[visualData]=visualFields})};
    var visualLoaded=LoadSynthetic(visual,requiredExtensions:noodleCapability);
    Check(CompatibleAnalysis.Analyze(visualLoaded.Input,reproduceKnownDefects:false).LeftBins.Sum()==2&&visualLoaded.Input.Notes.All(n=>n.CutDirectionAngleOffset==0),$"v{major} dissolve track preserves swings and orientation");
    Check(WallAlertAdapter.Count(visualLoaded.SquatJson!,120,false,true)==1&&visualLoaded.Json==visual.ToString(),$"v{major} dissolve track preserves walls and source");
    foreach(string variant in new[]{"position","interactable","parent","reference","nonfinite","bad-easing","repeat","wrong-alias","bad-time","bad-frame"})
    {
        var changed=(JObject)visual.DeepClone();var ev=(JObject)changed[visualCustom]![major==2?"_customEvents":"customEvents"]![0]!;var fields=(JObject)ev[visualData]!;
        if(variant=="position")fields[major==2?"_position":"offsetPosition"]=new JArray(1,0,0);
        if(variant=="interactable")fields[major==2?"_interactable":"interactable"]=new JArray(0);
        if(variant=="parent")ev[major==2?"_type":"t"]="AssignTrackParent";
        if(variant=="reference")fields[major==2?"_dissolve":"dissolve"]="named-points";
        if(variant=="nonfinite")fields[major==2?"_duration":"duration"]=double.PositiveInfinity;
        if(variant=="bad-easing")fields[major==2?"_easing":"easing"]="unknown";
        if(variant=="repeat")fields[major==2?"_repeat":"repeat"]=2;
        if(variant=="wrong-alias")fields[major==2?"dissolve":"_dissolve"]=new JArray(0);
        if(variant=="bad-time")ev[major==2?"_time":"b"]=-1;
        if(variant=="bad-frame")fields[major==2?"_dissolve":"dissolve"]=new JArray(new JArray(0,"bad"));
        bool visualRejected=false;try{LoadSynthetic(changed,requiredExtensions:noodleCapability);}catch(NotSupportedException){visualRejected=true;}
        Check(visualRejected,$"v{major} visual context rejects {variant}");
    }
    foreach(string dynamicContext in new[]{"event","environment","player"})
    {
        var changed=(JObject)staticTrackMap.DeepClone();var custom=new JObject();changed[major==2?"_customData":"customData"]=custom;
        if(dynamicContext=="environment")custom[major==2?"_environment":"environment"]=new JArray(new JObject());
        else custom[major==2?"_customEvents":"customEvents"]=new JArray(new JObject {[major==2?"_type":"t"]=dynamicContext=="player"?"AssignPlayerToTrack":"AnimateTrack"});
        bool rejected=false;try{LoadSynthetic(changed,requiredExtensions:noodleCapability);}catch(NotSupportedException){rejected=true;}
        Check(rejected,$"Named tracks with {dynamicContext} context remain unsupported");
    }
}
foreach(int major in new[]{2,3})
{
    bool old=major==2;string customKey=old?"_customData":"customData",arcKey=old?"_sliders":"sliders";
    var arcBase=old?JObject.Parse("""{"_version":"2.6.0","_notes":[{"_time":1,"_lineIndex":0,"_lineLayer":0,"_type":0,"_cutDirection":1},{"_time":3,"_lineIndex":1,"_lineLayer":1,"_type":0,"_cutDirection":8}],"_sliders":[{"_headTime":1,"_headLineIndex":0,"_headLineLayer":0,"_headCutDirection":1,"_tailTime":3,"_tailLineIndex":1,"_tailLineLayer":1,"_tailCutDirection":8,"_colorType":0}]}"""):
        JObject.Parse("""{"version":"3.3.0","colorNotes":[{"b":1,"x":0,"y":0,"c":0,"d":1},{"b":3,"x":1,"y":1,"c":0,"d":8}],"sliders":[{"b":1,"x":0,"y":0,"c":0,"d":1,"tb":3,"tx":1,"ty":1,"tc":8}]}""");
    var baseline=LoadSynthetic(arcBase,requiredExtensions:noodleCapability);
    foreach(string variant in new[]{"direct","direct-arrow","empty-animation","animation","animation-arrow","ramp","both"})
    {
        var changed=(JObject)arcBase.DeepClone();var metadata=new JObject();changed[arcKey]![0]![customKey]=metadata;
        string opacity=old?"_dissolve":"dissolve",arrow=old?"_dissolveArrow":"dissolveArrow",animation=old?"_animation":"animation";
        if(variant=="direct"||variant=="direct-arrow")metadata[variant=="direct"?opacity:arrow]=new JArray {new JArray(0,0)};
        else
        {
            var visual=new JObject();metadata[animation]=visual;
            if(variant!="empty-animation")visual[variant=="animation-arrow"?arrow:opacity]=variant=="ramp"?new JArray(new JArray(0,0),new JArray(1,1)):new JArray(0);
            if(variant=="both")visual[arrow]=new JArray(.5);
        }
        var opacityLoaded=LoadSynthetic(changed,requiredExtensions:noodleCapability);
        Check(JToken.DeepEquals(JToken.FromObject(opacityLoaded.Input.Notes),JToken.FromObject(baseline.Input.Notes)),$"v{major} arc {variant} preserves full prepared note roles and tilt");
        Check(opacityLoaded.Json==changed.ToString()&&CompatibleAnalysis.Analyze(opacityLoaded.Input,reproduceKnownDefects:false).LeftBins.Sum()==2,$"v{major} arc {variant} preserves source and swings including invisible notes");
    }
    foreach(string variant in new[]{"position","rotation","interactable","scale","reference","wrong-alias","bad-value","bad-time","nonfinite","null","unknown","fake"})
    {
        var changed=(JObject)arcBase.DeepClone();var metadata=new JObject();changed[arcKey]![0]![customKey]=metadata;
        var animation=new JObject {[old?"_dissolve":"dissolve"]=new JArray(0)};metadata[old?"_animation":"animation"]=animation;
        if(variant=="position")animation[old?"_position":"offsetPosition"]=new JArray(1,0,0);
        if(variant=="rotation")animation[old?"_rotation":"offsetWorldRotation"]=new JArray(0,0,90);
        if(variant=="interactable")animation[old?"_interactable":"interactable"]=new JArray(0);
        if(variant=="scale")animation[old?"_scale":"scale"]=new JArray(2,2,2);
        if(variant=="reference")animation[old?"_dissolve":"dissolve"]="named-points";
        if(variant=="wrong-alias")animation[old?"dissolve":"_dissolve"]=new JArray(0);
        if(variant=="bad-value")animation[old?"_dissolve":"dissolve"]=new JArray(2);
        if(variant=="bad-time")animation[old?"_dissolve":"dissolve"]=new JArray(new JArray(0,1),new JArray(1,0));
        if(variant=="nonfinite")metadata[old?"_dissolve":"dissolve"]=new JArray(double.NaN);
        if(variant=="null")metadata[old?"_animation":"animation"]=JValue.CreateNull();
        if(variant=="unknown")animation["unknownOpacityProperty"]=new JArray(0);
        if(variant=="fake")metadata[old?"_fake":"NE_fake"]=true;
        bool rejected=false;try{LoadSynthetic(changed,requiredExtensions:noodleCapability);}catch(NotSupportedException){rejected=true;}
        Check(rejected,$"v{major} arc opacity rejects {variant}");
    }
}
foreach(int major in new[]{2,3})foreach(string kind in new[]{"note","bomb","wall","fake-note","fake-bomb","fake-wall"})
{
    bool old=major==2;string customKey=old?"_customData":"customData";
    var objectRoot=old?JObject.Parse("""{"_version":"2.6.0","_notes":[{"_time":1,"_lineIndex":0,"_lineLayer":0,"_type":0,"_cutDirection":1},{"_time":1,"_lineIndex":1,"_lineLayer":1,"_type":0,"_cutDirection":1},{"_time":2,"_lineIndex":2,"_lineLayer":0,"_type":3,"_cutDirection":9}],"_obstacles":[{"_time":6,"_duration":2,"_lineIndex":1,"_width":2,"_type":1}]}"""):
        JObject.Parse("""{"version":"3.3.0","colorNotes":[{"b":1,"x":0,"y":0,"c":0,"d":1},{"b":1,"x":1,"y":1,"c":0,"d":1}],"bombNotes":[{"b":2,"x":2,"y":0}],"obstacles":[{"b":6,"d":2,"x":1,"y":2,"w":2,"h":3}]}""");
    string collection=kind.EndsWith("wall",StringComparison.Ordinal)?(old?"_obstacles":"obstacles"):(old?"_notes":kind.EndsWith("bomb",StringComparison.Ordinal)?"bombNotes":"colorNotes");
    int index=old&&kind.EndsWith("bomb",StringComparison.Ordinal)?2:0;
    var baseMetadata=new JObject();objectRoot[collection]![index]![customKey]=baseMetadata;
    if(kind.StartsWith("fake-",StringComparison.Ordinal))baseMetadata[old?"_fake":"NE_fake"]=true;
    var objectBaseline=LoadSynthetic(objectRoot,requiredExtensions:noodleCapability);
    foreach(string variant in new[]{"direct","animated","animated-arrow","empty"})
    {
        var changed=(JObject)objectRoot.DeepClone();var metadata=(JObject)changed[collection]![index]![customKey]!;
        string opacity=old?"_dissolve":"dissolve";
        if(variant=="direct")metadata[opacity]=new JArray(0);
        else metadata[old?"_animation":"animation"]=variant=="empty"?new JObject():new JObject {[variant=="animated-arrow"?opacity+"Arrow":opacity]=new JArray {new JArray(0,0),new JArray(1,1)}};
        var opacityMap=LoadSynthetic(changed,requiredExtensions:noodleCapability);
        Check(JToken.DeepEquals(JToken.FromObject(opacityMap.Input.Notes),JToken.FromObject(objectBaseline.Input.Notes)),$"v{major} {kind} {variant} preserves physical notes, roles and two-note tilt");
        Check(WallAlertAdapter.Count(opacityMap.SquatJson!,120,false,true)==WallAlertAdapter.Count(objectBaseline.SquatJson!,120,false,true)&&opacityMap.Json==changed.ToString(),$"v{major} {kind} {variant} preserves coverage and source JSON");
    }
    if(kind.StartsWith("fake-",StringComparison.Ordinal))continue;
    foreach(string invalid in new[]{"position","rotation","interactable","scale","alias","reference","nan","unknown"})
    {
        var changed=(JObject)objectRoot.DeepClone();var animation=new JObject {[old?"_dissolve":"dissolve"]=new JArray(0)};
        changed[collection]![index]![customKey]![old?"_animation":"animation"]=animation;
        if(invalid=="position")animation[old?"_position":"offsetPosition"]=new JArray(1,0,0);
        if(invalid=="rotation")animation[old?"_rotation":"offsetWorldRotation"]=new JArray(0,0,90);
        if(invalid=="interactable")animation[old?"_interactable":"interactable"]=new JArray(0);
        if(invalid=="scale")animation[old?"_scale":"scale"]=new JArray(2,2,2);
        if(invalid=="alias")animation[old?"dissolve":"_dissolve"]=new JArray(0);
        if(invalid=="reference")animation[old?"_dissolve":"dissolve"]="named-points";
        if(invalid=="nan")animation[old?"_dissolve":"dissolve"]=new JArray(double.NaN);
        if(invalid=="unknown")animation["unknownVisualProperty"]=new JArray(0);
        bool rejected=false;try{LoadSynthetic(changed,requiredExtensions:noodleCapability);}catch(NotSupportedException){rejected=true;}
        Check(rejected,$"v{major} {kind} animation rejects {invalid}");
    }
}
foreach(int major in new[]{2,3})foreach(int hand in new[]{0,1})
{
    bool old=major==2;string customKey=old?"_customData":"customData",arcKey=old?"_sliders":"sliders",interaction=old?"_interactable":"uninteractable";
    var interactionRoot=old?JObject.Parse("""{"_version":"2.6.0","_notes":[{"_time":1,"_lineIndex":0,"_lineLayer":0,"_type":0,"_cutDirection":0},{"_time":3,"_lineIndex":1,"_lineLayer":1,"_type":0,"_cutDirection":8}],"_sliders":[{"_headTime":1,"_headLineIndex":0,"_headLineLayer":0,"_headCutDirection":0,"_tailTime":3,"_tailLineIndex":1,"_tailLineLayer":1,"_tailCutDirection":8,"_colorType":0}]}"""):
        JObject.Parse("""{"version":"3.3.0","colorNotes":[{"b":1,"x":0,"y":0,"c":0,"d":0},{"b":3,"x":1,"y":1,"c":0,"d":8}],"sliders":[{"b":1,"x":0,"y":0,"c":0,"d":0,"tb":3,"tx":1,"ty":1,"tc":8}]}""");
    foreach(var note in interactionRoot[old?"_notes":"colorNotes"]!)note[old?"_type":"c"]=hand;
    interactionRoot[arcKey]![0]![old?"_colorType":"c"]=hand;
    var interactionBaseline=LoadSynthetic(interactionRoot,requiredExtensions:noodleCapability);
    foreach(var flag in new JToken[]{JValue.CreateNull(),new JValue(false),new JValue(true)})
    {
        var changed=(JObject)interactionRoot.DeepClone();changed[arcKey]![0]![customKey]=new JObject {[interaction]=flag.DeepClone()};
        var interactionMap=LoadSynthetic(changed,requiredExtensions:noodleCapability);
        Check(JToken.DeepEquals(JToken.FromObject(interactionMap.Input.Notes),JToken.FromObject(interactionBaseline.Input.Notes)),$"v{major} hand {hand} arc {flag} preserves physical head/tail roles, timing and angles");
        Check(interactionMap.Json==changed.ToString()&&JToken.DeepEquals(JToken.FromObject(CompatibleAnalysis.Analyze(interactionMap.Input,false)),JToken.FromObject(CompatibleAnalysis.Analyze(interactionBaseline.Input,false))),$"v{major} hand {hand} arc interaction preserves corrected metrics and source");
    }
    foreach(string invalid in new[]{"number","string","alias","fake","note","wall"})
    {
        var changed=(JObject)interactionRoot.DeepClone();var data=new JObject {[interaction]=true};changed[arcKey]![0]![customKey]=data;
        if(invalid=="number")data[interaction]=1;
        if(invalid=="string")data[interaction]="false";
        if(invalid=="alias")data[old?"uninteractable":"_interactable"]=false;
        if(invalid=="fake")data[old?"_fake":"NE_fake"]=true;
        if(invalid=="note")changed[old?"_notes":"colorNotes"]![0]![customKey]=new JObject {[interaction]=old?false:true};
        if(invalid=="wall")changed[old?"_obstacles":"obstacles"]=new JArray {old?new JObject {["_time"]=5,["_duration"]=1,["_lineIndex"]=1,["_width"]=2,["_type"]=1,[customKey]=new JObject {[interaction]=false}}:
            new JObject {["b"]=5,["d"]=1,["x"]=1,["y"]=2,["w"]=2,["h"]=3,[customKey]=new JObject {[interaction]=true}}};
        bool rejected=false;try{LoadSynthetic(changed,requiredExtensions:noodleCapability);}catch(NotSupportedException){rejected=true;}
        Check(rejected!=(invalid=="wall"),$"v{major} arc interaction preserves the supported wall boundary for {invalid}");
    }
}
foreach(int major in new[]{2,3})
{
    bool old=major==2;string customKey=old?"_customData":"customData";
    var scopeRoot=old?JObject.Parse("""{"_version":"2.6.0","_notes":[{"_time":1,"_lineIndex":0,"_lineLayer":0,"_type":0,"_cutDirection":1},{"_time":1,"_lineIndex":1,"_lineLayer":1,"_type":0,"_cutDirection":1},{"_time":2,"_lineIndex":2,"_lineLayer":0,"_type":3}],"_obstacles":[{"_time":3,"_duration":2,"_lineIndex":1,"_width":2,"_type":1}]}"""):
        JObject.Parse("""{"version":"3.3.0","colorNotes":[{"b":1,"x":0,"y":0,"c":0,"d":1},{"b":1,"x":1,"y":1,"c":0,"d":1}],"bombNotes":[{"b":2,"x":2,"y":0}],"obstacles":[{"b":3,"d":2,"x":1,"y":2,"w":2,"h":3}]}""");
    var scopeBaseline=LoadSynthetic(scopeRoot,requiredExtensions:noodleCapability);
    var nestedBlob=new JObject {[old?"_fake":"NE_fake"]=true,[old?"_position":"coordinates"]=new JArray(50,30),[old?"_localRotation":"localRotation"]=new JArray(90,0,0),[old?"_track":"track"]="unbound",
        [old?"_requirements":"requirements"]=new JArray("UnknownExtension","Noodle Extensions","Mapping Extensions"),["fakeBurstSliders"]=new JArray(new JObject()),["customEvents"]=new JArray(new JObject {["t"]="AssignPlayerToTrack"})};
    foreach(string variant in new[]{"object","empty","null"})
    {
        var changed=(JObject)scopeRoot.DeepClone();
        foreach(var array in changed.Properties().Select(p=>p.Value).OfType<JArray>())foreach(var item in array)
            item[customKey]=new JObject {[customKey]=variant=="object"?nestedBlob.DeepClone():variant=="empty"?new JObject():JValue.CreateNull()};
        var scoped=LoadSynthetic(changed,requiredExtensions:noodleCapability);
        Check(JToken.DeepEquals(JToken.FromObject(scoped.Input.Notes),JToken.FromObject(scopeBaseline.Input.Notes)),$"v{major} nested {variant} preserves real notes, bombs and pair tilt");
        Check(WallAlertAdapter.Count(scoped.SquatJson!,120,false,true)==1&&scoped.Json==changed.ToString()&&!scoped.MappingExtensions&&!scoped.HasNoodleGeometry,$"v{major} nested {variant} preserves wall coverage, raw JSON and feature selection");
        var unextended=LoadSynthetic(changed);
        var plainBaseline=LoadSynthetic(scopeRoot);
        Check(!unextended.NoodleExtensions&&!unextended.MappingExtensions&&JToken.DeepEquals(JToken.FromObject(unextended.Input.Notes),JToken.FromObject(plainBaseline.Input.Notes)),$"v{major} nested {variant} does not activate extensions, fake loader or unknown tilt");
    }
    foreach(string invalid in new[]{"array","number","wrong-alias","nan","active-fake","active-requirement","root-requirement","lookalike-container","active-geometry"})
    {
        var changed=(JObject)scopeRoot.DeepClone();var metadata=new JObject {[customKey]=nestedBlob.DeepClone()};changed[old?"_notes":"colorNotes"]![0]![customKey]=metadata;
        if(invalid=="array")metadata[customKey]=new JArray();
        if(invalid=="number")metadata[customKey]=1;
        if(invalid=="wrong-alias"){metadata.Remove(customKey);metadata[old?"customData":"_customData"]=new JObject();}
        if(invalid=="nan")metadata[customKey]!["marker"]=new JRaw("NaN");
        if(invalid=="active-fake")metadata[old?"_fake":"NE_fake"]="true";
        if(invalid=="active-requirement")metadata[old?"_requirements":"requirements"]=new JArray("UnknownExtension");
        if(invalid=="root-requirement")changed[customKey]=new JObject {[old?"_requirements":"requirements"]=new JArray("UnknownExtension")};
        if(invalid=="lookalike-container")changed["marker"]=new JObject {[customKey]=new JObject {[customKey]=new JObject {["requirements"]=new JArray("UnknownExtension")}}};
        if(invalid=="active-geometry")metadata[old?"_rotation":"worldRotation"]=new JArray(0,45,0);
        bool rejected=false;try{LoadSynthetic(changed,requiredExtensions:noodleCapability);}catch(NotSupportedException){rejected=true;}
        Check(rejected,$"v{major} nested metadata does not bypass {invalid}");
    }
    var realFake=(JObject)scopeRoot.DeepClone();realFake[old?"_notes":"colorNotes"]![0]![customKey]=new JObject {[customKey]=nestedBlob.DeepClone(),[old?"_fake":"NE_fake"]=true};
    Check(LoadSynthetic(realFake,requiredExtensions:noodleCapability).Input.Notes.Count(n=>n.IsFake)==1,$"v{major} real fake flag remains active next to nested metadata");
}
var nestedFakeRoot=JObject.Parse("""{"version":"3.3.0","colorNotes":[{"b":1,"x":0,"y":0,"c":0,"d":1}],"customData":{"fakeColorNotes":[{"b":2,"x":1,"y":1,"c":0,"d":1,"customData":{"customData":{"requirements":["UnknownExtension"],"NE_fake":false}}}]}}""");
var nestedFakeLoaded=LoadSynthetic(nestedFakeRoot,requiredExtensions:noodleCapability);
Check(nestedFakeLoaded.Input.Notes.Count(n=>n.IsFake)==1&&nestedFakeLoaded.Input.Notes.Count(n=>!n.IsFake)==1,"Actual fake collection remains active around nested metadata");
var activeFakeRequirement=(JObject)nestedFakeRoot.DeepClone();activeFakeRequirement["customData"]!["fakeColorNotes"]![0]!["customData"]!["requirements"]=new JArray("UnknownExtension");
bool activeFakeRequirementRejected=false;try{LoadSynthetic(activeFakeRequirement,requiredExtensions:noodleCapability);}catch(NotSupportedException){activeFakeRequirementRejected=true;}
Check(activeFakeRequirementRejected,"Requirements in active fake-object metadata are not hidden with nested metadata");
foreach(int major in new[]{2,3})foreach(string kind in new[]{"note","bomb","wall","arc"})
{
    bool old=major==2;string customKey=old?"_customData":"customData",animationKey=old?"_animation":"animation",scaleKey=old?"_scale":"scale";
    var identityRoot=old?JObject.Parse("""{"_version":"2.6.0","_notes":[{"_time":1,"_lineIndex":0,"_lineLayer":0,"_type":0,"_cutDirection":1},{"_time":1,"_lineIndex":1,"_lineLayer":1,"_type":0,"_cutDirection":1},{"_time":3,"_lineIndex":1,"_lineLayer":1,"_type":0,"_cutDirection":8},{"_time":2,"_lineIndex":2,"_lineLayer":0,"_type":3}],"_obstacles":[{"_time":5,"_duration":2,"_lineIndex":1,"_width":2,"_type":1}],"_sliders":[{"_headTime":1,"_headLineIndex":0,"_headLineLayer":0,"_headCutDirection":1,"_tailTime":3,"_tailLineIndex":1,"_tailLineLayer":1,"_tailCutDirection":8,"_colorType":0}]}"""):
        JObject.Parse("""{"version":"3.3.0","colorNotes":[{"b":1,"x":0,"y":0,"c":0,"d":1},{"b":1,"x":1,"y":1,"c":0,"d":1},{"b":3,"x":1,"y":1,"c":0,"d":8}],"bombNotes":[{"b":2,"x":2,"y":0}],"obstacles":[{"b":5,"d":2,"x":1,"y":2,"w":2,"h":3}],"sliders":[{"b":1,"x":0,"y":0,"c":0,"d":1,"tb":3,"tx":1,"ty":1,"tc":8}]}""");
    string collection=kind=="arc"?(old?"_sliders":"sliders"):kind=="wall"?(old?"_obstacles":"obstacles"):old?"_notes":kind=="bomb"?"bombNotes":"colorNotes";
    int index=old&&kind=="bomb"?3:0;
    identityRoot[collection]![index]![customKey]=new JObject();
    if(old&&kind=="note")identityRoot[collection]![index]![customKey]!["_cutDirection"]=135;
    var identityBaseline=LoadSynthetic(identityRoot,requiredExtensions:noodleCapability);
    foreach(string variant in new[]{"unused-alias","unused-frames","wall-unit","wall-frames"})
    {
        if(variant.StartsWith("wall-",StringComparison.Ordinal)&&kind!="wall")continue;
        var changed=(JObject)identityRoot.DeepClone();var animation=new JObject();
        bool wallUnit=variant.StartsWith("wall-",StringComparison.Ordinal);
        string key=wallUnit?animationKey:old?"animation":"_animation";
        animation[wallUnit?scaleKey:old?"scale":"_scale"]=variant.EndsWith("frames",StringComparison.Ordinal)?new JArray {new JArray(1,1,1,0),new JArray(1,1,1,1)}:new JArray(1,1,1);
        changed[collection]![index]![customKey]![key]=animation;
        var identityMap=LoadSynthetic(changed,requiredExtensions:noodleCapability);
        Check(JToken.DeepEquals(JToken.FromObject(identityMap.Input.Notes),JToken.FromObject(identityBaseline.Input.Notes)),$"v{major} {kind} {variant} scale preserves complete physical notes, pair tilt and legacy cut angle");
        Check(identityMap.Json==changed.ToString()&&WallAlertAdapter.Count(identityMap.SquatJson!,120,false,true)==1,$"v{major} {kind} {variant} scale preserves source and squat coverage");
    }
    foreach(string invalid in new[]{"canonical-unit","canonical-unit-frames","active","nonuniform","short","null","nan","duplicate-time","reference","frame-option","unused-active","unused-mixed","unused-duplicate","unused-reference","unused-nan"})
    {
        if(kind=="wall"&&(invalid=="canonical-unit"||invalid=="canonical-unit-frames"))continue;
        var changed=(JObject)identityRoot.DeepClone();var animation=new JObject {[scaleKey]=new JArray(1,1,1)};
        changed[collection]![index]![customKey]![animationKey]=animation;
        if(invalid=="canonical-unit-frames")animation[scaleKey]=new JArray {new JArray(1,1,1,0),new JArray(1,1,1,1)};
        if(invalid=="active")animation[scaleKey]=new JArray(2,2,2);
        if(invalid=="nonuniform")animation[scaleKey]=new JArray(1,2,1);
        if(invalid=="short")animation[scaleKey]=new JArray(1,1);
        if(invalid=="null")animation[scaleKey]=new JArray(1,JValue.CreateNull(),1);
        if(invalid=="nan")animation[scaleKey]=new JArray(1,new JRaw("NaN"),1);
        if(invalid=="duplicate-time")animation[scaleKey]=new JArray {new JArray(1,1,1,0),new JArray(1,1,1,0)};
        if(invalid=="reference")animation[scaleKey]="named-points";
        if(invalid=="frame-option")animation[scaleKey]=new JArray {new JArray(1,1,1,0,"easeOutQuad")};
        if(invalid=="unused-active"||invalid=="unused-mixed")
        {
            changed[collection]![index]![customKey]![old?"animation":"_animation"]=new JObject {[old?"scale":"_scale"]=new JArray(invalid=="unused-active"?2:1,1,1)};
            if(invalid=="unused-mixed")changed[collection]![index]![customKey]![old?"animation":"_animation"]![old?"dissolve":"_dissolve"]=new JArray(0);
        }
        if(invalid=="unused-duplicate"||invalid=="unused-reference"||invalid=="unused-nan")
        {
            JToken invalidScale=invalid=="unused-reference"?new JValue("named-points"):invalid=="unused-nan"?new JArray(1,new JRaw("NaN"),1):new JArray {new JArray(1,1,1,0),new JArray(1,1,1,0)};
            changed[collection]![index]![customKey]![old?"animation":"_animation"]=new JObject {[old?"scale":"_scale"]=invalidScale};
        }
        bool rejected=false;try{LoadSynthetic(changed,requiredExtensions:noodleCapability);}catch(NotSupportedException){rejected=true;}
        Check(rejected,$"v{major} {kind} identity animation does not enable {invalid}");
    }
}
var entryPoints=JArray.Parse("[[0,0.8,0,0],[0,0,0,0.4]]");
Check(NoodleVectorCurve.TryRead(entryPoints,out var entryCurve)&&entryCurve!.RestoredBeforeNote&&!entryCurve.AlwaysZero
    &&Math.Abs(entryCurve.At(.2f).Y-.4f)<1e-6&&entryCurve.At(.5f).IsZero&&entryCurve.At(2).IsZero,
    "Linear entry vector interpolates and clamps to zero before nominal note time");
foreach(var invalidCurve in new JToken[]{JValue.CreateNull(),new JArray(),new JArray(0,0),new JValue("named-points"),
    JArray.Parse("[[0,1,0,0],[0,0,0,0]]"),JArray.Parse("[[0,1,0,0.4],[0,0,0,0.2]]"),
    JArray.Parse("[[0,1,0,-0.1],[0,0,0,0.4]]"),JArray.Parse("[[0,1,0,0],[0,0,0,1.1]]"),
    JArray.Parse("[[0,1,0,0],[0,0,0,0.4,\"easeOutBack\"]]"),new JArray(0,new JRaw("NaN"),0),new JArray(0,1e100,0),
    JArray.Parse("[[0,1,0,0],[0,0,0,1e-50]]")})
    Check(!NoodleVectorCurve.TryRead(invalidCurve,out _),"Malformed, annotated or unresolved vector points never become linear by default");
var entryMap=JObject.Parse("""{"version":"3.3.0","colorNotes":[{"b":2,"x":0,"y":0,"c":0,"d":1},{"b":4,"x":1,"y":1,"c":0,"d":1},{"b":6,"x":2,"y":0,"c":1,"d":8}],"obstacles":[]}""");
var entryBaseline=LoadSynthetic(entryMap,requiredExtensions:noodleCapability);
var baselineSwings=CompatibleAnalysis.Analyze(entryBaseline.Input,reproduceKnownDefects:false);
entryMap["colorNotes"]![0]!["customData"]=new JObject {["animation"]=new JObject {["offsetPosition"]=entryPoints.DeepClone()}};
var withEntry=LoadSynthetic(entryMap,requiredExtensions:noodleCapability);
var entrySwings=CompatibleAnalysis.Analyze(withEntry.Input,reproduceKnownDefects:false);
Check(entrySwings.LeftBins.SequenceEqual(baselineSwings.LeftBins)&&entrySwings.RightBins.SequenceEqual(baselineSwings.RightBins)
    &&withEntry.HasNoodleGeometry&&withEntry.Input.Notes[0].CutDirectionAngleOffset==null&&withEntry.Input.Notes[0].Direction==1,
    "Restored ordinary-note entry animation preserves nominal swings while physical orientation remains unknown");
var pairedEntry=(JObject)entryMap.DeepClone();pairedEntry["colorNotes"]![1]!["b"]=2;pairedEntry["colorNotes"]![1]!["y"]=2;
var pairedEntryResult=LoadSynthetic(pairedEntry,requiredExtensions:noodleCapability);
Check(pairedEntryResult.Input.Notes.Where(n=>n.Beat==2).All(n=>n.CutDirectionAngleOffset==null),
    "Uncertain entry pose propagates to its two-note orientation partner");
var separateArc=(JObject)entryMap.DeepClone();separateArc["sliders"]=new JArray(JObject.Parse("""{"b":4,"x":1,"y":1,"c":0,"d":1,"tb":5,"tx":3,"ty":0,"tc":1}"""));
Check(LoadSynthetic(separateArc,requiredExtensions:noodleCapability).Input.Notes[0].CutDirectionAngleOffset==null,
    "An unrelated ordinary arc does not reject a restored standalone note");
foreach(string invalidEntry in new[]{"late","at-note","nonzero","easing","time","scale","bomb-boundary","wall","arc-head","fake"})
{
    var changed=(JObject)entryMap.DeepClone();var animation=changed["colorNotes"]![0]!["customData"]!["animation"]!;
    if(invalidEntry=="late"||invalidEntry=="at-note")animation["offsetPosition"]![1]![3]=invalidEntry=="late"?.75:.5;
    if(invalidEntry=="nonzero")animation["offsetPosition"]![1]![0]=1;
    if(invalidEntry=="easing")((JArray)animation["offsetPosition"]![1]!).Add("easeOutBack");
    if(invalidEntry=="time")animation["time"]=new JArray(0);
    if(invalidEntry=="scale")animation["scale"]=new JArray(1,1,1);
    if(invalidEntry=="fake")changed["colorNotes"]![0]!["customData"]!["NE_fake"]=true;
    if(invalidEntry=="bomb-boundary"){changed["bombNotes"]=new JArray(new JObject {["b"]=2,["x"]=0,["y"]=0,["customData"]=changed["colorNotes"]![0]!["customData"]!.DeepClone()});changed["bombNotes"]![0]!["customData"]!["animation"]!["offsetPosition"]![1]![3]=.5;}
    if(invalidEntry=="wall")changed["obstacles"]=new JArray(new JObject {["b"]=2,["d"]=4,["x"]=1,["y"]=2,["w"]=2,["h"]=3,["customData"]=changed["colorNotes"]![0]!["customData"]!.DeepClone()});
    if(invalidEntry=="arc-head")changed["sliders"]=new JArray(new JObject {["b"]=2,["x"]=0,["y"]=0,["c"]=0,["d"]=1,["tb"]=4,["tx"]=1,["ty"]=1,["tc"]=1});
    bool entryRejected=false;try{LoadSynthetic(changed,requiredExtensions:noodleCapability);}catch(NotSupportedException){entryRejected=true;}
    Check(entryRejected,"Entry-position support preserves its narrow scope: "+invalidEntry);
}
foreach(int vectorMajor in new[]{2,3})foreach(string vectorKind in new[]{"note","bomb","wall","arc"})
{
    var data=vectorMajor==2?JObject.Parse("""{"_version":"2.6.0","_notes":[{"_time":2,"_lineIndex":0,"_lineLayer":0,"_type":0,"_cutDirection":1}],"_obstacles":[]}"""):
        JObject.Parse("""{"version":"3.3.0","colorNotes":[{"b":2,"x":0,"y":0,"c":0,"d":1}],"obstacles":[]}""");
    string customKey=vectorMajor==2?"_customData":"customData",collection=vectorMajor==2?"_notes":"colorNotes";
    if(vectorKind=="bomb")
    {
        if(vectorMajor==2)data[collection]![0]!["_type"]=3;
        else{collection="bombNotes";data[collection]=new JArray(new JObject {["b"]=2,["x"]=0,["y"]=0});}
    }
    if(vectorKind=="wall")
    {
        collection=vectorMajor==2?"_obstacles":"obstacles";
        data[collection]=new JArray(vectorMajor==2?new JObject {["_time"]=2,["_duration"]=4,["_lineIndex"]=1,["_type"]=1,["_width"]=2}:
            new JObject {["b"]=2,["d"]=4,["x"]=1,["y"]=2,["w"]=2,["h"]=3});
    }
    if(vectorKind=="arc")
    {
        collection=vectorMajor==2?"_sliders":"sliders";
        data[collection]=new JArray(vectorMajor==2?JObject.Parse("""{"_headTime":2,"_headLineIndex":0,"_headLineLayer":0,"_colorType":0,"_headCutDirection":1,"_tailTime":4,"_tailLineIndex":1,"_tailLineLayer":1,"_tailCutDirection":1}"""):
            JObject.Parse("""{"b":2,"x":0,"y":0,"c":0,"d":1,"tb":4,"tx":1,"ty":1,"tc":1}"""));
    }
    data[collection]![0]![customKey]=new JObject {[vectorMajor==2?"_animation":"animation"]=new JObject {[vectorMajor==2?"_position":"offsetPosition"]=new JArray(0,0,0)}};
    var zeroPositionMap=LoadSynthetic(data,requiredExtensions:noodleCapability);
    Check(vectorKind=="bomb"||zeroPositionMap.Input.Notes.Length>0,$"v{vectorMajor} {vectorKind} constant-zero position is neutral");
}
Check(NoodleVectorCurve.TryRead(JArray.Parse("[[0,0,0,0,\"easeInCubic\"],[1,1,1,1,\"easeOutQuad\"]]"),out var destinationCurve)
    &&Math.Abs(destinationCurve!.At(.25f).X-.4375f)<1e-6,"Destination frame controls easing; source-frame annotation is not reused");
foreach(string family in new[]{"Quad","Cubic","Quart","Quint","Sine","Circ","Expo","Bounce"})foreach(string mode in new[]{"In","Out","InOut"})
{
    string easing="ease"+mode+family;
    var curvePoints=new JArray(new JArray(1,2,3,0),new JArray(0,0,0,.4f,easing));
    Check(NoodleVectorCurve.TryRead(curvePoints,out var curvedEntry)&&curvedEntry!.RestoredBeforeNote&&curvedEntry.At(.5f).IsZero,
        "Supported easing preserves verified zero tail: "+easing);
}
foreach(int namedMajor in new[]{2,3})
{
    string customKey=namedMajor==2?"_customData":"customData",noteKey=namedMajor==2?"_notes":"colorNotes",positionKey=namedMajor==2?"_position":"offsetPosition";
    var namedMap=namedMajor==2?JObject.Parse("""{"_version":"2.6.0","_notes":[{"_time":2,"_lineIndex":0,"_lineLayer":0,"_type":0,"_cutDirection":1}],"_obstacles":[]}"""):
        JObject.Parse("""{"version":"3.3.0","colorNotes":[{"b":2,"x":0,"y":0,"c":0,"d":1}],"obstacles":[]}""");
    namedMap[noteKey]![0]![customKey]=new JObject {[namedMajor==2?"_animation":"animation"]=new JObject {[positionKey]="zero"}};
    namedMap[customKey]=new JObject {[namedMajor==2?"_pointDefinitions":"pointDefinitions"]=namedMajor==2?
        new JArray(new JObject {["_name"]="zero",["_points"]=new JArray(0,0,0)}):new JObject {["zero"]=new JArray(0,0,0)}};
    string original=namedMap.ToString();var namedResult=LoadSynthetic(namedMap,requiredExtensions:noodleCapability);
    Check(namedResult.Json==original&&!namedResult.HasNoodleGeometry&&namedResult.Input.Notes[0].CutDirectionAngleOffset==0,
        $"v{namedMajor} named zero position resolves without mutating source or hiding known orientation");
    foreach(string invalidName in new[]{"missing","case","wrong-shape","alias-chain","bad-curve"})
    {
        var badName=(JObject)namedMap.DeepClone();var reference=badName[noteKey]![0]![customKey]![namedMajor==2?"_animation":"animation"]!;
        if(invalidName=="missing")reference[positionKey]="absent";
        if(invalidName=="case")reference[positionKey]="Zero";
        if(invalidName=="wrong-shape")badName[customKey]![namedMajor==2?"_pointDefinitions":"pointDefinitions"]=namedMajor==2?new JObject():new JArray();
        if(invalidName=="alias-chain"||invalidName=="bad-curve")
        {
            JToken value=invalidName=="alias-chain"?new JValue("other"):new JArray(new JArray(0,0,0,0,"splineCatmullRom"));
            if(namedMajor==2)badName[customKey]!["_pointDefinitions"]![0]!["_points"]=value;else badName[customKey]!["pointDefinitions"]!["zero"]=value;
        }
        bool namedRejected=false;try{LoadSynthetic(badName,requiredExtensions:noodleCapability);}catch(NotSupportedException){namedRejected=true;}
        Check(namedRejected,$"v{namedMajor} invalid named position never becomes a zero offset: {invalidName}");
        Check(LoadSynthetic(badName,features:AnalysisFeatures.Squats,requiredExtensions:noodleCapability).Input.Notes.Length==0,
            $"v{namedMajor} hidden notes do not resolve irrelevant point definitions: {invalidName}");
    }
    if(namedMajor==2)
    {
        var duplicate=(JObject)namedMap.DeepClone();((JArray)duplicate[customKey]!["_pointDefinitions"]!).Add(duplicate[customKey]!["_pointDefinitions"]![0]!.DeepClone());
        bool duplicateRejected=false;try{LoadSynthetic(duplicate,requiredExtensions:noodleCapability);}catch(NotSupportedException){duplicateRejected=true;}
        Check(duplicateRejected,"Duplicate legacy definitions remain unsupported");
    }
    else
    {
        var curved=(JObject)namedMap.DeepClone();curved[customKey]!["pointDefinitions"]!["zero"]=new JArray(new JArray(0,.8f,0,0),new JArray(0,0,0,.4f,"easeOutSine"));
        var curvedResult=LoadSynthetic(curved,requiredExtensions:noodleCapability);
        Check(curvedResult.HasNoodleGeometry&&curvedResult.Input.Notes[0].CutDirectionAngleOffset==null&&curvedResult.Json==curved.ToString(),
            "Named eased entry retains source fingerprint and unknown physical orientation");
    }
}
foreach(int motionMajor in new[]{2,3})foreach(string motionFlag in new[]{motionMajor==2?"_disableNoteGravity":"disableNoteGravity",motionMajor==2?"_disableNoteLook":"disableNoteLook"})
{
    string customKey=motionMajor==2?"_customData":"customData",collection=motionMajor==2?"_notes":"colorNotes";
    var motionMap=motionMajor==2?JObject.Parse("""{"_version":"2.6.0","_notes":[{"_time":2,"_lineIndex":0,"_lineLayer":0,"_type":0,"_cutDirection":1},{"_time":2,"_lineIndex":1,"_lineLayer":2,"_type":0,"_cutDirection":1}],"_obstacles":[]}"""):
        JObject.Parse("""{"version":"3.3.0","colorNotes":[{"b":2,"x":0,"y":0,"c":0,"d":1},{"b":2,"x":1,"y":2,"c":0,"d":1}],"obstacles":[]}""");
    var normalMotion=CompatibleAnalysis.Analyze(LoadSynthetic(motionMap,requiredExtensions:noodleCapability).Input,reproduceKnownDefects:false);
    motionMap[collection]![0]![customKey]=new JObject {[motionFlag]=true};
    var flaggedMotion=LoadSynthetic(motionMap,requiredExtensions:noodleCapability);
    var flaggedMetrics=CompatibleAnalysis.Analyze(flaggedMotion.Input,reproduceKnownDefects:false);
    Check(flaggedMotion.HasNoodleGeometry&&flaggedMotion.Input.Notes.All(n=>n.CutDirectionAngleOffset==null)
        &&normalMotion.LeftBins.SequenceEqual(flaggedMetrics.LeftBins)&&flaggedMotion.Json==motionMap.ToString(),
        $"v{motionMajor} {motionFlag} preserves nominal swings and source, marks motion and paired physical tilt uncertain");
    foreach(string invalidMotion in new[]{"string","integer","fake","arc-head","bomb","invalid-color"})
    {
        var changed=(JObject)motionMap.DeepClone();
        if(invalidMotion=="string")changed[collection]![0]![customKey]![motionFlag]="true";
        if(invalidMotion=="integer")changed[collection]![0]![customKey]![motionFlag]=1;
        if(invalidMotion=="fake")changed[collection]![0]![customKey]![motionMajor==2?"_fake":"NE_fake"]=true;
        if(invalidMotion=="invalid-color")changed[collection]![0]![motionMajor==2?"_type":"c"]=-1;
        if(invalidMotion=="bomb")
        {
            if(motionMajor==2)changed[collection]![0]!["_type"]=3;
            else changed["bombNotes"]=new JArray(new JObject {["b"]=2,["x"]=0,["y"]=0,[customKey]=changed[collection]![0]![customKey]!.DeepClone()});
        }
        if(invalidMotion=="arc-head")changed[motionMajor==2?"_sliders":"sliders"]=new JArray(motionMajor==2?
            JObject.Parse("""{"_headTime":2,"_headLineIndex":0,"_headLineLayer":0,"_colorType":0,"_headCutDirection":1,"_tailTime":4,"_tailLineIndex":1,"_tailLineLayer":2,"_tailCutDirection":1}"""):
            JObject.Parse("""{"b":2,"x":0,"y":0,"c":0,"d":1,"tb":4,"tx":1,"ty":2,"tc":1}"""));
        bool motionRejected=false;try{LoadSynthetic(changed,requiredExtensions:noodleCapability);}catch(NotSupportedException){motionRejected=true;}
        Check(invalidMotion=="bomb"&&motionMajor==3?!motionRejected:motionRejected,$"v{motionMajor} {motionFlag} scope for {invalidMotion}");
    }
    if(motionMajor==2)
    {
        var angled=(JObject)motionMap.DeepClone();angled[collection]![0]![customKey]!["_cutDirection"]=17;
        var angledResult=LoadSynthetic(angled,requiredExtensions:noodleCapability);
        Check(angledResult.Input.Notes[0].CutDirectionAngleOffset==null&&angledResult.Input.Notes[0].Direction==1,
            "Legacy cut-angle override cannot restore a claimed physical tilt after non-default motion");
    }
}
var combinedMotion=(JObject)entryMap.DeepClone();combinedMotion["colorNotes"]![0]!["customData"]!["disableNoteGravity"]=true;
combinedMotion["colorNotes"]![0]!["customData"]!["disableNoteLook"]=true;
var combinedMotionResult=LoadSynthetic(combinedMotion,requiredExtensions:noodleCapability);
Check(combinedMotionResult.HasNoodleGeometry&&combinedMotionResult.Input.Notes[0].CutDirectionAngleOffset==null
    &&CompatibleAnalysis.Analyze(combinedMotionResult.Input,reproduceKnownDefects:false).LeftBins.SequenceEqual(entrySwings.LeftBins),
    "Combined gravity/look flags and restored entry offset retain validated nominal swings without claiming physical tilt");
foreach(int opacityMajor in new[]{2,3})foreach(string channel in new[]{"dissolve","dissolveArrow"})
{
    bool old=opacityMajor==2;
    var sample=old?JObject.Parse("""{"_version":"2.6.0","_notes":[{"_time":2,"_lineIndex":0,"_lineLayer":0,"_type":0,"_cutDirection":1}]}"""):
        JObject.Parse("""{"version":"3.3.0","colorNotes":[{"b":2,"x":0,"y":0,"c":0,"d":1}]}""");
    var easingNames=new List<string>{"easeLinear"};
    foreach(string family in new[]{"Quad","Cubic","Quart","Quint","Sine","Circ","Expo","Bounce"})
        foreach(string mode in new[]{"In","Out","InOut"})easingNames.Add("ease"+mode+family);
    foreach(string easingName in easingNames.Concat(new[]{"unknown","easeOutElastic","splineCatmullRom"}))
    {
        var changed=(JObject)sample.DeepClone();
        var frames=new JArray(new JArray(1,0),new JArray(0,.49,easingName));
        changed[old?"_notes":"colorNotes"]![0]![old?"_customData":"customData"]=new JObject {
            [old?"_animation":"animation"]=new JObject {[old?"_"+channel:channel]=frames}};
        bool rejected=false;LoadedMap? opacityLoaded=null;
        try{opacityLoaded=LoadSynthetic(changed,requiredExtensions:noodleCapability);}catch(NotSupportedException){rejected=true;}
        bool supported=easingNames.Contains(easingName);
        Check(supported?!rejected&&opacityLoaded!.Json==changed.ToString()&&opacityLoaded.Input.Notes.Length==1:rejected,
            $"v{opacityMajor} {channel} scalar frame easing {easingName} is explicitly validated");
    }
}
var rotationBase=JObject.Parse("""{"version":"3.3.0","colorNotes":[{"b":2,"x":0,"y":0,"c":0,"d":1},{"b":4,"x":1,"y":0,"c":0,"d":1}]}""");
var rotationBaseline=LoadSynthetic(rotationBase,requiredExtensions:noodleCapability);
var rotationBins=CompatibleAnalysis.Analyze(rotationBaseline.Input,reproduceKnownDefects:false).LeftBins;
var rotationEasings=new List<string>{"easeLinear"};
foreach(string family in new[]{"Quad","Cubic","Quart","Quint","Sine","Circ","Expo","Bounce"})
    foreach(string mode in new[]{"In","Out","InOut"})rotationEasings.Add("ease"+mode+family);
foreach(string easing in rotationEasings)foreach(bool withPosition in new[]{false,true})
{
    var changed=(JObject)rotationBase.DeepClone();
    var animation=new JObject {["localRotation"]=new JArray(new JArray(180,0,90,.125),new JArray(0,0,0,.375,easing))};
    if(withPosition)animation["offsetPosition"]=new JArray(new JArray(1,2,-3,0),new JArray(0,0,0,.4));
    changed["colorNotes"]![0]!["customData"]=new JObject {["animation"]=animation};
    var rotationLoaded=LoadSynthetic(changed,requiredExtensions:noodleCapability);
    Check(rotationLoaded.HasNoodleGeometry&&rotationLoaded.Json==changed.ToString()
        &&rotationLoaded.Input.Notes[0].CutDirectionAngleOffset==null
        &&CompatibleAnalysis.Analyze(rotationLoaded.Input,reproduceKnownDefects:false).LeftBins.SequenceEqual(rotationBins),
        $"Restored local rotation {easing}, position={withPosition}, preserves nominal swings with unknown pose");
}
foreach(string invalid in new[]{"late","exact-note","nonzero","unknown-easing","spline","named","duplicate-time","wrong-alias","world","fake","arc","chain","bomb","invalid-color","v2"})
{
    var changed=(JObject)rotationBase.DeepClone();
    var frames=new JArray(new JArray(180,0,90,.125),new JArray(0,0,0,.375,"easeOutSine"));
    var metadata=new JObject {["animation"]=new JObject {["localRotation"]=frames}};
    changed["colorNotes"]![0]!["customData"]=metadata;
    if(invalid=="late")frames[1]![3]=.75;
    if(invalid=="exact-note")frames[1]![3]=.5;
    if(invalid=="nonzero")frames[1]![0]=1;
    if(invalid=="unknown-easing")frames[1]![4]="unknown";
    if(invalid=="spline")((JArray)frames[1]!).Add("splineCatmullRom");
    if(invalid=="named")metadata["animation"]!["localRotation"]="rotation";
    if(invalid=="duplicate-time")frames[1]![3]=.125;
    if(invalid=="wrong-alias"||invalid=="world")
    {metadata["animation"]![invalid=="world"?"offsetWorldRotation":"_localRotation"]=frames.DeepClone();((JObject)metadata["animation"]!).Remove("localRotation");}
    if(invalid=="fake")metadata["fake"]=true;
    if(invalid=="arc")changed["sliders"]=JArray.Parse("""[{"b":2,"x":0,"y":0,"c":0,"d":1,"tb":4,"tx":1,"ty":0,"tc":1}]""");
    if(invalid=="chain")changed["burstSliders"]=JArray.Parse("""[{"b":2,"x":0,"y":0,"c":0,"d":1,"tb":3,"tx":1,"ty":0,"sc":3,"s":1}]""");
    if(invalid=="bomb"){changed["bombNotes"]=new JArray(new JObject {["b"]=2,["x"]=0,["y"]=0,["customData"]=metadata.DeepClone()});changed["colorNotes"]![0]!["customData"]=null;}
    if(invalid=="invalid-color")changed["colorNotes"]![0]!["c"]=-1;
    if(invalid=="v2")changed=JObject.Parse("""{"_version":"2.6.0","_notes":[{"_time":2,"_lineIndex":0,"_lineLayer":0,"_type":0,"_cutDirection":1,"_customData":{"_animation":{"_localRotation":[[180,0,90,0.125],[0,0,0,0.375]]}}}]}""");
    bool rejected=false;try{LoadSynthetic(changed,requiredExtensions:noodleCapability);}catch(NotSupportedException){rejected=true;}
    Check(rejected,$"Local rotation entry rejects {invalid}");
}
var trackBase=JObject.Parse("""{"version":"3.3.0","colorNotes":[{"b":2,"x":0,"y":0,"c":0,"d":1},{"b":4,"x":1,"y":0,"c":1,"d":1}]}""");
var trackBaseline=LoadSynthetic(trackBase,requiredExtensions:noodleCapability);
var trackBaselineResult=CompatibleAnalysis.Analyze(trackBaseline.Input,reproduceKnownDefects:false);
foreach(string geometry in new[]{"offsetPosition","scale","both"})foreach(bool multiple in new[]{false,true})foreach(string easing in new[]{"easeLinear","easeOutBounce"})
{
    var changed=(JObject)trackBase.DeepClone();
    changed["colorNotes"]![0]!["customData"]=new JObject {["track"]="fixture"};
    changed["colorNotes"]![1]!["customData"]=new JObject {["track"]="other"};
    var data=new JObject {["track"]=multiple?new JArray("fixture","other"):new JValue("fixture"),["duration"]=2,["easing"]=easing};
    if(geometry!="scale")data["offsetPosition"]=JArray.Parse("[[1,2,3,0],[0,0,0,0.375]]");
    if(geometry!="offsetPosition")data["scale"]=JArray.Parse("[[2,3,4,0],[1,1,1,0.375]]");
    changed["customData"]=new JObject {["customEvents"]=new JArray(new JObject {["b"]=0,["t"]="AssignPathAnimation",["d"]=data})};
    var trackLoaded=LoadSynthetic(changed,requiredExtensions:noodleCapability);
    var result=CompatibleAnalysis.Analyze(trackLoaded.Input,reproduceKnownDefects:false);
    Check(trackLoaded.HasNoodleGeometry&&trackLoaded.Json==changed.ToString()&&trackLoaded.Input.Notes[0].CutDirectionAngleOffset==null
        &&result.LeftBins.SequenceEqual(trackBaselineResult.LeftBins)&&result.RightBins.SequenceEqual(trackBaselineResult.RightBins),
        $"Nominal-neutral path {geometry}, multiple={multiple}, {easing}, preserves nominal geometry and source");
}
var nominalTrackMap=(JObject)trackBase.DeepClone();
nominalTrackMap["colorNotes"]![0]!["customData"]=new JObject {["track"]="fixture"};
nominalTrackMap["customData"]=JObject.Parse("""{"customEvents":[{"b":0,"t":"AssignPathAnimation","d":{"track":"fixture","duration":2,"scale":[[2,2,2,0],[1,1,1,0.375]]}}]}""");
foreach(string invalid in new[]{"nonunit","exact-note","late","named","duplicate-target","repeat","unknown-field","player","parent","nonzero-animate","fake","bomb-fake","wall","arc-head","chain","v2"})
{
    var changed=(JObject)nominalTrackMap.DeepClone();var ev=changed["customData"]!["customEvents"]![0]!;var data=ev["d"]!;
    if(invalid=="nonunit")data["scale"]![1]![0]=2;
    if(invalid=="exact-note")data["scale"]![1]![3]=.5;
    if(invalid=="late")data["scale"]![1]![3]=.75;
    if(invalid=="named")data["scale"]="curve";
    if(invalid=="duplicate-target")data["track"]=new JArray("fixture","fixture");
    if(invalid=="repeat")data["repeat"]=1;
    if(invalid=="unknown-field")data["rotation"]=new JArray(0,0,0);
    if(invalid=="player"||invalid=="parent")ev["t"]=invalid=="player"?"AssignPlayerToTrack":"AssignTrackParent";
    if(invalid=="nonzero-animate"){ev["t"]="AnimateTrack";((JObject)data).Remove("scale");data["offsetPosition"]=JArray.Parse("[[1,0,0,0],[1,0,0,0.375]]");}
    if(invalid=="fake")changed["colorNotes"]![0]!["customData"]!["fake"]=true;
    if(invalid=="bomb-fake")changed["bombNotes"]=JArray.Parse("""[{"b":2,"x":0,"y":0,"customData":{"track":"fixture","fake":true}}]""");
    if(invalid=="wall")changed["obstacles"]=JArray.Parse("""[{"b":2,"d":1,"x":1,"y":2,"w":2,"h":3,"customData":{"track":"fixture"}}]""");
    if(invalid=="arc-head")changed["sliders"]=JArray.Parse("""[{"b":2,"x":0,"y":0,"c":0,"d":1,"tb":4,"tx":1,"ty":0,"tc":1}]""");
    if(invalid=="chain")changed["burstSliders"]=JArray.Parse("""[{"b":2,"x":0,"y":0,"c":0,"d":1,"tb":3,"tx":1,"ty":0,"sc":3,"s":1}]""");
    if(invalid=="v2")changed=JObject.Parse("""{"_version":"2.6.0","_notes":[{"_time":2,"_lineIndex":0,"_lineLayer":0,"_type":0,"_cutDirection":1,"_customData":{"_track":"fixture"}}],"_customData":{"_customEvents":[{"_time":0,"_type":"AssignPathAnimation","_data":{"_track":"fixture","_scale":[1,1,1]}}]}}""");
    bool rejected=false;try{LoadSynthetic(changed,requiredExtensions:noodleCapability);}catch(NotSupportedException){rejected=true;}
    Check(rejected,$"Nominal path profile rejects {invalid}");
}
var zeroTrackMap=(JObject)nominalTrackMap.DeepClone();var zeroTrackEvent=zeroTrackMap["customData"]!["customEvents"]![0]!;
zeroTrackEvent["t"]="AnimateTrack";((JObject)zeroTrackEvent["d"]!).Remove("scale");zeroTrackEvent["d"]!["offsetPosition"]=new JArray(0,0,0);
Check(LoadSynthetic(zeroTrackMap,requiredExtensions:noodleCapability).HasNoodleGeometry,"Always-zero AnimateTrack offset is nominal-neutral");
var pathWithVisual=(JObject)nominalTrackMap.DeepClone();
((JArray)pathWithVisual["customData"]!["customEvents"]!).Add(JObject.Parse("""{"b":1,"t":"AnimateTrack","d":{"track":"fixture","duration":3,"easing":"easeOutQuint","dissolve":[[1,0],[0,1]]}}"""));
Check(LoadSynthetic(pathWithVisual,requiredExtensions:noodleCapability).HasNoodleGeometry,"Nominal path composes with separately verified visual track easing");
var pathReplacement=(JObject)nominalTrackMap.DeepClone();var replacementEvent=pathReplacement["customData"]!["customEvents"]![0]!.DeepClone();
replacementEvent["b"]=.5;replacementEvent["d"]!["scale"]![0]![0]=5;
((JArray)pathReplacement["customData"]!["customEvents"]!).Add(replacementEvent);
Check(LoadSynthetic(pathReplacement,requiredExtensions:noodleCapability).HasNoodleGeometry,"Repeated path definitions remain neutral when their nominal endpoints agree");
var songTrackMap=(JObject)trackBase.DeepClone();
songTrackMap["colorNotes"]![0]!["customData"]=new JObject {["track"]="fixture"};
songTrackMap["colorNotes"]![1]!["customData"]=new JObject {["track"]="fixture"};
songTrackMap["customData"]=JObject.Parse("""{"customEvents":[{"b":0,"t":"AnimateTrack","d":{"track":"fixture","duration":2,"easing":"easeLinear","offsetPosition":[[0,1,0,0.125],[0,0,0,0.374]]}}]}""");
var songTrackLoaded=LoadSynthetic(songTrackMap,requiredExtensions:noodleCapability);
Check(songTrackLoaded.HasNoodleGeometry&&songTrackLoaded.Json==songTrackMap.ToString()&&songTrackLoaded.Input.Notes.All(n=>n.CutDirectionAngleOffset==null)
    &&CompatibleAnalysis.Analyze(songTrackLoaded.Input,reproduceKnownDefects:false).LeftBins.SequenceEqual(trackBaselineResult.LeftBins),
    "Finished song-time offsets preserve nominal swings and source with conservative physical pose");
foreach(string variant in new[]{"active","boundary","event-start","nonlinear","repeat","overlap","same-time","tempo","bad-duration","bad-easing","constant-end","fake","bomb-active","wall","linked","named"})
{
    var changed=(JObject)songTrackMap.DeepClone();var ev=changed["customData"]!["customEvents"]![0]!;var data=ev["d"]!;
    if(variant=="active")changed["colorNotes"]![0]!["b"]=.5;
    if(variant=="boundary"){data["offsetPosition"]![1]![3]=.5;changed["colorNotes"]![0]!["b"]=1;}
    if(variant=="event-start")changed["colorNotes"]![0]!["b"]=0;
    if(variant=="nonlinear")data["easing"]="easeOutBounce";
    if(variant=="repeat")data["repeat"]=1;
    if(variant=="overlap"||variant=="same-time")
    {var other=ev.DeepClone();other["b"]=variant=="overlap"?1:0;((JArray)changed["customData"]!["customEvents"]!).Add(other);}
    if(variant=="tempo")changed["bpmEvents"]=JArray.Parse("[{\"b\":1,\"m\":180}]");
    if(variant=="bad-duration")data["duration"]="bad";
    if(variant=="bad-easing")data["easing"]=new JArray();
    if(variant=="constant-end")data["offsetPosition"]![1]![1]=1;
    if(variant=="fake")changed["colorNotes"]![0]!["customData"]!["fake"]=true;
    if(variant=="bomb-active")changed["bombNotes"]=JArray.Parse("""[{"b":0.5,"x":0,"y":0,"customData":{"track":"fixture"}}]""");
    if(variant=="wall")changed["obstacles"]=JArray.Parse("""[{"b":2,"d":1,"x":1,"y":2,"w":2,"h":3,"customData":{"track":"fixture"}}]""");
    if(variant=="linked")changed["sliders"]=JArray.Parse("""[{"b":2,"x":0,"y":0,"c":0,"d":1,"tb":4,"tx":1,"ty":0,"tc":1}]""");
    if(variant=="named")data["offsetPosition"]="curve";
    bool rejected=false;try{LoadSynthetic(changed,requiredExtensions:noodleCapability);}catch(NotSupportedException){rejected=true;}
    Check(rejected,"Song-time offset profile rejects "+variant);
}
foreach(string variant in new[]{"before","replacement","path-scale","zero-bpm-event"})
{
    var changed=(JObject)songTrackMap.DeepClone();var events=(JArray)changed["customData"]!["customEvents"]!;
    if(variant=="before"){events[0]!["b"]=5;}
    if(variant=="replacement"){var other=events[0]!.DeepClone();other["b"]=2.5;other["d"]!["duration"]=1;events.Add(other);}
    if(variant=="path-scale")events.Add(JObject.Parse("""{"b":0,"t":"AssignPathAnimation","d":{"track":"fixture","scale":[[2,2,2,0],[1,1,1,0.375]]}}"""));
    if(variant=="zero-bpm-event")changed["bpmEvents"]=new JArray(new JObject {["b"]=0,["m"]=120});
    Check(LoadSynthetic(changed,requiredExtensions:noodleCapability).HasNoodleGeometry,"Song-time offset accepts "+variant);
}
var monotonicEasings=new List<string>{"easeLinear"};
foreach(string family in new[]{"Quad","Cubic","Quart","Quint","Sine","Circ","Expo"})
    foreach(string mode in new[]{"In","Out","InOut"})monotonicEasings.Add("ease"+mode+family);
foreach(string easing in monotonicEasings)
{
    var changed=(JObject)songTrackMap.DeepClone();changed["customData"]!["customEvents"]![0]!["d"]!["easing"]=easing;
    Check(LoadSynthetic(changed,requiredExtensions:noodleCapability).HasNoodleGeometry,"Monotonic event clock retains completed offset: "+easing);
}
foreach(var sample in new[]{(Easing:"easeOutSine",Beat:.6f,Accept:true),(Easing:"easeInCubic",Beat:1.5f,Accept:true),(Easing:"easeInQuint",Beat:1.5f,Accept:false)})
{
    var changed=(JObject)songTrackMap.DeepClone();changed["colorNotes"]![0]!["b"]=sample.Beat;
    changed["customData"]!["customEvents"]![0]!["d"]!["easing"]=sample.Easing;
    bool accepted=true;try{LoadSynthetic(changed,requiredExtensions:noodleCapability);}catch(NotSupportedException){accepted=false;}
    Check(accepted==sample.Accept,"Event easing changes completion time independently of object time: "+sample.Easing);
}
var bombEasings=new List<string>(monotonicEasings);bombEasings.AddRange(new[]{"easeInBounce","easeOutBounce","easeInOutBounce"});
foreach(string easing in bombEasings)
{
    var changed=(JObject)trackBase.DeepClone();changed["bombNotes"]=new JArray(new JObject {["b"]=2,["x"]=0,["y"]=0,
        ["customData"]=new JObject {["animation"]=new JObject {["offsetPosition"]=new JArray(new JArray(0,4,1,.125),new JArray(0,0,0,.375,easing))}}});
    var result=LoadSynthetic(changed,requiredExtensions:noodleCapability);
    Check(result.HasNoodleGeometry&&result.Json==changed.ToString()
        &&CompatibleAnalysis.Analyze(result.Input,reproduceKnownDefects:false).LeftBins.SequenceEqual(trackBaselineResult.LeftBins),
        "Bomb entry returns before nominal time without changing swings: "+easing);
}
foreach(string invalid in new[]{"late","boundary","nonzero","rotation","scale","time","fake","cut-flags","v2"})
{
    var changed=(JObject)trackBase.DeepClone();var animation=JObject.Parse("""{"offsetPosition":[[0,4,0,0.125],[0,0,0,0.375]]}""");
    var metadata=new JObject {["animation"]=animation};changed["bombNotes"]=new JArray(new JObject {["b"]=2,["x"]=0,["y"]=0,["customData"]=metadata});
    if(invalid=="late"||invalid=="boundary")animation["offsetPosition"]![1]![3]=invalid=="late"?.75:.5;
    if(invalid=="nonzero")animation["offsetPosition"]![1]![1]=1;
    if(invalid=="rotation")animation["localRotation"]=JArray.Parse("[[0,90,0,0],[0,0,0,0.375]]");
    if(invalid=="scale")animation["scale"]=new JArray(1,1,1);
    if(invalid=="time")animation["time"]=new JArray(0);
    if(invalid=="fake")metadata["fake"]=true;
    if(invalid=="cut-flags")metadata["disableBadCutSpeed"]=true;
    if(invalid=="v2")changed=JObject.Parse("""{"_version":"2.6.0","_notes":[{"_time":2,"_lineIndex":0,"_lineLayer":0,"_type":3,"_cutDirection":8,"_customData":{"_animation":{"_position":[[0,4,0,0.125],[0,0,0,0.375]]}}}]}""");
    bool rejected=false;try{LoadSynthetic(changed,requiredExtensions:noodleCapability);}catch(NotSupportedException){rejected=true;}
    Check(rejected,"Bomb entry rejects "+invalid);
}
foreach(string flag in new[]{"disableNoteGravity","disableNoteLook","both"})foreach(bool coordinates in new[]{false,true})foreach(bool entry in new[]{false,true})
{
    var changed=(JObject)trackBase.DeepClone();var metadata=new JObject();
    if(flag!="disableNoteLook")metadata["disableNoteGravity"]=true;
    if(flag!="disableNoteGravity")metadata["disableNoteLook"]=true;
    if(coordinates)metadata["coordinates"]=new JArray(.25,1.5);
    if(entry)metadata["animation"]=JObject.Parse("""{"offsetPosition":[[0,4,0,0.125],[0,0,0,0.375]]}""");
    changed["bombNotes"]=new JArray(new JObject {["b"]=2,["x"]=0,["y"]=1,["customData"]=metadata});
    var result=LoadSynthetic(changed,requiredExtensions:noodleCapability);
    var actual=CompatibleAnalysis.Analyze(result.Input,reproduceKnownDefects:false);
    Check(result.HasNoodleGeometry&&result.Json==changed.ToString()&&actual.LeftBins.SequenceEqual(trackBaselineResult.LeftBins)
        &&actual.RightBins.SequenceEqual(trackBaselineResult.RightBins)&&actual.FirstLeft==trackBaselineResult.FirstLeft&&actual.FirstRight==trackBaselineResult.FirstRight,
        $"Bomb flags {flag}, coords={coordinates}, entry={entry}, preserve supported nominal metrics without asserting physical bomb height");
}
foreach(string invalid in new[]{"string","integer","fake","cut-direction","cut-speed","cut-type","v2"})
{
    var changed=(JObject)trackBase.DeepClone();var metadata=new JObject {["disableNoteGravity"]=true};
    changed["bombNotes"]=new JArray(new JObject {["b"]=2,["x"]=0,["y"]=1,["customData"]=metadata});
    if(invalid=="string")metadata["disableNoteGravity"]="true";
    if(invalid=="integer")metadata["disableNoteGravity"]=1;
    if(invalid=="fake")metadata["fake"]=true;
    if(invalid=="cut-direction")metadata["disableBadCutDirection"]=true;
    if(invalid=="cut-speed")metadata["disableBadCutSpeed"]=true;
    if(invalid=="cut-type")metadata["disableBadCutSaberType"]=true;
    if(invalid=="v2")changed=JObject.Parse("""{"_version":"2.6.0","_notes":[{"_time":2,"_lineIndex":0,"_lineLayer":1,"_type":3,"_cutDirection":8,"_customData":{"_disableNoteGravity":true}}]}""");
    bool rejected=false;try{LoadSynthetic(changed,requiredExtensions:noodleCapability);}catch(NotSupportedException){rejected=true;}
    Check(rejected,"Bomb motion flags reject "+invalid);
}
foreach(string geometry in new[]{"offsetPosition","scale","both","song"})foreach(string flag in new[]{"none","gravity","look","both"})foreach(bool entry in new[]{false,true})
{
    var changed=(JObject)trackBase.DeepClone();var metadata=new JObject {["track"]="fixture"};
    if(flag=="gravity"||flag=="both")metadata["disableNoteGravity"]=true;
    if(flag=="look"||flag=="both")metadata["disableNoteLook"]=true;
    if(entry)metadata["animation"]=JObject.Parse("""{"offsetPosition":[[0,4,0,0],[0,0,0,0.375]]}""");
    changed["bombNotes"]=new JArray(new JObject {["b"]=2,["x"]=0,["y"]=1,["customData"]=metadata});
    var fields=new JObject {["track"]="fixture",["duration"]=2};
    if(geometry!="scale")fields["offsetPosition"]=JArray.Parse("[[0,1,0,0],[0,0,0,0.375]]");
    if(geometry=="scale"||geometry=="both")fields["scale"]=JArray.Parse("[[2,2,2,0],[1,1,1,0.375]]");
    changed["customData"]=new JObject {["customEvents"]=new JArray(new JObject {["b"]=0,["t"]=geometry=="song"?"AnimateTrack":"AssignPathAnimation",["d"]=fields})};
    var result=LoadSynthetic(changed,requiredExtensions:noodleCapability);var metrics=CompatibleAnalysis.Analyze(result.Input,reproduceKnownDefects:false);
    Check(result.HasNoodleGeometry&&result.Json==changed.ToString()&&metrics.LeftBins.SequenceEqual(trackBaselineResult.LeftBins)
        &&metrics.RightBins.SequenceEqual(trackBaselineResult.RightBins)&&metrics.FirstLeft==trackBaselineResult.FirstLeft&&metrics.FirstRight==trackBaselineResult.FirstRight,
        $"Nominal bomb track {geometry}, flags={flag}, entry={entry}, preserves supported metrics and history guard");
}
foreach(string invalid in new[]{"late-scale","nonunit","active-song","repeat","fake","named","parent"})
{
    var changed=(JObject)trackBase.DeepClone();changed["bombNotes"]=JArray.Parse("""[{"b":2,"x":0,"y":1,"customData":{"track":"fixture"}}]""");
    changed["customData"]=JObject.Parse("""{"customEvents":[{"b":0,"t":"AssignPathAnimation","d":{"track":"fixture","duration":2,"scale":[[2,2,2,0],[1,1,1,0.375]]}}]}""");
    var ev=changed["customData"]!["customEvents"]![0]!;var data=ev["d"]!;
    if(invalid=="late-scale")data["scale"]![1]![3]=.5;
    if(invalid=="nonunit")data["scale"]![1]![0]=2;
    if(invalid=="active-song"){ev["t"]="AnimateTrack";((JObject)data).Remove("scale");data["offsetPosition"]=JArray.Parse("[[0,1,0,0],[0,0,0,0.375]]");changed["bombNotes"]![0]!["b"]=.5;}
    if(invalid=="repeat")data["repeat"]=1;
    if(invalid=="fake")changed["bombNotes"]![0]!["customData"]!["fake"]=true;
    if(invalid=="named")data["scale"]="curve";
    if(invalid=="parent")ev["t"]="AssignTrackParent";
    bool rejected=false;try{LoadSynthetic(changed,requiredExtensions:noodleCapability);}catch(NotSupportedException){rejected=true;}
    Check(rejected,"Nominal bomb tracks reject "+invalid);
}
var interactionBase=JObject.Parse("""{"version":"3.3.0","colorNotes":[{"b":2,"x":0,"y":0,"c":0,"d":1}],"obstacles":[{"b":4,"d":2,"x":0,"y":2,"w":4,"h":3}]}""");
var wallInteractionBaseline=LoadSynthetic(interactionBase,requiredExtensions:noodleCapability);
foreach(string shape in new[]{"definite","offset","rotation","track","size"})
{
    var source=(JObject)interactionBase.DeepClone();var decoration=(JObject)source["obstacles"]![0]!.DeepClone();decoration["b"]=100;
    var metadata=new JObject {["uninteractable"]=true};decoration["customData"]=metadata;
    if(shape=="definite")metadata["animation"]=JObject.Parse("""{"definitePosition":[[0,0,-5,0],[0,0,-5,1,"easeOutQuint"]]}""");
    if(shape=="offset")metadata["animation"]=JObject.Parse("""{"offsetPosition":[0,5,4]}""");
    if(shape=="rotation")metadata["localRotation"]=new JArray(45,30,90);
    if(shape=="track")metadata["track"]="decorative";
    if(shape=="size")metadata["size"]=new JArray(20,30,40);
    ((JArray)source["obstacles"]!).Add(decoration);
    var physical=LoadSynthetic(source,requiredExtensions:noodleCapability);
    Check(physical.HasNoodleGeometry&&physical.Json==source.ToString()&&JToken.DeepEquals(JToken.FromObject(physical.Input),JToken.FromObject(wallInteractionBaseline.Input))
        &&WallAlertAdapter.Count(physical.SquatJson!,120,false,true)==WallAlertAdapter.Count(wallInteractionBaseline.SquatJson!,120,false,true),"Non-interactive "+shape+" walls do not affect physical coverage, window tail or source");
    metadata["uninteractable"]=false;bool rejected=false;try{LoadSynthetic(source,requiredExtensions:noodleCapability);}catch(NotSupportedException){rejected=true;}
    Check(rejected==(shape!="track"),"Interactive "+shape+" wall still follows its geometry support boundary");
}
foreach(var value in new JToken[]{new JValue(false),JValue.CreateNull()})
{
    var source=(JObject)interactionBase.DeepClone();source["obstacles"]![0]!["customData"]=new JObject {["uninteractable"]=value};
    var physical=LoadSynthetic(source,requiredExtensions:noodleCapability);
    Check(physical.Input.Walls.Length==1&&WallAlertAdapter.Count(physical.SquatJson!,120,false,true)==WallAlertAdapter.Count(wallInteractionBaseline.SquatJson!,120,false,true),"False/null wall interaction preserves physical squats");
}
foreach(var value in new JToken[]{new JValue(1),new JValue("true"),new JArray(true)})
{
    var source=(JObject)interactionBase.DeepClone();source["obstacles"]![0]!["customData"]=new JObject {["uninteractable"]=value};
    bool rejected=false;try{LoadSynthetic(source,requiredExtensions:noodleCapability);}catch(NotSupportedException){rejected=true;}
    Check(rejected,"Malformed wall interaction flags remain unsupported");
}
var legacyInteraction=JObject.Parse("""{"_version":"2.6.0","_notes":[{"_time":2,"_lineIndex":1,"_lineLayer":0,"_type":0,"_cutDirection":1}],"_obstacles":[{"_time":2,"_lineIndex":1,"_type":1,"_duration":2,"_width":2}]}""");
var legacyPhysical=LoadSynthetic(legacyInteraction,requiredExtensions:noodleCapability);
foreach(string shape in new[]{"animation","rotation","track","scale"})
{
    var source=(JObject)legacyInteraction.DeepClone();
    var wall=new JObject {["_time"]=100,["_lineIndex"]=0,["_type"]=0,["_duration"]=0,["_width"]=0,
        ["_customData"]=new JObject {["_interactable"]=false}};
    var custom=(JObject)wall["_customData"]!;
    if(shape=="animation")custom["_animation"]=JObject.Parse("""{"_definitePosition":[[0,2,4,0],[0,8,4,1]]}""");
    if(shape=="rotation")custom["_localRotation"]=new JArray(30,60,90);
    if(shape=="track")custom["_track"]="decoration";
    if(shape=="scale")custom["_scale"]=new JArray(8,10,12);
    ((JArray)source["_obstacles"]!).Add(wall);
    var legacyLoaded=LoadSynthetic(source,requiredExtensions:noodleCapability);
    Check(legacyLoaded.HasNoodleGeometry&&legacyLoaded.Json==source.ToString()&&JToken.DeepEquals(JToken.FromObject(legacyLoaded.Input),JToken.FromObject(legacyPhysical.Input))
        &&WallAlertAdapter.Count(legacyLoaded.SquatJson!,120,false,true)==WallAlertAdapter.Count(legacyPhysical.SquatJson!,120,false,true),
        "Legacy non-interactive "+shape+" wall does not change physical walls, source or swing tail");
}
foreach(var value in new JToken[]{new JValue(true),JValue.CreateNull()})
{
    var source=(JObject)legacyInteraction.DeepClone();source["_obstacles"]![0]!["_customData"]=new JObject {["_interactable"]=value};
    var legacyLoaded=LoadSynthetic(source,requiredExtensions:noodleCapability);
    Check(legacyLoaded.Input.Walls.Length==1&&WallAlertAdapter.Count(legacyLoaded.SquatJson!,120,false,true)==WallAlertAdapter.Count(legacyPhysical.SquatJson!,120,false,true),"Legacy true/null walls remain physical");
}
foreach(var value in new JToken[]{new JValue(0),new JValue("false"),new JArray(false)})
{
    var source=(JObject)legacyInteraction.DeepClone();source["_obstacles"]![0]!["_customData"]=new JObject {["_interactable"]=value};
    bool legacyRejected=false;try{LoadSynthetic(source,requiredExtensions:noodleCapability);}catch(NotSupportedException){legacyRejected=true;}
    Check(legacyRejected,"Legacy malformed wall interaction flag is rejected");
}
foreach(string mode in new[]{"Standard","Lawless","OneSaber","NoArrows"})foreach(int hand in new[]{0,1})foreach(int direction in new[]{0,4,8})
{
    var plain=JObject.Parse("""{"version":"3.3.0","colorNotes":[{"b":2,"x":1,"y":0,"c":0,"d":1,"a":17}]}""");plain["colorNotes"]![0]!["c"]=hand;plain["colorNotes"]![0]!["d"]=direction;
    var changed=(JObject)plain.DeepClone();changed["colorNotes"]![0]!["customData"]=new JObject {["worldRotation"]=new JArray(0,-0.0,0)};
    var nominal=LoadSynthetic(plain,requiredExtensions:noodleCapability,characteristic:mode);var zeroWorld=LoadSynthetic(changed,requiredExtensions:noodleCapability,characteristic:mode);
    Check(JToken.DeepEquals(JToken.FromObject(nominal.Input),JToken.FromObject(zeroWorld.Input))&&zeroWorld.HasNoodleGeometry==nominal.HasNoodleGeometry&&zeroWorld.Json==changed.ToString(),"Explicit zero world rotation preserves nominal data, tilt and source in "+mode);
}
foreach(string invalid in new[]{"yaw","pitch","roll","short","long","null","scalar","nan","string","event","event-alias","360Degree","90Degree","bomb","fake","legacy"})
{
    var source=JObject.Parse("""{"version":"3.3.0","colorNotes":[{"b":2,"x":1,"y":0,"c":0,"d":1,"customData":{"worldRotation":[0,0,0]}}]}""");var metadata=source["colorNotes"]![0]!["customData"]!;
    if(invalid=="yaw")metadata["worldRotation"]![1]=30;
    if(invalid=="pitch")metadata["worldRotation"]![0]=1;
    if(invalid=="roll")metadata["worldRotation"]![2]=360;
    if(invalid=="short")metadata["worldRotation"]=new JArray(0,0);
    if(invalid=="long")metadata["worldRotation"]=new JArray(0,0,0,0);
    if(invalid=="null")metadata["worldRotation"]=JValue.CreateNull();
    if(invalid=="scalar")metadata["worldRotation"]=0;
    if(invalid=="nan")metadata["worldRotation"]![0]=double.NaN;
    if(invalid=="string")metadata["worldRotation"]![0]="0";
    if(invalid=="event"||invalid=="event-alias")source[invalid=="event"?"rotationEvents":"_rotationEvents"]=new JArray(new JObject {["b"]=1,["r"]=30});
    if(invalid=="bomb"){source["bombNotes"]=new JArray(new JObject {["b"]=2,["x"]=1,["y"]=0,["customData"]=metadata.DeepClone()});((JObject)source["colorNotes"]![0]!).Remove("customData");}
    if(invalid=="fake")metadata["NE_fake"]=true;
    if(invalid=="legacy")source=JObject.Parse("""{"_version":"2.6.0","_notes":[{"_time":2,"_lineIndex":1,"_lineLayer":0,"_type":0,"_cutDirection":1,"_customData":{"_rotation":[0,0,0]}}]}""");
    bool rejected=false;try{LoadSynthetic(source,requiredExtensions:noodleCapability,characteristic:invalid.EndsWith("Degree",StringComparison.Ordinal)?invalid:"Standard");}catch(NotSupportedException){rejected=true;}
    Check(rejected,"Zero world rotation does not broaden "+invalid);
}
foreach(string mode in new[]{"Standard","Lawless","OneSaber","NoArrows"})foreach(int hand in new[]{0,1})foreach(int direction in new[]{0,4,8})
{
    var source=JObject.Parse("""{"version":"3.3.0","colorNotes":[{"b":2,"x":1,"y":0,"c":0,"d":1,"a":17},{"b":3,"x":1,"y":1,"c":0,"d":1}],"sliders":[{"b":2,"c":0,"x":1,"y":0,"d":1,"tb":3,"tx":1,"ty":1,"tc":1}]}""");
    foreach(var note in source["colorNotes"]!){note["c"]=hand;note["d"]=direction;}
    source["sliders"]![0]!["c"]=hand;source["sliders"]![0]!["d"]=direction;source["sliders"]![0]!["tc"]=direction;
    var changed=(JObject)source.DeepClone();changed["sliders"]![0]!["customData"]=new JObject {["worldRotation"]=new JArray(0,0,0)};
    var nominal=LoadSynthetic(source,requiredExtensions:noodleCapability,characteristic:mode);var zeroArc=LoadSynthetic(changed,requiredExtensions:noodleCapability,characteristic:mode);
    Check(JToken.DeepEquals(JToken.FromObject(nominal.Input),JToken.FromObject(zeroArc.Input))&&zeroArc.HasNoodleGeometry==nominal.HasNoodleGeometry&&zeroArc.Json==changed.ToString(),"Zero arc world rotation preserves head/tail roles, angles, timing and source in "+mode);
}
foreach(string invalid in new[]{"yaw","pitch","roll","scalar","short","null","string","nan","event","360Degree","fake","chain"})
{
    var source=JObject.Parse("""{"version":"3.3.0","colorNotes":[{"b":2,"x":1,"y":0,"c":0,"d":1}],"sliders":[{"b":2,"c":0,"x":1,"y":0,"d":1,"tb":3,"tx":1,"ty":1,"tc":1,"customData":{"worldRotation":[0,0,0]}}]}""");var metadata=source["sliders"]![0]!["customData"]!;
    if(invalid=="yaw")metadata["worldRotation"]![1]=30;
    if(invalid=="pitch")metadata["worldRotation"]![0]=1;
    if(invalid=="roll")metadata["worldRotation"]![2]=360;
    if(invalid=="scalar")metadata["worldRotation"]=0;
    if(invalid=="short")metadata["worldRotation"]=new JArray(0,0);
    if(invalid=="null")metadata["worldRotation"]=JValue.CreateNull();
    if(invalid=="string")metadata["worldRotation"]![0]="0";
    if(invalid=="nan")metadata["worldRotation"]![0]=double.NaN;
    if(invalid=="event")source["rotationEvents"]=new JArray(new JObject {["b"]=1,["r"]=30});
    if(invalid=="fake")metadata["NE_fake"]=true;
    if(invalid=="chain"){source["burstSliders"]=source["sliders"]!.DeepClone();source["burstSliders"]![0]!["sc"]=4;source["burstSliders"]![0]!["s"]=1;source.Remove("sliders");}
    bool rejected=false;try{LoadSynthetic(source,requiredExtensions:noodleCapability,characteristic:invalid=="360Degree"?invalid:"Standard");}catch(NotSupportedException){rejected=true;}
    Check(rejected,"Zero arc world rotation does not broaden "+invalid);
}
foreach(int color in new[]{0,1})foreach(int direction in Enumerable.Range(0,9))foreach(int slices in new[]{2,5,9})
{
    var source=JObject.Parse("""{"version":"3.3.0","colorNotes":[{"b":2,"x":0,"y":0,"c":0,"d":1}],"burstSliders":[{"b":2,"c":0,"x":0,"y":0,"d":1,"tb":2,"tx":1,"ty":2,"sc":5,"s":1}]}""");
    source["colorNotes"]![0]!["c"]=color;source["colorNotes"]![0]!["d"]=direction;source["burstSliders"]![0]!["c"]=color;source["burstSliders"]![0]!["d"]=direction;source["burstSliders"]![0]!["sc"]=slices;
    var zeroBurst=LoadSynthetic(source,requiredExtensions:noodleCapability);var result=CompatibleAnalysis.Analyze(zeroBurst.Input,reproduceKnownDefects:false);
    Check(zeroBurst.Input.Notes.Length==1&&zeroBurst.Input.Notes[0].Time==1&&zeroBurst.Input.Notes[0].Direction==direction&&zeroBurst.Input.Notes[0].SliceCount==slices&&zeroBurst.Input.Notes[0].CutDirectionAngleOffset==0
        &&result.LeftBins.Sum()==(color==0?1:0)&&result.RightBins.Sum()==(color==1?1:0)&&zeroBurst.Json==source.ToString(),"Zero-duration real-head burst remains one nominal swing with source direction and time");
}
foreach(string invalid in new[]{"negative","orphan","tail-anchor","arc-tail"})
{
    var source=JObject.Parse("""{"version":"3.3.0","colorNotes":[{"b":2,"x":0,"y":0,"c":0,"d":1}],"burstSliders":[{"b":2,"c":0,"x":0,"y":0,"d":1,"tb":2,"tx":1,"ty":2,"sc":5,"s":1}]}""");
    if(invalid=="negative")source["burstSliders"]![0]!["tb"]=1;
    if(invalid=="orphan")source["colorNotes"]=new JArray();
    if(invalid=="tail-anchor")((JArray)source["colorNotes"]!).Add(new JObject {["b"]=2,["x"]=1,["y"]=2,["c"]=1,["d"]=1});
    if(invalid=="arc-tail")source["sliders"]=new JArray(new JObject {["b"]=1,["c"]=0,["x"]=2,["y"]=1,["d"]=1,["tb"]=2,["tx"]=1,["ty"]=2,["tc"]=1});
    bool rejected=false;try{LoadSynthetic(source,requiredExtensions:noodleCapability);}catch(NotSupportedException){rejected=true;}
    Check(rejected,"Zero-duration burst rejects unvalidated "+invalid);
}
foreach(int color in new[]{0,1})foreach(int direction in Enumerable.Range(0,9))foreach(float tailBeat in new[]{2f,3f})
{
    var source=JObject.Parse("""{"version":"3.3.0","colorNotes":[{"b":2,"x":0,"y":0,"c":0,"d":1}],"burstSliders":[{"b":2,"c":0,"x":0,"y":0,"d":1,"tb":2,"tx":1,"ty":2,"sc":1,"s":1}]}""");
    source["colorNotes"]![0]!["c"]=color;source["colorNotes"]![0]!["d"]=direction;source["burstSliders"]![0]!["c"]=color;source["burstSliders"]![0]!["d"]=direction;source["burstSliders"]![0]!["tb"]=tailBeat;
    var singleBurst=LoadSynthetic(source,requiredExtensions:noodleCapability);var result=CompatibleAnalysis.Analyze(singleBurst.Input,reproduceKnownDefects:false);
    Check(singleBurst.Input.Notes.Length==1&&singleBurst.Input.Notes[0].Time==1&&singleBurst.Input.Notes[0].Direction==direction&&singleBurst.Input.Notes[0].SliceCount==1
        &&result.LeftBins.Sum()==(color==0?1:0)&&result.RightBins.Sum()==(color==1?1:0)&&singleBurst.Json==source.ToString(),"Single-slice real-head burst preserves its one physical note and nominal swing");
    source["colorNotes"]=new JArray();bool rejected=false;try{LoadSynthetic(source,requiredExtensions:noodleCapability);}catch(NotSupportedException){rejected=true;}
    Check(rejected,"Single-slice burst without a physical head remains unsupported");
}
foreach(int color in new[]{0,1})foreach(int direction in new[]{1,8})foreach(bool selfTail in new[]{false,true})foreach(bool secondChain in new[]{false,true})foreach(bool reverse in new[]{false,true})
{
    var source=JObject.Parse("""{"version":"3.3.0","colorNotes":[{"b":2,"x":0,"y":0,"c":0,"d":1},{"b":2,"x":1,"y":2,"c":1,"d":1}],"burstSliders":[{"b":2,"c":0,"x":0,"y":0,"d":1,"tb":2,"tx":1,"ty":2,"sc":1,"s":1}]}""");
    source["colorNotes"]![0]!["c"]=color;source["colorNotes"]![0]!["d"]=direction;source["colorNotes"]![1]!["c"]=1-color;
    var chain=source["burstSliders"]![0]!;chain["c"]=color;chain["d"]=direction;if(selfTail){chain["tx"]=0;chain["ty"]=0;}
    if(secondChain)((JArray)source["burstSliders"]!).Add(new JObject {["b"]=2,["c"]=1-color,["x"]=1,["y"]=2,["d"]=1,["tb"]=2,["tx"]=1,["ty"]=2,["sc"]=1,["s"]=1});
    if(reverse){source["colorNotes"]=new JArray(((JArray)source["colorNotes"]!).Reverse());source["burstSliders"]=new JArray(((JArray)source["burstSliders"]!).Reverse());}
    var tailOverlap=LoadSynthetic(source,requiredExtensions:noodleCapability);var result=CompatibleAnalysis.Analyze(tailOverlap.Input,reproduceKnownDefects:false);
    Check(tailOverlap.Input.Notes.Length==2&&result.LeftBins.Sum()==1&&result.RightBins.Sum()==1&&tailOverlap.Input.Notes.All(n=>n.Time==1&&n.SliceCount==1&&n.CutDirectionAngleOffset==0)
        &&tailOverlap.Json==source.ToString(),"Zero single-slice self/opposite-hand tail retains both physical swings and input ordering independence");
}
var scopeFixture=JObject.Parse("""{"version":"3.3.0","colorNotes":[{"b":2,"x":0,"y":0,"c":0,"d":1},{"b":4,"x":1,"y":1,"c":0,"d":1}],"sliders":[{"b":2,"c":0,"x":0,"y":0,"d":1,"tb":4,"tx":1,"ty":1,"tc":1,"customData":{"track":"arcs"}}],"obstacles":[{"b":2,"d":2,"x":0,"y":2,"w":4,"h":3,"customData":{"track":"decor"}}],"customData":{"customEvents":[{"b":0,"t":"AssignPathAnimation","d":{"track":"decor","definitePosition":[[0,10,20,0],[0,-10,20,1]]}},{"b":0,"t":"AssignPathAnimation","d":{"track":"arcs","dissolve":[[0,0]]}}]}}""");
var scopePlain=(JObject)scopeFixture.DeepClone();scopePlain.Remove("customData");
foreach(string type in new[]{"AssignPathAnimation","AnimateTrack"})foreach(string property in new[]{"definitePosition","offsetPosition","localRotation","worldRotation","scale"})
{
    var source=(JObject)scopeFixture.DeepClone();var ev=source["customData"]!["customEvents"]![0]!;ev["t"]=type;ev["d"]!["duration"]=2;
    var curve=ev["d"]!["definitePosition"]!.DeepClone();((JObject)ev["d"]!).Remove("definitePosition");ev["d"]![property]=curve;
    var scoped=LoadSynthetic(source,features:AnalysisFeatures.FirstNotes,requiredExtensions:noodleCapability);var nominal=LoadSynthetic(scopePlain,features:AnalysisFeatures.FirstNotes,requiredExtensions:noodleCapability);
    Check(JToken.DeepEquals(JToken.FromObject(scoped.Input.Notes),JToken.FromObject(nominal.Input.Notes))&&scoped.Json==source.ToString(),"Disjoint "+type+" "+property+" does not reject validated arc data when walls are not requested");
    bool rejected=false;try{LoadSynthetic(source,requiredExtensions:noodleCapability);}catch(NotSupportedException){rejected=true;}
    Check(rejected,"Requesting affected walls still rejects unvalidated "+type+" "+property);
}
foreach(string invalid in new[]{"shared","array-shared","parent","player","unknown-event","environment","repeat","unknown-property","invalid-target","invalid-curve","alias"})
{
    var source=(JObject)scopeFixture.DeepClone();var ev=source["customData"]!["customEvents"]![0]!;var data=ev["d"]!;
    if(invalid=="shared")data["track"]="arcs";
    if(invalid=="array-shared")data["track"]=new JArray("decor","arcs");
    if(invalid=="parent"){ev["t"]="AssignTrackParent";data["parentTrack"]="decor";data["childrenTracks"]=new JArray("arcs");}
    if(invalid=="player")ev["t"]="AssignPlayerToTrack";
    if(invalid=="unknown-event")ev["t"]="FutureEvent";
    if(invalid=="environment")source["customData"]!["environment"]=new JArray(new JObject {["track"]="arcs"});
    if(invalid=="repeat")data["repeat"]=2;
    if(invalid=="unknown-property")data["futureTransform"]=new JArray(1,2,3);
    if(invalid=="invalid-target")data["track"]=new JArray("decor",1);
    if(invalid=="invalid-curve")data["definitePosition"]=new JArray(1,"bad",3);
    if(invalid=="alias"){source["customData"]!["_customEvents"]=source["customData"]!["customEvents"]!.DeepClone();((JObject)source["customData"]!).Remove("customEvents");}
    bool rejected=false;try{LoadSynthetic(source,features:AnalysisFeatures.FirstNotes,requiredExtensions:noodleCapability);}catch(NotSupportedException){rejected=true;}
    Check(rejected,"Scoped track validation keeps conservative boundary for "+invalid);
}
foreach(int densityMajor in new[]{2,3})foreach(bool densityFake in new[]{false,true})foreach(bool densityPlayer in new[]{false,true})
{
    string densityKey=densityMajor==2?"_customData":"customData",densityNotes=densityMajor==2?"_notes":"colorNotes";
    var densityRoot=densityMajor==2?JObject.Parse("""{"_version":"2.6.0","_notes":[{"_time":2,"_type":0},{"_time":4,"_type":1}],"_events":[{"_time":2,"_type":100,"_floatValue":180}]}"""):
        JObject.Parse("""{"version":"3.3.0","colorNotes":[{"b":2,"c":0},{"b":4,"c":1}],"bpmEvents":[{"b":2,"m":180}]}""");
    foreach(var densityNote in densityRoot[densityNotes]!)densityNote[densityKey]=new JObject{[densityMajor==2?"_rotation":"worldRotation"]=new JArray(30,90,0)};
    densityRoot[densityNotes]![0]![densityKey]![densityMajor==2?"_fake":"NE_fake"]=densityFake;
    if(densityPlayer)densityRoot[densityKey]=new JObject{[densityMajor==2?"_customEvents":"customEvents"]=new JArray(new JObject{
        [densityMajor==2?"_time":"b"]=0,[densityMajor==2?"_type":"t"]="AssignPlayerToTrack",[densityMajor==2?"_data":"d"]=new JObject{[densityMajor==2?"_track":"track"]="player"}})};
    var densityMap=LoadSynthetic(densityRoot,features:AnalysisFeatures.Density,requiredExtensions:noodleCapability);
    var densityAnalysis=CompatibleAnalysis.Analyze(densityMap.Input,reproduceKnownDefects:false,features:AnalysisFeatures.Density);
    Check(densityMap.Json==densityRoot.ToString()&&densityMap.Input.Notes.Length==2,"Independent density preserves original JSON and colored object count");
    Check(densityMap.Input.Notes.Select(n=>n.Time).Zip(new[]{1f,5f/3},(a,b)=>Math.Abs(a-b)<.00001).All(x=>x),"Independent density uses BPM-aware native nominal times");
    Check(densityAnalysis.LeftBins.Length==0&&densityAnalysis.RightBins.Length==0&&densityMap.Input.Notes.All(n=>n.CutDirectionAngleOffset==null),"Independent density does not calculate hidden swings or first-note pose");
    Check(Math.Abs(densityAnalysis.Segments.Sum()*densityMap.Input.Duration/90-(densityFake?1:2))<.0001,"Independent density excludes fake notes");
    bool densityFullRejected=false;try{LoadSynthetic(densityRoot,features:AnalysisFeatures.FirstNotes,requiredExtensions:noodleCapability);}catch(NotSupportedException){densityFullRejected=true;}
    Check(densityFullRejected,"Density support cannot admit unsupported world/player geometry");
    foreach(string densityMode in new[]{"Standard","Lawless","OneSaber","NoArrows"})
    {
        var modeMap=LoadSynthetic(densityRoot,features:AnalysisFeatures.Density,requiredExtensions:noodleCapability,characteristic:densityMode);
        Check(CompatibleAnalysis.Analyze(modeMap.Input,reproduceKnownDefects:false,features:AnalysisFeatures.Density).Segments.SequenceEqual(densityAnalysis.Segments),"Nominal density in validated flat characteristic "+densityMode);
    }
    bool densityModeRejected=false;try{LoadSynthetic(densityRoot,features:AnalysisFeatures.Density,requiredExtensions:noodleCapability,characteristic:"Lightshow");}catch(NotSupportedException){densityModeRejected=true;}
    Check(densityModeRejected,"Independent density does not admit unverified characteristics");
    foreach(string densityBoundary in new[]{"arcs","chains","fake-chains","invalid-fake","invalid-time","invalid-float-time","invalid-color"})
    {
        var densityBad=(JObject)densityRoot.DeepClone();
        if(densityBoundary=="arcs")densityBad[densityMajor==2?"_sliders":"sliders"]=new JArray(new JObject{[densityMajor==2?"_headTime":"b"]="bad"});
        if(densityBoundary=="chains")densityBad["burstSliders"]=new JArray(new JObject());
        if(densityBoundary=="fake-chains"){densityBad[densityKey]??=new JObject();densityBad[densityKey]!["fakeBurstSliders"]=new JArray(new JObject());}
        if(densityBoundary=="invalid-fake")densityBad[densityNotes]![0]![densityKey]![densityMajor==2?"_fake":"NE_fake"]="true";
        if(densityBoundary=="invalid-time")densityBad[densityNotes]![0]![densityMajor==2?"_time":"b"]="bad";
        if(densityBoundary=="invalid-float-time")densityBad[densityNotes]![0]![densityMajor==2?"_time":"b"]=double.MaxValue;
        if(densityBoundary=="invalid-color")densityBad[densityNotes]![0]![densityMajor==2?"_type":"c"]=long.MaxValue;
        bool densityRejected=false;try{LoadSynthetic(densityBad,features:AnalysisFeatures.Density,requiredExtensions:noodleCapability);}catch(NotSupportedException){densityRejected=true;}
        Check(densityRejected,"Independent density keeps conservative boundary: "+densityBoundary);
    }
}
foreach(bool realBomb in new[]{false,true})foreach(bool fakeBomb in new[]{false,true})
{
    var arcDensity=JObject.Parse("""{"version":"3.3.0","colorNotes":[{"b":2,"x":0,"y":0,"c":0,"d":1,"customData":{"worldRotation":[30,90,0]}},{"b":4,"x":1,"y":1,"c":1,"d":1}],"sliders":[{"b":2,"x":0,"y":0,"c":0,"d":1,"tb":4,"tx":1,"ty":1,"tc":1}],"bombNotes":[],"customData":{}}""");
    if(realBomb)((JArray)arcDensity["bombNotes"]!).Add(new JObject{["b"]=4,["x"]=1,["y"]=1});
    if(fakeBomb)arcDensity["customData"]!["fakeBombNotes"]=new JArray(new JObject{["b"]=4,["x"]=1,["y"]=1});
    bool rejected=false;
    try
    {
        var arcDensityLoaded=LoadSynthetic(arcDensity,features:AnalysisFeatures.Density,requiredExtensions:noodleCapability);
        Check(arcDensityLoaded.Features.Contains("noodle-density-only")&&arcDensityLoaded.Input.Notes.Count(n=>!n.IsFake)==(realBomb?3:2),"Ordinary v3 arcs preserve nominal density and promoted bombs");
        Check(arcDensityLoaded.Json==arcDensity.ToString(),"Arc density preserves source JSON");
    }
    catch(NotSupportedException){rejected=true;}
    Check(!rejected,"Independent arc density classifies real and fake bombs");
}
foreach(bool fakeTarget in new[]{false,true})
{
    var rowDensity=JObject.Parse("""{"version":"3.3.0","colorNotes":[],"bombNotes":[{"b":4,"x":1,"y":0,"customData":{"coordinates":[-0.5,0.25],"worldRotation":[30,90,0]}},{"b":8,"x":3,"y":2}],"sliders":[{"b":0,"x":0,"y":0,"c":0,"d":1,"tb":4,"tx":1,"ty":0,"customData":{"tailCoordinates":[-0.5,0.25]}}],"customData":{"customEvents":[{"b":0,"t":"AssignPlayerToTrack","d":{"track":"player"}}]}}""");
    rowDensity["bombNotes"]![0]!["customData"]!["NE_fake"]=fakeTarget;
    var rowDensityLoaded=LoadSynthetic(rowDensity,features:AnalysisFeatures.Density,requiredExtensions:noodleCapability);
    Check(rowDensityLoaded.Input.Notes.Count(n=>!n.IsFake)==(fakeTarget?0:1),"Density row projection preserves fake ownership and custom bomb/arc coordinates");
    Check(rowDensityLoaded.Json==rowDensity.ToString(),"Density row projection preserves original source");
    bool poseRejected=false;try{LoadSynthetic(rowDensity,features:AnalysisFeatures.FirstNotes,requiredExtensions:noodleCapability);}catch(NotSupportedException){poseRejected=true;}
    Check(poseRejected,"Density row support cannot admit unsupported player/world pose");
    foreach(string invalid in new[]{"coordinate-type","coordinate-range","base-coordinate","bomb-time","metadata"})
    {
        var badRow=(JObject)rowDensity.DeepClone();
        if(invalid=="coordinate-type")badRow["bombNotes"]![0]!["customData"]!["coordinates"]=new JValue("invalid");
        if(invalid=="coordinate-range")badRow["bombNotes"]![0]!["customData"]!["coordinates"]=new JArray(double.MaxValue,0);
        if(invalid=="base-coordinate")badRow["bombNotes"]![0]!["x"]=new JValue(.5);
        if(invalid=="bomb-time")badRow["bombNotes"]![0]!["b"]=JValue.CreateNull();
        if(invalid=="metadata")badRow["bombNotes"]![0]!["customData"]=new JValue("invalid");
        bool rejected=false;try{LoadSynthetic(badRow,features:AnalysisFeatures.Density,requiredExtensions:noodleCapability);}catch(NotSupportedException){rejected=true;}
        Check(rejected,"Density row rejects malformed classifier inputs: "+invalid);
    }
}
foreach(bool physicalHead in new[]{false,true})foreach(int slices in new[]{2,4})
{
    var chainDensity=JObject.Parse("""{"version":"3.3.0","colorNotes":[],"burstSliders":[{"b":2,"x":0,"y":0,"c":0,"d":1,"tb":4,"tx":1,"ty":1,"sc":4,"s":1,"customData":{"worldRotation":[30,90,0]}}],"bpmEvents":[{"b":3,"m":180}],"customData":{"customEvents":[{"b":0,"t":"AssignPlayerToTrack","d":{"track":"player"}}]}}""");
    chainDensity["burstSliders"]![0]!["sc"]=slices;
    if(physicalHead)((JArray)chainDensity["colorNotes"]!).Add(new JObject{["b"]=2,["x"]=0,["y"]=0,["c"]=0,["d"]=1});
    var chainDensityLoaded=LoadSynthetic(chainDensity,features:AnalysisFeatures.Density,requiredExtensions:noodleCapability);
    Check(chainDensityLoaded.Input.Notes.Length==1&&chainDensityLoaded.Input.Notes[0].SliceCount==(physicalHead?slices:slices-1),"Nominal density groups validated chain head/slices once");
    Check(Math.Abs(chainDensityLoaded.Input.Notes[0].Time-(physicalHead?1:1+(11f/6-1)/(slices-1)))<.00001f,"Density chain uses BPM-aware first actual object time");
    Check(chainDensityLoaded.Json==chainDensity.ToString(),"Chain density preserves original JSON");
    var nullMetadata=(JObject)chainDensity.DeepClone();
    nullMetadata["burstSliders"]![0]!["customData"]=JValue.CreateNull();
    foreach(var note in nullMetadata["colorNotes"]!)note["customData"]=JValue.CreateNull();
    nullMetadata["bombNotes"]=new JArray(new JObject{["b"]=8,["x"]=3,["y"]=2,["customData"]=JValue.CreateNull()});
    bool nullRejected=false;try{LoadSynthetic(nullMetadata,features:AnalysisFeatures.Density,requiredExtensions:noodleCapability);}catch(NotSupportedException){nullRejected=true;}
    Check(nullRejected,"Density rows reject explicit null metadata unsupported by the installed custom reader");
    bool fullRejected=false;try{LoadSynthetic(chainDensity,features:AnalysisFeatures.FirstNotes,requiredExtensions:noodleCapability);}catch(NotSupportedException){fullRejected=true;}
    Check(fullRejected,"Chain density cannot admit unverified player/world geometry");
    foreach(string invalid in new[]{"fake-chain","slice-type","slice-range","slice-missing","interval","fake-head","duplicate-head","bomb-object","bomb-collection"})
    {
        if(!physicalHead&&(invalid=="fake-head"||invalid=="duplicate-head"))continue;
        var badChain=(JObject)chainDensity.DeepClone();var source=badChain["burstSliders"]![0]!;
        if(invalid=="fake-chain")source["customData"]!["NE_fake"]=true;
        if(invalid=="slice-type")source["sc"]=new JValue("bad");
        if(invalid=="slice-range")source["sc"]=4097;
        if(invalid=="slice-missing")((JObject)source).Remove("sc");
        if(invalid=="interval")source["tb"]=1;
        if(invalid=="fake-head")badChain["colorNotes"]![0]!["customData"]=new JObject{["NE_fake"]=true};
        if(invalid=="duplicate-head")((JArray)badChain["colorNotes"]!).Add(badChain["colorNotes"]![0]!.DeepClone());
        if(invalid=="bomb-object")badChain["bombNotes"]=new JArray(new JValue("invalid"));
        if(invalid=="bomb-collection")badChain["bombNotes"]=new JObject();
        bool rejected=false;try{LoadSynthetic(badChain,features:AnalysisFeatures.Density,requiredExtensions:noodleCapability);}catch(NotSupportedException){rejected=true;}
        Check(rejected,"Density chains retain conservative ownership boundary: "+invalid);
    }
}
foreach(bool defaultArc in new[]{false,true})
{
    var defaultDensity=JObject.Parse("""{"version":"3.3.0","colorNotes":[{"customData":{"worldRotation":[30,90,0]}}]}""");
    if(defaultArc)defaultDensity["sliders"]=new JArray(new JObject());
    var defaultDensityLoaded=LoadSynthetic(defaultDensity,features:AnalysisFeatures.Density,requiredExtensions:noodleCapability);
    Check(defaultDensityLoaded.Input.Notes.Length==1&&defaultDensityLoaded.Input.Notes[0].Time==0&&defaultDensityLoaded.Input.Notes[0].Color==0,"Omitted v3 density fields use native zero defaults");
    Check(defaultDensityLoaded.Json==defaultDensity.ToString(),"Default normalization preserves original density source");
    foreach(string badField in new[]{"b","c"})foreach(JToken badValue in new JToken[]{JValue.CreateNull(),new JValue("invalid")})
    {
        var badDefaults=(JObject)defaultDensity.DeepClone();badDefaults["colorNotes"]![0]![badField]=badValue.DeepClone();
        bool rejected=false;try{LoadSynthetic(badDefaults,features:AnalysisFeatures.Density,requiredExtensions:noodleCapability);}catch(NotSupportedException){rejected=true;}
        Check(rejected,"Zero defaults do not admit invalid explicit density fields: "+badField);
    }
}
foreach(var densityStructured in new[]{v2ArcGeometry,noodleBurstMap})
{
    var densityFull=LoadSynthetic(densityStructured,requiredExtensions:noodleCapability);
    var densitySelective=LoadSynthetic(densityStructured,features:AnalysisFeatures.Density,requiredExtensions:noodleCapability);
    Check(CompatibleAnalysis.Analyze(densityFull.Input,reproduceKnownDefects:false,features:AnalysisFeatures.Density).Segments
        .SequenceEqual(CompatibleAnalysis.Analyze(densitySelective.Input,reproduceKnownDefects:false,features:AnalysisFeatures.Density).Segments),
        "Previously validated arcs/chains keep their normal density path");
    Check(densitySelective.Features.Contains("noodle-density-only")==(densityStructured["burstSliders"]?.Any()==true),"Structured density uses the validated nominal row path for v3 chains");
}
// Combined extensions admit nominal density independently of geometry.
var combinedRequirements=new[]{"Mapping Extensions","Noodle Extensions"};
var combinedNominal=JObject.Parse("""{"version":"3.3.0","colorNotes":[{"b":2,"x":1500,"y":-2,"c":0,"d":1,"customData":{"coordinates":[0.25,1]}},{"b":4,"x":-2,"y":0,"c":1,"d":1}],"sliders":[{"b":2,"x":1500,"y":-2,"c":0,"tb":4,"tx":-2,"ty":0}],"customData":{"customEvents":[{"b":0,"t":"AssignPlayerToTrack","d":{"track":"player"}}]}}""");
for(int mask=0;mask<512;mask++)
{
    var selectedFeatures=(AnalysisFeatures)mask;
    bool expected=(selectedFeatures&~AnalysisFeatures.OwnDeaths)==0
        ||(selectedFeatures&AnalysisFeatures.Density)!=0&&(selectedFeatures&~(AnalysisFeatures.Density|AnalysisFeatures.OwnDeaths))==0;
    bool accepted=false;
    try
    {
        var combinedLoaded=LoadSynthetic(combinedNominal,features:selectedFeatures,requiredExtensions:combinedRequirements);
        accepted=true;
        Check(combinedLoaded.Json==combinedNominal.ToString(),"Combined projection retains immutable source: "+mask);
        Check(combinedLoaded.Input.Notes.Length==((selectedFeatures&AnalysisFeatures.Density)!=0?2:0),"Combined projection selects only requested nominal notes: "+mask);
    }
    catch(NotSupportedException){}
    Check(accepted==expected,"Combined extension feature boundary: "+mask);
}
foreach(string blocked in new[]{"bomb","chain","fake-chain","invalid-bombs","invalid-arcs","fake-arc","v2","v4"})
{
    var root=(JObject)combinedNominal.DeepClone();
    if(blocked=="bomb")root["bombNotes"]=new JArray(new JObject{["b"]=1});
    if(blocked=="chain")root["burstSliders"]=new JArray(new JObject{["b"]=1});
    if(blocked=="fake-chain")root["customData"]!["fakeBurstSliders"]=new JArray(new JObject());
    if(blocked=="invalid-bombs")root["bombNotes"]="invalid";
    if(blocked=="invalid-arcs")root["sliders"]="invalid";
    if(blocked=="fake-arc")root["sliders"]![0]!["customData"]=new JObject{["NE_fake"]=true};
    if(blocked=="v2")root["version"]="2.6.0";
    if(blocked=="v4")root["version"]="4.0.0";
    bool rejected=false;try{LoadSynthetic(root,features:AnalysisFeatures.Density,requiredExtensions:combinedRequirements);}catch(NotSupportedException){rejected=true;}
    Check(rejected,"Combined density retains unvalidated boundary: "+blocked);
}
var combinedStandard=JObject.Parse("""{"version":"3.3.0","colorNotes":[{"b":2,"x":0,"y":0,"c":0,"d":1},{"b":4,"x":1,"y":1,"c":1,"d":8},{"b":6,"x":1,"y":0,"c":0,"d":0,"customData":{"coordinates":[-1,0.5]}}],"sliders":[{"b":2,"x":0,"y":0,"c":0,"d":1,"tb":4,"tx":1,"ty":1,"tc":8}],"obstacles":[{"b":1,"x":1500,"y":1500,"w":2500,"h":1500,"d":4}]}""");
for(int mask=0;mask<512;mask++)
{
    bool accepted=false;var combinedFlags=(AnalysisFeatures)mask;
    try
    {
        var current=LoadSynthetic(combinedStandard,features:combinedFlags,requiredExtensions:combinedRequirements,includeSwingWindowWalls:false);accepted=true;
        Check(current.Json==combinedStandard.ToString(),"Canonical combined note source stays immutable: "+mask);
        Check(current.Input.Walls.Length==0,"Combined note-only admission never computes hidden wall geometry: "+mask);
    }
    catch(NotSupportedException){}
    catch(ArgumentException)when((combinedFlags&(AnalysisFeatures.Squats|AnalysisFeatures.Reference))!=0){}
    Check(accepted==((combinedFlags&(AnalysisFeatures.Squats|AnalysisFeatures.Reference))==0),"Canonical combined feature boundary: "+mask);
}
foreach(string kind in new[]{"head","tail","near-head","near-tail","arc-coordinate","fake-array","encoded-direction","extended-column","extended-layer"})
{
    var changed=(JObject)combinedStandard.DeepClone();
    if(kind is "head" or "tail" or "near-head" or "near-tail")changed["colorNotes"]![2]!["b"]=kind switch {"head"=>2f,"tail"=>4f,"near-head"=>2.002f,_=>3.998f};
    if(kind=="arc-coordinate")changed["sliders"]![0]!["customData"]=new JObject{["coordinates"]=new JArray(0,1)};
    if(kind=="fake-array")changed["customData"]=new JObject{["fakeColorNotes"]=new JArray(new JObject{["b"]=1})};
    if(kind=="encoded-direction")changed["colorNotes"]![0]!["d"]=1050;
    if(kind=="extended-column")changed["colorNotes"]![0]!["x"]=1500;
    if(kind=="extended-layer")changed["colorNotes"]![0]!["y"]=1500;
    bool blocked=false;try{LoadSynthetic(changed,features:AnalysisFeatures.FirstNotes,requiredExtensions:combinedRequirements,includeSwingWindowWalls:false);}catch(NotSupportedException){blocked=true;}
    if(kind is "extended-column" or "extended-layer")
    {
        Check(!blocked,"Extended combined coordinates allow nominal metrics without tilt: "+kind);
        var projected=LoadSynthetic(changed,features:AnalysisFeatures.FirstNotes,requiredExtensions:combinedRequirements,includeSwingWindowWalls:false);
        Check(projected.Features.Contains("noodle-nominal-notes")&&projected.Input.Notes.All(n=>!n.CutDirectionAngleOffset.HasValue),"Nominal coordinates never expose a physical tilt: "+kind);
    }
    else if(kind is "fake-array" or "encoded-direction")Check(blocked,"Canonical combined note ownership/geometry boundary: "+kind);
    else Check(!blocked,"Combined ordinary arc raw/custom roles are admitted: "+kind);
    Check(LoadSynthetic(changed,features:AnalysisFeatures.Density,requiredExtensions:combinedRequirements).Input.Notes.Count(n=>!n.IsFake)==3,"Combined ownership boundary preserves independently validated graph: "+kind);
}
foreach(string mode in new[]{"Lightshow","90Degree","360Degree","UnknownMode"})
{
    bool blocked=false;try{LoadSynthetic(combinedStandard,features:AnalysisFeatures.FirstNotes,requiredExtensions:combinedRequirements,includeSwingWindowWalls:false,characteristic:mode);}catch(NotSupportedException){blocked=true;}
    Check(blocked,"Combined canonical admission remains limited to validated flat modes: "+mode);
}
var nominalCombined=(JObject)combinedStandard.DeepClone();nominalCombined["colorNotes"]![0]!["x"]=1500;
for(int mask=0;mask<512;mask++)
{
    var requested=(AnalysisFeatures)mask;bool accepted=false;
    bool identity=(requested&~AnalysisFeatures.OwnDeaths)==0;
    bool density=(requested&AnalysisFeatures.Density)!=0&&(requested&~(AnalysisFeatures.Density|AnalysisFeatures.OwnDeaths))==0;
    bool visible=(requested&(AnalysisFeatures.Average|AnalysisFeatures.Peak|AnalysisFeatures.Median|AnalysisFeatures.FirstNotes))!=0;
    try
    {
        var projected=LoadSynthetic(nominalCombined,features:requested,requiredExtensions:combinedRequirements,includeSwingWindowWalls:false);accepted=true;
        Check(projected.Json==nominalCombined.ToString()&&projected.Input.Walls.Length==0,"Nominal source and hidden walls stay intact: "+mask);
        Check(projected.Features.Contains("noodle-nominal-notes")==visible&&projected.Input.Notes.All(n=>!n.CutDirectionAngleOffset.HasValue),"Nominal feature masks never fabricate tilt or compute hidden orientation: "+mask);
    }
    catch(NotSupportedException){}
    catch(ArgumentException)when((requested&(AnalysisFeatures.Squats|AnalysisFeatures.Reference))!=0){}
    Check(accepted==((requested&(AnalysisFeatures.Squats|AnalysisFeatures.Reference))==0&&(identity||density||visible)),"Independent nominal note feature boundary: "+mask);
}
foreach(string forbidden in new[]{"player","track","rotation","animation","speed","coordinate-reference","fake-array","bomb","chain","fake-arc","unknown-root","invalid-direction","invalid-anchor"})
{
    var changed=(JObject)nominalCombined.DeepClone();var note=(JObject)changed["colorNotes"]![0]!;
    if(forbidden=="player")changed["customData"]=JObject.Parse("""{"customEvents":[{"b":0,"t":"AssignPlayerToTrack","d":{"track":"player"}}]}""");
    if(forbidden=="track")note["customData"]=new JObject{["track"]="track"};
    if(forbidden=="rotation")note["customData"]=new JObject{["worldRotation"]=new JArray(0,0,30)};
    if(forbidden=="animation")note["customData"]=new JObject{["animation"]=new JObject{["offsetPosition"]=new JArray(1,0,0)}};
    if(forbidden=="speed")changed["njsEvents"]=new JArray(new JObject{["b"]=0});
    if(forbidden=="coordinate-reference")note["customData"]=new JObject{["coordinates"]="named"};
    if(forbidden=="fake-array")changed["customData"]=new JObject{["fakeColorNotes"]=new JArray(new JObject())};
    if(forbidden=="bomb")changed["bombNotes"]=new JArray(new JObject{["b"]=2});
    if(forbidden=="chain")changed["burstSliders"]=new JArray(new JObject{["b"]=2});
    if(forbidden=="fake-arc")changed["sliders"]![0]!["customData"]=new JObject{["NE_fake"]=true};
    if(forbidden=="unknown-root")changed["customData"]=new JObject{["unknownGameplay"]=true};
    if(forbidden=="invalid-direction")note["d"]=1000;
    if(forbidden=="invalid-anchor")note["customData"]=new JObject{["_isAnchor"]="invalid"};
    bool blocked=false;try{LoadSynthetic(changed,features:AnalysisFeatures.FirstNotes|AnalysisFeatures.Average,requiredExtensions:combinedRequirements,includeSwingWindowWalls:false);}catch(NotSupportedException){blocked=true;}
    Check(blocked,"Nominal combined metrics retain angular/ownership boundary: "+forbidden);
}
{
    var roles=JObject.Parse("""{"version":"3.3.0","colorNotes":[{"b":2,"x":0,"y":0,"c":0,"d":1},{"b":4,"x":2,"y":0,"c":0,"d":1},{"b":4,"x":3,"y":1,"c":1,"d":1}],"sliders":[{"b":2,"x":0,"y":0,"c":0,"d":1,"tb":4,"tx":2,"ty":0,"tc":1},{"b":4,"x":2,"y":0,"c":1,"d":1,"tb":6,"tx":0,"ty":2,"tc":1,"customData":{"coordinates":[1,1]}}]}""");
    var standalone=LoadSynthetic(roles,features:AnalysisFeatures.FirstNotes,requiredExtensions:noodleCapability,includeSwingWindowWalls:false);
    var combined=LoadSynthetic(roles,features:AnalysisFeatures.FirstNotes,requiredExtensions:combinedRequirements,includeSwingWindowWalls:false);
    Check(standalone.Input.Notes[1].ScoringType==3&&combined.Input.Notes[1].ScoringType==6,"Combined arc tail keeps its raw head while standalone custom rows do not inherit it");
    Check(combined.Input.Notes[2].ScoringType==2&&standalone.Input.Notes[2].ScoringType==2,"Custom head matches remain additive after raw roles");
    Check(combined.Json==roles.ToString()&&combined.HasNoodleGeometry,"Combined arc ownership keeps source identity and unverified Legacy indices");
}
Console.WriteLine($"Passed {checks} checks.");
