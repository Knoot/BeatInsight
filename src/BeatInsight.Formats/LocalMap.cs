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
    public bool HasNoodleChains { get; set; }
    public bool HasNoodleGeometry { get; set; }
    public string? SquatJson { get; set; }
    public bool NoodleExtensions { get; set; }
    public bool MappingExtensions { get; set; }
    public MapInput Input { get; set; } = new MapInput();
    public string Json { get; set; } = "";
    public string? AudioJson { get; set; }
    public float Bpm { get; set; }
    public string Version { get; set; } = "";
    public string[] Features { get; set; } = Array.Empty<string>();
}
public static class LocalMap
{
    public const int AnalysisProfileVersion=78;
    static float F(JToken? t, float fallback = 0) => (float?)t ?? fallback;
    static int I(JToken? t, int fallback = 0) => (int?)t ?? fallback;
    static int AudioInteger(JToken? value,string field)
    {
        try{return I(value);}catch(Exception e) when(e is FormatException||e is OverflowException||e is InvalidCastException||e is ArgumentException)
        {throw new NotSupportedException("Invalid AudioData integer: "+field,e);}
    }
    static float AudioBeat(JToken? value,string field)
    {
        try{return F(value);}catch(Exception e) when(e is FormatException||e is OverflowException||e is InvalidCastException||e is ArgumentException)
        {throw new NotSupportedException("Invalid AudioData beat: "+field,e);}
    }
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
    public static LoadedMap Load(MapCase test,AnalysisFeatures features=AnalysisFeatures.All,bool includeSwingWindowWalls=true,JumpSettingsSnapshot? jumpSettings=null)
    {
        if(!includeSwingWindowWalls&&(features&(AnalysisFeatures.Reference|AnalysisFeatures.Squats))!=0)
            throw new ArgumentException("Independent note analysis cannot include wall-dependent features",nameof(features));
        var info = Info(test.Folder);
        string json = System.IO.File.ReadAllText(SafePath(test.Folder, test.File));
        var root = JObject.Parse(json);
        var selected = info["_difficultyBeatmapSets"] != null
            ? Items(info["_difficultyBeatmapSets"]).Where(s => (string?)s["_beatmapCharacteristicName"] == test.Characteristic).SelectMany(s=>Items(s["_difficultyBeatmaps"])).First(d => (string?)d["_difficulty"] == test.Difficulty && (string?)d["_beatmapFilename"] == test.File)
            : Items(info["difficultyBeatmaps"]).First(d => (string?)d["characteristic"] == test.Characteristic && (string?)d["difficulty"] == test.Difficulty && (string?)d["beatmapDataFilename"] == test.File);
        string version = (string?)(root["version"] ?? root["_version"]) ?? "2.0.0";
        int metadataMajor=Version.TryParse(version,out var metadataVersion)?metadataVersion.Major:0;
        var requirements = ((JContainer)selected).DescendantsAndSelf().OfType<JProperty>().Concat(NoodleMetadataScope.ActiveProperties(root,metadataMajor)).Where(p => p.Name == "_requirements" || p.Name == "requirements").SelectMany(p => Items(p.Value)).Select(t => t.ToString()).Distinct().ToArray();
        bool mappingExtensions=requirements.Contains("Mapping Extensions");
        bool noodleExtensions=requirements.Contains("Noodle Extensions");
        if(noodleExtensions&&mappingExtensions)throw new NotSupportedException("Combined Mapping Extensions and Noodle Extensions gameplay has not been validated");
        var unsupportedRequirements=requirements.Where(r=>r!="Chroma"&&r!="Mapping Extensions"&&r!="Noodle Extensions").ToArray();
        if (unsupportedRequirements.Length > 0) throw new NotSupportedException("Required extensions: " + string.Join(", ", unsupportedRequirements));
        if (!noodleExtensions&&NoodleMetadataScope.ActiveProperties(root,metadataMajor).Any(p => p.Name == "_fake" || p.Name == "fake" || p.Name == "NE_fake" || p.Name == "fakeColorNotes" || p.Name == "fakeBombNotes" || p.Name == "fakeObstacles" || p.Name == "fakeBurstSliders")) throw new NotSupportedException("Custom fake objects require extension loader");
        if(!Version.TryParse(version,out var parsedVersion)||parsedVersion.Major<2||parsedVersion.Major>4)
            throw new NotSupportedException("Unsupported beatmap version: "+version);
        int major=parsedVersion.Major;
        float bpm = F(info["_beatsPerMinute"] ?? info["audio"]?["bpm"]);
        if (bpm <= 0||float.IsNaN(bpm)||float.IsInfinity(bpm)) throw new NotSupportedException("Non-positive or non-finite initial BPM; no valid vanilla timeline");
        bool needNotes=(features&(AnalysisFeatures.Average|AnalysisFeatures.Peak|AnalysisFeatures.Median|AnalysisFeatures.FirstNotes|AnalysisFeatures.Density|AnalysisFeatures.ImportedDeaths|AnalysisFeatures.Reference))!=0;
        bool needDuration=(features&(AnalysisFeatures.Density|AnalysisFeatures.ImportedDeaths|AnalysisFeatures.OwnDeaths|AnalysisFeatures.Reference))!=0;
        bool orientation=(features&(AnalysisFeatures.FirstNotes|AnalysisFeatures.Reference))!=0;
        bool needWalls=(features&(AnalysisFeatures.Squats|AnalysisFeatures.Reference))!=0
            ||includeSwingWindowWalls&&(features&(AnalysisFeatures.Average|AnalysisFeatures.Peak|AnalysisFeatures.Median))!=0;
        if(noodleExtensions)NoodleFakeData.Append(root,major,needNotes,needWalls);
        bool independentDensity=noodleExtensions&&(features&AnalysisFeatures.Density)!=0
            &&(features&(AnalysisFeatures.Average|AnalysisFeatures.Peak|AnalysisFeatures.Median|AnalysisFeatures.FirstNotes|AnalysisFeatures.ImportedDeaths|AnalysisFeatures.Reference|AnalysisFeatures.Squats))==0;
        if(independentDensity)independentDensity=NoodleDensityProjection.Project(root,major,test.Characteristic);
        if(noodleExtensions&&needNotes)
        {
            NoodlePositionDefinitions.ResolveNotes(root,major);
            NoodleZeroWorldRotation.Project(root,major,test.Characteristic);
        }
        bool wallInteraction=noodleExtensions&&needWalls&&NoodleWallInteraction.Project(root,major);
        bool wallEntry=noodleExtensions&&needWalls&&NoodleWallEntry.Project(root,selected,major,bpm,jumpSettings);
        var nominalTracks=new HashSet<string>(StringComparer.Ordinal);
        if(noodleExtensions)NoodleStaticProfile.Validate(root,major,needNotes,needWalls,bpm,out nominalTracks);
        bool wallLength=noodleExtensions&&needWalls&&NoodleWallData.HasLength(root,major);
        var correctedWallEnds=new Dictionary<JToken,float>();
        string? squatJson=noodleExtensions&&(features&AnalysisFeatures.Squats)!=0?NoodleFakeData.WithoutFakeWalls(root,major):null;
        if(!needNotes&&!wallLength)return new LoadedMap {HasNoodleGeometry=wallInteraction||wallEntry,Json=json,Bpm=bpm,Version=version,MappingExtensions=mappingExtensions,NoodleExtensions=noodleExtensions,Features=requirements.Select(r=>r=="Chroma"?"chroma":r=="Mapping Extensions"?"mapping-extensions":"noodle-static-subset").ToArray(),
            SquatJson=squatJson,Input=new MapInput {Duration=needDuration?ReadDuration(info,test.Folder):0}};
        JObject? audio = null; string? audioJson = null;
        if (major == 4)
        {
            string? audioFile=(string?)info["audio"]?["audioDataFilename"];
            if(string.IsNullOrWhiteSpace(audioFile))throw new NotSupportedException("Missing AudioData filename for v4 timing");
            try{audioJson=System.IO.File.ReadAllText(SafePath(test.Folder,audioFile));}
            catch(Exception e) when(e is FileNotFoundException||e is DirectoryNotFoundException)
            {throw new NotSupportedException("AudioData file is missing for v4 timing",e);}
            try{audio=JObject.Parse(audioJson);}
            catch(Newtonsoft.Json.JsonException e){throw new NotSupportedException("Invalid AudioData JSON",e);}
            if(audio["bpmData"] is not JArray)throw new NotSupportedException("Missing or invalid AudioData BPM ranges");
            foreach(string collection in new[]{"colorNotes","colorNotesData","bombNotes","bombNotesData","arcs","arcsData","chains","chainsData","obstacles","obstaclesData"})
                if(root[collection]!=null&&root[collection]!.Type!=JTokenType.Null&&root[collection] is not JArray)
                    throw new NotSupportedException("Invalid v4 collection: "+collection);
        }
        var changes = new List<(float Beat, float Time, float Bpm)> { (0,0,bpm) };
        if (major == 4)
        {
            changes.Clear(); float frequency = AudioInteger(audio!["songFrequency"],"songFrequency");
            foreach (var b in Items(audio["bpmData"]))
            {
                if(b is not JObject)throw new NotSupportedException("Invalid AudioData BPM range");
                int start=AudioInteger(b["si"],"si"),end=AudioInteger(b["ei"],"ei");
                float startBeat=AudioBeat(b["sb"],"sb"),endBeat=AudioBeat(b["eb"],"eb");
                changes.Add((startBeat,start/frequency,(endBeat-startBeat)/((float)(end-start)/frequency)*60f));
            }
        }
        else
        {
            bool legacyBpm=major==2&&parsedVersion<new Version("2.5.0");
            var events = major == 2 ? Items(root["_events"]).Where(e => I(e["_type"]) == 100 || legacyBpm&&I(e["_type"])==10) : Items(root["bpmEvents"]);
            foreach (var e in events.OrderBy(e => F(e[major == 2 ? "_time" : "b"])))
            {
                float beat = F(e[major == 2 ? "_time" : "b"]), next = legacyBpm&&I(e["_value"])!=0 ? I(e["_value"]) : F(e[major == 2 ? "_floatValue" : "m"]);
                var last = changes.Last(); changes.Add((beat, last.Time + (beat-last.Beat)/last.Bpm*60f, next));
            }
        }
        float Time(float beat)
        {
            if(changes.Count==0)throw new NotSupportedException("AudioData has no timing ranges for gameplay objects");
            var c = changes.First();
            foreach (var candidate in changes.Skip(1))
            {
                if (candidate.Beat >= beat) break;
                if(major!=4&&(candidate.Bpm<=0||float.IsNaN(candidate.Bpm)||float.IsInfinity(candidate.Bpm)))
                    throw new NotSupportedException("Non-positive or non-finite BPM affects a gameplay object; no valid vanilla timeline");
                c=candidate;
            }
            if(c.Bpm<=0||float.IsNaN(c.Bpm)||(major!=4&&float.IsInfinity(c.Bpm)))
                throw new NotSupportedException("Invalid tempo for a gameplay object");
            float time=c.Time+(beat-c.Beat)/c.Bpm*60f;
            if(float.IsNaN(time)||float.IsInfinity(time))throw new NotSupportedException("Non-finite time for a gameplay object");
            return time;
        }
        if(wallLength)
        {
            float BeatAtTime(float seconds)
            {
                var segment=changes[0];
                foreach(var next in changes)if(next.Time<=seconds)segment=next;else break;
                if(!float.IsFinite(segment.Bpm)||segment.Bpm<=0)throw new NotSupportedException("Invalid BPM for wall length");
                return segment.Beat+(seconds-segment.Time)*segment.Bpm/60f;
            }
            float movement=F(selected["_noteJumpMovementSpeed"]??selected["noteJumpMovementSpeed"]);
            string projection=NoodleWallData.Prepare(root,major,movement,Time,BeatAtTime,correctedWallEnds);
            if((features&AnalysisFeatures.Squats)!=0)squatJson=projection;
            if(!needNotes)return new LoadedMap {Json=json,Bpm=bpm,Version=version,NoodleExtensions=true,SquatJson=squatJson,Features=new[]{"noodle-static-subset","noodle-short-wall-length"},Input=new MapInput {Duration=needDuration?ReadDuration(info,test.Folder):0}};
        }
        JToken IndexedData(string collection,JToken? index)
        {
            int i;
            try{i=I(index);}catch(Exception e) when(e is FormatException||e is OverflowException||e is InvalidCastException||e is ArgumentException)
            {throw new NotSupportedException("Invalid v4 index for "+collection,e);}
            if(root[collection] is not JArray array||i<0||i>=array.Count||array[i] is not JObject)
                throw new NotSupportedException("Missing or invalid referenced v4 data: "+collection);
            return array[i];
        }
        var notes = new List<MapNote>();
        HashSet<MapNote>? nominalMotionNotes=null;
        Dictionary<MapNote,float>? legacyDirectionAngles=null;
        var rawNotes=Items(root[major == 2 ? "_notes" : "colorNotes"]).ToList();
        if((major==2||noodleExtensions)&&rawNotes.Zip(rawNotes.Skip(1),(a,b)=>F(a[major==2?"_time":"b"])>F(b[major==2?"_time":"b"])).Any(x=>x))
        {
            if(noodleExtensions)rawNotes=rawNotes.OrderBy(n=>F(n[major==2?"_time":"b"])).ToList();
            else rawNotes.Sort((a,b)=>F(a["_time"]).CompareTo(F(b["_time"])));
        }
        foreach (var n in rawNotes)
        {
            var data = major == 4 ? IndexedData("colorNotesData",n["i"]) : n;
            int color = I(data[major == 2 ? "_type" : "c"]);
            if (color != 0 && color != 1) continue;
            float beat = F(n[major == 2 ? "_time" : "b"]);
            bool motionFlags=noodleExtensions&&(NoodleMotionFlags.HasActive(data,major)
                ||major==3&&NoodleNominalTracks.Assigned(data["customData"]?["track"],nominalTracks));
            bool customRotation=orientation&&(motionFlags||new[]{"customData","_customData"}.Select(k=>data[k]).Where(t=>t!=null).OfType<JContainer>()
                .SelectMany(t=>NoodleMetadataScope.OrientationProperties(t,major,noodleExtensions)).Any(p=>new[]{"rotation","localRotation","animation","position","coordinates","cutDirection","_rotation","_localRotation","_animation","_position","_cutDirection"}.Contains(p.Name)&&(!noodleExtensions||p.Name!="coordinates"&&p.Name!="_position"&&(major!=2||p.Name!="_cutDirection"))
                    &&!(noodleExtensions&&(NoodleVisualEvents.IsNeutralObjectAnimation(p.Name,p.Value,major)||major==3&&NoodleV3NoteAliases.Contains(p.Name)))));
            notes.Add(new MapNote { IsFake=noodleExtensions&&NoodleFakeData.IsFake(data,major),Beat = beat, Time = Time(beat), X = I(data[major == 2 ? "_lineIndex" : "x"]), Y = I(data[major == 2 ? "_lineLayer" : "y"]), Color = color, Direction = I(data[major == 2 ? "_cutDirection" : "d"]),
                CutDirectionAngleOffset=orientation?(customRotation?(float?)null:F(data["a"])):null });
            if(noodleExtensions)NoodleGeometry.Apply(notes.Last(),data,major);
            if(motionFlags){nominalMotionNotes??=new HashSet<MapNote>();nominalMotionNotes.Add(notes.Last());}
            if(noodleExtensions&&major==3&&data["customData"]?["animation"]?["offsetPosition"] is JToken entryOffset
                &&NoodleVectorCurve.TryRead(entryOffset,out var entryCurve)&&!entryCurve!.AlwaysZero)
            {
                nominalMotionNotes??=new HashSet<MapNote>();nominalMotionNotes.Add(notes.Last());
            }
            if(noodleExtensions&&major==3&&data["customData"]?["animation"]?["localRotation"]!=null)
            {
                nominalMotionNotes??=new HashSet<MapNote>();nominalMotionNotes.Add(notes.Last());
            }
            if(noodleExtensions&&major==2&&(data["_customData"] as JObject)?["_cutDirection"] is JToken angle&&angle.Type!=JTokenType.Null)
            {
                legacyDirectionAngles??=new Dictionary<MapNote,float>();
                legacyDirectionAngles.Add(notes.Last(),(float)angle);
            }
        }
        var arcs=new List<PreparedSlider>();var chains=new List<PreparedSlider>();
        string arcKey=major==2?"_sliders":major==4?"arcs":"sliders";
        var rawArcs=Items(root[arcKey]).ToList();
        if(major==2&&!noodleExtensions&&rawArcs.Zip(rawArcs.Skip(1),(a,b)=>F(a["_headTime"])>F(b["_headTime"])).Any(x=>x))
            rawArcs.Sort((a,b)=>F(a["_headTime"]).CompareTo(F(b["_headTime"])));
        if(noodleExtensions)rawArcs=rawArcs.OrderBy(a=>F(a[major==2?"_headTime":"b"])).ToList();
        foreach (var arc in rawArcs)
        {
            if(major==2)
            {
                float head=F(arc["_headTime"]);
                arcs.Add(new PreparedSlider {Head=new MapNote {Beat=head,Time=Time(head),X=I(arc["_headLineIndex"]),Y=I(arc["_headLineLayer"]),Color=I(arc["_colorType"])},
                    TailTime=Time(F(arc["_tailTime"])),TailX=I(arc["_tailLineIndex"]),TailY=I(arc["_tailLineLayer"])});
                if(noodleExtensions)
                {
                    var slider=arcs.Last();NoodleGeometry.Apply(slider.Head,arc,major);
                    var tail=NoodleGeometry.Read(arc,major,"tailCoordinates");slider.NoodleTailX=tail.X;slider.NoodleTailY=tail.Y;
                }
                continue;
            }
            if(major==4)IndexedData("arcsData",arc["ai"]);
            var hd = major == 4 ? IndexedData("colorNotesData",arc["hi"]) : arc;
            var td = major == 4 ? IndexedData("colorNotesData",arc["ti"]) : arc;
            float beat=F(arc[major==4?"hb":"b"]);
            arcs.Add(new PreparedSlider {Head=new MapNote {Beat=beat,Time=Time(beat),X=I(hd["x"]),Y=I(hd["y"]),Color=I(hd["c"])},
                TailTime=Time(F(arc["tb"])),TailX=I(td[major==4?"x":"tx"]),TailY=I(td[major==4?"y":"ty"])});
            if(noodleExtensions)
            {
                var slider=arcs.Last();NoodleGeometry.Apply(slider.Head,arc,major);
                var tail=NoodleGeometry.Read(arc,major,"tailCoordinates");slider.NoodleTailX=tail.X;slider.NoodleTailY=tail.Y;
            }
        }
        // A chain head is counted once. Original game scoring marks it specially, and reference implementation
        // reconstructs it from SliderData after the ordinary notes at the same time.
        foreach (var chain in Items(root[major == 4 ? "chains" : "burstSliders"]))
        {
            float beat = F(chain[major == 4 ? "hb" : "b"]);
            var data = major == 4 ? IndexedData("colorNotesData",chain["i"]) : chain;
            int x = I(data["x"]), y = I(data["y"]), color = I(data["c"]);
            var chainData=major==4?IndexedData("chainsData",chain["ci"]):chain;
            chains.Add(new PreparedSlider {Chain=true,Head=new MapNote { Beat = beat, Time = Time(beat), X=x, Y=y, Color=color, Direction=I(data["d"]),SliceCount=I(chainData[major==4?"c":"sc"],1) },
                TailTime=Time(F(chain["tb"])),TailX=I(chainData["tx"]),TailY=I(chainData["ty"])});
            if(noodleExtensions)
            {
                if(chainData["sc"]?.Type!=JTokenType.Integer)throw new NotSupportedException("Noodle burst slice count must be an integer");
                var slider=chains.Last();slider.Noodle=true;NoodleGeometry.Apply(slider.Head,chain,major);
                var tail=NoodleGeometry.Read(chain,major,"tailCoordinates");slider.NoodleTailX=tail.X;slider.NoodleTailY=tail.Y;
            }
        }
        bool nominalBombMotion=false;
        var bombs=major==2?rawNotes.Where(n=>I(n["_type"])==3).Select(n=>new MapNote {Beat=F(n["_time"]),Time=Time(F(n["_time"])),X=I(n["_lineIndex"]),Y=I(n["_lineLayer"]),Color=-1,Direction=9,ScoringType=0}).ToList():
            Items(root["bombNotes"]).Select(n=>{var d=major==4?IndexedData("bombNotesData",n["i"]):n;return new MapNote {Beat=F(n["b"]),Time=Time(F(n["b"])),X=I(d["x"]),Y=I(d["y"]),Color=-1,Direction=9,ScoringType=0};}).ToList();
        if(mappingExtensions)
        {
            foreach(var n in notes.Concat(bombs).Concat(arcs.Select(a=>a.Head)).Concat(chains.Select(c=>c.Head)))
            {
                n.MappingExtensions=true;
                if(n.Direction<0||n.Direction>9&&!MappingNotes.IsExtendedDirection(n.Direction))
                    throw new NotSupportedException("Invalid Mapping Extensions cut direction");
            }
        }
        if(noodleExtensions)
        {
            var rawBombs=major==2?rawNotes.Where(n=>I(n["_type"])==3):Items(root["bombNotes"]);
            foreach(var pair in bombs.Zip(rawBombs,(note,source)=>(note,source)))
            {
                pair.note.IsFake=NoodleFakeData.IsFake(pair.source,major);NoodleGeometry.Apply(pair.note,pair.source,major);
                if(major==3&&(NoodleMotionFlags.HasActive(pair.source,major)
                    ||NoodleNominalTracks.Assigned(pair.source["customData"]?["track"],nominalTracks)))nominalBombMotion=true;
                if(major==3&&pair.source["customData"]?["animation"]?["offsetPosition"] is JToken offset
                    &&NoodleVectorCurve.TryRead(offset,out var curve)&&!curve!.AlwaysZero)nominalBombMotion=true;
            }
            bombs=bombs.OrderBy(n=>n.Beat).ToList();
        }
        if(noodleExtensions)NoodleBurstPreparation.Validate(notes,bombs,arcs,chains);
        var prepared=NotePreparation.Prepare(notes,bombs,arcs,chains,chainsBeforeArcs:major==4,prepareOrientation:orientation);
        if(nominalMotionNotes!=null&&nominalMotionNotes.Any(n=>n.ScoringType!=1||n.SliceCount!=1))
            throw new NotSupportedException("Non-default motion on linked or chain notes has not been validated");
        // NoodleNoteData applies this override to already processed note rows.
        // Direction also affects swings when the first-note metric is hidden.
        if(legacyDirectionAngles!=null)foreach(var pair in legacyDirectionAngles)
        {
            if(pair.Key.Direction!=8)pair.Key.Direction=1;
            pair.Key.CutDirectionAngleOffset=orientation?pair.Value:(float?)null;
        }
        if(orientation&&nominalMotionNotes!=null)foreach(var note in nominalMotionNotes)note.CutDirectionAngleOffset=null;
        var walls = new List<MapWall>();
        foreach (var w in includeSwingWindowWalls&&(features&(AnalysisFeatures.Average|AnalysisFeatures.Peak|AnalysisFeatures.Median|AnalysisFeatures.Reference))!=0?Items(root[major == 2 ? "_obstacles" : "obstacles"]):Enumerable.Empty<JToken>())
        {
            var data = major == 4 ? IndexedData("obstaclesData",w["i"]) : w;
            float beat = F(w[major == 2 ? "_time" : "b"]), end = beat + F(data[major == 2 ? "_duration" : "d"]);
            int type = I(data["_type"]);
            var wall=new MapWall { IsFake=noodleExtensions&&NoodleFakeData.IsFake(data,major),Beat=beat, EndBeat=end, Time=Time(beat), Duration=Time(end)-Time(beat), X=I(data[major == 2 ? "_lineIndex" : "x"]), Y=major==2 ? (type==1?2:0) : I(data["y"]), Width=I(data[major == 2 ? "_width" : "w"]), Height=major==2 ? (type==1?3:5) : I(data["h"]) };
            if(noodleExtensions&&!wall.IsFake)
            {
                var shape=BeatInsight.WallAlert.NoodleWallAnalysis.Read(data,major,true);
                if(correctedWallEnds.TryGetValue(w,out float physicalEnd))wall.CorrectedDuration=physicalEnd-wall.Time;
                wall.InfluencesWindowEnd=shape.Width>0&&shape.Height>0&&shape.X<3&&shape.X+shape.Width>1;
            }
            if((wall.Width>0||wall.InfluencesWindowEnd==true)&&wall.Duration>0)walls.Add(wall);
        }
        var mapFeatures = new List<string> { "v"+major };
        if(requirements.Contains("Chroma"))mapFeatures.Add("chroma");
        if(mappingExtensions)mapFeatures.Add("mapping-extensions");
        if(noodleExtensions)mapFeatures.Add("noodle-static-subset");
        if(independentDensity)mapFeatures.Add("noodle-density-only");
        if (changes.Count > 1) mapFeatures.Add("bpm-changes");
        if (rawArcs.Count>0) mapFeatures.Add("arcs");
        if (Items(root[major==4?"chains":"burstSliders"]).Any()) mapFeatures.Add("chains");
        if (notes.Any(n => n.Direction==8)) mapFeatures.Add("dots");
        float duration = needDuration?ReadDuration(info,test.Folder):0;
        return new LoadedMap { HasNoodleChains=noodleExtensions&&chains.Count>0,HasNoodleGeometry=wallInteraction||wallEntry||nominalBombMotion||nominalMotionNotes?.Count>0||notes.Concat(bombs).Concat(arcs.Select(a=>a.Head)).Concat(chains.Select(a=>a.Head)).Any(n=>n.NoodleX.HasValue||n.NoodleY.HasValue)||arcs.Concat(chains).Any(a=>a.NoodleTailX.HasValue||a.NoodleTailY.HasValue),Json=json,SquatJson=squatJson, AudioJson=audioJson, Bpm=bpm, Version=version, MappingExtensions=mappingExtensions,NoodleExtensions=noodleExtensions,Features=mapFeatures.ToArray(), Input=new MapInput { Duration=duration, Notes=prepared, Walls=walls.OrderBy(w=>w.Time).ToArray() } };
    }
    static float ReadDuration(JObject info,string folder)
    {
        float duration=F(info["audio"]?["songDuration"]);
        return duration>0?duration:OggDuration(SafePath(folder,(string)(info["audio"]?["songFilename"]??info["_songFilename"])!));
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
