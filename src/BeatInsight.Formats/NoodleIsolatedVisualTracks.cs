using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json.Linq;

namespace BeatInsight.Formats;

// A property-only note/arc track can remain nominal in a map with unrelated scene
// animation. Never infer neutrality across environment, material or parent links.
internal static class NoodleIsolatedVisualTracks
{
    static bool Number(JToken? value,double min,double max)=>value!=null
        &&value.Type is JTokenType.Integer or JTokenType.Float
        &&double.IsFinite((double)value)&&(double)value>=min&&(double)value<=max;
    static string[] Names(JToken? value)
    {
        if(value==null||NoodleStaticProfile.IsEmptyTrackValue(value))return Array.Empty<string>();
        NoodleStaticProfile.ValidateTrackAssignment(value,true);
        return value is JArray array?array.Select(n=>(string)n!).ToArray():new[]{(string)value!};
    }
    internal static bool ColorPoints(JToken value)
    {
        if(value is not JArray points||points.Count==0)return false;
        bool Color(JToken v)=>Number(v,-1000000,1000000);
        if(points.Count==4&&points.All(Color))return true;
        double previous=-1;
        foreach(var point in points)
        {
            if(point is not JArray frame||frame.Count is not (5 or 6)
                ||!frame.Take(4).All(Color)||!Number(frame[4],0,1))return false;
            double time=(double)frame[4];if(time<=previous)return false;previous=time;
            if(frame.Count==6&&(frame[5].Type!=JTokenType.String||!NoodleVectorEasing.TryRead((string)frame[5]!,out _)))return false;
        }
        return true;
    }
    static bool Visual(JObject data)
    {
        bool property=false;
        foreach(var p in data.Properties())
        {
            if(p.Name=="track")continue;
            if(p.Name=="duration"){if(!Number(p.Value,0,1000000))return false;continue;}
            if(p.Name=="repeat"){if(p.Value.Type!=JTokenType.Integer||!Number(p.Value,0,1000000))return false;continue;}
            if(p.Name=="easing")
            {if(p.Value.Type!=JTokenType.String||!NoodleVectorEasing.TryRead((string)p.Value!,out _))return false;continue;}
            if(p.Name=="color"){if(!ColorPoints(p.Value))return false;property=true;continue;}
            if(p.Name=="dissolve"||p.Name=="dissolveArrow")
            {if(!NoodleVisualEvents.Points(p.Value))return false;property=true;continue;}
            return false;
        }
        return property;
    }
    internal static HashSet<string> Read(JObject root,int major,bool notes)
    {
        var candidates=new HashSet<string>(StringComparer.Ordinal);
        if(major!=3||!notes)return candidates;
        try
        {
            if(root["customData"] is not JObject custom
                ||!NoodleStaticProfile.IsEmptyTrackValue(root["environment"])
                ||!NoodleStaticProfile.IsEmptyTrackValue(root["_environment"])
                ||root["_customData"]!=null&&root["_customData"]!.Type!=JTokenType.Null&&root["_customData"] is not JObject {Count:0}
                ||!NoodleStaticProfile.IsEmptyTrackValue(custom["_environment"])
                ||!NoodleStaticProfile.IsEmptyTrackValue(custom["_customEvents"]))return candidates;
            var excluded=new HashSet<string>(StringComparer.Ordinal);
            foreach(string key in new[]{"colorNotes","bombNotes","sliders","burstSliders","obstacles"})
            {
                if(root[key]==null||root[key]!.Type==JTokenType.Null)continue;
                if(root[key] is not JArray items)return new HashSet<string>(StringComparer.Ordinal);
                foreach(var item in items)
                {
                    if(item is not JObject||item["customData"]!=null&&item["customData"]!.Type!=JTokenType.Null&&item["customData"] is not JObject)
                        return new HashSet<string>(StringComparer.Ordinal);
                    var metadata=item["customData"] as JObject;
                    if(metadata?["_track"]!=null||item["_customData"] is JObject legacy&&!NoodleStaticProfile.IsEmptyTrackValue(legacy["_track"]))
                        return new HashSet<string>(StringComparer.Ordinal);
                    var names=Names(metadata?["track"]);
                    bool ordinary=(key=="colorNotes"||key=="sliders")&&!NoodleFakeData.IsFake(item,3)&&((int?)item["c"]??0) is 0 or 1;
                    (ordinary?candidates:excluded).UnionWith(names);
                }
            }
            candidates.ExceptWith(excluded);
            if(candidates.Count==0)return candidates;
            void ExcludeBindings(JToken value)
            {
                foreach(var p in ((JContainer)value).DescendantsAndSelf().OfType<JProperty>())
                    if(p.Name=="track"||p.Name=="_track")excluded.UnionWith(Names(p.Value));
            }
            if(custom["environment"]!=null&&custom["environment"]!.Type!=JTokenType.Null)
            {
                if(custom["environment"] is not JArray environment||environment.Any(e=>e is not JObject))return new HashSet<string>(StringComparer.Ordinal);
                ExcludeBindings(environment);
            }
            if(custom["materials"]!=null&&custom["materials"]!.Type!=JTokenType.Null)
            {
                if(custom["materials"] is not JObject materials||materials.Properties().Any(p=>p.Value.Type!=JTokenType.Null&&p.Value is not JObject))
                    return new HashSet<string>(StringComparer.Ordinal);
                ExcludeBindings(materials);
            }
            if(custom["customEvents"]!=null&&custom["customEvents"]!.Type!=JTokenType.Null)
            {
                if(custom["customEvents"] is not JArray events)return new HashSet<string>(StringComparer.Ordinal);
                foreach(var ev in events)
                {
                    if(ev is not JObject obj||obj["d"] is not JObject data||!Number(obj["b"],0,1000000)
                        ||obj["t"]?.Type!=JTokenType.String||obj.Properties().Any(p=>p.Name!="b"&&p.Name!="t"&&p.Name!="d"))return new HashSet<string>(StringComparer.Ordinal);
                    string type=(string)obj["t"]!;
                    if(type=="AssignTrackParent")
                    {
                        if(data["parentTrack"]?.Type!=JTokenType.String||Names(data["parentTrack"]).Length!=1
                            ||data["childrenTracks"] is not JArray {Count:>0}
                            ||data.Properties().Any(p=>p.Name!="parentTrack"&&p.Name!="childrenTracks"&&p.Name!="worldPositionStays")
                            ||data["worldPositionStays"]!=null&&data["worldPositionStays"]!.Type!=JTokenType.Boolean)return new HashSet<string>(StringComparer.Ordinal);
                        excluded.UnionWith(Names(data["parentTrack"]));excluded.UnionWith(Names(data["childrenTracks"]));
                    }
                    else if(type=="AnimateTrack"||type=="AssignPathAnimation")
                    {
                        var targets=Names(data["track"]);if(targets.Length==0)return new HashSet<string>(StringComparer.Ordinal);
                        if(targets.Any(candidates.Contains)&&(type!="AnimateTrack"||!Visual(data)))excluded.UnionWith(targets);
                    }
                    else return new HashSet<string>(StringComparer.Ordinal);
                }
            }
            candidates.ExceptWith(excluded);return candidates;
        }
        catch(Exception e) when(e is NotSupportedException or FormatException or OverflowException or InvalidCastException or ArgumentException)
        {return new HashSet<string>(StringComparer.Ordinal);}
    }
    internal static bool Contains(JToken value,HashSet<string> names)
    {
        var assigned=Names(value);return assigned.Length>0&&assigned.All(names.Contains);
    }
}
