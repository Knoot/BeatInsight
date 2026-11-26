using System.Collections.Concurrent;
using BeatInsight.Core;
using BeatInsight.Formats;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

int checks=0;
void Check(bool condition,string name){if(!condition)throw new Exception(name);checks++;}
Task Dispatch(Action action){action();return Task.CompletedTask;}
TaskCompletionSource<T> Signal<T>()=>new(TaskCreationOptions.RunContinuationsAsynchronously);
async Task Done(Task task)=>await task.WaitAsync(TimeSpan.FromSeconds(10));

// A non-cooperative old load cannot publish into a newer selection. Intermediate
// queued requests are cancelled before loading; CPU work remains serialized.
var started=Signal<bool>();var release=Signal<string>();var loads=new ConcurrentQueue<string>();
using(var coordinator=new SelectionCoordinator<string,string>(async (request,token)=> {
    loads.Enqueue(request);
    if(request=="A"){started.TrySetResult(true);return await release.Task;}
    return request+"-result";
},Dispatch))
{
    var ready=new ConcurrentQueue<string>();coordinator.Changed+=s=>{if(s.Status==SelectionStatus.Ready)ready.Enqueue(s.Request!);};
    var a=coordinator.SelectAsync("A");await Done(started.Task);
    var b=coordinator.SelectAsync("B");var c=coordinator.SelectAsync("C");
    Check(coordinator.Current.Status==SelectionStatus.Loading&&coordinator.Current.Request=="C"&&coordinator.Current.Result==null,"New selection immediately clears old results");
    release.SetResult("stale-A");await Done(Task.WhenAll(a,b,c));
    Check(loads.SequenceEqual(["A","C"]),"Only the latest queued selection loads");
    Check(ready.SequenceEqual(["C"])&&coordinator.Current.Result=="C-result","Late non-cooperative result cannot publish");
    await coordinator.ClearAsync();
    Check(coordinator.Current.Status==SelectionStatus.Idle&&coordinator.Current.Result==null&&coordinator.Current.Request==null,"Deactivation clears data and identity");
}

// Error and unsupported results must not retain a previous successful result.
using(var coordinator=new SelectionCoordinator<string,string>((request,token)=>request switch {
    "unsupported"=>Task.FromException<string>(new NotSupportedException("required extension")),
    "missing"=>Task.FromException<string>(new FileNotFoundException("missing map")),
    "error"=>Task.FromException<string>(new InvalidDataException("invalid map")),
    _=>Task.FromResult("ready")
},Dispatch))
{
    foreach(var item in new[]{("unsupported",SelectionStatus.Unsupported),("missing",SelectionStatus.Unavailable),("error",SelectionStatus.Error)})
    {
        await coordinator.SelectAsync("good");await coordinator.SelectAsync(item.Item1);
        Check(coordinator.Current.Status==item.Item2&&coordinator.Current.Result==null&&coordinator.Current.Message!=null,"Explicit failure status without stale or zero-valued result");
    }
    await coordinator.UnavailableAsync("game content loading");
    Check(coordinator.Current.Status==SelectionStatus.Unavailable&&coordinator.Current.Request==null,"Unavailable game content invalidates pending selection");
    await coordinator.SelectAsync("good");Check(coordinator.Current.Status==SelectionStatus.Ready,"Recovery after unsupported/error selection");
}

// Even an already queued main-thread callback must recheck its revision.
var dispatcher=new ManualDispatcher();
using(var coordinator=new SelectionCoordinator<string,string>((request,token)=>Task.FromResult(request),dispatcher.Dispatch))
{
    var published=new List<string>();coordinator.Changed+=s=>{if(s.Status==SelectionStatus.Ready)published.Add(s.Request!);};
    var a=coordinator.SelectAsync("A");(await dispatcher.Take())(); // Loading A.
    var staleReady=await dispatcher.Take(); // Ready A is queued, not yet shown.
    var b=coordinator.SelectAsync("B");staleReady();
    Check(coordinator.Current.Request=="B"&&coordinator.Current.Result==null,"Queued old ready callback cannot replace new selection");
    (await dispatcher.Take())();(await dispatcher.Take())();await Done(Task.WhenAll(a,b));
    Check(published.SequenceEqual(["B"]),"Only newest ready callback is delivered");
    var cleared=coordinator.ClearAsync();
    Check(coordinator.Current.Status==SelectionStatus.Idle,"Clear takes effect before UI dispatch");
    (await dispatcher.Take())();await Done(cleared);
}

var disposalRelease=Signal<string>();var disposalStarted=Signal<bool>();
var disposal=new SelectionCoordinator<string,string>(async (r,t)=>{disposalStarted.SetResult(true);return await disposalRelease.Task;},Dispatch);
int disposalEvents=0;disposal.Changed+=s=>{if(s.Status==SelectionStatus.Ready)disposalEvents++;};
var pending=disposal.SelectAsync("A");await Done(disposalStarted.Task);disposal.Dispose();disposalRelease.SetResult("late");await Done(pending);
Check(disposalEvents==0&&disposal.Current.Status==SelectionStatus.Idle,"Plugin disable prevents late publications");
bool rejected=false;try{await disposal.SelectAsync("B");}catch(ObjectDisposedException){rejected=true;}
Check(rejected,"Disposed coordinator cannot restart a request");

var clearRelease=Signal<string>();var clearStarted=Signal<bool>();
using(var coordinator=new SelectionCoordinator<string,string>(async (r,t)=>{clearStarted.SetResult(true);return await clearRelease.Task;},Dispatch))
{
    var work=coordinator.SelectAsync("A");await Done(clearStarted.Task);await coordinator.ClearAsync();clearRelease.SetResult("late");await Done(work);
    Check(coordinator.Current.Status==SelectionStatus.Idle&&coordinator.Current.Result==null,"Late load cannot revive a deactivated menu");
}

// Stale exceptions must not overwrite the newer request either.
var errorRelease=Signal<bool>();var errorStarted=Signal<bool>();
using(var coordinator=new SelectionCoordinator<string,string>(async (r,t)=> {
    if(r=="A"){errorStarted.SetResult(true);await errorRelease.Task;throw new NotSupportedException("stale extension");}
    return r;
},Dispatch))
{
    var a=coordinator.SelectAsync("A");await Done(errorStarted.Task);var b=coordinator.SelectAsync("B");errorRelease.SetResult(true);await Done(Task.WhenAll(a,b));
    Check(coordinator.Current.Status==SelectionStatus.Ready&&coordinator.Current.Request=="B","Stale unsupported/error result is ignored");
}

string scratch=BeatInsight.Tooling.LocalArtifacts.PathFor("menu-checks",Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(Path.Combine(scratch,"maps"));
var info=JObject.Parse("""{"version":"4.0.1","audio":{"bpm":120,"songDuration":12},"difficultyBeatmaps":[{"characteristic":"Standard","difficulty":"Expert","beatmapDataFilename":"maps/Expert.dat"},{"characteristic":"Standard","difficulty":"Hard","beatmapDataFilename":"maps/Hard.dat"}]}""");
string expert=Path.Combine(scratch,"maps","Expert.dat"),hard=Path.Combine(scratch,"maps","Hard.dat");
File.WriteAllText(Path.Combine(scratch,"Info.dat"),info.ToString());
File.WriteAllText(expert,"""{"version":"3.3.0","colorNotes":[{"b":2,"x":0,"y":0,"c":0,"d":1},{"b":2.5,"x":1,"y":0,"c":0,"d":1},{"b":4,"x":3,"y":1,"c":1,"d":0}]}""");
File.WriteAllText(hard,"""{"_version":"2.0.0","_notes":[{"_time":4,"_lineIndex":3,"_type":1,"_cutDirection":1}]}""");
string key=PlayHistory.Key("custom_level_TEST",3,"Standard");
string legacyFile=Path.Combine(scratch,"SongPlayData.json"),ownFile=Path.Combine(scratch,DeathHistoryStore.FileName);
File.WriteAllText(legacyFile,JsonConvert.SerializeObject(new Dictionary<string,PlayRecord[]> {[key]=[
    new(){LastNote=1,Date=1000,Param=16},new(){LastNote=2,Date=2000,IsPractice=true}]}));
DeathHistoryStore.Append(ownFile,new DeathRecord {Id="synthetic-menu-death",MapKey=key,SongTimeSeconds=3,Date=3000,TimingSource="completion-song-time-fallback"});
string legacyBefore=File.ReadAllText(legacyFile),ownBefore=File.ReadAllText(ownFile);
var request=new SelectedMapRequest("custom_level_TEST","Standard","Expert",3,expert,legacyFile,ownFile);
var analysis=await SelectedMapAnalyzer.LoadAsync(request,CancellationToken.None);
Check(analysis.MapKey==key&&analysis.MapVersion=="3.3.0"&&analysis.Duration==12,"Exact selection identity and nested file resolution");
Check(analysis.Corrected.LeftBins.Sum()==2&&analysis.Corrected.RightBins.Sum()==1&&analysis.MedianLeft==2&&analysis.MedianRight==1,"UI snapshot contains corrected per-hand statistics");
Check(analysis.HasLeftNote&&analysis.HasRightNote&&analysis.Corrected.Segments.Length==90&&analysis.Corrected.Colors.Length==90,"Directions availability and graph data are present");
Check(analysis.LegacyHistoryStatus=="available"&&analysis.OwnHistoryStatus=="available"&&analysis.DeathMarkers.Length==2,"Both history sources and practice filtering");
Check(analysis.Corrected.HistoryIndices.Length==0&&analysis.Compatible.HistoryIndices.Length==1,"Legacy history is not reindexed against corrected notes");
Check(analysis.DeathMarkers.Any(m=>m.ColorHex=="#FFFF00"&&m.SongTimeSeconds==1.25)&&analysis.DeathMarkers.Any(m=>m.ColorHex=="#FF0000"&&m.PositionStatus=="completion-song-time-estimate"),"Legacy and own markers retain positions, colors and precision");
Check(File.ReadAllText(legacyFile)==legacyBefore&&File.ReadAllText(ownFile)==ownBefore,"Selection analysis never writes history");
// A visual-only track changes neither menu values nor history indexing. Exercise
// every feature combination, including the strict Reference path and all-hidden.
using(var visualStream=System.Reflection.Assembly.GetExecutingAssembly().GetManifestResourceStream("BeatInsight.IsolatedVisualTrack.json")!)
using(var visualReader=new StreamReader(visualStream))
{
    var source=JObject.Parse(visualReader.ReadToEnd());
    var plain=(JObject)source.DeepClone();plain.Remove("customData");
    foreach(var note in plain["colorNotes"]!)((JObject)note).Remove("customData");
    string visualFolder=Path.Combine(scratch,"visual-tracks"),plainFolder=Path.Combine(scratch,"plain-tracks");
    foreach(var item in new[]{(visualFolder,source,true),(plainFolder,plain,false)})
    {
        Directory.CreateDirectory(item.Item1);
        var metadata=JObject.Parse("""{"version":"4.0.1","audio":{"bpm":120,"songDuration":12},"difficultyBeatmaps":[{"characteristic":"Standard","difficulty":"Expert","beatmapDataFilename":"map.dat"}]}""");
        if(item.Item3)metadata["difficultyBeatmaps"]![0]!["customData"]=new JObject {["requirements"]=new JArray("Noodle Extensions","Chroma")};
        File.WriteAllText(Path.Combine(item.Item1,"Info.dat"),metadata.ToString());File.WriteAllText(Path.Combine(item.Item1,"map.dat"),item.Item2.ToString());
    }
    string visualFile=Path.Combine(visualFolder,"map.dat");
    for(int flags=0;flags<512;flags++)
    {
        var features=(AnalysisFeatures)flags;
        SelectedMapAnalysis Read(string folder)=>SelectedMapAnalyzer.Load(new SelectedMapRequest("custom_level_TEST","Standard","Expert",3,Path.Combine(folder,"map.dat"),legacyFile,ownFile,features:features));
        var actual=Read(visualFolder);var expected=Read(plainFolder);
        var actualJson=JObject.FromObject(actual);var expectedJson=JObject.FromObject(expected);
        actualJson.Remove("BeatmapSha256");expectedJson.Remove("BeatmapSha256");
        Check(JToken.DeepEquals(actualJson,expectedJson),"Visual-only tracks preserve menu values and history across feature flags: "+flags);
        Check(actual.BeatmapSha256==((features&AnalysisFeatures.OwnDeaths)!=0?BeatmapFingerprint.Compute(source.ToString()):""),"Visual-only tracks retain exact source fingerprint only when requested: "+flags);
    }
    var dirty=(JObject)source.DeepClone();dirty["customData"]!["environment"]![0]!["track"]="notes";
    File.WriteAllText(visualFile,dirty.ToString());
    var partial=SelectedMapAnalyzer.Load(new SelectedMapRequest("custom_level_TEST","Standard","Expert",3,visualFile,legacyFile,ownFile,features:new MenuVisibility().Features));
    Check(partial.NoteError!=null&&partial.Corrected.LeftBins.Length==0&&partial.IndependentDensity&&partial.OwnHistoryStatus=="available"&&partial.LegacyHistoryStatus=="note-analysis-unavailable",
        "Shared environment binding retains density/own history but masks unsupported note metrics and imported indices");
    File.WriteAllText(visualFile,source.ToString());
    Check(File.ReadAllText(legacyFile)==legacyBefore&&File.ReadAllText(ownFile)==ownBefore,"Visual track analysis never writes either history source");
}
// Bundled snapshots use exactly the same parser, hash and history rules without
// resolving a fake filesystem path or writing temporary map documents.
Check(AnalysisAvailabilityHints.Reason("Bundled map audio offsets have not been validated")=="Audio timing offsets for bundled maps are not supported yet.","Bundled offset refusal has a specific English explanation");
Check(AnalysisAvailabilityHints.Reason("This beatmap data provider is not supported by the menu analyzer")=="This map's data source is not supported yet.","Unknown provider refusal has a specific English explanation");
var documents=MapDocuments.Bundled(File.ReadAllText(expert),null,120,12,"Standard","Expert");
var filesBefore=Directory.GetFiles(scratch,"*",SearchOption.AllDirectories).Order().ToArray();
for(int memoryMask=0;memoryMask<256;memoryMask++)
{
    var features=(AnalysisFeatures)memoryMask;
    var diskResult=SelectedMapAnalyzer.Load(new SelectedMapRequest("custom_level_TEST","Standard","Expert",3,expert,legacyFile,ownFile,features:features));
    var memoryResult=await SelectedMapAnalyzer.LoadAsync(new SelectedMapRequest("custom_level_TEST","Standard","Expert",3,"not-on-disk.dat",legacyFile,ownFile,features:features,documents:documents),CancellationToken.None);
    Check(JsonConvert.SerializeObject(memoryResult)==JsonConvert.SerializeObject(diskResult),"Memory/file selection parity including fingerprint and history: "+memoryMask);
}
Check(Directory.GetFiles(scratch,"*",SearchOption.AllDirectories).Order().SequenceEqual(filesBefore),"Memory analysis creates no map files");
Check(File.ReadAllText(legacyFile)==legacyBefore&&File.ReadAllText(ownFile)==ownBefore,"Memory analysis leaves both histories unchanged");
var malformedDocuments=new MapDocuments("{","{");
Check(SelectedMapAnalyzer.Load(new SelectedMapRequest("builtin-test","Standard","Expert",3,"",features:AnalysisFeatures.None,documents:malformedDocuments)).Features==AnalysisFeatures.None,"Hidden bundled analysis does not parse documents");
using(var memoryCancellation=new CancellationTokenSource())
{
    memoryCancellation.Cancel();bool cancelled=false;
    try{await SelectedMapAnalyzer.LoadAsync(new SelectedMapRequest("builtin-test","Standard","Expert",3,"",documents:malformedDocuments),memoryCancellation.Token);}
    catch(OperationCanceledException){cancelled=true;}
    Check(cancelled,"Cancelled bundled analysis does not parse documents");
}
for(int mask=0;mask<256;mask++)
{
    var features=(AnalysisFeatures)mask;
    bool Has(AnalysisFeatures f)=>(features&f)!=0;
    var partial=SelectedMapAnalyzer.Load(new SelectedMapRequest("custom_level_TEST","Standard","Expert",3,expert,legacyFile,ownFile,features:features));
    bool bins=Has(AnalysisFeatures.Average|AnalysisFeatures.Peak|AnalysisFeatures.Median);
    bool same=partial.Features==features&&partial.Corrected.LeftAverage==(Has(AnalysisFeatures.Average)?analysis.Corrected.LeftAverage:0)
        &&partial.Corrected.RightAverage==(Has(AnalysisFeatures.Average)?analysis.Corrected.RightAverage:0)
        &&partial.Corrected.LeftPeak==(Has(AnalysisFeatures.Peak)?analysis.Corrected.LeftPeak:0)
        &&partial.Corrected.RightPeak==(Has(AnalysisFeatures.Peak)?analysis.Corrected.RightPeak:0)
        &&partial.MedianLeft==(Has(AnalysisFeatures.Median)?analysis.MedianLeft:0)
        &&partial.MedianRight==(Has(AnalysisFeatures.Median)?analysis.MedianRight:0)
        &&(bins?partial.Corrected.LeftBins.SequenceEqual(analysis.Corrected.LeftBins):partial.Corrected.LeftBins.Length==0)
        &&(Has(AnalysisFeatures.Density)?partial.Corrected.Segments.SequenceEqual(analysis.Corrected.Segments)&&
            JsonConvert.SerializeObject(partial.Corrected.Colors)==JsonConvert.SerializeObject(analysis.Corrected.Colors):partial.Corrected.Colors.Length==0&&partial.Corrected.Segments.Length==0)
        &&partial.SquatCount==(Has(AnalysisFeatures.Squats)?analysis.SquatCount:null)
        &&(Has(AnalysisFeatures.FirstNotes)?partial.FirstLeftNote?.CutDirectionAngleOffset==analysis.FirstLeftNote?.CutDirectionAngleOffset&&partial.FirstRightNote?.Time==analysis.FirstRightNote?.Time:partial.FirstLeftNote==null&&partial.FirstRightNote==null)
        &&partial.LegacyHistoryStatus==(Has(AnalysisFeatures.ImportedDeaths)?analysis.LegacyHistoryStatus:"not-requested")
        &&partial.OwnHistoryStatus==(Has(AnalysisFeatures.OwnDeaths)?analysis.OwnHistoryStatus:"not-requested")
        &&partial.DeathMarkers.Length==((Has(AnalysisFeatures.ImportedDeaths)?1:0)+(Has(AnalysisFeatures.OwnDeaths)?1:0))
        &&partial.BeatmapSha256==(Has(AnalysisFeatures.OwnDeaths)?analysis.BeatmapSha256:"")
        &&partial.Compatible.LeftBins.Length==0;
    Check(same,"Every selective calculation matches enabled full results and omits hidden results: "+mask);
}
var disabled=SelectedMapAnalyzer.Load(new SelectedMapRequest("custom_level_TEST","Standard","Expert",3,Path.Combine(scratch,"does-not-exist.dat"),features:AnalysisFeatures.None));
Check(disabled.Features==AnalysisFeatures.None&&disabled.DeathMarkers.Length==0,"All hidden performs no map or history IO");
string validExpert=File.ReadAllText(expert);
try
{
    File.WriteAllText(expert,"""{"version":"3.3.0","colorNotes":[{"b":"invalid-note-time"}],"obstacles":[]}""");
    var squatOnly=SelectedMapAnalyzer.Load(new SelectedMapRequest("custom_level_TEST","Standard","Expert",3,expert,features:AnalysisFeatures.Squats));
    var ownOnlyAnalysis=SelectedMapAnalyzer.Load(new SelectedMapRequest("custom_level_TEST","Standard","Expert",3,expert,ownHistoryFile:ownFile,features:AnalysisFeatures.OwnDeaths));
    Check(squatOnly.SquatCount==0&&squatOnly.SquatError==null&&ownOnlyAnalysis.OwnHistoryStatus=="available",
        "Squat-only and own-history-only requests never prepare or analyze notes");
}
finally{File.WriteAllText(expert,validExpert);}
var damagedAngle=JObject.Parse(validExpert);damagedAngle["colorNotes"]![0]!["a"]="invalid-hidden-angle";
try
{
    File.WriteAllText(expert,damagedAngle.ToString());
    var averagesOnly=SelectedMapAnalyzer.Load(new SelectedMapRequest("custom_level_TEST","Standard","Expert",3,expert,features:AnalysisFeatures.Average));
    Check(averagesOnly.Corrected.LeftAverage==analysis.Corrected.LeftAverage,"Hidden first-note orientation is neither read nor calculated");
}
finally{File.WriteAllText(expert,validExpert);}
var other=SelectedMapAnalyzer.Load(new SelectedMapRequest("custom_level_TEST","Standard","Hard",2,hard,legacyFile,ownFile));
Check(other.MapKey!=key&&!other.HasLeftNote&&other.HasRightNote&&other.DeathMarkers.Length==0&&other.LegacyHistoryStatus=="key-absent","Difficulty change does not reuse another difficulty's data/history");
File.WriteAllText(legacyFile,"{");
Check(SelectedMapAnalyzer.Load(request).LegacyHistoryStatus=="invalid-data","Corrupt history does not hide map analysis or become zero deaths");
File.WriteAllText(ownFile,"{\"schemaVersion\":99,\"deaths\":[]}");
Check(SelectedMapAnalyzer.Load(request).OwnHistoryStatus=="unsupported-schema","Future own history schema remains explicit");
string absent=Path.Combine(scratch,"absent",DeathHistoryStore.FileName);
var missing=SelectedMapAnalyzer.Load(new SelectedMapRequest("custom_level_TEST","Standard","Expert",3,expert,null,absent));
Check(missing.OwnHistoryStatus=="source-missing"&&!Directory.Exists(Path.GetDirectoryName(absent)),"Reading absent history creates no files/directories");
using(var coordinator=new SelectionCoordinator<SelectedMapRequest,SelectedMapAnalysis>(SelectedMapAnalyzer.LoadAsync,Dispatch))
{
    var row=(JObject)info["difficultyBeatmaps"]![0]!;row["customData"]=JObject.Parse("""{"requirements":["Unknown Extension"]}""");
    File.WriteAllText(Path.Combine(scratch,"Info.dat"),info.ToString());await coordinator.SelectAsync(request);
    Check(coordinator.Current.Status==SelectionStatus.Unsupported&&coordinator.Current.Result==null,"Info requirements reject an unsupported map before UI publication");
    row["customData"]=JObject.Parse("""{"requirements":["Noodle Extensions"]}""");
    File.WriteAllText(Path.Combine(scratch,"Info.dat"),info.ToString());await coordinator.SelectAsync(request);
    Check(coordinator.Current.Status==SelectionStatus.Ready,"Nominal Noodle subset reaches the menu publication path");
    row.Remove("customData");File.WriteAllText(Path.Combine(scratch,"Info.dat"),info.ToString());
    string saved=File.ReadAllText(expert);File.WriteAllText(expert,"{");await coordinator.SelectAsync(request);
    Check(coordinator.Current.Status==SelectionStatus.Error,"Corrupt selected JSON has explicit error status");
    File.WriteAllText(expert,saved);await coordinator.SelectAsync(request);
    Check(coordinator.Current.Status==SelectionStatus.Ready,"Selection recovers after input is repaired");
    await coordinator.SelectAsync(new SelectedMapRequest("custom_level_TEST","Standard","Expert",3,Path.Combine(scratch,"missing.dat")));
    Check(coordinator.Current.Status==SelectionStatus.Unavailable,"Missing selected map is unavailable, not empty success");
    await coordinator.SelectAsync(new SelectedMapRequest("custom_level_TEST","Standard","Expert",2,expert));
    Check(coordinator.Current.Status==SelectionStatus.Error,"Difficulty/rank disagreement cannot select wrong history");
}
foreach(int major in new[]{2,3})
{
    string isolated=Path.Combine(scratch,"independent-walls-v"+major);Directory.CreateDirectory(isolated);
    var isolatedInfo=JObject.Parse("""{"version":"4.0.1","audio":{"bpm":120,"songDuration":12},"difficultyBeatmaps":[{"characteristic":"Standard","difficulty":"Expert","beatmapDataFilename":"map.dat","customData":{"requirements":["Noodle Extensions"]}}]}""");
    File.WriteAllText(Path.Combine(isolated,"Info.dat"),isolatedInfo.ToString());
    string file=Path.Combine(isolated,"map.dat"),customKey=major==2?"_customData":"customData",wallKey=major==2?"_obstacles":"obstacles";
    var root=major==2?JObject.Parse("""{"_version":"2.6.0","_notes":[{"_time":2,"_lineIndex":0,"_lineLayer":0,"_type":0,"_cutDirection":1},{"_time":4,"_lineIndex":1,"_lineLayer":0,"_type":0,"_cutDirection":1},{"_time":6,"_lineIndex":3,"_lineLayer":1,"_type":1,"_cutDirection":0}],"_obstacles":[{"_time":2,"_duration":400,"_lineIndex":1,"_type":1,"_width":2}]}"""):
        JObject.Parse("""{"version":"3.3.0","colorNotes":[{"b":2,"x":0,"y":0,"c":0,"d":1},{"b":4,"x":1,"y":0,"c":0,"d":1},{"b":6,"x":3,"y":1,"c":1,"d":0}],"obstacles":[{"b":2,"d":400,"x":1,"y":2,"w":2,"h":3}]}""");
    SelectedMapAnalysis Read(AnalysisFeatures features)=>SelectedMapAnalyzer.Load(new SelectedMapRequest("independent-walls","Standard","Expert",3,file,features:features));
    var menuFeatures=new MenuVisibility().Features;File.WriteAllText(file,root.ToString());var baseline=Read(menuFeatures);
    foreach(string failure in new[]{"track","pitch"})
    {
        var changed=(JObject)root.DeepClone();changed[wallKey]![0]![customKey]=new JObject();
        if(failure=="track")
        {
            changed[wallKey]![0]![customKey]![major==2?"_track":"track"]="moving-wall";
            changed[customKey]=new JObject {[major==2?"_customEvents":"customEvents"]=new JArray(new JObject {
                [major==2?"_time":"b"]=0,[major==2?"_type":"t"]="AnimateTrack",[major==2?"_data":"d"]=new JObject {
                    [major==2?"_track":"track"]="moving-wall",[major==2?"_duration":"duration"]=8,
                    [major==2?"_position":"offsetPosition"]=new JArray(new JArray(0,0,0,0),new JArray(1,0,0,1))}})};
        }
        else changed[wallKey]![0]![customKey]![major==2?"_localRotation":"localRotation"]=new JArray(10,0,0);
        File.WriteAllText(file,changed.ToString());var partial=Read(menuFeatures);
        Check(partial.Corrected.LeftAverage==baseline.Corrected.LeftAverage&&partial.Corrected.RightAverage==baseline.Corrected.RightAverage
            &&partial.Corrected.LeftPeak==baseline.Corrected.LeftPeak&&partial.MedianLeft==baseline.MedianLeft&&partial.MedianRight==baseline.MedianRight
            &&partial.FirstLeftNote?.CutDirectionAngleOffset==baseline.FirstLeftNote?.CutDirectionAngleOffset&&partial.Corrected.Segments.SequenceEqual(baseline.Corrected.Segments)
            &&partial.SquatCount==null&&partial.SquatError!=null&&SelectedMapPresentation.Create(partial,DrawColor.White,DrawColor.White).Squats=="—"
            &&partial.Corrected.LeftBins.Length<baseline.Corrected.LeftBins.Length,
            $"v{major} {failure} walls cannot hide independent note metrics; long zero tail is excluded safely");
        var hidden=Read(menuFeatures&~AnalysisFeatures.Squats);
        Check(hidden.SquatCount==null&&hidden.SquatError==null&&hidden.Corrected.LeftAverage==baseline.Corrected.LeftAverage,$"v{major} hidden unsupported squats remain unrequested");
        bool wallRejected=false;try{Read(AnalysisFeatures.All);}catch(NotSupportedException){wallRejected=true;}
        Check(wallRejected,$"v{major} reference analysis still requires complete validated walls");
        var movingNotes=(JObject)changed.DeepClone();movingNotes[major==2?"_notes":"colorNotes"]![0]![customKey]=new JObject {
            [major==2?"_animation":"animation"]=new JObject {[major==2?"_position":"offsetPosition"]=new JArray(1,0,0)}};
        File.WriteAllText(file,movingNotes.ToString());var densityPartial=Read(menuFeatures);
        Check(densityPartial.NoteError!=null&&densityPartial.IndependentDensity&&densityPartial.Corrected.LeftBins.Length==0
            &&densityPartial.Corrected.Segments.SequenceEqual(baseline.Corrected.Segments),$"v{major} unsupported moving notes expose density only, never swing statistics");
        if(failure=="pitch")
        {
            var noteReason=AnalysisAvailabilityHints.Reason(densityPartial.NoteError);
            var wallReason=AnalysisAvailabilityHints.Reason(densityPartial.SquatError);
            Check(noteReason.Contains("object animation")&&wallReason.Contains("Tilted walls")&&noteReason!=wallReason,
                $"v{major} mixed note/wall failures retain separate readable reasons");
        }
        wallRejected=false;try{Read(menuFeatures&~AnalysisFeatures.Density);}catch(NotSupportedException){wallRejected=true;}
        Check(wallRejected,$"v{major} hidden density cannot admit otherwise unavailable moving notes/walls");
    }
    var noteOnlyFailure=(JObject)root.DeepClone();noteOnlyFailure[major==2?"_notes":"colorNotes"]![0]![customKey]=new JObject {
        [major==2?"_animation":"animation"]=new JObject {[major==2?"_position":"offsetPosition"]=new JArray(1,0,0)}};
    File.WriteAllText(file,noteOnlyFailure.ToString());var wallsPartial=Read(menuFeatures);
    Check(wallsPartial.NoteError!=null&&wallsPartial.SquatError==null&&wallsPartial.SquatCount==baseline.SquatCount,
        $"v{major} independently validated squats survive unsupported notes");
    var wallsView=SelectedMapPresentation.Create(wallsPartial,DrawColor.White,DrawColor.White);
    Check(wallsView.Average=="—"&&wallsView.Peak=="—"&&wallsView.Median=="—"&&wallsView.Left==null&&wallsView.Right==null&&wallsView.Colors.Length==90&&wallsPartial.IndependentDensity
        &&wallsView.Squats==baseline.SquatCount!.Value.ToString()&&wallsView.Duration==12,
        $"v{major} unsupported notes render dashes while preserving squat value and duration");
    Check(wallsPartial.Corrected.RightBins.Length==0&&wallsPartial.Corrected.LeftBins.Length==0,
        $"v{major} note failure never fabricates corrected note statistics");
    Check(Read(AnalysisFeatures.Squats).NoteError==null,$"v{major} squat-only selection does not analyze hidden notes");
    foreach(var mask in new[]{menuFeatures&~(AnalysisFeatures.Squats|AnalysisFeatures.Density),AnalysisFeatures.All})
    {
        bool noteRejected=false;try{Read(mask);}catch(NotSupportedException){noteRejected=true;}
        Check(noteRejected,$"v{major} no wall fallback when squats hidden or reference requested");
    }
    string partialHistory=Path.Combine(isolated,DeathHistoryStore.FileName);
    DeathHistoryStore.Append(partialHistory,new DeathRecord {Id="independent-wall-death",MapKey=PlayHistory.Key("independent-walls",3,"Standard"),SongTimeSeconds=3,Date=3000});
    string historySnapshot=File.ReadAllText(partialHistory);
    var wallsHistory=SelectedMapAnalyzer.Load(new SelectedMapRequest("independent-walls","Standard","Expert",3,file,legacyFile,partialHistory,features:menuFeatures));
    Check(wallsHistory.LegacyHistoryStatus=="note-analysis-unavailable"&&wallsHistory.DeathMarkers.All(m=>m.Source!="legacy")
        &&wallsHistory.DeathMarkers.Any(m=>m.Source=="beatinsight"&&m.NormalizedPosition==.25)&&File.ReadAllText(partialHistory)==historySnapshot
        &&wallsHistory.BeatmapSha256==BeatmapFingerprint.Compute(File.ReadAllText(file)),
        $"v{major} unsupported note indices stay hidden while own timed history remains read-only");
    var player=(JObject)root.DeepClone();player[customKey]=new JObject {[major==2?"_customEvents":"customEvents"]=new JArray(new JObject {
        [major==2?"_time":"b"]=0,[major==2?"_type":"t"]="AssignPlayerToTrack",[major==2?"_data":"d"]=new JObject {[major==2?"_track":"track"]="player"}})};
    File.WriteAllText(file,player.ToString());var playerPartial=Read(menuFeatures);
    Check(playerPartial.NoteError!=null&&playerPartial.IndependentDensity&&playerPartial.Corrected.LeftBins.Length==0
        &&SelectedMapPresentation.Create(playerPartial,DrawColor.White,DrawColor.White).Left==null,
        $"v{major} player movement remains unsupported for swings/pose while nominal density is independent");
    if(major==3)foreach(string linked in new[]{"arc-bomb","chain"})
    {
        var linkedMap=(JObject)player.DeepClone();
        if(linked=="arc-bomb")
        {
            linkedMap["bombNotes"]=new JArray(new JObject{["b"]=4,["x"]=1,["y"]=0});
            linkedMap["sliders"]=new JArray(new JObject{["b"]=2,["x"]=0,["y"]=0,["c"]=0,["d"]=1,["tb"]=4,["tx"]=1,["ty"]=0,["tc"]=1});
        }
        else linkedMap["burstSliders"]=new JArray(new JObject{["b"]=2,["x"]=0,["y"]=0,["c"]=0,["d"]=1,["tb"]=3,["tx"]=1,["ty"]=1,["sc"]=4,["s"]=1});
        File.WriteAllText(file,linkedMap.ToString());
        var linkedPartial=SelectedMapAnalyzer.Load(new SelectedMapRequest("independent-walls","Standard","Expert",3,file,legacyFile,partialHistory,features:menuFeatures));
        var linkedView=SelectedMapPresentation.Create(linkedPartial,DrawColor.White,DrawColor.White);
        Check(linkedPartial.IndependentDensity&&linkedView.Colors.Length==90&&linkedView.Average=="—"&&linkedView.Peak=="—"&&linkedView.Median=="—"&&linkedView.Left==null&&linkedView.Right==null,
            "Linked density exposes graph without unsupported statistics or pose: "+linked);
        Check(linkedPartial.LegacyHistoryStatus=="note-analysis-unavailable"&&linkedPartial.DeathMarkers.All(m=>m.Source!="legacy")
            &&linkedPartial.DeathMarkers.Any(m=>m.Source=="beatinsight"&&m.NormalizedPosition==.25)&&File.ReadAllText(partialHistory)==historySnapshot
            &&linkedPartial.BeatmapSha256==BeatmapFingerprint.Compute(File.ReadAllText(file)),"Linked density preserves source identity and read-only timed history: "+linked);
        bool hiddenRejected=false;try{Read(menuFeatures&~AnalysisFeatures.Density);}catch(NotSupportedException){hiddenRejected=true;}
        Check(hiddenRejected,"Hidden linked density cannot admit unsupported geometry: "+linked);
    }
    var emptyPlayer=(JObject)player.DeepClone();emptyPlayer[wallKey]=new JArray();File.WriteAllText(file,emptyPlayer.ToString());
    var zeroWalls=SelectedMapAnalyzer.Load(new SelectedMapRequest("independent-walls","Standard","Expert",3,file,legacyFile,partialHistory,features:menuFeatures));
    var zeroView=SelectedMapPresentation.Create(zeroWalls,DrawColor.White,DrawColor.White);
    Check(zeroWalls.NoteError!=null&&zeroWalls.IndependentDensity&&zeroWalls.SquatError==null&&zeroWalls.SquatCount==0&&zeroView.Squats=="0"&&zeroView.Average=="—"&&zeroView.Left==null,
        $"v{major} absent obstacles yield a proven zero without admitting player motion or note pose");
    Check(zeroWalls.LegacyHistoryStatus=="note-analysis-unavailable"&&zeroWalls.DeathMarkers.Any(m=>m.Source=="beatinsight"&&m.NormalizedPosition==.25)&&File.ReadAllText(partialHistory)==historySnapshot
        &&zeroWalls.BeatmapSha256==BeatmapFingerprint.Compute(emptyPlayer.ToString()),$"v{major} zero-wall fallback preserves timed own history and source identity");
    var hiddenZero=Read(menuFeatures&~AnalysisFeatures.Squats);
    Check(hiddenZero.SquatCount==null&&hiddenZero.SquatError==null&&hiddenZero.NoteError!=null,$"v{major} hidden zero counter is not computed");
    emptyPlayer[wallKey]=root[wallKey]!.DeepClone();File.WriteAllText(file,emptyPlayer.ToString());
    Check(Read(menuFeatures).SquatCount==null,$"v{major} one obstacle restores the player-motion collision barrier");
}
{
    var metadata=JObject.Parse("""{"version":"4.0.1","audio":{"bpm":120,"songDuration":12},"difficultyBeatmaps":[{"characteristic":"Standard","difficulty":"Expert","beatmapDataFilename":"map.dat","customData":{"requirements":["Mapping Extensions","Noodle Extensions"]}}]}""");
    var source=JObject.Parse("""{"version":"3.3.0","colorNotes":[{"b":2,"x":0,"y":0,"c":0,"d":1},{"b":4,"x":1,"y":1,"c":1,"d":8}],"sliders":[{"b":2,"x":0,"y":0,"c":0,"d":1,"tb":4,"tx":1,"ty":1,"tc":8}],"obstacles":[{"b":1,"x":1500,"y":1500,"w":1500,"h":1500,"d":4}]}""");
    string historyPath=Path.Combine(scratch,"combined-notes",DeathHistoryStore.FileName);
    DeathHistoryStore.Append(historyPath,new DeathRecord{Id="combined-note-death",MapKey=PlayHistory.Key("combined-notes",3,"Standard"),SongTimeSeconds=3,Date=3000});
    string snapshot=File.ReadAllText(historyPath);
    SelectedMapAnalysis ReadCombined()=>SelectedMapAnalyzer.Load(new SelectedMapRequest("combined-notes","Standard","Expert",3,"",legacyFile,historyPath,
        features:new MenuVisibility().Features,documents:new MapDocuments(metadata.ToString(),source.ToString(),file:"map.dat")));
    var result=ReadCombined();var view=SelectedMapPresentation.Create(result,DrawColor.White,DrawColor.White);
    Check(result.NoteError==null&&result.SquatError!=null&&result.SquatCount==null&&!result.IndependentDensity,"Canonical combined notes survive unsupported extended walls in menu");
    Check(view.Average!="—"&&view.Peak!="—"&&view.Median!="—"&&view.Left!=null&&view.Right!=null&&view.Colors.Length==90&&view.Squats=="—","Combined partial menu displays only validated note metrics");
    Check(result.LegacyHistoryStatus=="extension-positions-unverified"&&result.DeathMarkers.All(m=>m.Source!="legacy")&&result.DeathMarkers.Any(m=>m.Source=="beatinsight"&&m.NormalizedPosition==.25)
        &&result.BeatmapSha256==BeatmapFingerprint.Compute(source.ToString())&&File.ReadAllText(historyPath)==snapshot,"Combined note admission keeps Legacy indices closed and own timed history read-only");
    source["colorNotes"]![0]!["customData"]=new JObject{["coordinates"]=new JArray(-.5f,.25f)};
    var blocked=ReadCombined();var blockedView=SelectedMapPresentation.Create(blocked,DrawColor.White,DrawColor.White);
    Check(blocked.NoteError==null&&!blocked.IndependentDensity&&blockedView.Average!="—"&&blockedView.Left!=null&&blockedView.Colors.Length==90,"Ordinary linked coordinates admit corrected raw/custom arc roles");
    Check(blocked.LegacyHistoryStatus=="extension-positions-unverified"&&blocked.SquatCount==null,"Arc role support keeps Legacy indices and wall geometry closed");
    source["colorNotes"]![0]!["x"]=1500;
    var nominal=ReadCombined();var nominalView=SelectedMapPresentation.Create(nominal,DrawColor.White,DrawColor.White);
    Check(nominal.NoteError==null&&!nominal.IndependentDensity&&nominal.SquatCount==null&&nominal.SquatError!=null&&nominalView.Average!="—"&&nominalView.Median!="—","Combined nominal note metrics survive unvalidated spatial coordinates");
    Check(nominalView.Left?.Direction==1&&nominalView.Right?.Direction==8&&nominalView.Left.RotationZ==null&&nominalView.Right.RotationZ==null,"Nominal first arrows/dots expose unknown tilt rather than an invented angle");
    Check(nominal.Corrected.Segments.SequenceEqual(blocked.Corrected.Segments)&&nominal.LegacyHistoryStatus=="extension-positions-unverified"&&nominal.DeathMarkers.All(m=>m.Source!="legacy")
        &&nominal.BeatmapSha256==BeatmapFingerprint.Compute(source.ToString())&&File.ReadAllText(historyPath)==snapshot,"Nominal metrics preserve graph/source identity and read-only history while keeping Legacy indices closed");

}
DrawColor leftColor=new DrawColor(.8f,.1f,.2f),rightColor=new DrawColor(.1f,.6f,1);
string privateDiagnostic="Unexpected parser failure <b>PRIVATE_DIAGNOSTIC_PAYLOAD</b>";
Check(!AnalysisAvailabilityHints.Reason(privateDiagnostic).Contains("PRIVATE_DIAGNOSTIC_PAYLOAD")&&!AnalysisAvailabilityHints.Reason(privateDiagnostic).Contains("<"),
    "Unknown diagnostics cannot leak exception details or rich-text markup into tooltips");
string rotationDiagnostic="Unsupported gameplay data: worldRotation (<b>PRIVATE_DIAGNOSTIC_PAYLOAD</b>)";
Check(AnalysisAvailabilityHints.Selection(SelectionStatus.Unsupported,rotationDiagnostic).Contains("world rotation")
    &&!AnalysisAvailabilityHints.Selection(SelectionStatus.Unsupported,rotationDiagnostic).Contains("PRIVATE_DIAGNOSTIC_PAYLOAD"),
    "Known unsupported geometry has a readable reason without echoing diagnostic contents");
Check(AnalysisAvailabilityHints.Selection(SelectionStatus.Loading,rotationDiagnostic)=="Loading..."
    &&AnalysisAvailabilityHints.Selection(SelectionStatus.Idle,rotationDiagnostic)=="Select a map.",
    "New loading/idle states cannot retain an earlier map's unsupported reason");
Check(AnalysisAvailabilityHints.Selection(SelectionStatus.Unavailable,rotationDiagnostic).Contains("found or loaded")
    &&!AnalysisAvailabilityHints.Selection(SelectionStatus.Unavailable,rotationDiagnostic).Contains("not supported"),
    "Missing map data is distinguished from unsupported map geometry");
Check(AnalysisAvailabilityHints.Selection(SelectionStatus.Unavailable,"Game content is not ready").Contains("not loaded")
    &&AnalysisAvailabilityHints.Selection(SelectionStatus.Error,privateDiagnostic)=="The selected map data could not be analyzed.",
    "Pending game content and analysis errors have separate nontechnical messages");
var readyPresentation=SelectedMapPresentation.Create(analysis,leftColor,rightColor);
Check(readyPresentation.Average=="2.0 / 1.0"&&readyPresentation.Peak=="2 / 1"&&readyPresentation.Median=="2 / 1","AVG displays tenths; PEAK/MEDIAN preserve their format and hand order");
Check(readyPresentation.Squats=="0","A real zero squat count is not an error dash");
var hiddenAverage=MenuDrawing.Panel(readyPresentation);
new MenuVisibility {Average=false}.Apply(hiddenAverage);
Check(hiddenAverage.Labels.All(l=>l.Group!=DrawGroup.Average)&&hiddenAverage.Triangles.All(t=>t.Group!=DrawGroup.Average)&&
    hiddenAverage.Labels.Any(l=>l.Group==DrawGroup.Peak)&&hiddenAverage.Triangles.Any(t=>t.Group==DrawGroup.FirstNotes),
    "Hiding average removes its icon/text while preserving other metrics and notes");
var hiddenAll=new MenuVisibility {Average=false,Peak=false,Median=false,Squats=false,FirstNotes=false,Density=false,ImportedDeaths=false,OwnDeaths=false};
var emptyPanel=MenuDrawing.Panel(readyPresentation);hiddenAll.Apply(emptyPanel);
Check(!hiddenAll.HasStatistics&&!hiddenAll.HasTimeline&&emptyPanel.Labels.Count==0&&emptyPanel.Triangles.Count==0,
    "Disabling all statistics leaves no geometry, labels or row reservation");
var statisticOrder=new[]{DrawGroup.Average,DrawGroup.Peak,DrawGroup.Median,DrawGroup.Squats,DrawGroup.FirstNotes};
for(int mask=0;mask<32;mask++)
{
    var selectedVisibility=new MenuVisibility {Average=(mask&1)!=0,Peak=(mask&2)!=0,Median=(mask&4)!=0,Squats=(mask&8)!=0,FirstNotes=(mask&16)!=0};
    var columns=new StatisticsLayout(selectedVisibility,MenuDrawing.Panel(readyPresentation)).Columns;
    var expectedGroups=statisticOrder.Where(selectedVisibility.Includes).ToArray();
    bool valid=columns.Select(c=>c.Group).SequenceEqual(expectedGroups);
    if(columns.Count>0)
    {
        if(columns.Count==1)valid&=Math.Abs(columns[0].Left+columns[0].Right-100)<.001f;
        else valid&=Math.Abs(columns[0].Left)<.001f&&Math.Abs(columns[^1].Right-100)<.001f;
        for(int i=0;i<columns.Count;i++)valid&=columns[i].Right>columns[i].Left&&(i==0||columns[i].Left>=columns[i-1].Right-.001f);
        for(int i=2;i<columns.Count;i++)valid&=Math.Abs((columns[i].Left-columns[i-1].Right)-(columns[1].Left-columns[0].Right))<.001f;
    }
    Check(valid,"Visible metric content aligns to both edges with equal gaps, or centers a single metric: "+mask);
}
var asymmetric=new Drawing();
asymmetric.Labels.Add(new DrawLabel {Group=DrawGroup.Squats,X=70,Width=5});
var centered=new StatisticsLayout(new MenuVisibility(),asymmetric,new StatisticsBounds(DrawGroup.Squats,65,68));
Check(centered.Columns.Single().Left==45&&centered.Columns.Single().Right==55&&centered.Offset(DrawGroup.Squats)==-20,
    "Single metric centers its visible icon and text bounds rather than the original column");
var peakVisibility=new MenuVisibility {Average=false,Median=false,Squats=false,FirstNotes=false};
var peakDrawing=MenuDrawing.Panel(readyPresentation);peakVisibility.Apply(peakDrawing);
var peakLayout=new StatisticsLayout(peakVisibility,peakDrawing);
float oldPeakX=peakDrawing.Labels.Single().X,oldPeakWidth=peakDrawing.Labels.Single().Width;
float oldTriangleX=peakDrawing.Triangles[0].A.X;
peakLayout.Apply(peakDrawing);
Check(Math.Abs(peakLayout.Columns.Single().Left+peakLayout.Columns.Single().Right-100)<.001f&&
    Math.Abs(peakDrawing.Labels.Single().X-oldPeakX-peakLayout.Offset(DrawGroup.Peak))<.001f&&
    Math.Abs(peakDrawing.Triangles[0].A.X-oldTriangleX-peakLayout.Offset(DrawGroup.Peak))<.001f&&
    peakDrawing.Labels.Single().Width==oldPeakWidth,"A single metric centers; icon and value translate together without stretching");
var errorPresentation=SelectedMapPresentation.Create(null,leftColor,rightColor);
Check(new[]{errorPresentation.Average,errorPresentation.Peak,errorPresentation.Median,errorPresentation.Squats}.All(v=>v=="—")&&errorPresentation.Left==null&&errorPresentation.Right==null,"Unavailable/error presentation consists of dashes, not fake zeros or cubes");
var dot=FirstNoteVisual.From(new MapNote {Direction=8,CutDirectionAngleOffset=23},rightColor)!;
var arrow=FirstNoteVisual.From(new MapNote {Direction=4,CutDirectionAngleOffset=20},leftColor)!;
Check(dot.Direction==8&&dot.RotationZ==23&&arrow.RotationZ==-115,"Dot differs from arrow and nominal cube tilt includes angle offset");
Check(FirstNoteVisual.From(new MapNote {Direction=9},leftColor)==null&&FirstNoteVisual.From(null,leftColor)==null,"Invalid or absent first note renders a dash");
var unknown=FirstNoteVisual.From(new MapNote {Direction=6},leftColor)!;
Check(unknown.RotationZ==null&&unknown.Direction==6,"Unknown tilt preserves direction while requesting dotted background");
Check(Enumerable.Range(0,9).Select(direction=>FirstNoteVisual.From(new MapNote {Direction=direction,CutDirectionAngleOffset=0},leftColor)!.ShouldBlink)
    .SequenceEqual(new[]{true,false,true,true,true,true,false,false,false}),"Only side/up arrows blink; downward arrows and dots remain steady");
Check(FirstNoteVisual.From(new MapNote {Direction=1,CutDirectionAngleOffset=90},leftColor)!.ShouldBlink&&
    !FirstNoteVisual.From(new MapNote {Direction=0,CutDirectionAngleOffset=180},leftColor)!.ShouldBlink,
    "Blink follows displayed physical orientation, including angle offsets");
Check(FirstNoteVisual.From(new MapNote {Direction=2},leftColor)!.ShouldBlink&&!unknown.ShouldBlink,
    "Unknown physical tilt falls back to the declared direction");
var blinkDrawing=MenuDrawing.StatisticsRow(new MenuRenderData {
    Left=FirstNoteVisual.From(new MapNote {Direction=2,CutDirectionAngleOffset=0},leftColor),
    Right=FirstNoteVisual.From(new MapNote {Direction=8,CutDirectionAngleOffset=0},rightColor)});
Check(blinkDrawing.Triangles.Any(t=>t.Blink)&&blinkDrawing.Triangles.Where(t=>t.Blink).All(t=>t.A.X>70)&&
    blinkDrawing.Triangles.Where(t=>t.Color.B==rightColor.B&&t.Color.R==rightColor.R).All(t=>!t.Blink),
    "Blink tags stay on the affected note, leaving the other hand and statistics steady");
var knownPanel=MenuDrawing.Panel(new MenuRenderData {Left=FirstNoteVisual.From(new MapNote {Direction=6,CutDirectionAngleOffset=0},leftColor)});
var dottedPanel=MenuDrawing.Panel(new MenuRenderData {Left=unknown});
Check(dottedPanel.Triangles.Count>knownPanel.Triangles.Count,"Unknown tilt adds dotted face geometry");
Check(MenuDrawing.Panel(errorPresentation).Labels.All(t=>t.Text=="—"),"Rendered error panel has no headings or fake values");
Check(MenuDrawing.Graph(errorPresentation).Labels.Single().Text=="—","Unavailable graph renders a dash");
var graphFixture=new MenuRenderData {Duration=120,Colors=Enumerable.Range(0,90).Select(i=>CompatibleAnalysis.Color(i/6f)).ToArray(),Markers=[
    new(){Source="legacy",NormalizedPosition=.5,SongTimeSeconds=60},new(){Source="beatinsight",NormalizedPosition=.5,SongTimeSeconds=60},
    new(){Source="legacy",NormalizedPosition=null,SongTimeSeconds=null}]};
var graphDrawing=MenuDrawing.Graph(graphFixture);
var markersWithoutDensity=MenuDrawing.CompactGraph(new MenuRenderData {Duration=graphFixture.Duration,Markers=graphFixture.Markers},1);
Check(markersWithoutDensity.Triangles.Count(t=>t.Group==DrawGroup.ImportedDeaths||t.Group==DrawGroup.OwnDeaths)==8&&
    markersWithoutDensity.Triangles.Count(t=>t.Group==DrawGroup.HistoryGuide)==2&&markersWithoutDensity.Labels.Count==0,
    "History markers retain a neutral guide without calculating hidden density or showing an error dash");
var noDeaths=MenuDrawing.CompactGraph(new MenuRenderData {Duration=100},1);
new MenuVisibility {Density=false}.Apply(noDeaths);
Check(noDeaths.Triangles.Count==2&&noDeaths.Triangles.All(t=>t.Group==DrawGroup.HistoryGuide),
    "Empty history still provides a visible guide and hover target when density is disabled");
new MenuVisibility {Density=false,ImportedDeaths=false,OwnDeaths=false}.Apply(noDeaths);
Check(noDeaths.Triangles.Count==0,"Disabling both history sources removes their neutral guide");
var unavailableHistoryGuide=MenuDrawing.CompactGraph(new MenuRenderData(),1);
new MenuVisibility {Density=false,ImportedDeaths=false}.Apply(unavailableHistoryGuide);
Check(unavailableHistoryGuide.Triangles.Count==2&&unavailableHistoryGuide.Labels.Count==0,
    "Unavailable map data retains a history hover guide even with density disabled");
Check(readyPresentation.ImportedHistory?.Tooltip.Contains("note indices")==true&&readyPresentation.OwnHistory?.Tooltip.Contains("Completion-time estimates")==true,
    "Presentation carries source-specific availability and precision summaries");
var hiddenHistory=SelectedMapPresentation.Create(new SelectedMapAnalysis {Features=AnalysisFeatures.Average},leftColor,rightColor);
Check(hiddenHistory.ImportedHistory==null&&hiddenHistory.OwnHistory==null,"Hidden sources do not build history summaries");
var ownOnly=MenuDrawing.CompactGraph(graphFixture,1);
new MenuVisibility {Density=false,ImportedDeaths=false}.Apply(ownOnly);
Check(ownOnly.Triangles.Count==4&&ownOnly.Triangles.All(t=>t.Group==DrawGroup.OwnDeaths&&t.Color.R==1&&t.Color.G==0),
    "Density/imported marker toggles independently preserve red own markers");
var reverseSources=new MenuRenderData {Duration=graphFixture.Duration,Colors=graphFixture.Colors,Markers=graphFixture.Markers.Reverse().ToArray()};
Check(MenuDrawing.CompactGraph(reverseSources,1).Triangles.TakeLast(4).All(t=>t.Color.R==1&&t.Color.G==0&&t.Color.B==0),
    "Own red crosses draw last even if imported markers arrive after own records");
Check(graphDrawing.Triangles.Count==188&&graphDrawing.Labels.Count==5,"Graph draws 90 segments, two distinct source crosses and five time labels; invalid marker omitted");
Check(graphDrawing.Triangles.Any(t=>t.Color.R==1&&t.Color.G==1&&t.Color.B==0)&&graphDrawing.Triangles.Any(t=>t.Color.R==1&&t.Color.G==0&&t.Color.B==0),"History crosses keep exact yellow and red colors");
var compactGraph=MenuDrawing.CompactGraph(graphFixture,1);
var yellowCross=compactGraph.Triangles.Skip(180).Take(4).SelectMany(t=>new[]{t.A.Y,t.B.Y,t.C.Y}).ToArray();
var redCross=compactGraph.Triangles.Skip(184).SelectMany(t=>new[]{t.A.Y,t.B.Y,t.C.Y}).ToArray();
Check(compactGraph.Labels.Count==0&&Math.Abs((yellowCross.Min()+yellowCross.Max())/2-MenuDrawing.CompactMarkerCenter(1))<1e-5&&
    yellowCross.Max()<=.5f+1e-5&&yellowCross.SequenceEqual(redCross)&&
    compactGraph.Triangles.Skip(184).All(t=>t.Color.R==1&&t.Color.G==0),
    "Larger compact markers enter no more than half the band height, share an offset and draw red on top");
var crowded=new MenuRenderData {Duration=120,Colors=graphFixture.Colors,Markers=Enumerable.Range(0,10000).Select(i=>new DeathMarker {Source="beatinsight",NormalizedPosition=i/10000.0,SongTimeSeconds=i/10000.0*120}).ToArray()};
Check(MenuDrawing.Graph(crowded).Triangles.Count*3<65000&&crowded.Markers.Length==10000,"Crowded markers remain below Unity mesh limit without deleting history records");
var tiltedJson=JObject.Parse(File.ReadAllText(expert));tiltedJson["colorNotes"]![0]!["a"]=25;
File.WriteAllText(expert,tiltedJson.ToString());
Check(SelectedMapAnalyzer.Load(request).FirstLeftNote!.CutDirectionAngleOffset==25,"JSON angle offset reaches first-note presentation contract");
tiltedJson["colorNotes"]![0]!["customData"]=JObject.Parse("""{"localRotation":[0,0,12]}""");File.WriteAllText(expert,tiltedJson.ToString());
Check(SelectedMapAnalyzer.Load(request).FirstLeftNote!.CutDirectionAngleOffset==null,"Unsupported custom rotation is explicitly unknown instead of fabricated");
if(args.Length==2&&args[0]=="--preview")Preview.Write(args[1],graphFixture,leftColor,rightColor);
var jumpSnapshot=new JumpSettingsSnapshot(0,0,.5f);
var jumpEqual=new JumpSettingsSnapshot(0,0,.5f);
Check(jumpSnapshot.Equals(jumpEqual)&&jumpSnapshot.GetHashCode()==jumpEqual.GetHashCode(),"Equal jump snapshots retain value identity");
string JumpIdentity(AnalysisFeatures features,JumpSettingsSnapshot? settings)=>SelectedMapRequest.SelectionIdentity("custom_level_TEST",3,"Standard",features,settings);
foreach(var changed in new[]{new JumpSettingsSnapshot(1,0,.5f),new JumpSettingsSnapshot(0,1,.5f),new JumpSettingsSnapshot(0,0,.75f),new JumpSettingsSnapshot(0,MathF.BitIncrement(0f),.5f)})
{
    Check(!jumpSnapshot.Equals(changed)&&JumpIdentity(AnalysisFeatures.Squats,jumpSnapshot)!=JumpIdentity(AnalysisFeatures.Squats,changed),"Each jump input invalidates visible squat selection");
    Check(JumpIdentity(AnalysisFeatures.Average,jumpSnapshot)==JumpIdentity(AnalysisFeatures.Average,changed),"Hidden squats do not cause jump-settings reanalysis");
}
Check(JumpIdentity(AnalysisFeatures.Squats,null)!=JumpIdentity(AnalysisFeatures.Squats,jumpSnapshot),"Unknown jump policy is distinct from an explicit default");
var savedCulture=System.Globalization.CultureInfo.CurrentCulture;
string invariantJump=jumpSnapshot.CacheIdentity;
try{System.Globalization.CultureInfo.CurrentCulture=System.Globalization.CultureInfo.GetCultureInfo("fr-FR");Check(jumpSnapshot.CacheIdentity==invariantJump,"Jump selection identity is culture invariant");}
finally{System.Globalization.CultureInfo.CurrentCulture=savedCulture;}
foreach(var invalid in new[]{(Mode:2,Offset:0f,Fixed:.5f),(Mode:0,Offset:float.NaN,Fixed:.5f),(Mode:0,Offset:float.PositiveInfinity,Fixed:.5f),(Mode:1,Offset:0f,Fixed:0f),(Mode:1,Offset:0f,Fixed:float.NaN),(Mode:1,Offset:0f,Fixed:float.PositiveInfinity)})
{
    bool snapshotRejected=false;try{_ =new JumpSettingsSnapshot(invalid.Mode,invalid.Offset,invalid.Fixed);}catch(ArgumentOutOfRangeException){snapshotRejected=true;}
    Check(snapshotRejected,"Unknown/nonfinite jump settings cannot become a usable snapshot");
}
var jumpRequest=new SelectedMapRequest("custom_level_TEST","Standard","Expert",3,expert,features:AnalysisFeatures.Squats,jumpSettings:jumpSnapshot);
var jumpAnalysis=SelectedMapAnalyzer.Load(jumpRequest);
Check(ReferenceEquals(jumpRequest.JumpSettings,jumpSnapshot)&&ReferenceEquals(jumpAnalysis.JumpSettings,jumpSnapshot),"Primitive jump snapshot reaches worker result unchanged");
var hiddenJumpRequest=new SelectedMapRequest("custom_level_TEST","Standard","Expert",3,expert,features:AnalysisFeatures.Average,jumpSettings:jumpSnapshot);
Check(hiddenJumpRequest.JumpSettings==null&&SelectedMapAnalyzer.Load(hiddenJumpRequest).JumpSettings==null,"Hidden wall feature removes unused jump snapshot from worker");
var dynamicMovement=WallJumpMovement.Resolve(new JumpSettingsSnapshot(0,1,.5f),120,20,-1);
Check(dynamicMovement.NoteJumpSpeed==20&&dynamicMovement.HalfJumpSeconds==.5f&&dynamicMovement.JumpSeconds==1,"Movement resolver combines dynamic map/player offsets");
var staticMovement=WallJumpMovement.Resolve(new JumpSettingsSnapshot(1,2,.75f),120,20,-1);
Check(staticMovement.HalfJumpSeconds==.75f&&staticMovement.JumpSeconds==1.5f,"Static movement uses fixed half-jump seconds");
Check(WallJumpMovement.Resolve(new JumpSettingsSnapshot(1,2,.75f),120,20,-1,30,.25f).HalfJumpSeconds==.25f,"Object spawn offset replaces static seconds rather than adding beats");
foreach(string invalid in new[]{"unknown","bpm","map-speed","map-offset","object-speed","object-offset","static-offset","variable","overflow"})
{
    bool movementRejected=false;
    try{_ =WallJumpMovement.Resolve(invalid=="unknown"?null:new JumpSettingsSnapshot(invalid=="static-offset"?1:0,0,.5f),
        invalid=="bpm"?0:120,invalid=="map-speed"?0:20,invalid=="map-offset"?float.NaN:0,
        invalid=="object-speed"?-1:invalid=="overflow"?float.PositiveInfinity:(float?)null,
        invalid=="object-offset"?float.NaN:invalid=="static-offset"?-1:(float?)null,invalid=="variable");}
    catch(NotSupportedException){movementRejected=true;}
    Check(movementRejected,"Unknown wall movement cannot silently use defaults: "+invalid);
}
bool distanceRejected=false;
try{_ =WallJumpMovement.Resolve(new JumpSettingsSnapshot(1,0,float.MaxValue/4),120,20,0);}catch(NotSupportedException){distanceRejected=true;}
Check(distanceRejected,"Finite inputs cannot silently overflow physical jump distance");
var contactMovement=WallJumpMovement.Resolve(new JumpSettingsSnapshot(1,0,.5f),120,20,0);
var centerContact=WallContactInterval.AtPointPlane(contactMovement,10,0,0,40);
Check(centerContact.Start==10&&centerContact.End==12,"Canonical wall Z interval matches nominal front and wall duration");
var forwardContact=WallContactInterval.AtPointPlane(contactMovement,10,.4f,0,40);
Check(Math.Abs(forwardContact.Start-9.98f)<1e-6&&Math.Abs(forwardContact.End-11.98f)<1e-6,"Explicit forward head plane shifts both contact boundaries earlier");
var shiftedContact=WallContactInterval.AtPointPlane(contactMovement,10,0,.2f,40.2f);
Check(Math.Abs(shiftedContact.Start-10.01f)<1e-6&&Math.Abs(shiftedContact.End-12.01f)<1e-6,"Explicit local Z slab shift affects contact window");
foreach(string invalid in new[]{"movement","time","head","min","max","reversed","overflow"})
{
    bool contactRejected=false;
    try{_ =WallContactInterval.AtPointPlane(invalid=="movement"?null:contactMovement,invalid=="time"?float.NaN:10,
        invalid=="head"?float.PositiveInfinity:invalid=="overflow"?-float.MaxValue:0,
        invalid=="min"?float.NaN:invalid=="reversed"?2:invalid=="overflow"?float.MaxValue:0,
        invalid=="max"?float.PositiveInfinity:invalid=="reversed"?1:invalid=="overflow"?float.MaxValue:40);}
    catch(NotSupportedException){contactRejected=true;}
    Check(contactRejected,"Unknown/invalid wall contact geometry stays unavailable: "+invalid);
}
string wallEntryFolder=Path.Combine(scratch,"wall-entry");Directory.CreateDirectory(wallEntryFolder);
File.WriteAllText(Path.Combine(wallEntryFolder,"Info.dat"),"""{"_version":"2.1.0","_beatsPerMinute":120,"audio":{"songDuration":30},"_difficultyBeatmapSets":[{"_beatmapCharacteristicName":"Standard","_difficultyBeatmaps":[{"_difficulty":"Expert","_beatmapFilename":"map.dat","_noteJumpMovementSpeed":20,"_noteJumpStartBeatOffset":0,"_customData":{"_requirements":["Noodle Extensions"]}}]}]}""");
string wallEntryFile=Path.Combine(wallEntryFolder,"map.dat");
var wallEntryMap=JObject.Parse("""{"version":"3.3.0","colorNotes":[{"b":2,"x":0,"y":0,"c":0,"d":1},{"b":4,"x":1,"y":0,"c":1,"d":1}],"obstacles":[{"b":6,"d":0.25,"x":1,"y":2,"w":2,"h":3,"customData":{"animation":{"offsetPosition":[[-4,0,0,0.125],[0,0,0,0.375,"easeOutSine"]]}}}]}""");
var knownMovement=new JumpSettingsSnapshot(0,0,.5f,true);
SelectedMapAnalysis EntryRead(JObject root,JumpSettingsSnapshot? settings)
{
    File.WriteAllText(wallEntryFile,root.ToString());
    return SelectedMapAnalyzer.Load(new SelectedMapRequest("wall-entry","Standard","Expert",3,wallEntryFile,features:new MenuVisibility().Features,jumpSettings:settings));
}
var entryAccepted=EntryRead(wallEntryMap,knownMovement);
Check(entryAccepted.SquatCount==1&&entryAccepted.SquatError==null,"XY wall entry completed before nominal Z-contact yields squat count");
var entryLoaded=LocalMap.Load(LocalMap.CaseFromFile(wallEntryFile,"Standard","Expert"),new MenuVisibility().Features,jumpSettings:knownMovement);
Check(entryLoaded.Json==wallEntryMap.ToString()&&entryLoaded.HasNoodleGeometry&&JObject.Parse(entryLoaded.SquatJson!)["obstacles"]![0]!["customData"]!["animation"]!["offsetPosition"]==null,"Wall projection preserves original JSON and isolates validated squat input");
Check(EntryRead(wallEntryMap,null).SquatCount==null&&EntryRead(wallEntryMap,new JumpSettingsSnapshot(0,0,.5f)).SquatCount==null,"Unknown movement never assumes normal/default wall timing");
Check(EntryRead(wallEntryMap,new JumpSettingsSnapshot(1,0,.1f,true)).SquatCount==null,"Fixed jump setting can make the same wall entry overlap contact");
Check(SelectedMapRequest.SelectionIdentity("wall-entry",3,"Standard",AnalysisFeatures.Squats,knownMovement)!=SelectedMapRequest.SelectionIdentity("wall-entry",3,"Standard",AnalysisFeatures.Squats,new JumpSettingsSnapshot(0,0,.5f)),"Song movement gate participates in selection identity");
foreach(string invalid in new[]{"long","z-motion","late","rotation","scale","track","time","length","variable","tempo","environment","object-speed","object-offset"})
{
    var changed=(JObject)wallEntryMap.DeepClone();var wall=changed["obstacles"]![0]!;var custom=wall["customData"]!;var animation=custom["animation"]!;
    if(invalid=="long")wall["d"]=10;
    if(invalid=="z-motion")animation["offsetPosition"]![0]![2]=1;
    if(invalid=="late")animation["offsetPosition"]![1]![3]=.75;
    if(invalid=="rotation")custom["localRotation"]=new JArray(0,0,30);
    if(invalid=="scale")animation["scale"]=new JArray(1,1,1);
    if(invalid=="track")custom["track"]="fixture";
    if(invalid=="time")animation["time"]=new JArray(0);
    if(invalid=="length")custom["size"]=new JArray(2,3,1);
    if(invalid=="variable")changed["njsEvents"]=new JArray(new JObject());
    if(invalid=="tempo")changed["bpmEvents"]=new JArray(new JObject {["b"]=1,["m"]=180});
    if(invalid=="environment")changed["customData"]=new JObject {["environment"]=new JArray(new JObject())};
    if(invalid=="object-speed")custom["noteJumpMovementSpeed"]=80;
    if(invalid=="object-offset")custom["noteJumpStartBeatOffset"]=-1;
    var rejectedEntry=EntryRead(changed,knownMovement);
    Check(rejectedEntry.SquatCount==null&&rejectedEntry.SquatError!=null&&rejectedEntry.Corrected.LeftAverage==entryAccepted.Corrected.LeftAverage,"Wall entry rejects unproven contact while preserving notes: "+invalid);
}
Check(WallJumpMovement.FromMeasured(20,.5f,1).HalfJumpSeconds==.5f,"Measured runtime provider uses actual half-jump duration");
foreach(var value in new[]{(Speed:0f,Half:.5f,Jump:1f),(Speed:20f,Half:0f,Jump:1f),(Speed:20f,Half:.5f,Jump:2f),(Speed:20f,Half:.5f,Jump:float.NaN),(Speed:float.PositiveInfinity,Half:.5f,Jump:1f)})
{
    bool rejectedMovement=false;try{_ =WallJumpMovement.FromMeasured(value.Speed,value.Half,value.Jump);}catch(NotSupportedException){rejectedMovement=true;}
    Check(rejectedMovement,"Unusable measured runtime movement cannot authorize wall entry");
}
Console.WriteLine($"Passed {checks} menu selection/rendering checks; no VR/game process or user history used.");

sealed class ManualDispatcher
{
    readonly ConcurrentQueue<Action> queue=new();
    readonly SemaphoreSlim signal=new(0);
    public Task Dispatch(Action action)
    {
        var done=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        queue.Enqueue(()=>{try{action();done.SetResult();}catch(Exception e){done.SetException(e);}});signal.Release();return done.Task;
    }
    public async Task<Action> Take()
    {
        if(!await signal.WaitAsync(TimeSpan.FromSeconds(10)))throw new TimeoutException("Expected a dispatched callback");
        if(!queue.TryDequeue(out var action))throw new Exception("Dispatcher queue is empty");return action;
    }
}
