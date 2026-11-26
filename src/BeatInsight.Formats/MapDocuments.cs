using System;
using Newtonsoft.Json.Linq;

namespace BeatInsight.Formats;

// Immutable source documents only. No game assets, mutable JSON or filesystem
// fallback crosses into a worker when the game supplies a bundled map.
public sealed class MapDocuments
{
    public string InfoJson {get;}
    public string BeatmapJson {get;}
    public string? AudioJson {get;}
    public string File {get;}
    public MapDocuments(string infoJson,string beatmapJson,string? audioJson=null,string file="beatmap.dat")
    {
        InfoJson=infoJson??throw new ArgumentNullException(nameof(infoJson));
        BeatmapJson=beatmapJson??throw new ArgumentNullException(nameof(beatmapJson));
        AudioJson=audioJson;File=file??throw new ArgumentNullException(nameof(file));
    }
    public static MapDocuments Bundled(string json,string? audio,float bpm,float duration,string characteristic,string difficulty)
    {
        if(!float.IsFinite(bpm)||bpm<=0)throw new NotSupportedException("Bundled map BPM is unavailable");
        if(!float.IsFinite(duration)||duration<0)throw new NotSupportedException("Bundled map duration is unavailable");
        var audioInfo=new JObject{["bpm"]=bpm,["songDuration"]=duration};
        if(audio!=null)audioInfo["audioDataFilename"]="audio.dat";
        var info=new JObject{["version"]="4.0.1",["audio"]=audioInfo,["difficultyBeatmaps"]=new JArray(new JObject{
            ["characteristic"]=characteristic,["difficulty"]=difficulty,["beatmapDataFilename"]="beatmap.dat"})};
        return new MapDocuments(info.ToString(),json,audio);
    }
}
