using BeatInsight.WallAlert;
using Newtonsoft.Json.Linq;

namespace BeatInsight.Compare;

// The loader retains negative durations. No collider semantics are inferred.
static class NoodleWallIntervalChecks
{
    internal static object Run(Func<JObject,float,BeatmapData> load)
    {
        int cases=0,failed=0;var results=new List<object>();
        foreach(float bpm in new[]{120f,180f})foreach(float beat in new[]{0f,2.01f,69.534f,284.036f})
        foreach(float duration in new[]{-.036f,-.034f,0f,.034f})
        foreach(bool change in new[]{false,true})foreach(bool fake in new[]{false,true})
        {
            var root=JObject.Parse("""{"version":"3.3.0","colorNotes":[],"obstacles":[{"b":0,"x":1,"y":2,"d":1,"w":2,"h":3}],"bpmEvents":[]}""");
            var wall=root["obstacles"]![0]!;wall["b"]=beat;wall["d"]=duration;
            wall["customData"]=new JObject{["NE_fake"]=fake};
            if(change)((JArray)root["bpmEvents"]!).Add(new JObject{["b"]=2,["m"]=bpm==120?180:90});
            var walls=OracleBridge.GetItems(load(root,bpm)).OfType<ObstacleData>().ToArray();
            float Time(float b)=>change&&b>2?120/bpm+(b-2)*60/(bpm==120?180:90):b*60/bpm;
            bool rejected=false;string? reason=null;int? count=null;
            try{count=WallAlertAdapter.Count(root.ToString(),bpm,noodleExtensions:true);}
            catch(NotSupportedException ex){rejected=true;reason=ex.Message;}
            bool pass=walls.Length==1&&Math.Abs(walls[0].duration-(Time(beat+duration)-Time(beat)))<.00002f
                &&Math.Abs(walls[0].endBeat-(beat+duration))<.00002f
                &&(duration<0&&!fake?rejected&&reason=="Negative Noodle wall duration requires a validated collision profile":!rejected&&count==(fake?0:1));
            cases++;if(!pass)failed++;
            results.Add(new{bpm,beat,duration,change,fake,nativeDuration=walls.SingleOrDefault()?.duration,rejected,count,reason,pass});
        }
        return new{cases,failed,scope="Native custom v3 loader retains negative obstacle duration; analysis rejects physical interpretation explicitly",results};
    }
}
