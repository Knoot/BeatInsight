using System;
using System.Collections.Generic;
using System.Linq;
using BeatInsight.Core;

namespace BeatInsight.Formats;

// Nominal Z orientation at note time. Two-note rows can override the JSON offset;
// custom animated rotations remain explicitly unknown.
internal static class NoteOrientation
{
    public static void Prepare(List<MapNote> notes)
    {
        foreach(int color in new[]{0,1})
        {
            var row=new List<MapNote>();float start=0;
            foreach(var note in notes.Where(n=>n.Color==color&&n.Direction!=9))
            {
                if(note.Time>start+.001f){Finish(row);row.Clear();start=note.Time;}
                row.Add(note);
            }
            Finish(row);
        }
    }
    static void Finish(List<MapNote> row)
    {
        if(row.Count!=2)return;
        var a=row[0];var b=row[1];
        bool Known(MapNote n)=>n.Direction>=0&&n.Direction<=8||n.MappingExtensions&&MappingNotes.IsExtendedDirection(n.Direction);
        if(!Known(a)||!Known(b))
        {a.CutDirectionAngleOffset=b.CutDirectionAngleOffset=null;return;}
        if(a.Direction!=b.Direction&&a.Direction!=8&&b.Direction!=8)return;
        if(!a.CutDirectionAngleOffset.HasValue||!b.CutDirectionAngleOffset.HasValue)
        {a.CutDirectionAngleOffset=b.CutDirectionAngleOffset=null;return;}
        var reference=a.Direction==8?b:a;var other=ReferenceEquals(reference,a)?b:a;
        float X(MapNote n)=>n.MappingExtensions?MappingNotes.X(n.X):(n.X-1.5f)*.6f;
        float Y(MapNote n)=>n.MappingExtensions?MappingNotes.Y(n.Y):n.Y==0?.25f:n.Y==1?.85f:1.45f;
        float dx=X(other)-X(reference),dy=Y(other)-Y(reference);
        (float X,float Y) direction=reference.Direction switch {
            0=>(0,1),1=>(0,-1),2=>(-1,0),3=>(1,0),4=>(-.7071f,.7071f),
            5=>(.7071f,.7071f),6=>(-.7071f,-.7071f),7=>(.7071f,-.7071f),_=>(0,1)};
        if(reference.MappingExtensions&&MappingNotes.IsExtendedDirection(reference.Direction))
        {
            double angle=MappingNotes.Angle(reference.Direction)*Math.PI/180;
            direction=((float)Math.Sin(angle),-(float)Math.Cos(angle));
        }
        float Signed(float x,float y)
        {
            float denominator=(float)Math.Sqrt((direction.X*direction.X+direction.Y*direction.Y)*(x*x+y*y));
            if(denominator<1e-15f)return 0;
            float cosine=Math.Max(-1,Math.Min(1,(direction.X*x+direction.Y*y)/denominator));
            float angle=(float)Math.Acos(cosine)*57.29578f;
            return angle*(direction.X*y-direction.Y*x>=0?1:-1);
        }
        float forward=Signed(dx,dy),backward=Signed(-dx,-dy);
        float offset=Math.Abs(forward)<Math.Abs(backward)?forward:backward;
        if(reference.Direction==8&&other.Direction==8)
        {reference.CutDirectionAngleOffset=other.CutDirectionAngleOffset=offset;return;}
        if(Math.Abs(offset)>40)return;
        reference.CutDirectionAngleOffset=offset;
        other.CutDirectionAngleOffset=offset+(other.Direction==8&&reference.Direction>3&&reference.Direction!=8?45:0);
    }
}
