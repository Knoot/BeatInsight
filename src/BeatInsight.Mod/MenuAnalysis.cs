using System.Linq;
using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using BeatInsight.Core;
using BeatInsight.Formats;
using HarmonyLib;
using IPA.Utilities.Async;

namespace BeatInsight.Mod;

public sealed class MenuSelection
{
    public string LevelId {get;}
    public string Characteristic {get;}
    public string Difficulty {get;}
    public int DifficultyRank {get;}
    public AnalysisFeatures Features {get;}
    public JumpSettingsSnapshot? JumpSettings {get;}
    public bool PreserveExtendedWallLayers {get;}
    internal BeatmapLevelsModel Model {get;}
    internal BeatmapKey Key {get;}
    internal MenuSelection(BeatmapLevelsModel model,BeatmapKey key,AnalysisFeatures features,JumpSettingsSnapshot? jumpSettings,bool preserveExtendedWallLayers)
    {
        PreserveExtendedWallLayers=preserveExtendedWallLayers;
        Model=model;Key=key;Features=features;JumpSettings=jumpSettings;LevelId=key.levelId;Characteristic=key.beatmapCharacteristic.serializedName;
        Difficulty=key.difficulty.ToString();DifficultyRank=(int)key.difficulty;
    }
}

// UI-facing data contract. Changed is dispatched to the Unity main thread.
// No UI rendering or gameplay is required to test the underlying coordinator/analyzer.
public static class MenuAnalysis
{
    static SelectionCoordinator<MenuSelection,SelectedMapAnalysis>? requests;
    static StandardLevelDetailViewController? active;
    static BeatmapLevelsModel? model;
    static PlayerDataModel? playerModel;
    static string? submittedKey;
    static readonly SelectionState<MenuSelection,SelectedMapAnalysis> idle=new SelectionState<MenuSelection,SelectedMapAnalysis>(0,SelectionStatus.Idle);
    public static SelectionState<MenuSelection,SelectedMapAnalysis> Current=>requests?.Current??idle;
    public static event Action<SelectionState<MenuSelection,SelectedMapAnalysis>>? Changed;

    internal static void Enable()
    {
        if(requests!=null)return;
        RuntimeCapture.HistoryWritten+=HistoryWritten;
        requests=new SelectionCoordinator<MenuSelection,SelectedMapAnalysis>(LoadAsync,
            action=>UnityMainThreadTaskScheduler.Factory.StartNew(action));
        requests.Changed+=state=> {
            Plugin.Log.Debug("Selection analysis: "+state.Status+(state.Message==null?"":"; "+state.Message));
            if(RuntimeDiagnostics.CanRecord)
            {
                var r=state.Result;
                RuntimeDiagnostics.Record("menu.analysis",new{state.Revision,status=state.Status.ToString(),state.Message,
                    map=r?.MapKey,hash=r?.BeatmapSha256,features=r?.Features.ToString(),squats=r?.SquatCount,squatError=r?.SquatError,noteError=r?.NoteError,
                    independentDensity=r?.IndependentDensity,densityError=r?.DensityError,
                    ownHistory=r?.OwnHistoryStatus,legacyHistory=r?.LegacyHistoryStatus,
                    markers=r?.DeathMarkers.TakeLast(12).Select(m=>new{m.Source,m.SongTimeSeconds,m.ColorHex,m.PositionStatus}).ToArray()});
            }
            Notify(state);
        };
    }

    internal static void Activate(StandardLevelDetailViewController controller,BeatmapLevelsModel loader)
    {
        Unsubscribe();Observe(requests?.ClearAsync()??Task.CompletedTask);
        active=controller;model=loader;submittedKey=null;
        playerModel=(PlayerDataModel)AccessTools.Field(typeof(StandardLevelDetailViewController),"_playerDataModel").GetValue(controller);
        controller.didChangeDifficultyBeatmapEvent+=DifficultyChanged;
        controller.didChangeContentEvent+=ContentChanged;
        Submit(controller);
    }

    internal static void Deactivate(StandardLevelDetailViewController controller)
    {
        if(!ReferenceEquals(active,controller))return;
        Unsubscribe();Observe(requests?.ClearAsync()??Task.CompletedTask);
    }

    internal static void Clear(StandardLevelDetailViewController controller)
    {
        if(!ReferenceEquals(active,controller))return;
        submittedKey=null;Observe(requests?.ClearAsync()??Task.CompletedTask);
    }

    static void Unsubscribe()
    {
        if(!ReferenceEquals(active,null))
        {
            active!.didChangeDifficultyBeatmapEvent-=DifficultyChanged;
            active.didChangeContentEvent-=ContentChanged;
        }
        active=null;model=null;playerModel=null;submittedKey=null;
    }

    static void DifficultyChanged(StandardLevelDetailViewController controller)
    {try {Submit(controller);}catch(Exception e){Unavailable(e.Message);}}

    static void ContentChanged(StandardLevelDetailViewController controller,StandardLevelDetailViewController.ContentType content)
    {
        if(!ReferenceEquals(active,controller))return;
        if(content==StandardLevelDetailViewController.ContentType.OwnedAndReady)DifficultyChanged(controller);
        else Unavailable("Game content: "+content);
    }

    static void Unavailable(string message)
    {
        submittedKey=null;Observe(requests?.UnavailableAsync(message)??Task.CompletedTask);
    }

    static void Submit(StandardLevelDetailViewController controller)
    {
        if(requests==null||!ReferenceEquals(active,controller))return;
        var features=Plugin.Config.Visibility().Features;
        if(features==AnalysisFeatures.None)
        {
            if(submittedKey!=null||requests.Current.Status!=SelectionStatus.Idle)Observe(requests.ClearAsync());
            submittedKey=null;return;
        }
        bool ready=(bool)AccessTools.Field(typeof(StandardLevelDetailViewController),"_contentIsOwnedAndReady").GetValue(controller);
        if(!ready){Unavailable("Game content is not ready");return;}
        var key=controller.beatmapKey;
        if(controller.beatmapLevel==null||!key.IsValid()||model==null){Clear(controller);return;}
        var jump=SnapshotJumpSettings(features);
        bool preserve=false;
        if((features&AnalysisFeatures.Squats)!=0)
        {
            var converter=AccessTools.Method(typeof(BeatmapTypeConverters),"ConvertNoteLineLayer",new[]{typeof(int)});
            preserve=Harmony.GetPatchInfo(converter)?.Postfixes.Any(p=>p.PatchMethod.DeclaringType?.FullName=="MappingExtensions.HarmonyPatches.BeatmapTypeConvertersConvertNoteLineLayerPatch")==true;
        }
        string identity=SelectedMapRequest.SelectionIdentity(key.levelId,(int)key.difficulty,key.beatmapCharacteristic.serializedName,features,jump,preserve);
        if(submittedKey==identity&&(requests.Current.Status==SelectionStatus.Loading||requests.Current.Status==SelectionStatus.Ready))return;
        submittedKey=identity;Observe(requests.SelectAsync(new MenuSelection(model,key,features,jump,preserve)));
    }

    static JumpSettingsSnapshot? SnapshotJumpSettings(AnalysisFeatures features)
    {
        if((features&AnalysisFeatures.Squats)==0)return null;
        var settings=playerModel?.playerData?.playerSpecificSettings;
        if(settings==null)return null;
        try{return new JumpSettingsSnapshot((int)settings.noteJumpDurationTypeSettings,settings.noteJumpStartBeatOffset,settings.noteJumpFixedDuration,
            playerModel!.playerData.gameplayModifiers!=null&&(int)playerModel.playerData.gameplayModifiers.songSpeed==0&&!playerModel.playerData.gameplayModifiers.fastNotes);}
        catch(ArgumentOutOfRangeException){return null;}
    }

    internal static void PlayerSettingsChanged(PlayerData player)
    {
        if(active==null)return;
        Observe(UnityMainThreadTaskScheduler.Factory.StartNew(()=> {
            if(active==null||!ReferenceEquals(playerModel?.playerData,player))return;
            try {Submit(active);}catch(Exception e){Unavailable(e.Message);}
        }));
    }

    internal static void SettingsChanged()
    {
        if(active!=null&&requests!=null&&requests.Current.Request?.Features!=Plugin.Config.Visibility().Features)
            DifficultyChanged(active);
    }

    static async Task<SelectedMapAnalysis> LoadAsync(MenuSelection selection,CancellationToken token)
    {
        var result=await UnityMainThreadTaskScheduler.Factory.StartNew(()=> {
            token.ThrowIfCancellationRequested();
            return selection.Model.LoadBeatmapLevelDataAsync(selection.LevelId,BeatmapLevelDataVersion.Original,token);
        }).Unwrap().ConfigureAwait(false);
        token.ThrowIfCancellationRequested();
        var snapshot=await UnityMainThreadTaskScheduler.Factory.StartNew(()=> {
            token.ThrowIfCancellationRequested();
            if(result.isError||result.beatmapLevelData==null)throw new IOException("Game loader could not load the selected level");
            string userData=IPA.Utilities.UnityGame.UserDataPath;
            RuntimeDiagnostics.Record("menu.source",new{provider=result.beatmapLevelData.GetType().FullName,selection.LevelId,
                selection.Characteristic,selection.Difficulty,features=selection.Features.ToString()});
            if(result.beatmapLevelData is BeatmapLevelDataSO bundled&&!selection.LevelId.StartsWith("custom_level_",StringComparison.OrdinalIgnoreCase))
            {
                var level=selection.Model.GetBeatmapLevel(selection.LevelId)??throw new FileNotFoundException("Selected level metadata is unavailable");
                if(level.songTimeOffset!=0)throw new NotSupportedException("Bundled map audio offsets have not been validated");
                var key=selection.Key;
                string json=result.beatmapLevelData.GetBeatmapString(in key)??throw new FileNotFoundException("Selected bundled beatmap data is unavailable");
                string? audio=result.beatmapLevelData.GetAudioDataString();
                bool needDuration=(selection.Features&(AnalysisFeatures.Density|AnalysisFeatures.ImportedDeaths|AnalysisFeatures.OwnDeaths|AnalysisFeatures.Reference))!=0;
                float duration=needDuration?(bundled.songAudioClip!=null?bundled.songAudioClip.length:level.songDuration):0;
                var documents=MapDocuments.Bundled(json,audio,level.beatsPerMinute,duration,selection.Characteristic,selection.Difficulty);
                RuntimeDiagnostics.Record("menu.documents",new{source="bundle",selection.LevelId,
                    selection.Characteristic,selection.Difficulty,characters=json.Length,audioCharacters=audio?.Length,bpm=level.beatsPerMinute,duration});
                return new SelectedMapRequest(selection.LevelId,selection.Characteristic,selection.Difficulty,selection.DifficultyRank,"",
                    Path.Combine(userData,"SongPlayData.json"),Path.Combine(userData,DeathHistoryStore.FileName),features:selection.Features,
                    jumpSettings:selection.JumpSettings,preserveExtendedWallLayers:selection.PreserveExtendedWallLayers,documents:documents);
            }
            if(!(result.beatmapLevelData is FileSystemBeatmapLevelData files))
                throw new NotSupportedException("This beatmap data provider is not supported by the menu analyzer");
            var entries=(Dictionary<(BeatmapCharacteristicSO,BeatmapDifficulty),FileDifficultyBeatmap>)
                AccessTools.Field(typeof(FileSystemBeatmapLevelData),"_difficultyBeatmaps").GetValue(files);
            if(!entries.TryGetValue((selection.Key.beatmapCharacteristic,selection.Key.difficulty),out var entry))
                throw new FileNotFoundException("Selected difficulty is missing from the loaded level");
            string file=(string)AccessTools.Field(typeof(FileDifficultyBeatmap),"_beatmapPath").GetValue(entry);
            return new SelectedMapRequest(selection.LevelId,selection.Characteristic,selection.Difficulty,selection.DifficultyRank,file,
                Path.Combine(userData,"SongPlayData.json"),Path.Combine(userData,DeathHistoryStore.FileName),features:selection.Features,jumpSettings:selection.JumpSettings,preserveExtendedWallLayers:selection.PreserveExtendedWallLayers);
        }).ConfigureAwait(false);
        token.ThrowIfCancellationRequested();
        // Returning to the menu can race with our background death write. Await
        // the already queued write before opening history; never block Unity.
        if((selection.Features&AnalysisFeatures.OwnDeaths)!=0)await RuntimeCapture.PendingHistoryWrites().ConfigureAwait(false);
        token.ThrowIfCancellationRequested();
        return await SelectedMapAnalyzer.LoadAsync(snapshot,token).ConfigureAwait(false);
    }

    static async void Observe(Task task)
    {
        try {await task.ConfigureAwait(false);}
        catch(Exception e){Plugin.Log.Error("Selection request failed: "+e);}
    }

    static void HistoryWritten()
    {
        Observe(UnityMainThreadTaskScheduler.Factory.StartNew(()=> {
            if(active==null||requests==null||(Plugin.Config.Visibility().Features&AnalysisFeatures.OwnDeaths)==0)return;
            submittedKey=null;Submit(active);
        }));
    }

    static void Notify(SelectionState<MenuSelection,SelectedMapAnalysis> state)
    {
        if(Changed==null)return;
        foreach(Action<SelectionState<MenuSelection,SelectedMapAnalysis>> observer in Changed.GetInvocationList())
        {
            try {observer(state);}
            catch(Exception e){Plugin.Log.Error("Selection UI observer failed: "+e);}
        }
    }

    internal static void Disable()
    {
        RuntimeCapture.HistoryWritten-=HistoryWritten;
        Unsubscribe();requests?.Dispose();requests=null;
        Notify(idle);Changed=null;
    }
}
