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

string workspace=Path.GetFullPath(Path.Combine(AppContext.BaseDirectory,"../../../../../"));
string scratch=Path.Combine(workspace,".local","menu-checks",Guid.NewGuid().ToString("N"));
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
    var row=(JObject)info["difficultyBeatmaps"]![0]!;row["customData"]=JObject.Parse("""{"requirements":["Noodle Extensions"]}""");
    File.WriteAllText(Path.Combine(scratch,"Info.dat"),info.ToString());await coordinator.SelectAsync(request);
    Check(coordinator.Current.Status==SelectionStatus.Unsupported&&coordinator.Current.Result==null,"Info requirements reject an unsupported map before UI publication");
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
DrawColor leftColor=new DrawColor(.8f,.1f,.2f),rightColor=new DrawColor(.1f,.6f,1);
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
