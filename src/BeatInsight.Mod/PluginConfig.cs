using System.ComponentModel;
using BeatInsight.Core;
using BeatSaberMarkupLanguage.Attributes;

namespace BeatInsight.Mod;

public class PluginConfig:INotifyPropertyChanged
{
    [UIValue("show-average")] public virtual bool ShowAverage {get;set;}=true;
    [UIValue("show-peak")] public virtual bool ShowPeak {get;set;}=true;
    [UIValue("show-median")] public virtual bool ShowMedian {get;set;}=true;
    [UIValue("show-squats")] public virtual bool ShowSquats {get;set;}=true;
    [UIValue("show-first-notes")] public virtual bool ShowFirstNotes {get;set;}=true;
    [UIValue("show-density")] public virtual bool ShowDensity {get;set;}=true;
    [UIValue("show-imported-deaths")] public virtual bool ShowImportedDeaths {get;set;}=true;
    [UIValue("show-own-deaths")] public virtual bool ShowOwnDeaths {get;set;}=true;
    [UIValue("record-deaths")] public virtual bool RecordDeaths {get;set;}=true;
    [UIValue("squat-warnings")] public virtual bool SquatWarningsEnabled {get;set;}=true;
    [UIValue("squat-warning-lead")] public virtual float SquatWarningLeadTime {get;set;}=2f;
    [UIValue("squat-warning-circles")] public virtual bool SquatWarningCircles {get;set;}
    public event PropertyChangedEventHandler? PropertyChanged;
    public virtual void RaisePropertyChanged(string name)=>PropertyChanged?.Invoke(this,new PropertyChangedEventArgs(name));
    public virtual void Changed()=>MenuSettings.ConfigChanged();
    public virtual void OnReload()=>MenuSettings.ConfigChanged();
    internal void ReleaseUIBindings()=>PropertyChanged=null;
    internal MenuVisibility Visibility()=>new MenuVisibility {Average=ShowAverage,Peak=ShowPeak,Median=ShowMedian,
        Squats=ShowSquats,FirstNotes=ShowFirstNotes,Density=ShowDensity,ImportedDeaths=ShowImportedDeaths,OwnDeaths=ShowOwnDeaths};
}
