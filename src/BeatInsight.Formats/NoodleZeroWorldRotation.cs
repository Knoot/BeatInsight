using System;
using System.Linq;
using Newtonsoft.Json.Linq;

namespace BeatInsight.Formats;

internal static class NoodleZeroWorldRotation
{
    internal static void Project(JObject root,int major,string characteristic)
    {
        if(major!=3||!(characteristic=="Standard"||characteristic=="Lawless"||characteristic=="OneSaber"||characteristic=="NoArrows"))return;
        if(new[]{"rotationEvents","_rotationEvents","_events","events"}.Any(k=>!NoodleStaticProfile.IsEmptyTrackValue(root[k])))return;
        foreach(string collection in new[]{"colorNotes","sliders"})
        {
        if(root[collection] is not JArray notes)continue;
        foreach(var note in notes)
        {
            if(note is not JObject||NoodleFakeData.IsFake(note,major)||note["customData"] is not JObject custom)continue;
            if(((int?)note["c"]??0) is not (0 or 1))continue;
            if(custom["worldRotation"] is JArray rotation&&rotation.Count==3&&rotation.All(v=>(v.Type==JTokenType.Integer||v.Type==JTokenType.Float)&&(double)v==0))
                custom.Remove("worldRotation");
        }
        }
    }
}
