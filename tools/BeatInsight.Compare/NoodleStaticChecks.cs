using BeatInsight.Core;
using BeatInsight.Formats;
using BeatInsight.WallAlert;
using Newtonsoft.Json.Linq;
using System.Security.Cryptography;

namespace BeatInsight.Compare;

static class NoodleStaticChecks
{
    public static object Run(OriginalOracle oracle,string gameRoot,string reportPath)
    {
        string fixtures=Path.Combine(Path.GetDirectoryName(Path.GetFullPath(reportPath))!,"noodle-static-fixtures");
        var results=new List<object>();int failed=0,rejected=0,cases=0;
        foreach(int major in new[]{2,3})
        foreach(string kind in new[]{"bare","cosmetic","false-fake","empty-fakes","chroma","coordinates","rotation","size","track","animation","unknown","fake","uninteractable","player","fake-array","invalid-flag","own-only"})
        {
            cases++;string name=$"v{major}-{kind}",folder=Path.Combine(fixtures,name);Directory.CreateDirectory(folder);
            string customKey=major==2?"_customData":"customData";
            var noteCustom=new JObject();
            var root=new JObject {[major==2?"_version":"version"]=major==2?"2.6.0":"3.3.0",
                [major==2?"_notes":"colorNotes"]=major==2?
                    new JArray(new JObject {["_time"]=1,["_lineIndex"]=0,["_lineLayer"]=0,["_type"]=0,["_cutDirection"]=1,[customKey]=noteCustom},new JObject {["_time"]=3,["_lineIndex"]=3,["_lineLayer"]=1,["_type"]=1,["_cutDirection"]=8}):
                    new JArray(new JObject {["b"]=1,["x"]=0,["y"]=0,["c"]=0,["d"]=1,[customKey]=noteCustom},new JObject {["b"]=3,["x"]=3,["y"]=1,["c"]=1,["d"]=8}),
                [customKey]=new JObject()};
            // JObject insertion can clone an already parented token; edit the stored object.
            noteCustom=(JObject)root[major==2?"_notes":"colorNotes"]![0]![customKey]!;
            switch(kind)
            {
                case "cosmetic": case "chroma":noteCustom[major==2?"_color":"color"]=new JArray(1,0,0);noteCustom[major==2?"_noteJumpMovementSpeed":"noteJumpMovementSpeed"]=20;break;
                case "false-fake":noteCustom[major==2?"_fake":"NE_fake"]=false;break;
                case "empty-fakes":root[customKey]!["fakeColorNotes"]=new JArray();root[customKey]!["fakeObstacles"]=new JArray();break;
                case "fake":noteCustom[major==2?"_fake":"NE_fake"]=true;break;
                case "invalid-flag":noteCustom[major==2?"_fake":"NE_fake"]="false";break;
                case "coordinates":noteCustom[major==2?"_position":"coordinates"]=new JArray(.5,1);break;
                case "rotation":noteCustom[major==2?"_localRotation":"localRotation"]=new JArray(0,0,30);break;
                case "size":noteCustom[major==2?"_scale":"scale"]=new JArray(2,2,2);break;
                case "track":noteCustom[major==2?"_track":"track"]="moving-notes";root[customKey]![major==2?"_customEvents":"customEvents"]=new JArray(new JObject {[major==2?"_type":"t"]="AnimateTrack"});break;
                case "animation":case "own-only":noteCustom[major==2?"_animation":"animation"]=new JObject {["position"]=new JArray(0,1)};break;
                case "unknown":noteCustom["unknownGameplayProperty"]=true;break;
                case "uninteractable":noteCustom[major==2?"_interactable":"uninteractable"]=major==2?false:true;break;
                case "player":root[customKey]![major==2?"_customEvents":"customEvents"]=new JArray(new JObject {[major==2?"_type":"t"]="AssignPlayerToTrack"});break;
                case "fake-array":root[customKey]!["fakeColorNotes"]=new JArray(new JObject {["b"]=2,["x"]=1,["y"]=1,["c"]=0,["d"]=1});break;
            }
            var requirements=new JArray("Noodle Extensions");if(kind=="chroma")requirements.Add("Chroma");
            var info=new JObject {["_version"]="2.1.0",["_beatsPerMinute"]=120,["audio"]=new JObject {["songDuration"]=10},
                ["_difficultyBeatmapSets"]=new JArray(new JObject {["_beatmapCharacteristicName"]="Standard",["_difficultyBeatmaps"]=new JArray(new JObject {["_difficulty"]="Expert",["_beatmapFilename"]="map.dat",["_customData"]=new JObject {["_requirements"]=requirements}})})};
            File.WriteAllText(Path.Combine(folder,"Info.dat"),info.ToString());File.WriteAllText(Path.Combine(folder,"map.dat"),root.ToString());
            bool reject=kind is not ("bare" or "cosmetic" or "false-fake" or "empty-fakes" or "chroma" or "own-only" or "fake" or "fake-array" or "coordinates");
            var differences=new Dictionary<string,object>();string status="pass";
            try
            {
                var request=new MapCase {Folder=folder,Characteristic="Standard",Difficulty="Expert",File="map.dat"};
                var map=LocalMap.Load(request,kind=="own-only"?AnalysisFeatures.OwnDeaths:AnalysisFeatures.All);
                if(reject)differences["unsafe-acceptance"]="Unvalidated gameplay transformation was accepted";
                else if(kind=="own-only")
                {
                    if(map.Input.Notes.Length!=0||map.AudioJson!=null)differences["hidden-work"]="Hidden gameplay was analyzed for own marker data";
                }
                else if(kind=="coordinates")
                {
                    var note=map.Input.Notes.First();
                    if(note.NoodleX!=2.5f||note.NoodleY!=1f)differences["coordinates"]="Independent static coordinate fixture differs";
                }
                else
                {
                    var game=GameLoader.Load(gameRoot,map.Json,map.Bpm,map.AudioJson,noodleExtensions:map.NoodleExtensions);
                    foreach(var d in Comparison.Diff(oracle.Analyze(map.Input,game),CompatibleAnalysis.Analyze(map.Input,history:OriginalOracle.HistoryFixture(map.Input))))differences[d.Key]=d.Value;
                    if(Comparison.InputDiff(oracle.LastPrepared.Notes,map.Input.Notes.Where(n=>n.ScoringType<=3).ToArray())>0)differences["prepared-notes"]="Nominal note input differs";
                    var menu=SelectedMapAnalyzer.Load(new SelectedMapRequest("synthetic-noodle",request.Characteristic,request.Difficulty,3,LocalMap.SafePath(folder,"map.dat"),features:AnalysisFeatures.Average|AnalysisFeatures.Peak|AnalysisFeatures.Median|AnalysisFeatures.FirstNotes|AnalysisFeatures.Density|AnalysisFeatures.Squats));
                    if(menu.SquatError!=null||menu.SquatCount!=WallAlertAdapter.Count(map.SquatJson??map.Json,map.Bpm))differences["menu"]="Menu data path failed";
                }
            }
            catch(NotSupportedException e){status="unsupported";rejected++;if(!reject)differences["unexpected-rejection"]=e.Message;}
            catch(Exception e){differences["error"]=(e.InnerException??e).Message;}
            if(differences.Count>0){failed++;status="mismatch";}
            results.Add(new {name,status,differences});
        }
        string assembly=Path.Combine(gameRoot,"Plugins/NoodleExtensions.dll");
        string? extensionHash=File.Exists(assembly)?Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(assembly))):null;
        if(extensionHash!=null&&extensionHash!="FE50124E33631EED7ECB78EA1A19BB7EA64CC12974B0298DAD991672C2ABDA95")
            throw new InvalidDataException("Noodle Extensions changed; reinspect nominal row processing before comparing the subset");
        return new {schemaVersion=1,profileVersion=BeatInsight.Formats.LocalMap.AnalysisProfileVersion,cases,failed,rejected,gameStarted=false,userHistoryRead=false,referenceRole="raw-loader-compatibility-diagnostic-only",fullExtensionRuntime="not-tested",
            inspectedExtensionSha256=extensionHash,
            scope="nominal Noodle gameplay subset with fake identity; installed custom JSON/fake deserializers and reference IL; spatial/interaction overrides remain restricted",results};
    }
}
