using BeatInsight.Core;
using BeatInsight.Formats;
using BeatInsight.WallAlert;
using Newtonsoft.Json.Linq;
using System.Runtime.Loader;

namespace BeatInsight.Compare;

static class NoodleFakeChecks
{
    public static object Run(OriginalOracle oracle,string gameRoot,string reportPath)
    {
        string fixtures=Path.Combine(Path.GetDirectoryName(Path.GetFullPath(reportPath))!,"noodle-fake-fixtures");
        var results=new List<object>();int failed=0;
        foreach(int major in new[]{2,3})foreach(string kind in new[]{"plain","duplicates","arc","arc-duplicates","tilted","all-fake","fake-wall","custom-first","real-uninteractable"})
        {
            string folder=Path.Combine(fixtures,$"v{major}-{kind}");Directory.CreateDirectory(folder);
            string custom=major==2?"_customData":"customData",fakeKey=major==2?"_fake":"NE_fake";
            JObject Note(float b,int x,bool fake,int direction=1)=>major==2?
                new JObject {["_time"]=b,["_lineIndex"]=x,["_lineLayer"]=0,["_type"]=0,["_cutDirection"]=direction,[custom]=new JObject {[fakeKey]=fake}}:
                new JObject {["b"]=b,["x"]=x,["y"]=0,["c"]=0,["d"]=direction,[custom]=new JObject {[fakeKey]=fake}};
            var real=new JArray(Note(2,0,kind=="all-fake"),Note(4,1,kind=="all-fake"));
            var fakes=new JArray(Note(1,0,true,0),Note(3,2,true));
            if(kind=="duplicates"||kind=="arc-duplicates"){fakes.Add(Note(2,0,true));fakes.Add(Note(4,1,true));}
            if(kind=="tilted"){var tilted=Note(2,1,true);tilted[major==2?"_lineLayer":"y"]=2;fakes.Add(tilted);}
            var root=new JObject {[major==2?"_version":"version"]=major==2?"2.6.0":"3.3.0",[major==2?"_notes":"colorNotes"]=real,[custom]=new JObject()};
            if(major==2)foreach(var n in fakes)real.Add(n.DeepClone());else root[custom]!["fakeColorNotes"]=fakes;
            JObject wall=major==2?new JObject {["_time"]=5,["_duration"]=2,["_lineIndex"]=0,["_width"]=4,["_type"]=1,[custom]=new JObject {[fakeKey]=true,["_position"]=new JArray(50,30),["_animation"]=new JObject()}}:
                new JObject {["b"]=5,["d"]=2,["x"]=0,["y"]=2,["w"]=4,["h"]=3,[custom]=new JObject {["coordinates"]=new JArray(50,30),["animation"]=new JObject()}};
            if(kind=="fake-wall")
            {if(major==2)root["_obstacles"]=new JArray(wall);else root[custom]!["fakeObstacles"]=new JArray(wall);}
            if(kind=="arc"||kind=="arc-duplicates")root[major==2?"_sliders":"sliders"]=major==2?
                new JArray(new JObject {["_headTime"]=2,["_tailTime"]=4,["_headLineIndex"]=0,["_headLineLayer"]=0,["_tailLineIndex"]=1,["_tailLineLayer"]=0,["_colorType"]=0}):
                new JArray(new JObject {["b"]=2,["tb"]=4,["x"]=0,["y"]=0,["tx"]=1,["ty"]=0,["c"]=0,["d"]=1,["tc"]=1});
            if(kind=="arc-duplicates")
            {
                if(major==2){var bomb=Note(4,1,true);bomb["_type"]=3;real.Add(bomb);}
                else root[custom]!["fakeBombNotes"]=new JArray(new JObject {["b"]=4,["x"]=1,["y"]=0});
            }
            if(kind=="custom-first"){var property=root.Property(custom)!;property.Remove();root.AddFirst(property);}
            if(kind=="real-uninteractable")real[0]![custom]![major==2?"_interactable":"uninteractable"]=major==2?false:true;
            var info=new JObject {["_version"]="2.1.0",["_beatsPerMinute"]=120,["audio"]=new JObject {["songDuration"]=10},
                ["_difficultyBeatmapSets"]=new JArray(new JObject {["_beatmapCharacteristicName"]="Standard",["_difficultyBeatmaps"]=new JArray(new JObject {["_difficulty"]="Expert",["_beatmapFilename"]="map.dat",["_customData"]=new JObject {["_requirements"]=new JArray("Noodle Extensions")}})})};
            File.WriteAllText(Path.Combine(folder,"Info.dat"),info.ToString());File.WriteAllText(Path.Combine(folder,"map.dat"),root.ToString());
            var differences=new Dictionary<string,object>();string status="pass";
            try
            {
                var map=LocalMap.Load(new MapCase {Folder=folder,Characteristic="Standard",Difficulty="Expert",File="map.dat"});
                if(kind=="real-uninteractable")differences["interaction"]="Real uninteractable note was silently treated as fake";
                var game=GameLoader.Load(gameRoot,map.Json,map.Bpm,noodleExtensions:true);
                var native=OracleBridge.GetItems(game).OfType<NoteData>().Where(n=>(int)n.colorType==0).Select(n=>(n.time,n.lineIndex,Fake:NativeFakeRegistry.IsFake(n))).ToArray();
                var ours=map.Input.Notes.Where(n=>n.Color==0).Select(n=>(time:n.Time,lineIndex:n.X,Fake:n.IsFake)).ToArray();
                foreach(var n in map.Input.Notes.Where(n=>n.Color==0))
                {
                    var source=OracleBridge.GetItems(game).OfType<NoteData>().FirstOrDefault(v=>Math.Abs(v.time-n.Time)<1e-5&&v.lineIndex==n.X&&(int)v.noteLineLayer==n.Y&&NativeFakeRegistry.IsFake(v)==n.IsFake);
                    if(source==null||!n.CutDirectionAngleOffset.HasValue||Math.Abs(source.cutDirectionAngleOffset-n.CutDirectionAngleOffset.Value)>1e-4)differences["rotation-"+n.Beat+"-"+n.X]="Fake row changed the declared physical tilt";
                }
                if(!native.OrderBy(n=>n.time).ThenBy(n=>n.lineIndex).ThenBy(n=>n.Fake).SequenceEqual(ours.OrderBy(n=>n.time).ThenBy(n=>n.lineIndex).ThenBy(n=>n.Fake)))differences["fake-identity"]="Native custom metadata differs, including coincident objects";
                var original=oracle.Analyze(map.Input,game);var compatible=CompatibleAnalysis.Analyze(map.Input,history:OriginalOracle.HistoryFixture(map.Input));
                foreach(var d in Comparison.Diff(original,compatible))differences[d.Key]=d.Value;
                if(Comparison.InputDiff(oracle.LastPrepared.Notes,map.Input.Notes.Where(n=>n.ScoringType<=3).ToArray())!=0)differences["prepared-notes"]="Fake objects changed native input order or annotation";
                var corrected=CompatibleAnalysis.Analyze(map.Input,reproduceKnownDefects:false);
                if(corrected.LeftBins.Sum()!=(kind=="all-fake"?0:2))differences["corrected-swings"]="Fake notes entered corrected swing counts";
                if(compatible.LeftBins.Sum()<=corrected.LeftBins.Sum())differences["raw-loader-fake-diagnostic"]="Raw-loader fake diagnostic changed; this does not validate extension gameplay";
                if(kind=="fake-wall"&&WallAlertAdapter.Count(map.SquatJson??map.Json,map.Bpm)!=0)differences["squats"]="Decorative wall created a squat";
                var menu=SelectedMapAnalyzer.Load(new SelectedMapRequest("fake-fixture","Standard","Expert",3,LocalMap.SafePath(folder,"map.dat"),features:AnalysisFeatures.Average|AnalysisFeatures.FirstNotes|AnalysisFeatures.Squats));
                if(menu.HasLeftNote!=(kind!="all-fake")||menu.FirstLeftNote?.IsFake==true)differences["first-note"]="Fake note became the first gameplay cube";
                if(kind!="all-fake")
                {
                    var marker=DeathHistory.LegacyMarkers(new[]{new PlayRecord {LastNote=1,Date=1}},map.Input).Single();
                    if(marker.SongTimeSeconds!=2)differences["legacy-index"]="Fake notes shifted the second real note's history position";
                }
                if(File.ReadAllText(Path.Combine(folder,"map.dat"))!=map.Json)differences["mutation"]="Original map JSON was changed";
            }
            catch(NotSupportedException e){status="unsupported";if(kind!="real-uninteractable")differences["unexpected-rejection"]=e.Message;}
            catch(Exception e){differences["error"]=(e.InnerException??e).Message;}
            if(differences.Count>0){failed++;status="mismatch";}
            results.Add(new {major,kind,status,differences});
        }
        int runtimeMetadataCases=0;
        var customAssembly=AssemblyLoadContext.Default.Assemblies.Single(a=>a.GetName().Name=="CustomJSONData");
        var customType=customAssembly.GetType("CustomJSONData.CustomBeatmap.CustomData",true)!;
        var obstacleType=customAssembly.GetType("CustomJSONData.CustomBeatmap.CustomObstacleData",true)!;
        var constructor=obstacleType.GetConstructors().Single();
        foreach(int major in new[]{2,3})
        {
            var data=(System.Collections.IDictionary)Activator.CreateInstance(customType)!;
            string flag=major==2?"_fake":"NE_fake";data[flag]=true;
            var obstacle=constructor.Invoke(new object[]{1f,2f,4f,0,0,Enum.ToObject(constructor.GetParameters()[5].ParameterType,2),1f,4,3,data,new Version(major,0)});
            bool plainGeometry=CustomObjectFlags.HasCustomWallGeometry(obstacle);runtimeMetadataCases++;
            data[major==2?"_position":"coordinates"]=new List<object>{-1f,1.5f};
            bool customGeometry=CustomObjectFlags.HasCustomWallGeometry(obstacle);runtimeMetadataCases++;
            data.Remove(major==2?"_position":"coordinates");
            if(plainGeometry||!customGeometry){failed++;results.Add(new {major,status="mismatch",error="Runtime obstacle geometry guard failed"});}
            bool fake=CustomObjectFlags.IsFake(obstacle);runtimeMetadataCases++;
            data[flag]=false;data[major==2?"_interactable":"uninteractable"]=major==2?false:true;
            bool real=CustomObjectFlags.IsFake(obstacle);runtimeMetadataCases++;
            data[major==2?"NE_fake":"_fake"]=true;bool alias=CustomObjectFlags.IsFake(obstacle);runtimeMetadataCases++;
            if(!fake||real||alias){failed++;results.Add(new {major,status="mismatch",error="Runtime fake obstacle metadata was confused with interaction or the other version's flag"});}
        }
        return new {schemaVersion=1,profileVersion=BeatInsight.Formats.LocalMap.AnalysisProfileVersion,cases=18,runtimeMetadataCases,failed,gameStarted=false,userHistoryRead=false,referenceRole="raw-loader-compatibility-diagnostic-only",fullExtensionRuntime="not-tested",
            scope="installed custom JSON and Noodle fake-array deserializers, native object conversion and reference IL; fake/real swing separation, legacy indices and nominal row geometry",results};
    }
}
