using BeatInsight.Core;
using BeatInsight.Formats;
using BeatInsight.WallAlert;
using Newtonsoft.Json.Linq;

namespace BeatInsight.Compare;

static class ChromaChecks
{
    public static object Run(OriginalOracle oracle,string gameRoot,string reportPath,string? requestsPath=null)
    {
        string fixtures=Path.Combine(Path.GetDirectoryName(Path.GetFullPath(reportPath))!,"chroma-fixtures");
        var cases=new List<(string Name,MapCase Request,bool Reject)>();
        foreach(int version in new[]{2,3,4})
        foreach(string kind in new[]{"required","suggested","mixed-mapping","mixed-noodle","mixed-unknown","fake"})
        {
            string name=$"v{version}-{kind}",folder=Path.Combine(fixtures,name);Directory.CreateDirectory(folder);
            string custom=version==2?"_customData":"customData";
            var visual=new JObject {[version==2?"_color":"color"]=new JArray(.2,.8,.1,1),
                [version==2?"_disableSpawnEffect":"spawnEffect"]=false};
            JObject Note(int beat,int x,int color)=>version==2?
                new JObject {["_time"]=beat,["_lineIndex"]=x,["_lineLayer"]=0,["_type"]=color,["_cutDirection"]=1,[custom]=visual.DeepClone()}:
                new JObject {["b"]=beat,["x"]=x,["y"]=0,["c"]=color,["d"]=1,[custom]=visual.DeepClone()};
            JObject map=version==2?new JObject {["_version"]="2.6.0",["_notes"]=new JArray(Note(1,0,0),Note(2,1,0),Note(4,3,1)),
                ["_events"]=new JArray(new JObject {["_time"]=0,["_type"]=1,["_value"]=1,[custom]=visual.DeepClone()}),
                ["_obstacles"]=new JArray(new JObject {["_time"]=5,["_duration"]=1,["_lineIndex"]=0,["_width"]=4,["_type"]=1,[custom]=visual.DeepClone()})}:
                new JObject {["version"]=version==3?"3.3.0":"4.1.0",["colorNotes"]=new JArray(Note(1,0,0),Note(2,1,0),Note(4,3,1)),
                    ["obstacles"]=new JArray(new JObject {["b"]=5,["d"]=1,["x"]=0,["y"]=2,["w"]=4,["h"]=3,[custom]=visual.DeepClone()})};
            map[custom]=new JObject {[version==2?"_environment":"environment"]=new JArray(),
                [version==2?"_customEvents":"customEvents"]=new JArray()};
            if(version==4)
            {
                map["colorNotesData"]=new JArray(Note(1,0,0),Note(2,1,0),Note(4,3,1));
                map["colorNotes"]=new JArray(new JObject {["b"]=1,["i"]=0},new JObject {["b"]=2,["i"]=1},new JObject {["b"]=4,["i"]=2});
                map["obstaclesData"]=map["obstacles"]!.DeepClone();map["obstacles"]=new JArray(new JObject {["b"]=5,["i"]=0});
                File.WriteAllText(Path.Combine(folder,"AudioData.dat"),"{\"songFrequency\":44100,\"bpmData\":[{\"si\":0,\"ei\":441000,\"sb\":0,\"eb\":20}]}");
            }
            var capabilities=new JArray("Chroma");
            if(kind.StartsWith("mixed-"))capabilities.Add(kind switch {"mixed-mapping"=>"Mapping Extensions","mixed-noodle"=>"Noodle Extensions",_=>"Unknown Extension"});
            var metadata=new JObject {[version==2?(kind=="suggested"?"_suggestions":"_requirements"):(kind=="suggested"?"suggestions":"requirements")]=capabilities};
            if(kind=="fake")map[custom]![version==2?"_fake":"fakeColorNotes"]=version==2?new JValue(true):new JArray(Note(2,0,0));
            var difficulty=new JObject {[version==2?"_difficulty":"difficulty"]="Expert",[version==2?"_beatmapFilename":"beatmapDataFilename"]="map.dat",[custom]=metadata};
            JObject info;
            if(version==2)info=new JObject {["_version"]="2.1.0",["_beatsPerMinute"]=120,["audio"]=new JObject {["songDuration"]=10},
                ["_difficultyBeatmapSets"]=new JArray(new JObject {["_beatmapCharacteristicName"]="Standard",["_difficultyBeatmaps"]=new JArray(difficulty)})};
            else{difficulty["characteristic"]="Standard";info=new JObject {["version"]="4.0.1",["audio"]=new JObject {["bpm"]=120,["songDuration"]=10,["audioDataFilename"]="AudioData.dat"},["difficultyBeatmaps"]=new JArray(difficulty)};}
            File.WriteAllText(Path.Combine(folder,"Info.dat"),info.ToString());File.WriteAllText(Path.Combine(folder,"map.dat"),map.ToString());
            cases.Add((name,new MapCase {Folder=folder,Characteristic="Standard",Difficulty="Expert",File="map.dat"},(kind.StartsWith("mixed-")&&kind!="mixed-mapping"&&kind!="mixed-noodle")||kind=="mixed-noodle"&&version==4||kind=="fake"));
        }
        int libraryCases=0;
        if(requestsPath!=null)
            foreach(var request in JArray.Parse(File.ReadAllText(requestsPath)))cases.Add(("library-"+libraryCases++,request.ToObject<MapCase>()!,false));
        var results=new List<object>();int failed=0,rejected=0;
        foreach(var fixture in cases)
        {
            var differences=new Dictionary<string,object>();string status="pass";
            try
            {
                var map=LocalMap.Load(fixture.Request);
                if(fixture.Reject)differences["unsafe-acceptance"]="Unsupported gameplay extension or fake objects were accepted";
                else
                {
                    var game=GameLoader.Load(gameRoot,map.Json,map.Bpm,map.AudioJson,mappingExtensions:map.MappingExtensions,noodleExtensions:map.NoodleExtensions);
                    var expected=oracle.Analyze(map.Input,game);
                    var actual=CompatibleAnalysis.Analyze(map.Input,history:OriginalOracle.HistoryFixture(map.Input));
                    foreach(var d in Comparison.Diff(expected,actual))differences[d.Key]=d.Value;
                    var notes=map.Input.Notes.Where(n=>n.ScoringType<=3).ToArray();
                    if(Comparison.InputDiff(oracle.LastPrepared.Notes,notes)>0&&!(differences.Count==0&&Comparison.SameSimultaneousNotes(oracle.LastPrepared.Notes,notes)))
                        differences["prepared-notes"]="Native gameplay inputs differ";
                    int squats=WallAlertAdapter.Count(map.SquatJson??map.Json,map.Bpm);
                    foreach(var feature in new[]{AnalysisFeatures.Average,AnalysisFeatures.Peak,AnalysisFeatures.Median,AnalysisFeatures.FirstNotes,AnalysisFeatures.Density,AnalysisFeatures.ImportedDeaths})
                    {
                        var selective=LocalMap.Load(fixture.Request,feature);
                        if(feature==AnalysisFeatures.Density)
                        {
                            var fullDensity=CompatibleAnalysis.Analyze(map.Input,reproduceKnownDefects:false,features:feature);
                            var selectiveDensity=CompatibleAnalysis.Analyze(selective.Input,reproduceKnownDefects:false,features:feature);
                            if(!fullDensity.Segments.SequenceEqual(selectiveDensity.Segments))differences["selective-"+feature]="Selective density differs";
                        }
                        else if(Comparison.InputDiff(map.Input.Notes,selective.Input.Notes)>0)differences["selective-"+feature]="Selective note preparation changed";
                    }
                    int rank=Array.IndexOf(new[]{"Easy","Normal","Hard","Expert","ExpertPlus"},fixture.Request.Difficulty);
                    var menu=SelectedMapAnalyzer.Load(new SelectedMapRequest("synthetic-chroma",fixture.Request.Characteristic,fixture.Request.Difficulty,rank,
                        LocalMap.SafePath(fixture.Request.Folder,fixture.Request.File),features:AnalysisFeatures.Average|AnalysisFeatures.Peak|AnalysisFeatures.Median|AnalysisFeatures.FirstNotes|AnalysisFeatures.Density|AnalysisFeatures.Squats));
                    if(menu.SquatError!=null||menu.SquatCount!=squats)differences["menu-squats"]="Menu squat analysis failed or differs";
                    foreach(var feature in new[]{AnalysisFeatures.Squats,AnalysisFeatures.OwnDeaths})
                    {
                        var hidden=LocalMap.Load(fixture.Request,feature);
                        if(hidden.AudioJson!=null||hidden.Input.Notes.Length!=0)differences["hidden-"+feature]="Hidden note analysis performed work";
                    }
                }
            }
            catch(NotSupportedException e){status="unsupported";rejected++;if(!fixture.Reject)differences["unexpected-rejection"]=e.Message;}
            catch(Exception e){differences["error"]=(e.InnerException??e).Message;}
            if(differences.Count>0){failed++;status="mismatch";}
            results.Add(new {fixture.Name,status,differences});
        }
        return new {schemaVersion=1,profileVersion=BeatInsight.Formats.LocalMap.AnalysisProfileVersion,cases=cases.Count,libraryCases,failed,rejected,gameStarted=false,userHistoryRead=false,
            scope="Chroma-only gameplay statistics against vanilla loader and reference IL; visual effects and extension runtime not executed",results};
    }
}
