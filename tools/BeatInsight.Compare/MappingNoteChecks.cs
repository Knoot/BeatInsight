using BeatInsight.Core;
using BeatInsight.Formats;
using Newtonsoft.Json.Linq;

namespace BeatInsight.Compare;

static class MappingNoteChecks
{
    public static object Run(OriginalOracle oracle,string gameRoot,string reportPath)
    {
        string fixtures=Path.Combine(Path.GetDirectoryName(Path.GetFullPath(reportPath))!,"mapping-note-fixtures");
        var results=new List<object>();int failed=0,orientations=0;
        foreach(int major in new[]{2,3,4})
        {
            string folder=Path.Combine(fixtures,"v"+major);Directory.CreateDirectory(folder);
            var notes=new JArray();int beat=1;
            JObject Note(float b,int x,int y,int direction,int color=0)=>major==2?
                new JObject {["_time"]=b,["_lineIndex"]=x,["_lineLayer"]=y,["_type"]=color,["_cutDirection"]=direction}:
                new JObject {["b"]=b,["x"]=x,["y"]=y,["c"]=color,["d"]=direction,["a"]=17};
            foreach(var position in new[]{(-2000,-1000),(-1,4),(1000,1000),(1500,2500),(5000,3333),(0,0)})
                foreach(int direction in new[]{1000,1090,1180,1270,1360,2000,2090,2360})notes.Add(Note(beat++,position.Item1,position.Item2,direction));
            foreach(var pair in new[]{(1,1),(8,8),(1000,1000),(2090,2090),(1090,8)})
            {notes.Add(Note(beat,1000,1000,pair.Item1));notes.Add(Note(beat++,2000,2000,pair.Item2));}
            // Extended coordinates also pass through arc/chain head and tail annotation.
            notes.Add(Note(60,1500,1000,1090,1));notes.Add(Note(62,2500,2000,1090,1));notes.Add(Note(64,3500,3000,2090,1));
            notes.Add(Note(70,0,0,1000));notes.Add(Note(70.04f,0,0,1360));
            var root=new JObject {[major==2?"_version":"version"]=major==2?"2.6.0":major==3?"3.3.0":"4.1.0",[major==2?"_notes":"colorNotes"]=notes};
            if(major==2)
            {
                root["_sliders"]=new JArray(new JObject {["_headTime"]=60,["_tailTime"]=62,["_headLineIndex"]=1500,["_headLineLayer"]=1000,["_tailLineIndex"]=2500,["_tailLineLayer"]=2000,["_colorType"]=1});
                notes.Add(Note(63,-2000,3333,1,3));
            }
            else
            {
                root["sliders"]=new JArray(new JObject {["b"]=60,["tb"]=62,["x"]=1500,["y"]=1000,["tx"]=2500,["ty"]=2000,["c"]=1,["d"]=1090,["tc"]=1090,["mu"]=1,["tmu"]=1});
                root["burstSliders"]=new JArray(new JObject {["b"]=64,["tb"]=65,["x"]=3500,["y"]=3000,["tx"]=4500,["ty"]=3500,["c"]=1,["d"]=2090,["sc"]=4,["s"]=1});
                root["bombNotes"]=new JArray(new JObject {["b"]=63,["x"]=-2000,["y"]=3333});
            }
            if(major==4)
            {
                root["colorNotesData"]=notes.DeepClone();root["colorNotes"]=new JArray(notes.Select((n,i)=>new JObject {["b"]=n["b"]!.DeepClone(),["i"]=i}));
                root.Remove("sliders");root.Remove("burstSliders");
                root["arcs"]=new JArray(new JObject {["hb"]=60,["tb"]=62,["hi"]=58,["ti"]=59,["ai"]=0});root["arcsData"]=new JArray(new JObject {["m"]=1,["tm"]=1});
                root["chains"]=new JArray(new JObject {["hb"]=64,["tb"]=65,["i"]=60,["ci"]=0});root["chainsData"]=new JArray(new JObject {["tx"]=4500,["ty"]=3500,["c"]=4,["s"]=1});
                root["bombNotesData"]=root["bombNotes"]!.DeepClone();root["bombNotes"]=new JArray(new JObject {["b"]=63,["i"]=0});
                File.WriteAllText(Path.Combine(folder,"AudioData.dat"),"{\"songFrequency\":44100,\"bpmData\":[{\"si\":0,\"ei\":4410000,\"sb\":0,\"eb\":200}]}");
            }
            var difficulty=new JObject {["_difficulty"]="Expert",["_beatmapFilename"]="map.dat",["_customData"]=new JObject {["_requirements"]=new JArray("Mapping Extensions")}};
            var info=new JObject {["_version"]="2.1.0",["_beatsPerMinute"]=120,["audio"]=new JObject {["bpm"]=120,["songDuration"]=100,["audioDataFilename"]="AudioData.dat"},
                ["_difficultyBeatmapSets"]=new JArray(new JObject {["_beatmapCharacteristicName"]="Standard",["_difficultyBeatmaps"]=new JArray(difficulty)})};
            File.WriteAllText(Path.Combine(folder,"Info.dat"),info.ToString());File.WriteAllText(Path.Combine(folder,"map.dat"),root.ToString());
            var differences=new Dictionary<string,object>();
            try
            {
                var request=new MapCase {Folder=folder,Characteristic="Standard",Difficulty="Expert",File="map.dat"};
                var map=LocalMap.Load(request);
                var game=GameLoader.Load(gameRoot,map.Json,map.Bpm,map.AudioJson,mappingExtensions:true);
                var native=OracleBridge.GetItems(game).OfType<NoteData>().Select(n=>new {n.time,n.lineIndex,Y=(int)n.noteLineLayer,Color=(int)n.colorType,Direction=(int)n.cutDirection,Offset=n.cutDirectionAngleOffset}).ToArray();
                var expected=oracle.Analyze(map.Input,game);
                var actual=CompatibleAnalysis.Analyze(map.Input,history:OriginalOracle.HistoryFixture(map.Input));
                foreach(var d in Comparison.Diff(expected,actual))differences[d.Key]=d.Value;
                var corrected=CompatibleAnalysis.Analyze(map.Input,reproduceKnownDefects:false);
                if(actual.LeftBins[35]!=2||corrected.LeftBins[35]!=1)differences["REF-002"]="Equivalent 0/360 directions did not expose and correct the extra swing";
                if(Comparison.InputDiff(oracle.LastPrepared.Notes,map.Input.Notes.Where(n=>n.ScoringType<=3).ToArray())!=0)differences["prepared-notes"]="Encoded gameplay input differs";
                foreach(var n in map.Input.Notes)
                {
                    var source=native.FirstOrDefault(v=>Math.Abs(v.time-n.Time)<1e-5&&v.lineIndex==n.X&&v.Y==n.Y&&v.Color==n.Color&&v.Direction==n.Direction);
                    if(source==null||!n.CutDirectionAngleOffset.HasValue||Math.Abs(source.Offset-n.CutDirectionAngleOffset.Value)>1e-4)
                        differences["orientation-"+n.Beat+"-"+n.X]=new {native=source?.Offset,ours=n.CutDirectionAngleOffset};
                    orientations++;
                    var visual=FirstNoteVisual.From(n,DrawColor.White);
                    if(visual==null||MappingNotes.IsRotatedDot(n.Direction)&&visual.Direction!=8)differences["visual-"+n.Beat]="Missing note or rotated dot";
                }
                foreach(var feature in new[]{AnalysisFeatures.Average,AnalysisFeatures.Peak,AnalysisFeatures.Median,AnalysisFeatures.FirstNotes,AnalysisFeatures.Density,AnalysisFeatures.ImportedDeaths})
                    if(Comparison.InputDiff(map.Input.Notes,LocalMap.Load(request,feature).Input.Notes)!=0)differences["selective-"+feature]="Selective preparation differs";
            }
            catch(Exception e){differences["error"]=(e.InnerException??e).Message;}
            if(differences.Count>0)failed++;
            results.Add(new {major,status=differences.Count==0?"pass":"mismatch",differences});
        }
        return new {schemaVersion=1,profileVersion=BeatInsight.Formats.LocalMap.AnalysisProfileVersion,cases=3,orientations,failed,gameStarted=false,userHistoryRead=false,
            scope="v2/v3/v4 extended coordinates, directions, rotated dots, two-note rotations and arc/chain annotations; native adapter active",results};
    }
}
