using System;
using Newtonsoft.Json.Linq;

namespace BeatInsight.Formats;

internal static class NoodleFakeData
{
    public static bool IsFake(JToken item,int major)
    {
        JToken? flag=(item[major==2?"_customData":"customData"] as JObject)?[major==2?"_fake":"NE_fake"];
        if(flag==null||flag.Type==JTokenType.Null)return false;
        if(flag.Type!=JTokenType.Boolean)throw new NotSupportedException("Invalid Noodle fake flag: "+flag.Path);
        return (bool)flag;
    }
    public static bool Append(JObject root,int major,bool notes,bool walls)
    {
        if(major!=3||root["customData"] is not JObject custom)return false;
        bool appended=false;
        foreach(var pair in new[]{("fakeColorNotes","colorNotes"),("fakeBombNotes","bombNotes"),("fakeObstacles","obstacles")})
        {
            if(pair.Item2=="obstacles"?!walls:!notes)continue;
            if(custom[pair.Item1]==null||custom[pair.Item1]!.Type==JTokenType.Null)continue;
            if(custom[pair.Item1] is not JArray fakes)throw new NotSupportedException("Invalid Noodle fake array: "+custom[pair.Item1]!.Path);
            if(fakes.Count==0)continue;
            if(root[pair.Item2]==null)root[pair.Item2]=new JArray();
            if(root[pair.Item2] is not JArray target)throw new NotSupportedException("Invalid Noodle gameplay collection: "+pair.Item2);
            bool prepend=false;
            foreach(var property in root.Properties())
            {if(property.Name==pair.Item2)break;if(property.Name=="customData"){prepend=true;break;}}
            int offset=0;
            foreach(var item in fakes)
            {
                if(item is not JObject source)throw new NotSupportedException("Invalid Noodle fake object: "+item.Path);
                var clone=(JObject)source.DeepClone();
                if(clone["customData"]==null||clone["customData"]!.Type==JTokenType.Null)clone["customData"]=new JObject();
                if(clone["customData"] is not JObject data)throw new NotSupportedException("Invalid Noodle fake customData");
                data["NE_fake"]=true;
                if(prepend)target.Insert(offset++,clone);else target.Add(clone);
                appended=true;
            }
        }
        return appended;
    }
    public static string WithoutFakeWalls(JObject root,int major)
    {
        string name=major==2?"_obstacles":"obstacles",version=major==2?"_version":"version";
        var output=new JObject {[version]=root[version]?.DeepClone()??new JValue(major+".0.0")};
        var selected=new JArray();output[name]=selected;
        if(root[name] is JArray walls)foreach(var wall in walls)if(!IsFake(wall,major))selected.Add(wall.DeepClone());
        return output.ToString(Newtonsoft.Json.Formatting.None);
    }
}
