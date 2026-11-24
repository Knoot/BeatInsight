using System;
using System.Collections.Generic;
using Newtonsoft.Json.Linq;
using BeatInsight.WallAlert;
namespace BeatInsight.Formats;

internal static class NoodleWallData
{
    public static bool HasLength(JObject root,int major)
    {
        foreach(var item in root[major==2?"_obstacles":"obstacles"]??new JArray())
            if(!NoodleFakeData.IsFake(item,major)&&NoodleWallAnalysis.Read(item,major,true).Length.HasValue)return true;
        return false;
    }
    public static string Prepare(JObject root,int major,float speed,Func<float,float> time,Func<float,float> beatAtTime,Dictionary<JToken,float> ends)
    {
        bool old=major==2;string key=old?"_obstacles":"obstacles";
        var output=new JObject {[old?"_version":"version"]=root[old?"_version":"version"]?.DeepClone()};
        var selected=new JArray();output[key]=selected;
        foreach(var item in root[key]??new JArray())
        {
            if(NoodleFakeData.IsFake(item,major))continue;
            var shape=NoodleWallAnalysis.Read(item,major,true);
            var copy=(JObject)item.DeepClone();
            if(shape.Length.HasValue)
            {
                if((root["njsEvents"] as JArray)?.Count>0||(root["_njsEvents"] as JArray)?.Count>0)throw new NotSupportedException("Wall length with variable movement speed has not been validated");
                var custom=item[old?"_customData":"customData"];
                if(custom?[old?"_noteJumpMovementSpeed":"noteJumpMovementSpeed"]!=null)throw new NotSupportedException("Wall length with per-object movement speed has not been validated");
                float beat=(float?)item[old?"_time":"b"]??0,duration=(float?)item[old?"_duration":"d"]??0;
                float start=time(beat),end=time(beat+duration);
                float seconds=NoodleWallAnalysis.Duration(end-start,shape.Length,speed);
                float actualEnd=beatAtTime(start+seconds);ends[item]=start+seconds;
                if(seconds==0)continue;
                copy[old?"_duration":"d"]=Math.Max(0,actualEnd-beat);
                var size=(JArray)copy[old?"_customData":"customData"]![old?"_scale":"size"]!;
                size[2]=JValue.CreateNull();
            }
            selected.Add(copy);
        }
        return output.ToString(Newtonsoft.Json.Formatting.None);
    }
}
