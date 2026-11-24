using System;
using System.Linq;
using Newtonsoft.Json.Linq;

namespace BeatInsight.Formats;

// Physical wall projection only; source JSON remains intact.
internal static class NoodleWallInteraction
{
    internal static bool Project(JObject root,int major)
    {
        if(major!=2&&major!=3||root[major==2?"_obstacles":"obstacles"] is not JArray walls)return false;
        string customKey=major==2?"_customData":"customData",flagKey=major==2?"_interactable":"uninteractable";
        bool removed=false;
        foreach(var wall in walls.ToArray())
        {
            if(wall is not JObject||NoodleFakeData.IsFake(wall,major))continue;
            if(wall[customKey] is not JObject custom)continue;
            var flag=custom[flagKey];
            if(flag==null||flag.Type==JTokenType.Null){custom.Remove(flagKey);continue;}
            if(flag.Type!=JTokenType.Boolean)throw new NotSupportedException("Invalid Noodle wall interaction flag: "+flag.Path);
            bool ignored=major==2?!(bool)flag:(bool)flag;
            if(ignored){wall.Remove();removed=true;}else custom.Remove(flagKey);
        }
        return removed;
    }
}
