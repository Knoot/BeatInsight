using System;

namespace BeatInsight.Formats;

// Constant forward motion and known local Z slab only. Head Z is explicit;
// callers must validate rotation/scale/time overrides before using this window.
public sealed class WallContactInterval
{
    public float Start {get;}
    public float End {get;}
    WallContactInterval(float start,float end){Start=start;End=end;}
    static bool Finite(float value)=>!float.IsNaN(value)&&!float.IsInfinity(value);
    public static WallContactInterval AtPointPlane(WallJumpMovement? movement,float wallTime,float headZ,float localMinZ,float localMaxZ)
    {
        if(movement==null||!Finite(wallTime)||!Finite(headZ)||!Finite(localMinZ)||!Finite(localMaxZ)||localMaxZ<localMinZ)
            throw new NotSupportedException("Unresolved wall contact interval: unknown movement or invalid Z slab");
        float start=wallTime+(localMinZ-headZ)/movement.NoteJumpSpeed;
        float end=wallTime+(localMaxZ-headZ)/movement.NoteJumpSpeed;
        if(!Finite(start)||!Finite(end)||end<start)throw new NotSupportedException("Unrepresentable wall contact interval");
        return new WallContactInterval(start,end);
    }
}
