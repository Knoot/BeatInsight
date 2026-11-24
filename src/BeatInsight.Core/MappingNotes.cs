using System;

namespace BeatInsight.Core;

public static class MappingNotes
{
    public static bool IsExtendedDirection(int value)=>(value>=1000&&value<=1360)||(value>=2000&&value<=2360);
    public static bool IsRotatedDot(int value)=>value>=2000&&value<=2360;
    public static float Angle(int value)=>(value>=2000?2000:1000)-value;
    public static float X(int value)=>value>=1000||value<=-1000?-1.5f+(value<=-1000?value+2000f:value)*.6f/1000:(value-1.5f)*.6f;
    public static float Y(int value)
    {
        float step=1.45f-.85f;
        return value>=1000||value<=-1000?.85f-step-step+value*step/1000:.85f-step+value*step;
    }
}
