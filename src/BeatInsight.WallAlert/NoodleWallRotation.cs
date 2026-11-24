#nullable enable
using System;
using Newtonsoft.Json.Linq;
namespace BeatInsight.WallAlert;

internal static class NoodleWallRotation
{
    static double Angle(JToken value)
    {
        if(value.Type!=JTokenType.Integer&&value.Type!=JTokenType.Float)throw new NotSupportedException("Invalid static wall rotation");
        double angle=(float)value;
        if(!double.IsFinite(angle)||Math.Abs(angle)>1000000)throw new NotSupportedException("Unsupported static wall rotation range");
        angle%=360;return angle<0?angle+360:angle;
    }
    public static double Read(JObject? custom,bool old)
    {
        var world=custom?[old?"_rotation":"worldRotation"];
        if(world!=null&&world.Type!=JTokenType.Null)
        {
            if(world is JArray array)
            {
                if(array.Count!=3)throw new NotSupportedException("World rotation requires three components");
                foreach(var value in array)if(Angle(value)!=0)throw new NotSupportedException("World-rotated walls require a spatial timeline profile");
            }
            else if(Angle(world)!=0)throw new NotSupportedException("World-rotated walls require a spatial timeline profile");
        }
        var local=custom?[old?"_localRotation":"localRotation"];
        if(local==null||local.Type==JTokenType.Null)return 0;
        if(local is not JArray rotation||rotation.Count!=3)throw new NotSupportedException("Local wall rotation requires three components");
        if(Angle(rotation[0])!=0||Angle(rotation[1])!=0)throw new NotSupportedException("Wall pitch/yaw requires a spatial timeline profile");
        return Angle(rotation[2]);
    }
    public static NoodleWallShape Apply(NoodleWallShape shape,double angle)
    {
        if(angle==0||shape.Width==0||shape.Height==0)return shape;
        (double c,double s)=angle==90?(0d,1d):angle==180?(-1d,0d):angle==270?(0d,-1d):(Math.Cos(angle*Math.PI/180),Math.Sin(angle*Math.PI/180));
        double half=shape.Width*.5,center=shape.X+half,head=2-shape.Y;
        double low=double.NegativeInfinity,high=double.PositiveInfinity;
        // Inverse rotation at the head-height line: intersect both local slabs.
        void Clip(double a,double b,double minimum,double maximum)
        {
            if(a==0){if(b<minimum||b>maximum){low=1;high=0;}return;}
            double left=(minimum-b)/a,right=(maximum-b)/a;
            low=Math.Max(low,Math.Min(left,right));high=Math.Min(high,Math.Max(left,right));
        }
        Clip(c,s*head,-half,half);Clip(-s,c*head,0,shape.Height);
        double bottom=shape.Y-Math.Abs(s)*half+Math.Min(0,c*shape.Height);
        double top=shape.Y+Math.Abs(s)*half+Math.Max(0,c*shape.Height);
        if(high<=low)return new NoodleWallShape((float)center,(float)bottom,0,(float)(top-bottom),shape.Length);
        return new NoodleWallShape((float)(center+low),(float)bottom,(float)(high-low),(float)(top-bottom),shape.Length);
    }
}
