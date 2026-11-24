using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json.Linq;

namespace BeatInsight.Formats;

// Restrict only independent canonical track events. No parent/environment inference.
internal static class NoodleTrackScope
{
    static bool Number(JToken? token)
        =>token!=null&&(token.Type==JTokenType.Integer||token.Type==JTokenType.Float)
            &&(double)token>=0&&(double)token<=1000000;
    internal static bool TrySelect(JObject root,JArray events,bool notes,bool walls,out JArray selected)
    {
        selected=new JArray();
        foreach(var metadata in new JToken?[]{root,root["customData"],root["_customData"]})
            if(metadata is JObject obj&&new[]{"environment","_environment"}.Any(k=>!NoodleStaticProfile.IsEmptyTrackValue(obj[k])))return false;
        var tracks=new HashSet<string>(StringComparer.Ordinal);
        var collections=new List<string>();
        if(notes)collections.AddRange(new[]{"colorNotes","bombNotes","sliders","burstSliders"});
        if(walls)collections.Add("obstacles");
        try
        {
            foreach(string key in collections)
            {
                if(root[key]==null||root[key]!.Type==JTokenType.Null)continue;
                if(root[key] is not JArray items)return false;
                foreach(var item in items)
                {
                    if(item is not JObject)return false;
                    if(item["customData"] is JToken custom&&custom.Type!=JTokenType.Null&&custom is not JObject)return false;
                    var assignment=(item["customData"] as JObject)?["track"];
                    if(assignment==null||NoodleStaticProfile.IsEmptyTrackValue(assignment))continue;
                    NoodleStaticProfile.ValidateTrackAssignment(assignment,true);
                    foreach(var name in assignment is JArray a?a:new JArray(assignment.DeepClone()))tracks.Add((string)name!);
                }
            }
            foreach(var ev in events)
            {
                if(ev is not JObject obj||obj["d"] is not JObject data||!Number(obj["b"])||obj["t"]?.Type!=JTokenType.String
                    ||obj.Properties().Any(p=>p.Name!="b"&&p.Name!="t"&&p.Name!="d"))return false;
                string type=(string)obj["t"]!;
                if(type!="AnimateTrack"&&type!="AssignPathAnimation")return false;
                var target=data["track"];
                if(target==null||NoodleStaticProfile.IsEmptyTrackValue(target))return false;
                NoodleStaticProfile.ValidateTrackAssignment(target,true);
                foreach(var p in data.Properties())
                {
                    if(p.Name=="track")continue;
                    if(p.Name=="duration"){if(!Number(p.Value))return false;continue;}
                    if(p.Name=="easing")
                    {if(p.Value.Type!=JTokenType.String||!NoodleVectorEasing.TryRead((string)p.Value!,out _))return false;continue;}
                    if(p.Name=="dissolve"||p.Name=="dissolveArrow")
                    {if(!NoodleVisualEvents.Points(p.Value))return false;continue;}
                    if(p.Name!="offsetPosition"&&p.Name!="definitePosition"&&p.Name!="localRotation"&&p.Name!="worldRotation"&&p.Name!="scale")return false;
                    if(!NoodleVectorCurve.TryRead(p.Value,out _))return false;
                }
                var names=target is JArray a?a:new JArray(target.DeepClone());
                if(names.Any(n=>tracks.Contains((string)n!)))selected.Add(ev.DeepClone());
            }
        }
        catch(NotSupportedException){return false;}
        return true;
    }
}
