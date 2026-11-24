using System.Collections;
using System.Reflection;
using System.Runtime.Loader;
using Newtonsoft.Json.Linq;

namespace BeatInsight.Compare;

static class HeckMapTransformChecks
{
    public static object Run(string gameRoot,BeatmapData source,float bpm)
    {
        const BindingFlags flags=BindingFlags.Public|BindingFlags.NonPublic|BindingFlags.Instance|BindingFlags.Static;
        var mirror=AssemblyLoadContext.Default.Assemblies.Select(a=>a.GetType("BeatmapDataMirrorTransform")).First(t=>t!=null)!.GetMethod("CreateTransformedData",flags)!;
        JObject Metadata(BeatmapObjectData item)=>JObject.FromObject(item.GetType().GetProperty("customData")!.GetValue(item)!);
        var originals=OracleBridge.GetItems(source).OfType<BeatmapObjectData>().ToArray();
        var before=originals.Select(Metadata).ToArray();
        var baseline=new Dictionary<string,object>();
        _=HeckMapChecks.Run(gameRoot,source,bpm,capture:(id,data)=>baseline.Add(id,data));
        var results=new List<object>();int failed=0;bool previous=NoodleRowBridge.Enabled;NoodleRowBridge.Enabled=true;
        try
        {
            foreach(string mode in new[]{"copy","mirror","double-mirror","filtered-fake-notes"})
            {
                var copy=mode=="copy"?source.GetFilteredCopy(item=>item):mode=="filtered-fake-notes"?
                    source.GetFilteredCopy(item=>item is NoteData note&&Metadata(note)["NE_fake"]?.Value<bool>()==true?null!:item):
                    (BeatmapData)mirror.Invoke(null,new object[]{source})!;
                if(mode=="double-mirror")copy=(BeatmapData)mirror.Invoke(null,new object[]{copy})!;
                var transformed=new Dictionary<string,object>();
                var evidence=JObject.FromObject(HeckMapChecks.Run(gameRoot,copy,bpm,source,mode=="mirror",(id,data)=>transformed.Add(id,data)));
                var reload=HeckReloadChecks.Run(gameRoot,baseline,transformed);
                int structural=(int)evidence["structuralFailed"]!+(int)JObject.FromObject(reload)["failed"]!;
                if(ReferenceEquals(source,copy))structural++;
                for(int i=0;i<originals.Length;i++)if(!JToken.DeepEquals(before[i],Metadata(originals[i])))structural++;
                var errors=evidence["errors"]!.Values<string>().ToArray();
                bool unknownTrack=originals.Length==0&&errors.Length==2&&errors[1]!.Contains("Could not find track [missing]",StringComparison.Ordinal);
                bool boundary=(bool)evidence["onlyQuaternionFailures"]!&&(int)evidence["unityQuaternionFailures"]! >0;
                bool pass=structural==0&&((int)evidence["failed"]! ==0||unknownTrack||boundary);
                if(!pass)failed++;
                results.Add(new{mode,passed=pass,structuralFailed=structural,reload,evidence});
            }
        }
        finally{NoodleRowBridge.Enabled=previous;}
        return new{cases=4,failed,results,scope="Actual CJD copy/filter and native mirror then installed Heck/Noodle/Chroma manager with original and transformed contexts. Controlled constructor/row hooks; no reload DI, Unity pose or all-mod ordering."};
    }
}
