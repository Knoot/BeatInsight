using Newtonsoft.Json.Linq;

namespace BeatInsight.Formats;

internal static class NoodleMotionFlags
{
    internal static bool IsMotionFlag(string name,int major)=>name==(major==2?"_disableNoteGravity":"disableNoteGravity")
        ||name==(major==2?"_disableNoteLook":"disableNoteLook");
    internal static bool HasActive(JToken item,int major)
    {
        if(item[major==2?"_customData":"customData"] is not JObject custom)return false;
        foreach(var property in custom.Properties())
            if(IsMotionFlag(property.Name,major)&&property.Value.Type==JTokenType.Boolean&&(bool)property.Value)return true;
        return false;
    }
}
