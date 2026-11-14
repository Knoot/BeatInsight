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
    internal BeatmapLevelsModel Model {get;}
    internal BeatmapKey Key {get;}
    internal MenuSelection(BeatmapLevelsModel model,BeatmapKey key,AnalysisFeatures features)
    {
        Model=model;Key=key;Features=features;LevelId=key.levelId;Characteristic=key.beatmapCharacteristic.serializedName;
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
            Notify(state);
        };
    }

    internal static void Activate(StandardLevelDetailViewController controller,BeatmapLevelsModel loader)
    {
        Unsubscribe();Observe(requests?.ClearAsync()??Task.CompletedTask);
        active=controller;model=loader;submittedKey=null;
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
        active=null;model=null;submittedKey=null;
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
        string identity=PlayHistory.Key(key.levelId,(int)key.difficulty,key.beatmapCharacteristic.serializedName)+"|"+(int)features;
        if(submittedKey==identity&&(requests.Current.Status==SelectionStatus.Loading||requests.Current.Status==SelectionStatus.Ready))return;
        submittedKey=identity;Observe(requests.SelectAsync(new MenuSelection(model,key,features)));
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
            if(!(result.beatmapLevelData is FileSystemBeatmapLevelData files))
                throw new NotSupportedException("Only filesystem beatmaps are supported by the menu analyzer at this stage");
            var entries=(Dictionary<(BeatmapCharacteristicSO,BeatmapDifficulty),FileDifficultyBeatmap>)
                AccessTools.Field(typeof(FileSystemBeatmapLevelData),"_difficultyBeatmaps").GetValue(files);
            if(!entries.TryGetValue((selection.Key.beatmapCharacteristic,selection.Key.difficulty),out var entry))
                throw new FileNotFoundException("Selected difficulty is missing from the loaded level");
            string file=(string)AccessTools.Field(typeof(FileDifficultyBeatmap),"_beatmapPath").GetValue(entry);
            string userData=IPA.Utilities.UnityGame.UserDataPath;
            return new SelectedMapRequest(selection.LevelId,selection.Characteristic,selection.Difficulty,selection.DifficultyRank,file,
                Path.Combine(userData,"SongPlayData.json"),Path.Combine(userData,DeathHistoryStore.FileName),features:selection.Features);
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
