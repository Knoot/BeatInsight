using System;

namespace BeatInsight.Formats;

// Known constant-speed movement only. This is not permission to treat an
// animated wall as static, nor an estimate of player collision bounds.
public sealed class WallJumpMovement
{
    public float NoteJumpSpeed {get;}
    public float HalfJumpSeconds {get;}
    public float JumpSeconds=>2*HalfJumpSeconds;
    public float JumpDistance=>NoteJumpSpeed*JumpSeconds;
    WallJumpMovement(float speed,float half){NoteJumpSpeed=speed;HalfJumpSeconds=half;}
    static bool Finite(float x)=>!float.IsNaN(x)&&!float.IsInfinity(x);
    static void Require(bool condition,string reason)
    {if(!condition)throw new NotSupportedException("Unresolved wall movement: "+reason);}
    public static WallJumpMovement FromMeasured(float speed,float halfJumpSeconds,float jumpSeconds)
    {
        Require(Finite(speed)&&speed>0&&Finite(halfJumpSeconds)&&halfJumpSeconds>0&&Finite(jumpSeconds)
            &&jumpSeconds==2*halfJumpSeconds&&Finite(speed*jumpSeconds),"invalid measured constant movement");
        return new WallJumpMovement(speed,halfJumpSeconds);
    }
    public static WallJumpMovement Resolve(JumpSettingsSnapshot? player,float bpm,float mapSpeed,float mapBeatOffset,
        float? objectSpeed=null,float? objectSpawnOffset=null,bool variableSpeed=false)
    {
        Require(player!=null,"player jump policy is unknown");
        Require(!variableSpeed,"variable speed has not been validated");
        Require(Finite(bpm)&&bpm>0,"invalid BPM");
        Require(Finite(mapSpeed)&&mapSpeed>0,"map NJS is unknown or invalid");
        Require(Finite(mapBeatOffset),"invalid map beat offset");
        float speed=objectSpeed??mapSpeed;
        Require(Finite(speed)&&speed>0,"invalid per-object NJS");
        float selected=player!.Policy==0?mapBeatOffset+player.PlayerBeatOffset:player.FixedHalfJumpSeconds;
        float value=objectSpawnOffset??selected;
        Require(Finite(selected)&&Finite(value),"nonfinite jump value");
        float half;
        if(player.Policy==1)
        {
            Require(value>0,"non-positive fixed half-jump duration");
            half=value;
        }
        else
        {
            float beatSeconds=60/bpm,step=speed*beatSeconds;
            Require(Finite(step)&&step>0,"unrepresentable beat distance");
            float beats=4;
            while(step*beats>18-.001f)beats*=.5f;
            half=beatSeconds*Math.Max(.25f,beats+value);
        }
        Require(Finite(half)&&half>0&&Finite(2*half),"unrepresentable jump duration");
        Require(Finite(speed*(2*half)),"unrepresentable jump distance");
        return new WallJumpMovement(speed,half);
    }
}
