using System;
using System.Collections.Generic;
using Newtonsoft.Json.Linq;

namespace BeatInsight.Formats;

internal static class NoodlePositionDefinitions
{
    // Project only requested color-note offsets. Keep source JSON/fingerprint,
    // unrelated definitions, wall runtime data and event graphs untouched.
    internal static void ResolveNotes(JObject root,int major)
    {
        if(major!=2&&major!=3)return;
        bool old=major==2;string customKey=old?"_customData":"customData";
        Dictionary<string,JArray>? definitions=null;
        foreach(var item in root[old?"_notes":"colorNotes"]??new JArray())
        {
            if(item is not JObject obj||obj[customKey] is not JObject custom)continue;
            if(old&&obj["_type"] is JToken type&&(type.Type!=JTokenType.Integer||(int)type>1))continue;
            var animation=custom[old?"_animation":"animation"] as JObject;
            var value=animation?[old?"_position":"offsetPosition"];
            if(value?.Type!=JTokenType.String)continue;
            definitions??=Read(root[customKey]?[old?"_pointDefinitions":"pointDefinitions"],major);
            string? name=(string?)value;
            if(string.IsNullOrEmpty(name)||!definitions.TryGetValue(name!,out var points)||!NoodleVectorCurve.TryRead(points,out _))
                throw new NotSupportedException("Unresolved or unsupported position point definition: "+value.Path);
            animation![old?"_position":"offsetPosition"]=points.DeepClone();
        }
    }
    static Dictionary<string,JArray> Read(JToken? source,int major)
    {
        var definitions=new Dictionary<string,JArray>(StringComparer.Ordinal);
        void Add(string? name,JToken? value)
        {
            if(string.IsNullOrEmpty(name)||value is not JArray array||definitions.ContainsKey(name!))
                throw new NotSupportedException("Invalid or duplicate position point definition");
            definitions.Add(name!,array);
        }
        if(major==2&&source is JArray legacy)
        {
            foreach(var item in legacy)
            {
                if(item is not JObject definition||definition["_name"]?.Type!=JTokenType.String)
                    throw new NotSupportedException("Invalid legacy point definition");
                Add((string?)definition["_name"],definition["_points"]);
            }
        }
        else if(major==3&&source is JObject modern)
        {
            foreach(var property in modern.Properties())Add(property.Name,property.Value);
        }
        else throw new NotSupportedException("Missing or invalid position point definitions for this version");
        return definitions;
    }
}
