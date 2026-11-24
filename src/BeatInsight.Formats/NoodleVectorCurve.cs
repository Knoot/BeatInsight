using System;
using Newtonsoft.Json.Linq;

namespace BeatInsight.Formats;

// Inline vector points with verified easing. Unknown annotations and spline
// modes never fall back to linear; named references are resolved in map scope.
public sealed class NoodleVectorCurve
{
    public readonly struct Value
    {
        public readonly float X,Y,Z;
        public Value(float x,float y,float z){X=x;Y=y;Z=z;}
        public bool IsZero=>X==0&&Y==0&&Z==0;
    }
    readonly float[] times;
    readonly Value[] values;
    readonly NoodleVectorEasing.Kind[] easings;
    NoodleVectorCurve(float[] times,Value[] values,NoodleVectorEasing.Kind[] easings){this.times=times;this.values=values;this.easings=easings;}
    public bool HasZeroZ
    {get {foreach(var value in values)if(value.Z!=0)return false;return true;}}
    public bool AlwaysZero {get;private set;}
    public bool IsSettledZeroAt(float time)=>values[values.Length-1].IsZero&&time>times[times.Length-1];
    public bool SettledBeforeNote=>times[times.Length-1]<.5f;
    public bool RestoredBeforeNote=>values[values.Length-1].IsZero&&times[times.Length-1]<.5f;
    static bool Number(JToken token,out float value)
    {
        value=0;
        if(token.Type!=JTokenType.Integer&&token.Type!=JTokenType.Float)return false;
        try{value=(float)token;}
        catch(Exception e) when(e is OverflowException||e is InvalidCastException||e is FormatException){return false;}
        return !float.IsNaN(value)&&!float.IsInfinity(value)&&Math.Abs(value)<=1000000;
    }
    public static bool TryRead(JToken? token,out NoodleVectorCurve? curve)
    {
        curve=null;if(token is not JArray points||points.Count==0)return false;
        if(points.Count==3&&Number(points[0],out float x)&&Number(points[1],out float y)&&Number(points[2],out float z))
        {
            var value=new Value(x,y,z);curve=new NoodleVectorCurve(new[]{0f},new[]{value},new NoodleVectorEasing.Kind[1]) {AlwaysZero=value.IsZero};return true;
        }
        var times=new float[points.Count];var values=new Value[points.Count];var easings=new NoodleVectorEasing.Kind[points.Count];bool zero=true;float previous=-1;
        for(int i=0;i<points.Count;i++)
        {
            if(points[i] is not JArray frame||frame.Count<4||frame.Count>5||!Number(frame[0],out x)||!Number(frame[1],out y)||!Number(frame[2],out z)
                ||!Number(frame[3],out float time)||time<0||time>1||time<=previous)return false;
            if(frame.Count==5&&(frame[4].Type!=JTokenType.String||!NoodleVectorEasing.TryRead((string)frame[4]!,out easings[i])))return false;
            times[i]=time;values[i]=new Value(x,y,z);zero&=values[i].IsZero;previous=time;
        }
        curve=new NoodleVectorCurve(times,values,easings) {AlwaysZero=zero};return true;
    }
    public Value At(float time)
    {
        if(float.IsNaN(time)||float.IsInfinity(time))throw new ArgumentOutOfRangeException(nameof(time));
        if(time<=times[0])return values[0];
        int last=times.Length-1;if(time>=times[last])return values[last];
        int next=Array.BinarySearch(times,time);if(next>=0)return values[next];next=~next;
        int previous=next-1;float fraction=(time-times[previous])/(times[next]-times[previous]);
        fraction=NoodleVectorEasing.Apply(easings[next],fraction);
        var a=values[previous];var b=values[next];return new Value(a.X+(b.X-a.X)*fraction,a.Y+(b.Y-a.Y)*fraction,a.Z+(b.Z-a.Z)*fraction);
    }
}
