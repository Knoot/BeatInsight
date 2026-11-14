using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace BeatInsight.Core;

public readonly struct DrawColor
{
    public readonly float R,G,B,A;
    public DrawColor(float r,float g,float b,float a=1){R=r;G=g;B=b;A=a;}
    public static DrawColor White=>new DrawColor(1,1,1);
}
public readonly struct DrawPoint
{
    public readonly float X,Y;
    public DrawPoint(float x,float y){X=x;Y=y;}
}
public sealed class DrawTriangle
{
    public DrawPoint A,B,C;
    public DrawColor Color;
    public float AlphaA=1,AlphaB=1,AlphaC=1;
    public bool Blink;
    public DrawGroup Group;
}
public sealed class DrawLabel
{
    public float X,Y,Size,Width;
    public string Text="";
    public DrawGroup Group;
}
public sealed class Drawing
{
    internal DrawGroup Group;
    public readonly List<DrawTriangle> Triangles=new List<DrawTriangle>();
    public readonly List<DrawLabel> Labels=new List<DrawLabel>();
}
public sealed class FirstNoteVisual
{
    public int Direction;
    public float? RotationZ;
    public DrawColor Color;
    public bool ShouldBlink
    {
        get
        {
            if(Direction<0||Direction>=8)return false;
            // Classify the displayed arrow into the nearest of eight directions.
            // At zero rotation the arrow points down; side/up sectors flash.
            double angle=(RotationZ??CompatibleAnalysis.Angle(Direction))*Math.PI/180;
            return Math.Cos(angle)<=Math.Sin(Math.PI/8)+1e-6;
        }
    }
    public static FirstNoteVisual? From(MapNote? note,DrawColor color)
    {
        if(note==null||note.Direction<0||note.Direction>8)return null;
        float? rotation=null;
        if(note.CutDirectionAngleOffset.HasValue&&!float.IsNaN(note.CutDirectionAngleOffset.Value)&&!float.IsInfinity(note.CutDirectionAngleOffset.Value))
            rotation=CompatibleAnalysis.Angle(note.Direction)+note.CutDirectionAngleOffset.Value;
        return new FirstNoteVisual {Direction=note.Direction,RotationZ=rotation,Color=color};
    }
}
public sealed class MenuRenderData
{
    public HistoryOverview? ImportedHistory,OwnHistory;
    public string Average="—",Peak="—",Median="—",Squats="—";
    public FirstNoteVisual? Left,Right;
    public float Duration;
    public float[][] Colors=Array.Empty<float[]>();
    public DeathMarker[] Markers=Array.Empty<DeathMarker>();
    public static string Number(double value,string format="0.##")
        =>double.IsNaN(value)||double.IsInfinity(value)?"—":value.ToString(format,CultureInfo.InvariantCulture);
}

// Top-left coordinate system in menu units. Unity and the offline SVG preview
// consume the same triangles and label positions; no raster assets are required.
public static class MenuDrawing
{
    public const float Width=100,PanelHeight=8.3f,GraphHeight=10;
    static void Tri(Drawing d,DrawPoint a,DrawPoint b,DrawPoint c,DrawColor color)=>d.Triangles.Add(new DrawTriangle {A=a,B=b,C=c,Color=color,Group=d.Group});
    static void Quad(Drawing d,DrawPoint a,DrawPoint b,DrawPoint c,DrawPoint e,DrawColor color){Tri(d,a,b,c,color);Tri(d,a,c,e,color);}
    static void Rect(Drawing d,float x,float y,float w,float h,DrawColor c)=>Quad(d,new DrawPoint(x,y),new DrawPoint(x+w,y),new DrawPoint(x+w,y+h),new DrawPoint(x,y+h),c);
    static void Line(Drawing d,float x,float y,float ex,float ey,float thickness,DrawColor c)
    {
        float dx=ex-x,dy=ey-y,length=(float)Math.Sqrt(dx*dx+dy*dy);if(length<=0)return;
        float nx=-dy/length*thickness/2,ny=dx/length*thickness/2;
        Quad(d,new DrawPoint(x+nx,y+ny),new DrawPoint(ex+nx,ey+ny),new DrawPoint(ex-nx,ey-ny),new DrawPoint(x-nx,y-ny),c);
    }
    static void Label(Drawing d,string text,float x,float y,float width,float size)=>d.Labels.Add(new DrawLabel {Text=text,X=x,Y=y,Width=width,Size=size,Group=d.Group});
    public static Drawing Panel(MenuRenderData data)
    {
        var d=new Drawing();var white=DrawColor.White;
        d.Group=DrawGroup.Average;
        // Average trend, peak bars, median center line; no explanatory headings.
        Line(d,.3f,6,1.6f,4,.45f,white);Line(d,1.6f,4,3,5,.45f,white);Line(d,3,5,4.5f,2.6f,.45f,white);
        Line(d,4.5f,2.6f,4.2f,4,.4f,white);Line(d,4.5f,2.6f,3,2.7f,.4f,white);
        d.Group=DrawGroup.Peak;
        for(int i=0;i<3;i++)Rect(d,26+i*1.5f,6-(i+1)*1.2f,.85f,(i+1)*1.2f,white);
        d.Group=DrawGroup.Median;
        Line(d,46,3.2f,50.3f,3.2f,.32f,white);Line(d,46,4.7f,50.3f,4.7f,.65f,white);Line(d,46,6.2f,50.3f,6.2f,.32f,white);
        d.Group=DrawGroup.Average;Label(d,data.Average,5.3f,4.7f,18.5f,3.3f);
        d.Group=DrawGroup.Peak;Label(d,data.Peak,31,4.7f,14.7f,3.3f);
        d.Group=DrawGroup.Median;Label(d,data.Median,51,4.7f,14.7f,3.3f);
        // Five groups on one baseline: AVG, PEAK, MEDIAN, SQUAT, first notes.
        d.Group=DrawGroup.Squats;
        Rect(d,67.5f,1.7f,1,1,white);Line(d,68,3,69,4.5f,.5f,white);Line(d,69,4.5f,66.5f,4.7f,.5f,white);
        Line(d,66.5f,4.7f,68.5f,7.2f,.5f,white);Line(d,69,4.5f,71,7,.5f,white);Line(d,68.4f,3.5f,71,3,.45f,white);
        Label(d,data.Squats,71,4.7f,9,3.3f);
        d.Group=DrawGroup.FirstNotes;
        Note(d,data.Left,84,4.7f);Note(d,data.Right,91.5f,4.7f);
        return d;
    }
    public static Drawing StatisticsRow(MenuRenderData data)=>Panel(data);
    static void Note(Drawing d,FirstNoteVisual? note,float cx,float cy)
    {
        if(note==null){Label(d,"—",cx-2,cy,4,3.3f);return;}
        int firstTriangle=d.Triangles.Count;
        float angle=-(note.RotationZ??0)*(float)Math.PI/180;
        DrawPoint P(float x,float y)=>new DrawPoint(cx+x*(float)Math.Cos(angle)-y*(float)Math.Sin(angle),cy+x*(float)Math.Sin(angle)+y*(float)Math.Cos(angle));
        // Rounded corners and a narrow transparent fringe smooth the silhouette
        // even without MSAA. The fringe rotates with the physical note face.
        var inner=new DrawPoint[20];var outer=new DrawPoint[20];
        for(int corner=0;corner<4;corner++)for(int step=0;step<=4;step++)
        {
            float a=(-90+90*corner+22.5f*step)*(float)Math.PI/180;
            float x=corner<2?1.8f:-1.8f,y=corner==0||corner==3?-1.8f:1.8f;
            int i=corner*5+step;
            inner[i]=P(x+.6f*(float)Math.Cos(a),y+.6f*(float)Math.Sin(a));
            outer[i]=P(x+.72f*(float)Math.Cos(a),y+.72f*(float)Math.Sin(a));
        }
        for(int i=0;i<inner.Length;i++)
        {
            int next=(i+1)%inner.Length;
            Tri(d,P(0,0),inner[i],inner[next],note.Color);
            d.Triangles.Add(new DrawTriangle {A=inner[i],B=outer[i],C=outer[next],Color=note.Color,AlphaB=0,AlphaC=0,Group=d.Group});
            d.Triangles.Add(new DrawTriangle {A=inner[i],B=outer[next],C=inner[next],Color=note.Color,AlphaB=0,Group=d.Group});
        }
        if(!note.RotationZ.HasValue)
            for(float x=-2;x<=2;x+=1)for(float y=-2;y<=2;y+=1)
                Quad(d,P(x-.12f,y-.12f),P(x+.12f,y-.12f),P(x+.12f,y+.12f),P(x-.12f,y+.12f),new DrawColor(1,1,1,.4f));
        if(note.Direction==8)
        {
            for(int i=0;i<12;i++){float a=i*(float)Math.PI/6,b=(i+1)*(float)Math.PI/6;Tri(d,P(0,0),P(.55f*(float)Math.Cos(a),.55f*(float)Math.Sin(a)),P(.55f*(float)Math.Cos(b),.55f*(float)Math.Sin(b)),DrawColor.White);}
        }
        else
        {
            // Native arrow points down at zero rotation. If orientation is unknown,
            // keep the declared direction visible while the cube background is dotted.
            float extra=note.RotationZ.HasValue?0:-CompatibleAnalysis.Angle(note.Direction)*(float)Math.PI/180;
            DrawPoint A(float x,float y)=>P(x*(float)Math.Cos(extra)-y*(float)Math.Sin(extra),x*(float)Math.Sin(extra)+y*(float)Math.Cos(extra));
            Quad(d,A(-.3f,-1.35f),A(.3f,-1.35f),A(.3f,.25f),A(-.3f,.25f),DrawColor.White);
            Tri(d,A(-1,.1f),A(1,.1f),A(0,1.25f),DrawColor.White);
        }
        if(note.ShouldBlink)for(int i=firstTriangle;i<d.Triangles.Count;i++)d.Triangles[i].Blink=true;
    }
    public static Drawing Graph(MenuRenderData data)
    {
        var d=new Drawing {Group=DrawGroup.Density};
        if(data.Duration<=0||float.IsNaN(data.Duration)||float.IsInfinity(data.Duration))
        {
            d.Group=DrawGroup.HistoryGuide;Rect(d,0,4.2f,Width,2.2f,new DrawColor(.3f,.3f,.3f,.6f));
            d.Group=DrawGroup.Density;Label(d,"—",48,5,5,3);return d;
        }
        float step=data.Colors.Length>0?Width/data.Colors.Length:0;
        for(int i=0;i<data.Colors.Length;i++)
        {
            var c=data.Colors[i];if(c.Length<4)continue;
            Rect(d,i*step,4.2f,step+.02f,2.2f,new DrawColor(c[0],c[1],c[2],c[3]));
        }
        if(data.Colors.Length==0)
        {
            d.Group=DrawGroup.HistoryGuide;
            Rect(d,0,4.2f,Width,2.2f,new DrawColor(.3f,.3f,.3f,.6f));
        }
        var drawn=new HashSet<(string Source,int Position)>();
        // Both sources share the band edge; own records draw last, on top.
        foreach(var m in data.Markers.OrderBy(m=>m.Source=="beatinsight"?1:0))
        {
            if(!HistoryOverview.CanDraw(m))continue;
            double position=m.NormalizedPosition.GetValueOrDefault();
            // Coincident crosses on the same source track share geometry only;
            // full records and modifiers stay intact in the view data.
            if(!drawn.Add((m.Source,(int)Math.Round(position*1000))))continue;
            float x=Math.Max(.8f,Math.Min(Width-.8f,Width*(float)position)),y=4.2f;
            var color=m.Source=="legacy"?new DrawColor(1,1,0):new DrawColor(1,0,0);
            d.Group=m.Source=="legacy"?DrawGroup.ImportedDeaths:DrawGroup.OwnDeaths;
            Line(d,x-.6f,y-.6f,x+.6f,y+.6f,.4f,color);Line(d,x-.6f,y+.6f,x+.6f,y-.6f,.4f,color);
        }
        d.Group=DrawGroup.Density;
        for(int i=0;i<=4;i++)
        {
            int seconds=(int)(data.Duration*i/4);string time=(seconds/60)+":"+(seconds%60).ToString("00",CultureInfo.InvariantCulture);
            Label(d,time,i*23.5f,8.3f,8,1.8f);
        }
        return d;
    }
    public const float CompactMarkerScale=1.25f;
    public static float CompactMarkerHalfExtent=>CompactMarkerScale*(.6f+.2f/(float)Math.Sqrt(2));
    public static float CompactMarkerCenter(float height)=>height*.1f-Math.Max(0,CompactMarkerHalfExtent-height*.4f);
    public static Drawing CompactGraph(MenuRenderData data,float height)
    {
        var drawing=Graph(data);
        int bandTriangles=drawing.Triangles.Count(t=>t.Group==DrawGroup.Density||t.Group==DrawGroup.HistoryGuide);
        float edge=height*.1f;
        float center=0;
        for(int i=0;i<drawing.Triangles.Count;i++)
        {
            bool band=i<bandTriangles;
            if(!band&&(i-bandTriangles)%4==0)
            {
                int first=bandTriangles+(i-bandTriangles)/4*4;
                var points=drawing.Triangles.Skip(first).Take(4).SelectMany(t=>new[]{t.A.X,t.B.X,t.C.X}).ToArray();
                center=(points.Min()+points.Max())/2;
            }
            float clampedCenter=Math.Max(CompactMarkerHalfExtent,Math.Min(Width-CompactMarkerHalfExtent,center));
            DrawPoint Move(DrawPoint p)=>band
                ? new DrawPoint(p.X,edge+(p.Y-4.2f)*height*.8f/2.2f)
                : new DrawPoint(clampedCenter+(p.X-center)*CompactMarkerScale,CompactMarkerCenter(height)+(p.Y-4.2f)*CompactMarkerScale);
            var t=drawing.Triangles[i];t.A=Move(t.A);t.B=Move(t.B);t.C=Move(t.C);
        }
        drawing.Labels.Clear();
        if(data.Duration<=0||float.IsNaN(data.Duration)||float.IsInfinity(data.Duration)||bandTriangles==0&&drawing.Triangles.Count==0)
            Label(drawing,"—",48,height/2,5,Math.Min(3,height));
        return drawing;
    }
}
