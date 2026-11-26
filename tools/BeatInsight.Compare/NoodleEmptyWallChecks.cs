using System.Collections;
using System.Reflection;
using BeatInsight.Core;
using BeatInsight.Formats;
using BeatInsight.WallAlert;
using Newtonsoft.Json.Linq;

namespace BeatInsight.Compare;

static class NoodleEmptyWallChecks
{
    internal static object Run(string gameRoot,string? inventory=null)
    {
        int cases=0,failed=0;var results=new List<object>();
        int NativeCount(JObject root,int major)
        {
            var saved=NoodleNativeData.Deserialize(gameRoot,root,major.ToString());
            var items=(IEnumerable)(saved.GetType().GetProperty("obstacles")?.GetValue(saved)??saved.GetType().GetField("obstacles")?.GetValue(saved)
                ??throw new InvalidDataException("Native obstacle collection changed"));
            return items?.Cast<object>().Count()??0;
        }
        var info=JObject.Parse("""{"version":"4.0.1","audio":{"bpm":120,"songDuration":12},"difficultyBeatmaps":[{"characteristic":"Standard","difficulty":"Expert","beatmapDataFilename":"map.dat","customData":{"requirements":["Noodle Extensions"]}}]}""");
        foreach(int major in new[]{2,3})foreach(string shape in new[]{"missing","empty","null","ordinary","fake","invalid","fake-array"})
        foreach(bool own in new[]{false,true})
        {
            string wallKey=major==2?"_obstacles":"obstacles",customKey=major==2?"_customData":"customData";
            var root=JObject.Parse(major==2?"""{"_version":"2.6.0","_notes":[{"_time":2,"_lineIndex":0,"_lineLayer":0,"_type":0,"_cutDirection":1}],"_customData":{"_customEvents":[{"_time":0,"_type":"AssignPlayerToTrack","_data":{"_track":"player"}}]}}""":"""{"version":"3.3.0","colorNotes":[{"b":2,"x":0,"y":0,"c":0,"d":1}],"customData":{"customEvents":[{"b":0,"t":"AssignPlayerToTrack","d":{"track":"player"}}]}}""");
            if(shape=="empty")root[wallKey]=new JArray();
            if(shape=="null")root[wallKey]=JValue.CreateNull();
            if(shape=="invalid")root[wallKey]="invalid";
            if(shape is "ordinary" or "fake")
            {
                var wall=major==2?JObject.Parse("""{"_time":2,"_lineIndex":0,"_type":1,"_width":4,"_duration":1}"""):JObject.Parse("""{"b":2,"x":0,"y":2,"w":4,"h":3,"d":1}""");
                if(shape=="fake")wall[customKey]=new JObject{[major==2?"_fake":"NE_fake"]=true};
                root[wallKey]=new JArray(wall);
            }
            if(shape=="fake-array")
            {
                if(major==3)root[customKey]!["fakeObstacles"]=new JArray(new JObject{["b"]=2,["x"]=0,["y"]=2,["w"]=4,["h"]=3,["d"]=1});
                else root[wallKey]=new JArray(new JObject{["_time"]=2,["_lineIndex"]=0,["_type"]=1,["_width"]=4,["_duration"]=1,["_customData"]=new JObject{["_fake"]=true}});
            }
            bool accepted=false;int? count=null,nativeCount=null;bool unchanged=false;
            try
            {
                var loaded=LocalMap.LoadDocuments(new MapDocuments(info.ToString(),root.ToString(),file:"map.dat"),"Standard","Expert",AnalysisFeatures.Squats|(own?AnalysisFeatures.OwnDeaths:0));
                accepted=true;count=WallAlertAdapter.Count(loaded.SquatJson??loaded.Json,120,noodleExtensions:true);
                nativeCount=NativeCount(root,major);unchanged=loaded.Json==root.ToString()&&loaded.Input.Notes.Length==0;
            }
            catch(NotSupportedException){}
            bool expected=shape is "missing" or "empty";
            bool pass=accepted==expected&&(!expected||count==0&&nativeCount==0&&unchanged);
            cases++;if(!pass)failed++;results.Add(new{major,shape,own,accepted,count,nativeCount,pass});
        }
        if(inventory!=null)foreach(var item in JArray.Parse(File.ReadAllText(inventory)))
        {
            var selected=item.ToObject<MapCase>()!;string json=File.ReadAllText(Path.Combine(selected.Folder,selected.File));var root=JObject.Parse(json);
            int major=Version.Parse((string)(root["_version"]??root["version"])!).Major;
            var loaded=LocalMap.Load(selected,AnalysisFeatures.Squats|AnalysisFeatures.OwnDeaths);
            int own=WallAlertAdapter.Count(loaded.SquatJson??loaded.Json,loaded.Bpm,noodleExtensions:true),native=NativeCount(root,major);
            bool pass=own==0&&native==0&&loaded.Json==json;
            cases++;if(!pass)failed++;results.Add(new{name=Path.GetFileName(selected.Folder),own,native,pass});
        }
        return new{cases,failed,scope="Actual custom v2/v3 saved obstacle collections and independent zero-wall counter; no player pose or nonempty collision admission",results};
    }
}
