using System;
using BeatInsight.Core;

namespace BeatInsight.Formats;

public static class AnalysisAvailabilityHints
{
    // Do not display raw exceptions: they can contain machine paths, JSON paths
    // or markup. Unknown diagnostics keep a neutral explanation.
    public static string Reason(string? diagnostic)
    {
        bool Has(string text)=>diagnostic?.IndexOf(text,StringComparison.OrdinalIgnoreCase)>=0;
        if(Has("player track transformation"))return "Player movement or rotation is not supported yet.";
        if(Has("World-rotated walls"))return "Wall world rotation is not supported yet.";
        if(Has("Wall pitch/yaw"))return "Tilted walls are not supported yet.";
        if(Has("worldRotation"))return "Custom object world rotation is not supported yet.";
        if(Has("named tracks need"))return "The gameplay effects of this map's tracks are not verified yet.";
        if(Has("animation changes gameplay geometry"))return "This map's object animation is not supported yet.";
        if(Has("Negative Noodle wall duration"))return "Negative-duration walls are not supported yet.";
        if(Has("Invalid Noodle wall interval"))return "The map contains an invalid wall interval.";
        if(Has("Wall length exceeds"))return "A wall extends beyond the verified timing range.";
        if(Has("extended base coordinates"))return "These wall coordinates or dimensions are not supported yet.";
        if(Has("Required extensions:"))return "A required map extension is not supported yet.";
        if(Has("Combined extension linked custom coordinates"))return "Custom coordinates on linked notes are not supported with combined extensions yet.";
        if(Has("Combined Mapping Extensions and Noodle Extensions"))return "This combination of map extensions is not supported yet.";
        if(Has("Invalid BPM")||Has("Non-positive or non-finite initial BPM")||Has("Invalid tempo"))return "The map has invalid or unsupported timing.";
        if(Has("Game content is not ready"))return "The game has not loaded this map's data yet.";
        if(Has("Bundled map audio offsets"))return "Audio timing offsets for bundled maps are not supported yet.";
        if(Has("Bundled map BPM"))return "The game tempo metadata is unavailable.";
        if(Has("Bundled map duration"))return "The game audio duration is unavailable.";
        if(Has("beatmap data provider"))return "This map's data source is not supported yet.";
        return "This map uses gameplay data that is not supported yet.";
    }

    public static string Unavailable(string status,string? diagnostic)=>status+"\n"+Reason(diagnostic);

    public static string Selection(SelectionStatus status,string? diagnostic)=>status switch
    {
        SelectionStatus.Loading=>"Loading...",
        SelectionStatus.Idle=>"Select a map.",
        SelectionStatus.Unsupported=>Unavailable("This map is not supported yet.",diagnostic),
        SelectionStatus.Unavailable=>"Data unavailable.\n"+
            (diagnostic?.IndexOf("Game content is not ready",StringComparison.OrdinalIgnoreCase)>=0
                ?"The game has not loaded this map's data yet.":"The selected map data could not be found or loaded."),
        SelectionStatus.Error=>"The selected map data could not be analyzed.",
        _=>"Data unavailable."
    };
}
