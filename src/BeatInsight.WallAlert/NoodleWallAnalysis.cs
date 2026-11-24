#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json.Linq;

namespace BeatInsight.WallAlert;

// Nominal coverage at upper layer 2. Not a player collision simulation.
public readonly struct NoodleWallShape
{
    public readonly float X,Y,Width,Height;
    public readonly float? Length;
    public NoodleWallShape(float x,float y,float width,float height,float? length=null)
    {X=x;Y=y;Width=width;Height=height;Length=length;}
}
public static class NoodleWallAnalysis
{
    static float? Component(JToken? value,int index,int maximum,string field)
    {
        if(value==null||value.Type==JTokenType.Null)return null;
        if(value is not JArray array||array.Count>maximum)throw new NotSupportedException("Invalid Noodle wall "+field);
        if(index>=array.Count||array[index].Type==JTokenType.Null)return null;
        var token=array[index];
        if(token.Type!=JTokenType.Integer&&token.Type!=JTokenType.Float)throw new NotSupportedException("Invalid Noodle wall "+field);
        float number=(float)token;
        if(!float.IsFinite(number)||Math.Abs(number)>1000000)throw new NotSupportedException("Unsupported Noodle wall coordinate range: "+field);
        return number;
    }
    public static NoodleWallShape Read(JToken item,int major,bool allowLength=false,bool leftHanded=false)
    {
        if(major!=2&&major!=3)throw new NotSupportedException("Unsupported Noodle obstacle version");
        bool old=major==2;
        var customToken=item[old?"_customData":"customData"];
        if(customToken!=null&&customToken.Type!=JTokenType.Null&&customToken is not JObject)throw new NotSupportedException("Invalid Noodle wall customData");
        var custom=customToken as JObject;
        var position=custom?[old?"_position":"coordinates"];
        var size=custom?[old?"_scale":"size"];
        float? px=Component(position,0,2,"coordinates"),py=Component(position,1,2,"coordinates");
        float? width=Component(size,0,3,"size"),height=Component(size,1,3,"size");
        float? length=Component(size,2,3,"size");
        if(length.HasValue&&!allowLength)throw new NotSupportedException("Noodle wall length overrides require a physical timeline profile");
        if(width<0||height<0||length<0)throw new NotSupportedException("Negative Noodle wall dimensions are not supported");
        int type=(int?)item["_type"]??0;
        if(old&&type!=0&&type!=1)throw new NotSupportedException("Unsupported Noodle legacy obstacle type");
        float lane=(float?)item[old?"_lineIndex":"x"]??0;
        float effectiveWidth=width??(float?)item[old?"_width":"w"]??0;
        // In runtime input the base lane has already passed ObstacleData.Mirror.
        // Custom obstacle positions are mirrored later by the extension itself.
        float x=px.HasValue?(leftHanded?2-px.Value-effectiveWidth:px.Value+2):leftHanded&&width.HasValue?lane-effectiveWidth:lane;
        var shape=new NoodleWallShape(x,
            py??(old?(type==1?2:0):(float?)item["y"]??0),
            effectiveWidth,height??(old?(type==1?3:5):(float?)item["h"]??0),length);
        double roll=NoodleWallRotation.Read(custom,old);
        if(roll!=0&&!height.HasValue)throw new NotSupportedException("Local wall roll needs an explicit height; default height is clipped by the game");
        return NoodleWallRotation.Apply(shape,leftHanded&&roll!=0?360-roll:roll);
    }
    internal static WallAnalysis Analyze(string json,float bpm)
    {
        var root=JObject.Parse(json);bool old=root["_version"]!=null;
        var parts=new List<(float Beat,float End,NoodleWallShape Shape)>();
        foreach(var item in root[old?"_obstacles":"obstacles"]??new JArray())
        {
            var custom=item[old?"_customData":"customData"] as JObject;
            if((bool?)custom?[old?"_fake":"NE_fake"]==true)continue;
            var shape=Read(item,old?2:3);
            float beat=(float?)item[old?"_time":"b"]??0,duration=(float?)item[old?"_duration":"d"]??0;
            if(!float.IsFinite(beat)||!float.IsFinite(duration)||!float.IsFinite(beat+duration)||duration<0)throw new NotSupportedException("Invalid Noodle wall interval");
            if(shape.Width>0&&shape.Height>0&&shape.Y<=2&&shape.Y+shape.Height>2)parts.Add((beat,beat+duration,shape));
        }
        var snapshots=parts.Select(p=>new NoodleWallSnapshot(p.Beat,p.Beat,p.End,p.Shape)).ToArray();
        var times=Build(snapshots).Select(index=>{
            var time=TimeSpan.FromSeconds(Math.Max(0,snapshots[index].Beat)*60/(bpm>0?bpm:120));
            return $"{(int)time.TotalMinutes:00}:{time.Seconds:00}";
        });
        return new WallAnalysis(old?"v2":"v3",times);
    }
    public static int[] Build(IReadOnlyList<NoodleWallSnapshot> walls,float minimumTime=float.NegativeInfinity)
    {
        var ordered=walls.Select((wall,index)=>(wall.Time,wall.Beat,End:wall.EndBeat,wall.Shape,index)).Where(p=>float.IsFinite(p.Time)&&float.IsFinite(p.Beat)&&float.IsFinite(p.End)&&p.End>=p.Beat&&p.Time>=minimumTime&&p.Shape.Width>0&&p.Shape.Height>0&&p.Shape.Y<=2&&p.Shape.Y+p.Shape.Height>2).OrderBy(p=>p.Beat).ToArray();
        var triggers=new List<int>();float previous=float.NegativeInfinity;
        var active=new List<(float Time,float Beat,float End,NoodleWallShape Shape,int index)>();int next=0;
        foreach(var candidate in ordered.Where(p=>p.Shape.Y>0).GroupBy(p=>p.Beat).Select(g=>g.First()))
        {
            float beat=candidate.Beat;
            active.RemoveAll(p=>p.End+WallAnalyzer.CrouchWallCoverageToleranceBeats<beat);
            while(next<ordered.Length&&ordered[next].Beat<=beat)
            {
                var part=ordered[next++];
                if(part.End+WallAnalyzer.CrouchWallCoverageToleranceBeats>=beat)active.Add(part);
            }
            float covered=1;
            foreach(var part in active.OrderBy(p=>p.Shape.X))
            {
                if(part.Shape.X>covered)break;
                covered=Math.Max(covered,part.Shape.X+part.Shape.Width);
            }
            if(covered<3)continue;
            if(beat>previous+WallAnalyzer.CrouchWallSequenceToleranceBeats)
            {
                triggers.Add(candidate.index);
            }
            previous=beat;
        }
        return triggers.ToArray();
    }
    // Only the linear movement segment before the native avoided/despawn phase.
    public static float Duration(float originalSeconds,float? length,float speed)
    {
        if(!float.IsFinite(originalSeconds)||originalSeconds<0)throw new NotSupportedException("Invalid obstacle duration");
        if(!length.HasValue)return originalSeconds;
        if(!float.IsFinite(speed)||speed<=0)throw new NotSupportedException("Static wall length needs a known positive constant movement speed");
        if(!float.IsFinite(length.Value)||length.Value<0)throw new NotSupportedException("Invalid static wall length");
        float seconds=length.Value*.6f/speed;
        if(!float.IsFinite(seconds)||seconds>originalSeconds)throw new NotSupportedException("Wall length exceeds the verified linear lifetime segment");
        return seconds;
    }

}

public readonly struct NoodleWallSnapshot
{
    public readonly float Time,Beat,EndBeat;
    public readonly NoodleWallShape Shape;
    public NoodleWallSnapshot(float time,float beat,float endBeat,NoodleWallShape shape)
    {Time=time;Beat=beat;EndBeat=endBeat;Shape=shape;}
}
