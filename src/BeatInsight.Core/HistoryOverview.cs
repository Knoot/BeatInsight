using System;
using System.Collections.Generic;
using System.Linq;

namespace BeatInsight.Core;

public readonly struct HistoryHoverRegion
{
    public readonly bool Enabled;
    public readonly float Left,Right;
    public HistoryHoverRegion(bool enabled,float left,float right){Enabled=enabled;Left=left;Right=right;}
}

public static class HistoryHoverLayout
{
    public static HistoryHoverRegion Region(bool imported,bool own,bool ownSource)=>ownSource
        ?new HistoryHoverRegion(own,imported?.5f:0,1)
        :new HistoryHoverRegion(imported,0,own?.5f:1);
}

public sealed class HistoryOverview
{
    public string Tooltip {get;private set;}="";
    public int Records {get;private set;}
    public int Shown {get;private set;}
    public int Changed {get;private set;}
    public int Invalid {get;private set;}
    public int Unverified {get;private set;}
    public static bool CanDraw(DeathMarker marker)=>marker.SongTimeSeconds.HasValue&&marker.NormalizedPosition.HasValue&&
        !double.IsNaN(marker.SongTimeSeconds.Value)&&!double.IsInfinity(marker.SongTimeSeconds.Value)&&marker.SongTimeSeconds.Value>=0&&
        !double.IsNaN(marker.NormalizedPosition.Value)&&!double.IsInfinity(marker.NormalizedPosition.Value)&&
        marker.NormalizedPosition.Value>=0&&marker.NormalizedPosition.Value<=1&&marker.MapVersionStatus!="changed"&&marker.PositionStatus!="beatmap-changed";

    public static HistoryOverview Create(string source,string status,IEnumerable<DeathMarker> markers)
    {
        var result=new HistoryOverview();
        string title=source=="legacy"?"Imported history (yellow crosses)":"BeatInsight history (red crosses)";
        string? unavailable=status switch {
            "extension-positions-unverified"=>"Imported positions have not been verified for this map's custom geometry or chain processing.",
            "note-analysis-unavailable"=>"Imported note positions are unavailable because note analysis is unsupported.",
            "source-missing"=>"History file not found.","key-absent"=>"No records for this difficulty.",
            "source-unreadable"=>"History file could not be read.","invalid-data"=>"History file contains invalid data.",
            "unsupported-schema"=>"History format is not supported.","not-requested"=>"History was not requested.",
            "available"=>null,_=>"History is unavailable."};
        if(unavailable!=null){result.Tooltip=title+"\n"+unavailable;return result;}
        var records=markers.Where(m=>m.Source==source).ToArray();
        result.Records=records.Length;
        if(records.Length==0){result.Tooltip=title+"\nNo records for this difficulty.";return result;}
        var visible=records.Where(CanDraw).ToArray();
        result.Shown=visible.Length;
        result.Changed=records.Count(m=>m.MapVersionStatus=="changed"||m.PositionStatus=="beatmap-changed");
        result.Invalid=records.Length-result.Shown-result.Changed;
        result.Unverified=visible.Count(m=>m.MapVersionStatus!="verified");
        var lines=new List<string> {title,"Positions: "+result.Shown+" shown, "+(records.Length-result.Shown)+" hidden."};
        if(result.Changed>0)lines.Add("Changed map: "+result.Changed+" hidden.");
        if(result.Invalid>0)lines.Add("Invalid or unavailable positions: "+result.Invalid+" hidden.");
        if(source=="legacy")lines.Add("Positions are estimates from note indices; map versions are unverified.");
        else
        {
            int verified=visible.Length-result.Unverified;
            if(verified>0)lines.Add("Map version verified: "+verified+" shown positions.");
            if(result.Unverified>0)lines.Add("Map version unverified: "+result.Unverified+" shown positions.");
            int exact=visible.Count(m=>(m.TimingSource=="failure-song-time"||m.TimingSource=="zero-energy-song-time"));
            int estimated=visible.Count(m=>m.TimingSource=="completion-song-time-fallback");
            int unknown=visible.Length-exact-estimated;
            if(exact>0)lines.Add("Recorded failure time: "+exact+".");
            if(estimated>0)lines.Add("Completion-time estimates: "+estimated+".");
            if(unknown>0)lines.Add("Timing accuracy unspecified: "+unknown+".");
        }
        string Speed(HistoryModifiers m)=>m.SpeedPreset switch {"normal"=>"Normal","faster"=>"Faster","slower"=>"Slower","super-fast"=>"Super Fast",_=>"Unknown"};
        string Walls(HistoryModifiers m)=>m.Obstacles switch {"all"=>"All","none"=>"Disabled","full-height-only"=>"Full height only",_=>"Unknown"};
        lines.Add("Speed presets: "+string.Join(", ",records.Select(m=>Speed(m.Modifiers)).Distinct())+".");
        lines.Add("Bombs: "+string.Join(", ",records.Select(m=>m.Modifiers.NoBombs.HasValue?(m.Modifiers.NoBombs.Value?"Disabled":"Enabled"):"Unknown").Distinct())+".");
        lines.Add("Walls: "+string.Join(", ",records.Select(m=>Walls(m.Modifiers)).Distinct())+".");
        lines.Add("Current modifiers do not filter these records.");
        result.Tooltip=string.Join("\n",lines);return result;
    }
}
