using System;
using BeatInsight.Core;
using Newtonsoft.Json.Linq;
namespace BeatInsight.Formats;

internal static class NoodleGeometry
{
    public static (float? X,float? Y) Read(JToken item,int major,string? key=null)
    {
        var value=(item[major==2?"_customData":"customData"] as JObject)?[key??(major==2?"_position":"coordinates")];
        if(value==null||value.Type==JTokenType.Null)return (null,null);
        if(value is not JArray array||array.Count>2)throw new NotSupportedException("Invalid Noodle coordinates: "+value.Path);
        float? Component(int index)
        {
            if(index>=array.Count||array[index].Type==JTokenType.Null)return null;
            if(array[index].Type!=JTokenType.Integer&&array[index].Type!=JTokenType.Float)throw new NotSupportedException("Invalid Noodle coordinates: "+array[index].Path);
            float number=(float)array[index];
            if(float.IsNaN(number)||float.IsInfinity(number)||Math.Abs(number)>1000000)throw new NotSupportedException("Unsupported Noodle coordinate range: "+array[index].Path);
            return number;
        }
        var x=Component(0);return (x.HasValue?x+2f:null,Component(1));
    }
    public static void Apply(MapNote note,JToken item,int major)
    {var p=Read(item,major);note.NoodleX=p.X;note.NoodleY=p.Y;}
    static bool Approximately(float a,float b)=>Math.Abs(b-a)<Math.Max(1e-6f*Math.Max(Math.Abs(a),Math.Abs(b)),8*float.Epsilon);
    public static bool Same(MapNote a,MapNote b)=>Same(a,b.X,b.Y,b.NoodleX,b.NoodleY);
    public static bool Same(MapNote a,int x,int y,float? nx,float? ny)
    {
        if(!a.NoodleX.HasValue&&!a.NoodleY.HasValue&&!nx.HasValue&&!ny.HasValue)return a.X==x&&a.Y==y;
        return Approximately(a.NoodleX??a.X,nx??x)&&Approximately(a.NoodleY??a.Y,ny??y);
    }
}
