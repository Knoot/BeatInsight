using BeatInsight.Core;
using BeatInsight.Formats;
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
string projectRoot=Path.GetFullPath(Path.Combine(AppContext.BaseDirectory,"../../../../../"));
string scratch=Path.Combine(projectRoot,".local","history-checks",Guid.NewGuid().ToString("N"));
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
LoadedMap LoadSynthetic(JObject data,JObject? audioData=null)
{
    var folder=Path.Combine(scratch,Guid.NewGuid().ToString("N"));Directory.CreateDirectory(folder);
    var info=new JObject { ["version"]="4.0.1",["audio"]=new JObject {["bpm"]=120,["songDuration"]=100,["audioDataFilename"]="AudioData.dat"},
        ["difficultyBeatmaps"]=new JArray(new JObject {["characteristic"]="Standard",["difficulty"]="Expert",["beatmapDataFilename"]="map.dat"})};
    File.WriteAllText(Path.Combine(folder,"Info.dat"),info.ToString());File.WriteAllText(Path.Combine(folder,"map.dat"),data.ToString());
    if(audioData!=null)File.WriteAllText(Path.Combine(folder,"AudioData.dat"),audioData.ToString());
    return LocalMap.Load(LocalMap.Cases(folder).Single());
}
var legacyBpm=LoadSynthetic(JObject.Parse("""{"_version":"2.0.0","_notes":[{"_time":1,"_type":0,"_cutDirection":1}],"_events":[{"_time":0,"_type":10,"_value":2,"_floatValue":1}]}"""));
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
try {LoadSynthetic(JObject.Parse("""{"_version":"2.0.0","_events":[{"_time":1,"_type":10,"_value":0,"_floatValue":0}]}"""));}
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
Check(!warnings.Observe(1,2,1,1,3,2,1),"Elevated wall does not warn");
Check(!warnings.Observe(1,2,1,1,1,0,3),"Zero width wall does not warn");
Check(!warnings.Observe(1,2,1,1,1,2,0),"Zero height wall does not warn");
Console.WriteLine($"Passed {checks} checks.");
