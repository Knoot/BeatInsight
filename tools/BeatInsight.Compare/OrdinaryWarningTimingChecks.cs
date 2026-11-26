using BeatInsight.WallAlert;
using Newtonsoft.Json.Linq;

namespace BeatInsight.Compare;

static class OrdinaryWarningTimingChecks
{
    internal static object Run(string gameRoot)
    {
        int cases=0,failed=0;var results=new List<object>();
        foreach(int major in new[]{2,3})foreach(float initial in new[]{30f,45f,60f,120f,180f})foreach(bool change in new[]{false,true})
        {
            var root=major==2?JObject.Parse("""{"_version":"2.6.0","_notes":[],"_obstacles":[{"_time":1,"_duration":1,"_lineIndex":1,"_type":1,"_width":1},{"_time":2.3,"_duration":1,"_lineIndex":2,"_type":1,"_width":1}],"_events":[]}"""):
                JObject.Parse("""{"version":"3.3.0","colorNotes":[],"obstacles":[{"b":1,"d":1,"x":1,"y":1,"w":1,"h":3},{"b":2.3,"d":1,"x":2,"y":1,"w":1,"h":3}],"bpmEvents":[]}""");
            if(change)((JArray)root[major==2?"_events":"bpmEvents"]!).Add(major==2?new JObject{["_time"]=1,["_type"]=100,["_floatValue"]=30}:new JObject{["b"]=1,["m"]=30});
            var json=root.ToString();var native=GameLoader.Load(gameRoot,json,initial);
            var walls=OracleBridge.GetItems(native).OfType<ObstacleData>().OrderBy(w=>w.beat).ToArray();
            var sequence=new SquatWarningSequence();int signals=0;
            foreach(var w in walls)if(sequence.Observe(w.beat,w.endBeat,w.duration,w.lineIndex,(int)w.lineLayer,w.width,w.height))signals++;
            int menu=WallAlertAdapter.Count(json,initial);
            bool pass=walls.Length==2&&walls[0].beat==1&&walls[0].endBeat==2&&signals==0&&menu==0;
            cases++;if(!pass)failed++;
            results.Add(new{major,initial,change,nativeDurationSeconds=walls[0].duration,nativeBeatExtent=walls[0].endBeat-walls[0].beat,signals,menu,pass});
        }
        return new{cases,failed,results,scope="Installed vanilla loader/BPM conversion; disjoint center-lane wall halves must not combine after their beat-domain coverage has expired. Native duration is seconds; source/endBeat are beats. No Unity gameplay lifecycle."};
    }
}
