using System;
using Newtonsoft.Json.Linq;

namespace BeatInsight.Formats;

// Proven unused legacy keys in active v3 real-note metadata. Never a fallback
// for coordinates/scale/angle, and not a generic permission for unknown keys.
internal static class NoodleV3NoteAliases
{
    internal static bool Contains(string name)=>name=="_position"||name=="_scale"||name=="_cutDirection";
    internal static void Validate(string name,JToken value)
    {
        bool Number(JToken item)=>item.Type is JTokenType.Integer or JTokenType.Float
            &&!double.IsNaN((double)item)&&!double.IsInfinity((double)item)&&Math.Abs((double)item)<=1000000;
        bool valid=name switch
        {
            "_position"=>value is JArray position&&position.Count==2&&Number(position[0])&&Number(position[1]),
            "_scale"=>value is JArray scale&&scale.Count==3&&Number(scale[0])&&Number(scale[1])&&Number(scale[2])
                &&(double)scale[0]==1&&(double)scale[1]==1&&(double)scale[2]==1,
            "_cutDirection"=>Number(value),
            _=>false
        };
        if(!valid)throw new NotSupportedException("Unsupported Noodle Extensions gameplay data: invalid ignored v3 legacy alias ("+value.Path+")");
    }
}
