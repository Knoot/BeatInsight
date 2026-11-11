using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json.Linq;
using BeatInsight.Core;

namespace BeatInsight.Formats;

public sealed class MapCase
{
    public string Folder { get; set; } = "";
    public string Characteristic { get; set; } = "";
    public string Difficulty { get; set; } = "";
    public string File { get; set; } = "";
}
public sealed class LoadedMap
{
    public MapInput Input { get; set; } = new MapInput();
    public string Json { get; set; } = "";
    public string? AudioJson { get; set; }
    public float Bpm { get; set; }
    public string Version { get; set; } = "";
    public string[] Features { get; set; } = Array.Empty<string>();
}
public static class LocalMap
{
    static float F(JToken? t, float fallback = 0) => (float?)t ?? fallback;
    static int I(JToken? t, int fallback = 0) => (int?)t ?? fallback;
    static IEnumerable<JToken> Items(JToken? t) => t == null ? Enumerable.Empty<JToken>() : t.Children();
    public static string SafePath(string folder, string file)
    {
        string root = Path.GetFullPath(folder).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        string path = Path.GetFullPath(Path.Combine(root, file));
        if (!path.StartsWith(root, StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("Map path escapes folder");
        return path;
    }
    public static JObject Info(string folder) => JObject.Parse(System.IO.File.ReadAllText(Directory.EnumerateFiles(folder).Single(p => Path.GetFileName(p).Equals("info.dat", StringComparison.OrdinalIgnoreCase))));
    public static MapCase CaseFromFile(string file,string characteristic,string difficulty)
    {
        string full=Path.GetFullPath(file);
        if(!System.IO.File.Exists(full))throw new FileNotFoundException("Selected beatmap file is missing",full);
        for(string? folder=Path.GetDirectoryName(full);folder!=null;folder=Path.GetDirectoryName(folder))
        {
            if(!Directory.EnumerateFiles(folder).Any(p=>Path.GetFileName(p).Equals("info.dat",StringComparison.OrdinalIgnoreCase)))continue;
            var selected=Cases(folder).FirstOrDefault(c=>c.Characteristic==characteristic&&c.Difficulty==difficulty&&
                string.Equals(SafePath(c.Folder,c.File),full,StringComparison.OrdinalIgnoreCase));
            return selected??throw new NotSupportedException("Selected file does not match the characteristic/difficulty in Info.dat");
        }
        throw new FileNotFoundException("Info.dat was not found for the selected beatmap file");
    }
    public static IEnumerable<MapCase> Cases(string folder)
    {
        var info = Info(folder);
        if (info["_difficultyBeatmapSets"] != null)
            foreach (var set in Items(info["_difficultyBeatmapSets"]))
                foreach (var d in Items(set["_difficultyBeatmaps"])) yield return new MapCase { Folder = folder, Characteristic = (string)set["_beatmapCharacteristicName"]!, Difficulty = (string)d["_difficulty"]!, File = (string)d["_beatmapFilename"]! };
        else
            foreach (var d in Items(info["difficultyBeatmaps"])) yield return new MapCase { Folder = folder, Characteristic = (string)d["characteristic"]!, Difficulty = (string)d["difficulty"]!, File = (string)d["beatmapDataFilename"]! };
    }
    public static LoadedMap Load(MapCase test)
    {
        var info = Info(test.Folder);
        string json = System.IO.File.ReadAllText(SafePath(test.Folder, test.File));
        var root = JObject.Parse(json);
        var selected = info["_difficultyBeatmapSets"] != null
            ? Items(info["_difficultyBeatmapSets"]).Where(s => (string?)s["_beatmapCharacteristicName"] == test.Characteristic).SelectMany(s=>Items(s["_difficultyBeatmaps"])).First(d => (string?)d["_difficulty"] == test.Difficulty && (string?)d["_beatmapFilename"] == test.File)
            : Items(info["difficultyBeatmaps"]).First(d => (string?)d["characteristic"] == test.Characteristic && (string?)d["difficulty"] == test.Difficulty && (string?)d["beatmapDataFilename"] == test.File);
        var requirements = ((JContainer)selected).DescendantsAndSelf().OfType<JProperty>().Concat(root.Descendants().OfType<JProperty>()).Where(p => p.Name == "_requirements" || p.Name == "requirements").SelectMany(p => Items(p.Value)).Select(t => t.ToString()).Distinct().ToArray();
        if (requirements.Length > 0) throw new NotSupportedException("Required extensions: " + string.Join(", ", requirements));
        if (root.Descendants().OfType<JProperty>().Any(p => p.Name == "_fake" || p.Name == "fake" || p.Name == "fakeColorNotes" || p.Name == "fakeBurstSliders")) throw new NotSupportedException("Custom fake objects require extension loader");
        string version = (string?)(root["version"] ?? root["_version"]) ?? "2.0.0";
        int major = int.Parse(version.Split('.')[0]);
        float bpm = F(info["_beatsPerMinute"] ?? info["audio"]?["bpm"]);
        if (bpm <= 0) throw new InvalidDataException("Invalid initial BPM");
        JObject? audio = null; string? audioJson = null;
        if (major == 4)
        {
            audioJson = System.IO.File.ReadAllText(SafePath(test.Folder, (string)info["audio"]!["audioDataFilename"]!));
            audio = JObject.Parse(audioJson);
        }
        var changes = new List<(float Beat, float Time, float Bpm)> { (0,0,bpm) };
        if (major == 4)
        {
            changes.Clear(); float frequency = F(audio!["songFrequency"]);
            foreach (var b in Items(audio["bpmData"]))
                changes.Add((F(b["sb"]), F(b["si"])/frequency, (F(b["eb"])-F(b["sb"])) / ((float)(I(b["ei"])-I(b["si"]))/frequency)*60f));
        }
        else
        {
            bool legacyBpm=major==2&&new Version(version)<new Version("2.5.0");
            var events = major == 2 ? Items(root["_events"]).Where(e => I(e["_type"]) == 100 || legacyBpm&&I(e["_type"])==10) : Items(root["bpmEvents"]);
            foreach (var e in events.OrderBy(e => F(e[major == 2 ? "_time" : "b"])))
            {
                float beat = F(e[major == 2 ? "_time" : "b"]), next = legacyBpm&&I(e["_value"])!=0 ? I(e["_value"]) : F(e[major == 2 ? "_floatValue" : "m"]);
                if (next <= 0 || float.IsNaN(next) || float.IsInfinity(next)) throw new NotSupportedException("Non-positive or non-finite BPM after game-format preprocessing; no valid vanilla timeline (legacy lighting may require an extension loader)");
                var last = changes.Last(); changes.Add((beat, last.Time + (beat-last.Beat)/last.Bpm*60f, next));
            }
        }
        float Time(float beat)
        {
            var c = changes.First(); foreach (var candidate in changes.Skip(1)) { if (candidate.Beat >= beat) break; c = candidate; }
            return c.Time + (beat-c.Beat)/c.Bpm*60f;
        }
        var notes = new List<MapNote>();
        var rawNotes=Items(root[major == 2 ? "_notes" : "colorNotes"]).ToList();
        if(major==2&&rawNotes.Zip(rawNotes.Skip(1),(a,b)=>F(a["_time"])>F(b["_time"])).Any(x=>x))
            rawNotes.Sort((a,b)=>F(a["_time"]).CompareTo(F(b["_time"])));
        foreach (var n in rawNotes)
        {
            var data = major == 4 ? root["colorNotesData"]![I(n["i"])]! : n;
            int color = I(data[major == 2 ? "_type" : "c"]);
            if (color != 0 && color != 1) continue;
            float beat = F(n[major == 2 ? "_time" : "b"]);
            bool customRotation=new[]{"customData","_customData"}.Select(k=>data[k]).Where(t=>t!=null).OfType<JContainer>()
                .SelectMany(t=>t.Descendants().OfType<JProperty>()).Any(p=>new[]{"rotation","localRotation","animation","position","coordinates","cutDirection","_rotation","_localRotation","_animation","_position","_cutDirection"}.Contains(p.Name));
            notes.Add(new MapNote { Beat = beat, Time = Time(beat), X = I(data[major == 2 ? "_lineIndex" : "x"]), Y = I(data[major == 2 ? "_lineLayer" : "y"]), Color = color, Direction = I(data[major == 2 ? "_cutDirection" : "d"]),
                CutDirectionAngleOffset=customRotation?(float?)null:F(data["a"]) });
        }
        var arcs=new List<PreparedSlider>();var chains=new List<PreparedSlider>();
        foreach (var arc in Items(root[major == 4 ? "arcs" : "sliders"]))
        {
            var hd = major == 4 ? root["colorNotesData"]![I(arc["hi"])]! : arc;
            var td = major == 4 ? root["colorNotesData"]![I(arc["ti"])]! : arc;
            float beat=F(arc[major==4?"hb":"b"]);
            arcs.Add(new PreparedSlider {Head=new MapNote {Beat=beat,Time=Time(beat),X=I(hd["x"]),Y=I(hd["y"]),Color=I(hd["c"])},
                TailTime=Time(F(arc["tb"])),TailX=I(td[major==4?"x":"tx"]),TailY=I(td[major==4?"y":"ty"])});
        }
        // A chain head is counted once. Original game scoring marks it specially, and reference implementation
        // reconstructs it from SliderData after the ordinary notes at the same time.
        foreach (var chain in Items(root[major == 4 ? "chains" : "burstSliders"]))
        {
            float beat = F(chain[major == 4 ? "hb" : "b"]);
            var data = major == 4 ? root["colorNotesData"]![I(chain["i"])]! : chain;
            int x = I(data["x"]), y = I(data["y"]), color = I(data["c"]);
            var chainData=major==4?root["chainsData"]![I(chain["ci"])]!:chain;
            chains.Add(new PreparedSlider {Chain=true,Head=new MapNote { Beat = beat, Time = Time(beat), X=x, Y=y, Color=color, Direction=I(data["d"]),SliceCount=I(chainData[major==4?"c":"sc"],1) },
                TailTime=Time(F(chain["tb"])),TailX=I(chainData["tx"]),TailY=I(chainData["ty"])});
        }
        var bombs=major==2?rawNotes.Where(n=>I(n["_type"])==3).Select(n=>new MapNote {Beat=F(n["_time"]),Time=Time(F(n["_time"])),X=I(n["_lineIndex"]),Y=I(n["_lineLayer"]),Color=-1,Direction=9,ScoringType=0}).ToList():
            Items(root["bombNotes"]).Select(n=>{var d=major==4?root["bombNotesData"]![I(n["i"])]!:n;return new MapNote {Beat=F(n["b"]),Time=Time(F(n["b"])),X=I(d["x"]),Y=I(d["y"]),Color=-1,Direction=9,ScoringType=0};}).ToList();
        var prepared=NotePreparation.Prepare(notes,bombs,arcs,chains,chainsBeforeArcs:major==4);
        var walls = new List<MapWall>();
        foreach (var w in Items(root[major == 2 ? "_obstacles" : "obstacles"]))
        {
            var data = major == 4 ? root["obstaclesData"]![I(w["i"])]! : w;
            float beat = F(w[major == 2 ? "_time" : "b"]), end = beat + F(data[major == 2 ? "_duration" : "d"]);
            int type = I(data["_type"]);
            var wall=new MapWall { Beat=beat, EndBeat=end, Time=Time(beat), Duration=Time(end)-Time(beat), X=I(data[major == 2 ? "_lineIndex" : "x"]), Y=major==2 ? (type==1?2:0) : I(data["y"]), Width=I(data[major == 2 ? "_width" : "w"]), Height=major==2 ? (type==1?3:5) : I(data["h"]) };
            if(wall.Width>0&&wall.Duration>0)walls.Add(wall);
        }
        var features = new List<string> { "v"+major };
        if (changes.Count > 1) features.Add("bpm-changes");
        if (Items(root[major==4?"arcs":"sliders"]).Any()) features.Add("arcs");
        if (Items(root[major==4?"chains":"burstSliders"]).Any()) features.Add("chains");
        if (notes.Any(n => n.Direction==8)) features.Add("dots");
        float duration = F(info["audio"]?["songDuration"]);
        if (duration <= 0) duration = OggDuration(SafePath(test.Folder, (string)(info["audio"]?["songFilename"]??info["_songFilename"])!));
        return new LoadedMap { Json=json, AudioJson=audioJson, Bpm=bpm, Version=version, Features=features.ToArray(), Input=new MapInput { Duration=duration, Notes=prepared, Walls=walls.OrderBy(w=>w.Time).ToArray() } };
    }
    static float OggDuration(string path)
    {
        using var stream = System.IO.File.OpenRead(path);
        var head = new byte[(int)Math.Min(65536,stream.Length)]; stream.Read(head,0,head.Length);
        int frequency=0;
        for(int i=0;i+16<head.Length;i++) if(head[i]==1 && head[i+1]=='v' && head[i+2]=='o' && head[i+3]=='r' && head[i+4]=='b' && head[i+5]=='i' && head[i+6]=='s') { frequency=BitConverter.ToInt32(head,i+12);break; }
        if (frequency<=0) throw new NotSupportedException("Audio duration: expected Ogg Vorbis");
        stream.Position=Math.Max(0,stream.Length-131072); var tail=new byte[stream.Length-stream.Position];stream.Read(tail,0,tail.Length);
        long granule=-1;
        for(int i=0;i+27<tail.Length;i++) if(tail[i]=='O'&&tail[i+1]=='g'&&tail[i+2]=='g'&&tail[i+3]=='S') {long g=BitConverter.ToInt64(tail,i+6);if(g>=0)granule=g;}
        if(granule<0)throw new InvalidDataException("No final Ogg granule");
        return (float)((double)granule/frequency);
    }
}
