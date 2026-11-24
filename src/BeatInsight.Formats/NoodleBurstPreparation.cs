using System;
using System.Collections.Generic;
using System.Linq;
using BeatInsight.Core;
namespace BeatInsight.Formats;

internal static class NoodleBurstPreparation
{
    internal const int MaxSliceCount=4096;
    static IEnumerable<MapNote> Row(MapNote[] ordered,float time)
    {
        int low=0,high=ordered.Length;
        while(low<high){int middle=low+(high-low)/2;if(ordered[middle].Time<time-.001f)low=middle+1;else high=middle;}
        for(int i=low;i<ordered.Length&&ordered[i].Time<=time+.001f;i++)
            if(Math.Abs(ordered[i].Time-time)<.001f)yield return ordered[i];
    }
    public static void Validate(List<MapNote> notes,List<MapNote> bombs,List<PreparedSlider> arcs,List<PreparedSlider> chains)
    {
        if(chains.Count==0)return;
        var objects=notes.Concat(bombs).OrderBy(n=>n.Time).ToArray();
        var heads=chains.Select(s=>s.Head).OrderBy(n=>n.Time).ToArray();
        var arcPoints=arcs.Select(s=>s.Head).Concat(arcs.Select(s=>new MapNote {Time=s.TailTime,X=s.TailX,Y=s.TailY,NoodleX=s.NoodleTailX,NoodleY=s.NoodleTailY})).OrderBy(n=>n.Time).ToArray();
        var sourceNotes=new HashSet<MapNote>();
        // Prefix maxima allow isolated orphan validation without scanning all
        // other sliders for every missing head.
        var intervals=arcs.Concat(chains).OrderBy(s=>s.Head.Time).ToArray();
        var intervalIndex=new Dictionary<PreparedSlider,int>();var maxTail=new float[intervals.Length];
        for(int i=0;i<intervals.Length;i++){intervalIndex.Add(intervals[i],i);maxTail[i]=i==0?intervals[i].TailTime:Math.Max(maxTail[i-1],intervals[i].TailTime);}
        foreach(var chain in chains)
        {
            if(chain.Head.Color<0||chain.Head.Color>1||chain.Head.SliceCount<1||chain.Head.SliceCount>MaxSliceCount||chain.TailTime<chain.Head.Time)
                throw new NotSupportedException("Unsupported Noodle burst color, slice count or interval");
            var headRow=Row(objects,chain.Head.Time).ToArray();
            var matches=headRow.Where(n=>NoodleGeometry.Same(n,chain.Head)).ToArray();
            if(matches.Length==0)
            {
                if(chain.TailTime==chain.Head.Time||chain.Head.SliceCount==1)throw new NotSupportedException("Zero-duration or single-slice Noodle burst requires a real head");
                int index=intervalIndex[chain];
                bool overlap=index>0&&maxTail[index-1]>=chain.Head.Time-.001f||index+1<intervals.Length&&intervals[index+1].Head.Time<=chain.TailTime+.001f;
                int low=0,high=objects.Length;
                while(low<high){int middle=low+(high-low)/2;if(objects[middle].Time<chain.Head.Time-.001f)low=middle+1;else high=middle;}
                bool hasObjects=low<objects.Length&&objects[low].Time<=chain.TailTime+.001f;
                if(overlap||hasObjects||chain.Head.Direction<0||chain.Head.Direction>8)
                    throw new NotSupportedException("Noodle burst has no physical head; only isolated slice-only chains are supported");
                chain.IsOrphan=true;continue;
            }
            if(matches.Length!=1||matches[0].Time!=chain.Head.Time||matches[0].IsFake||matches[0].Color!=chain.Head.Color||matches[0].Direction!=chain.Head.Direction)
                throw new NotSupportedException("Noodle burst needs one exact real head with matching color and direction");
            if(!sourceNotes.Add(matches[0]))throw new NotSupportedException("Multiple Noodle bursts share one physical source note");
            chain.SourceNote=matches[0];
            var sameHand=headRow.Where(n=>n.Color==chain.Head.Color).ToArray();
            if(sameHand.Any(n=>n.Time!=chain.Head.Time||n.Direction<0||n.Direction>8))
                throw new NotSupportedException("Noodle burst supports exact note rows; nearby rows need an ordering profile");
            // The native per-color angle override applies only to exactly two notes.
            chain.RowPartner=sameHand.Length==2?sameHand.First(n=>!ReferenceEquals(n,matches[0])):null;
            if(Row(heads,chain.Head.Time).Any(other=>!ReferenceEquals(chain.Head,other)&&NoodleGeometry.Same(chain.Head,other)))
                throw new NotSupportedException("Overlapping Noodle burst heads need a dedicated ordering profile");
            if(Row(arcPoints,chain.Head.Time).Any(point=>NoodleGeometry.Same(chain.Head,point)&&point.Time!=chain.Head.Time))
                throw new NotSupportedException("Nearby Noodle burst/arc rows need an exact time-slice profile");
            var anchors=Row(objects,chain.TailTime).Where(n=>NoodleGeometry.Same(n,chain.TailX,chain.TailY,chain.NoodleTailX,chain.NoodleTailY)).ToArray();
            bool singleTailAnchor=chain.Head.SliceCount==1&&anchors.Length==1
                &&(ReferenceEquals(anchors[0],matches[0])||anchors[0].Color!=chain.Head.Color);
            if(chain.TailTime==chain.Head.Time&&((anchors.Length>0&&!singleTailAnchor)||Row(arcPoints,chain.TailTime).Any(point=>NoodleGeometry.Same(point,chain.TailX,chain.TailY,chain.NoodleTailX,chain.NoodleTailY))))
                throw new NotSupportedException("Zero-duration Noodle burst tail overlaps need a dedicated row profile");
            if(anchors.Length>0&&(anchors.Length!=1||anchors[0].Time!=chain.TailTime||anchors[0].IsFake||anchors[0].Color<0||anchors[0].Direction<0||anchors[0].Direction>8||
                Row(heads,chain.TailTime).Any(head=>head.Time!=chain.TailTime&&NoodleGeometry.Same(head,anchors[0]))||
                Row(arcPoints,chain.TailTime).Any(point=>point.Time!=chain.TailTime&&NoodleGeometry.Same(point,anchors[0]))))
                throw new NotSupportedException("Noodle burst tail needs one exact real anchor and exact overlapping slider endpoints");
        }
    }
    public static void Tilt(MapNote note,PreparedSlider chain)
    {
        // Both installed vanilla and custom burst factories use Any (8) for
        // tailCutDirection. The extension compares that field with the note.
        if(note.Direction!=8)return;
        // Noodle resets this dot to zero. A same-hand pair can run before or
        // after that reset. Only expose an angle when both outcomes agree.
        if(chain.RowPartner!=null&&PairCanRotateDot(note,chain.RowPartner))
            note.CutDirectionAngleOffset=null;
        else note.CutDirectionAngleOffset=0;
    }
    static bool PairCanRotateDot(MapNote dot,MapNote partner)
    {
        float X(MapNote n)=>(n.X-1.5f)*.6f;
        float Y(MapNote n)=>n.Y==0?.25f:n.Y==1?.85f:1.45f;
        bool bothDots=partner.Direction==8;
        float angle=NoodleBurstGeometry.Angle(bothDots?0:partner.Direction,X(dot)-X(partner),Y(dot)-Y(partner));
        if(!bothDots&&Math.Abs(angle)>40)return false;
        float dotAngle=angle+(!bothDots&&partner.Direction>3?45:0);
        return dotAngle!=0;

    }
}

// Exposed primitive contract for independent installed-helper checks.
public static class NoodleBurstGeometry
{
    public static float Angle(int direction,float x,float y)
    {
        (float X,float Y) d=direction switch {0=>(0,1),1=>(0,-1),2=>(-1,0),3=>(1,0),4=>(-.7071f,.7071f),5=>(.7071f,.7071f),6=>(-.7071f,-.7071f),7=>(.7071f,-.7071f),_=>(0,0)};
        float Signed(float a,float b)
        {
            float denominator=(float)Math.Sqrt((d.X*d.X+d.Y*d.Y)*(a*a+b*b));
            if(denominator<1e-15f)return 0;
            float cosine=Math.Max(-1,Math.Min(1,(d.X*a+d.Y*b)/denominator));
            float angle=(float)Math.Acos(cosine)*57.29578f;
            return angle*(d.X*b-d.Y*a>=0?1:-1);
        }
        float forward=Signed(x,y),backward=Signed(-x,-y);
        return Math.Abs(forward)<Math.Abs(backward)?forward:backward;
    }
}
