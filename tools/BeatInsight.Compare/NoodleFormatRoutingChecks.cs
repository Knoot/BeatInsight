using BeatInsight.Core;
using BeatInsight.Formats;
using Mono.Cecil;
using Newtonsoft.Json.Linq;

namespace BeatInsight.Compare;

static class NoodleFormatRoutingChecks
{
    internal static object Run(string gameRoot)
    {
        string path=Path.Combine(gameRoot,"Plugins/CustomJSONData.dll");
        if(Comparison.HashFile(path)!="4F9F8EFD8012159D8995B6D936E3C21B6AA6A35E319686FA3E9B04C2B6331F7C")throw new InvalidDataException("Reinspect changed custom format loader");
        using var module=ModuleDefinition.ReadModule(path);
        var saveTypes=module.Types.Where(t=>t.Namespace=="CustomJSONData.CustomBeatmap"&&t.Name.EndsWith("CustomBeatmapSaveData",StringComparison.Ordinal)).Select(t=>t.Name).OrderBy(t=>t).ToArray();
        int cases=1,failed=saveTypes.SequenceEqual(new[]{"Version2_6_0AndEarlierCustomBeatmapSaveData","Version3CustomBeatmapSaveData"})?0:1;
        var rows=new List<object>();
        foreach(string major in new[]{"2","3","4","5","1","0","03",""})
        {
            var root=JObject.Parse(major=="2"?"""{"_version":"2.6.0","_notes":[]}""":"""{"version":"3.3.0","colorNotes":[]}""");
            string? type=null;bool rejected=false;
            try{type=NoodleNativeData.Deserialize(gameRoot,root,major).GetType().Name;}catch(NotSupportedException){rejected=true;}
            bool pass=major=="2"?type=="Version2_6_0AndEarlierCustomBeatmapSaveData":major=="3"?type=="Version3CustomBeatmapSaveData":rejected;
            cases++;if(!pass)failed++;rows.Add(new{major,type,rejected,pass});
        }
        foreach(string major in new[]{"2","3"})foreach(string wrong in new[]{"4.0.0",major=="2"?"3.3.0":"2.6.0"})
        {
            var root=new JObject{[major=="2"?"_version":"version"]=wrong};bool rejected=false;
            try{NoodleNativeData.Deserialize(gameRoot,root,major);}catch(NotSupportedException){rejected=true;}
            cases++;if(!rejected)failed++;rows.Add(new{major,wrong,rejected,pass=rejected});
        }
        foreach(string major in new[]{"2","3"})
        {
            var root=JObject.Parse(major=="2"?"""{"_version":"2.6.0","version":"4.0.0","_notes":[]}""":"""{"version":"3.3.0","_version":"4.0.0","colorNotes":[]}""");
            bool pass=NoodleNativeData.Deserialize(gameRoot,root,major).GetType().Name==(major=="2"?"Version2_6_0AndEarlierCustomBeatmapSaveData":"Version3CustomBeatmapSaveData");
            cases++;if(!pass)failed++;rows.Add(new{major,name="inactive-version-alias",pass});
        }
        var info="""{"version":"4.0.1","audio":{"bpm":120,"songDuration":12,"audioDataFilename":"AudioData.dat"},"difficultyBeatmaps":[{"characteristic":"Standard","difficulty":"Expert","beatmapDataFilename":"map.dat","customData":{"requirements":["Noodle Extensions"]}}]}""";
        var documents=new MapDocuments(info,"""{"version":"4.0.0","colorNotes":[],"colorNotesData":[]}""",file:"map.dat");
        foreach(var feature in new[]{AnalysisFeatures.Average,AnalysisFeatures.Peak,AnalysisFeatures.Median,AnalysisFeatures.FirstNotes,AnalysisFeatures.Squats,AnalysisFeatures.Density,AnalysisFeatures.ImportedDeaths,AnalysisFeatures.Reference})
        {
            bool rejected=false;
            try{LocalMap.LoadDocuments(documents,"Standard","Expert",feature);}catch(NotSupportedException ex){rejected=ex.Message=="Noodle Extensions v4 gameplay has not been validated";}
            cases++;if(!rejected)failed++;rows.Add(new{feature=feature.ToString(),rejected,pass=rejected});
        }
        return new{cases,failed,saveTypes,results=rows,scope="Pinned installed custom deserializer formats and explicit CLI/production routing boundaries; no v4 custom gameplay admission"};
    }
}
