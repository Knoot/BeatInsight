using System;
using System.Linq;
using Newtonsoft.Json.Linq;

namespace BeatInsight.Formats;

// Time/color/direction statistics do not require spatial row ownership when
// every scoring object is an ordinary colored note. Tilt remains unknown.
internal static class NoodleCombinedNominalNotes
{
    internal static bool Project(JObject root,int major,string characteristic)
    {
        if(major!=3||characteristic is not ("Standard" or "Lawless" or "OneSaber" or "NoArrows"))return false;
        bool Empty(string key)=>root[key]==null||root[key] is JArray {Count:0};
        if(!Empty("bombNotes")||!Empty("burstSliders")||root["colorNotes"] is not JArray {Count:>0} notes)return false;
        if(root["sliders"]!=null&&root["sliders"] is not JArray)return false;
        foreach(string key in new[]{"rotationEvents","njsEvents","_rotationEvents","_njsEvents","environment","_environment","materials","_materials"})if(!Empty(key))return false;
        foreach(string key in new[]{"customData","_customData"})
        {
            if(root[key]==null)continue;
            if(root[key] is not JObject data)return false;
            foreach(var property in data.Properties())
            {
                if(property.Name=="bookmarksUseOfficialBpmEvents"){if(property.Value.Type!=JTokenType.Boolean)return false;continue;}
                if(property.Name is "bookmarks" or "_time" or "time" or "requirements" or "_requirements")continue;
                if(property.Name is "customEvents" or "_customEvents" or "environment" or "_environment" or "materials" or "_materials"
                    or "fakeColorNotes" or "fakeBombNotes" or "fakeObstacles" or "fakeBurstSliders")
                {if(property.Value is not JArray {Count:0})return false;continue;}
                return false;
            }
        }
        bool Metadata(JToken item,bool arc)
        {
            if(item["_customData"]!=null)return false;
            if(item["customData"]==null)return true;
            if(item["customData"] is not JObject data)return false;
            foreach(var property in data.Properties())
            {
                if(property.Name=="coordinates"||arc&&property.Name=="tailCoordinates")
                {
                    if(property.Value is not JArray point||point.Count>2)return false;
                    foreach(var v in point)if(v.Type!=JTokenType.Null&&(v.Type is not (JTokenType.Float or JTokenType.Integer)||!float.IsFinite((float)v)||Math.Abs((float)v)>1000000))return false;
                }
                else if(property.Name=="NE_fake")
                {if(property.Value.Type!=JTokenType.Boolean||arc&&(bool)property.Value)return false;}
                else if(property.Name=="_isAnchor")
                {if(property.Value.Type is not (JTokenType.Boolean or JTokenType.Null))return false;}
                else if(property.Name=="color")
                {
                    if(property.Value is not JArray color||color.Count is not (3 or 4)||color.Any(v=>v.Type is not (JTokenType.Float or JTokenType.Integer)||!float.IsFinite((float)v)))return false;
                }
                else return false;
            }
            return true;
        }
        bool Integer(JToken item,string key,int min,int max)=>item[key]==null||item[key]!.Type==JTokenType.Integer&&(double)item[key]!>=min&&(double)item[key]!<=max;
        foreach(var note in notes)
            if(note is not JObject||!Integer(note,"c",0,1)||!Integer(note,"d",0,8)||!Integer(note,"x",-1000000,1000000)||!Integer(note,"y",-1000000,1000000)||!Metadata(note,false))return false;
        foreach(var arc in (root["sliders"] as JArray)??new JArray())
            if(arc is not JObject||!Metadata(arc,true)||!Integer(arc,"d",0,8)||!Integer(arc,"tc",0,8))return false;
        var directions=notes.Select(n=>n["d"]?.DeepClone()??new JValue(0)).ToArray();
        // The existing nominal projection independently validates times, colors,
        // ordinary arcs and fake ownership, retaining BPM and immutable source.
        if(!NoodleDensityProjection.Project(root,major,characteristic))return false;
        var projected=(JArray)root["colorNotes"]!;
        if(projected.Count!=directions.Length)throw new NotSupportedException("Nominal note projection changed colored note identity");
        for(int i=0;i<directions.Length;i++)projected[i]["d"]=directions[i];
        return true;
    }
}
