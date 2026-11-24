using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using BeatInsight.Core;
using Newtonsoft.Json;
using BeatInsight.WallAlert;

namespace BeatInsight.Formats;

// Primitive snapshot only: no Unity objects cross into the worker.
public sealed class SelectedMapRequest
{
    public string LevelId {get;}
    public string Characteristic {get;}
    public string Difficulty {get;}
    public int DifficultyRank {get;}
    public string BeatmapFile {get;}
    public string? LegacyHistoryFile {get;}
    public string? OwnHistoryFile {get;}
    public string? UnavailableReason {get;}
    public AnalysisFeatures Features {get;}
    public JumpSettingsSnapshot? JumpSettings {get;}
    public bool PreserveExtendedWallLayers {get;}
    public SelectedMapRequest(string levelId,string characteristic,string difficulty,int difficultyRank,string beatmapFile,
        string? legacyHistoryFile=null,string? ownHistoryFile=null,string? unavailableReason=null,AnalysisFeatures features=AnalysisFeatures.All,JumpSettingsSnapshot? jumpSettings=null,bool preserveExtendedWallLayers=false)
    {
        LevelId=levelId;Characteristic=characteristic;Difficulty=difficulty;DifficultyRank=difficultyRank;BeatmapFile=beatmapFile;
        LegacyHistoryFile=legacyHistoryFile;OwnHistoryFile=ownHistoryFile;UnavailableReason=unavailableReason;
        PreserveExtendedWallLayers=(features&AnalysisFeatures.Squats)!=0&&preserveExtendedWallLayers;
        Features=features;JumpSettings=(features&AnalysisFeatures.Squats)!=0?jumpSettings:null;
    }
    public static string SelectionIdentity(string levelId,int rank,string characteristic,AnalysisFeatures features,JumpSettingsSnapshot? jumpSettings,bool preserveExtendedWallLayers=false)
        =>PlayHistory.Key(levelId,rank,characteristic)+"|"+(int)features+
            ((features&AnalysisFeatures.Squats)!=0?"|jump:"+(jumpSettings?.CacheIdentity??"unknown")+(preserveExtendedWallLayers?"|extended-wall-layers":""):"");
}

public sealed class SelectedMapAnalysis
{
    public JumpSettingsSnapshot? JumpSettings {get;set;}
    public AnalysisFeatures Features {get;set;}=AnalysisFeatures.All;
    public string MapKey {get;set;}="";
    public string BeatmapSha256 {get;set;}="";
    public string MapVersion {get;set;}="";
    public float Duration {get;set;}
    public AnalysisResult Compatible {get;set;}=new AnalysisResult();
    public AnalysisResult Corrected {get;set;}=new AnalysisResult();
    public double MedianLeft {get;set;}
    public double MedianRight {get;set;}
    public string MedianPolicy=>WindowStatistics.ActiveHandMedianPolicy;
    public string MedianUnit=>"swings/s";
    public string GraphUnit=>"prepared-objects/s";
    public bool HasLeftNote {get;set;}
    public bool HasRightNote {get;set;}
    public MapNote? FirstLeftNote {get;set;}
    public MapNote? FirstRightNote {get;set;}
    public int? SquatCount {get;set;}
    public string? SquatError {get;set;}
    public string? NoteError {get;set;}
    public bool IndependentDensity {get;set;}
    public string? DensityError {get;set;}
    public string LegacyHistoryStatus {get;set;}="not-requested";
    public string OwnHistoryStatus {get;set;}="not-requested";
    public DeathMarker[] DeathMarkers {get;set;}=Array.Empty<DeathMarker>();
}

public static class SelectedMapAnalyzer
{
    public static Task<SelectedMapAnalysis> LoadAsync(SelectedMapRequest request,CancellationToken token)
        =>Task.Run(()=>Load(request,token),token);

    public static SelectedMapAnalysis Load(SelectedMapRequest request,CancellationToken token=default)
    {
        token.ThrowIfCancellationRequested();
        if(request.UnavailableReason!=null)throw new NotSupportedException(request.UnavailableReason);
        if(string.IsNullOrWhiteSpace(request.LevelId)||string.IsNullOrWhiteSpace(request.Characteristic)||request.DifficultyRank<0||request.DifficultyRank>4)
            throw new ArgumentException("Exact levelId, characteristic and difficulty are required");
        string[] difficulties={"Easy","Normal","Hard","Expert","ExpertPlus"};
        if(difficulties[request.DifficultyRank]!=request.Difficulty)throw new ArgumentException("Difficulty name and rank disagree");
        var features=request.Features;
        bool Has(AnalysisFeatures feature)=>(features&feature)!=0;
        string key=PlayHistory.Key(request.LevelId,request.DifficultyRank,request.Characteristic);
        if(features==AnalysisFeatures.None)return new SelectedMapAnalysis {MapKey=key,Features=features};
        var selected=LocalMap.CaseFromFile(request.BeatmapFile,request.Characteristic,request.Difficulty);
        LoadedMap map;string? wallFailure=null,noteFailure=null,densityFailure=null;
        try{map=LocalMap.Load(selected,features,jumpSettings:request.JumpSettings);}
        catch(NotSupportedException e) when(!Has(AnalysisFeatures.Reference))
        {
            token.ThrowIfCancellationRequested();
            // Walls can only append zero swing bins. Positive-bin averages,
            // peaks, active medians and note directions do not depend on that tail.
            // A second validated load still rejects unsupported note/player data.
            try
            {
                map=LocalMap.Load(selected,features&~AnalysisFeatures.Squats,includeSwingWindowWalls:false,jumpSettings:request.JumpSettings);
                if(Has(AnalysisFeatures.Squats))wallFailure=e.Message;
            }
            catch(NotSupportedException notesError) when(Has(AnalysisFeatures.Squats)||Has(AnalysisFeatures.Density))
            {
                token.ThrowIfCancellationRequested();
                // Keep a separately validated wall result; never project unsupported notes.
                var wallFeatures=(features&AnalysisFeatures.Squats)|((features&(AnalysisFeatures.OwnDeaths|AnalysisFeatures.Density))!=0?AnalysisFeatures.OwnDeaths:AnalysisFeatures.None);
                try{map=LocalMap.Load(selected,wallFeatures,jumpSettings:request.JumpSettings);}
                catch(NotSupportedException wallError) when(Has(AnalysisFeatures.Density))
                {
                    wallFailure=Has(AnalysisFeatures.Squats)?wallError.Message:null;
                    map=LocalMap.Load(selected,AnalysisFeatures.OwnDeaths,jumpSettings:request.JumpSettings);
                }
                noteFailure=notesError.Message;
            }
        }
        token.ThrowIfCancellationRequested();
        string legacyStatus="not-requested";
        var records=Has(AnalysisFeatures.ImportedDeaths)&&noteFailure==null&&!(map.HasNoodleGeometry||map.HasNoodleChains)?ReadLegacy(request.LegacyHistoryFile,key,out legacyStatus):new List<PlayRecord>();
        if(Has(AnalysisFeatures.ImportedDeaths)&&(map.HasNoodleGeometry||map.HasNoodleChains))legacyStatus="extension-positions-unverified";
        if(Has(AnalysisFeatures.ImportedDeaths)&&noteFailure!=null)legacyStatus="note-analysis-unavailable";
        var own=!Has(AnalysisFeatures.OwnDeaths)||request.OwnHistoryFile==null?new DeathHistoryReadResult {Status="not-requested"}:DeathHistoryStore.Read(request.OwnHistoryFile);
        token.ThrowIfCancellationRequested();
        var compatible=Has(AnalysisFeatures.Reference)?CompatibleAnalysis.Analyze(map.Input,history:records):new AnalysisResult();
        // Legacy positions must stay on the compatible sequence. The UI gets
        // explicit DeathMarkers, never legacy indices projected onto corrected notes.
        var corrected=noteFailure==null&&(features&(AnalysisFeatures.Average|AnalysisFeatures.Peak|AnalysisFeatures.Median|AnalysisFeatures.FirstNotes|AnalysisFeatures.Density))!=0
            ?CompatibleAnalysis.Analyze(map.Input,reproduceKnownDefects:false,features:features&~AnalysisFeatures.ImportedDeaths):new AnalysisResult();
        bool independentDensity=false;
        if(noteFailure!=null&&Has(AnalysisFeatures.Density))
        {
            try
            {
                var density=LocalMap.Load(selected,AnalysisFeatures.Density,includeSwingWindowWalls:false);
                corrected=CompatibleAnalysis.Analyze(density.Input,reproduceKnownDefects:false,features:AnalysisFeatures.Density);
                independentDensity=true;
            }
            catch(NotSupportedException e){densityFailure=e.Message;}
        }
        string hash="";
        if(Has(AnalysisFeatures.OwnDeaths))
        {
            hash=BeatmapFingerprint.Compute(map.Json);
        }
        var markers=(Has(AnalysisFeatures.ImportedDeaths)&&records.Count>0?DeathHistory.LegacyMarkers(records,map.Input):Array.Empty<DeathMarker>())
            .Concat(Has(AnalysisFeatures.OwnDeaths)?DeathHistory.OwnMarkers(own.Records,key,map.Input.Duration,hash):Array.Empty<DeathMarker>()).ToArray();
        int? squats=null;string? squatError=wallFailure;
        if(Has(AnalysisFeatures.Squats)&&wallFailure==null)try {squats=WallAlertAdapter.Count(map.SquatJson??map.Json,map.Bpm,map.MappingExtensions,map.NoodleExtensions,request.PreserveExtendedWallLayers);}catch(Exception e){squatError=e.Message;}
        if(noteFailure!=null&&!squats.HasValue&&!independentDensity)throw new NotSupportedException("Note analysis unavailable: "+noteFailure+"; squat analysis unavailable: "+squatError+"; density unavailable: "+densityFailure);
        token.ThrowIfCancellationRequested();
        return new SelectedMapAnalysis {JumpSettings=request.JumpSettings,Features=features,MapKey=key,BeatmapSha256=hash,MapVersion=map.Version,Duration=map.Input.Duration,
            Compatible=compatible,Corrected=corrected,MedianLeft=Has(AnalysisFeatures.Median)?WindowStatistics.ActiveWindowMedian(corrected.LeftBins):0,
            MedianRight=Has(AnalysisFeatures.Median)?WindowStatistics.ActiveWindowMedian(corrected.RightBins):0,HasLeftNote=Has(AnalysisFeatures.FirstNotes)&&map.Input.Notes.Any(n=>n.Color==0&&!n.IsFake),
            HasRightNote=Has(AnalysisFeatures.FirstNotes)&&map.Input.Notes.Any(n=>n.Color==1&&!n.IsFake),FirstLeftNote=Has(AnalysisFeatures.FirstNotes)?map.Input.Notes.FirstOrDefault(n=>n.Color==0&&!n.IsFake):null,FirstRightNote=Has(AnalysisFeatures.FirstNotes)?map.Input.Notes.FirstOrDefault(n=>n.Color==1&&!n.IsFake):null,
            SquatCount=squats,SquatError=squatError,NoteError=noteFailure,IndependentDensity=independentDensity,DensityError=densityFailure,LegacyHistoryStatus=legacyStatus,OwnHistoryStatus=own.Status,DeathMarkers=markers};
    }

    static List<PlayRecord> ReadLegacy(string? file,string key,out string status)
    {
        var empty=new List<PlayRecord>();
        if(file==null){status="not-requested";return empty;}
        if(!System.IO.File.Exists(file)){status="source-missing";return empty;}
        try
        {
            var root=JsonConvert.DeserializeObject<Dictionary<string,List<PlayRecord>>>(System.IO.File.ReadAllText(file));
            if(root==null){status="invalid-data";return empty;}
            if(!root.TryGetValue(key,out var records)){status="key-absent";return empty;}
            if(records==null||records.Any(r=>r==null)){status="invalid-data";return empty;}
            status="available";return records.Where(r=>r.IsPractice!=true).ToList();
        }
        catch(JsonException){status="invalid-data";return empty;}
        catch(IOException){status="source-unreadable";return empty;}
        catch(UnauthorizedAccessException){status="source-unreadable";return empty;}
    }
}
