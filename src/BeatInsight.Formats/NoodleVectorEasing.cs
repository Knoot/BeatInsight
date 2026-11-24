using System;
using System.Collections.Generic;

namespace BeatInsight.Formats;

internal static class NoodleVectorEasing
{
    internal enum Kind {Linear,InQuad,OutQuad,InOutQuad,InCubic,OutCubic,InOutCubic,InQuart,OutQuart,InOutQuart,
        InQuint,OutQuint,InOutQuint,InSine,OutSine,InOutSine,InCirc,OutCirc,InOutCirc,InExpo,OutExpo,InOutExpo,InBounce,OutBounce,InOutBounce}
    static readonly Dictionary<string,Kind> Names=BuildNames();
    static Dictionary<string,Kind> BuildNames()
    {
        var names=new Dictionary<string,Kind>(StringComparer.Ordinal);
        foreach(Kind kind in Enum.GetValues(typeof(Kind)))names.Add("ease"+kind,kind);
        return names;
    }
    internal static bool TryRead(string name,out Kind kind)=>Names.TryGetValue(name,out kind);
    static float Power(float t,int power)
    {
        float result=t;for(int i=1;i<power;i++)result*=t;return result;
    }
    static float OutBounce(float t)
    {
        if(t<4f/11)return 121*t*t/16;
        if(t<8f/11)return 9.075f*t*t-9.9f*t+3.4f;
        if(t<.9f)return 12.066482f*t*t-19.635458f*t+8.898061f;
        return 10.8f*t*t-20.52f*t+10.72f;
    }
    internal static float Apply(Kind kind,float t)
    {
        if(kind==Kind.Linear)return t;
        if(kind>=Kind.InQuad&&kind<=Kind.InOutQuint)
        {
            int index=(int)kind-1,power=index/3+2;
            return index%3==0?Power(t,power):index%3==1?1-Power(1-t,power):t<.5f?.5f*Power(2*t,power):1-.5f*Power(2-2*t,power);
        }
        const float pi=3.1415927f;
        switch(kind)
        {
            case Kind.InSine:return 1-(float)Math.Cos(t*(pi*.5f));
            case Kind.OutSine:return (float)Math.Sin(t*(pi*.5f));
            case Kind.InOutSine:return .5f*(1-(float)Math.Cos(t*pi));
            case Kind.InCirc:return 1-(float)Math.Sqrt(Math.Max(0,1-t*t));
            case Kind.OutCirc:return (float)Math.Sqrt(Math.Max(0,1-(t-1)*(t-1)));
            case Kind.InOutCirc:return t<.5f?.5f*(1-(float)Math.Sqrt(Math.Max(0,1-4*t*t))):.5f*((float)Math.Sqrt(Math.Max(0,1-4*(t-1)*(t-1)))+1);
            case Kind.InExpo:return t<=0?t:(float)Math.Pow(2,10*(t-1));
            case Kind.OutExpo:return t>=1?t:1-(float)Math.Pow(2,-10*t);
            case Kind.InOutExpo:return t==0||t>=1?t:t<.5f?.5f*(float)Math.Pow(2,20*t-10):1-.5f*(float)Math.Pow(2,-20*t+10);
            case Kind.InBounce:return 1-OutBounce(1-t);
            case Kind.OutBounce:return OutBounce(t);
            case Kind.InOutBounce:return t<.5f?.5f*(1-OutBounce(1-2*t)):.5f*OutBounce(2*t-1)+.5f;
            default:throw new ArgumentOutOfRangeException(nameof(kind));
        }
    }
}
