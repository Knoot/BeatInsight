using System;
using System.Collections.Generic;

namespace BeatInsight.Core;

public readonly struct StatisticsColumn
{
    public readonly DrawGroup Group;
    public readonly float Left,Right,Offset;
    internal StatisticsColumn(DrawGroup group,float left,float right,float offset)
    {Group=group;Left=left;Right=right;Offset=offset;}
}

public readonly struct StatisticsBounds
{
    public readonly DrawGroup Group;
    public readonly float Left,Right;
    public StatisticsBounds(DrawGroup group,float left,float right){Group=group;Left=left;Right=right;}
}

// Geometry, native icons and hover regions share the same normalized columns.
public sealed class StatisticsLayout
{
    static readonly DrawGroup[] order={DrawGroup.Average,DrawGroup.Peak,DrawGroup.Median,DrawGroup.Squats,DrawGroup.FirstNotes};
    public readonly List<StatisticsColumn> Columns=new List<StatisticsColumn>();
    public StatisticsLayout(MenuVisibility visibility,Drawing drawing,params StatisticsBounds[] icons)
    {
        var bounds=new Dictionary<DrawGroup,(float Left,float Right)>();
        void Include(DrawGroup group,float left,float right)
        {
            if(!visibility.Includes(group))return;
            if(bounds.TryGetValue(group,out var old)){left=Math.Min(left,old.Left);right=Math.Max(right,old.Right);}
            bounds[group]=(left,right);
        }
        foreach(var t in drawing.Triangles)Include(t.Group,Math.Min(t.A.X,Math.Min(t.B.X,t.C.X)),Math.Max(t.A.X,Math.Max(t.B.X,t.C.X)));
        foreach(var l in drawing.Labels)Include(l.Group,l.X,l.X+l.Width);
        foreach(var icon in icons)Include(icon.Group,icon.Left,icon.Right);
        int count=0;float occupied=0;
        foreach(var group in order)if(bounds.TryGetValue(group,out var b)){count++;occupied+=b.Right-b.Left;}
        if(count==0)return;
        float gap=count>1?Math.Max(0,(100-occupied)/(count-1)):0;
        float targetLeft=count==1?(100-occupied)/2:0;
        foreach(var group in order)
        {
            if(!bounds.TryGetValue(group,out var b))continue;
            float width=b.Right-b.Left;
            Columns.Add(new StatisticsColumn(group,targetLeft,targetLeft+width,targetLeft-b.Left));
            targetLeft+=width+gap;
        }
    }
    public float Offset(DrawGroup group)
    {foreach(var column in Columns)if(column.Group==group)return column.Offset;return 0;}
    public void Apply(Drawing drawing)
    {
        foreach(var triangle in drawing.Triangles)
        {
            float offset=Offset(triangle.Group);
            triangle.A=new DrawPoint(triangle.A.X+offset,triangle.A.Y);
            triangle.B=new DrawPoint(triangle.B.X+offset,triangle.B.Y);
            triangle.C=new DrawPoint(triangle.C.X+offset,triangle.C.Y);
        }
        foreach(var label in drawing.Labels)label.X+=Offset(label.Group);
    }
}
