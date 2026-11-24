using System;
using System.Globalization;

namespace BeatInsight.Formats;

// Player settings only; map NJS/offset remain part of the selected map input.
public sealed class JumpSettingsSnapshot : IEquatable<JumpSettingsSnapshot>
{
    public int Policy {get;}
    public bool StandardSongMovement {get;}
    public float PlayerBeatOffset {get;}
    public float FixedHalfJumpSeconds {get;}
    public JumpSettingsSnapshot(int policy,float playerBeatOffset,float fixedHalfJumpSeconds,bool standardSongMovement=false)
    {
        if(policy!=0&&policy!=1)throw new ArgumentOutOfRangeException(nameof(policy));
        if(float.IsNaN(playerBeatOffset)||float.IsInfinity(playerBeatOffset))throw new ArgumentOutOfRangeException(nameof(playerBeatOffset));
        if(float.IsNaN(fixedHalfJumpSeconds)||float.IsInfinity(fixedHalfJumpSeconds)||fixedHalfJumpSeconds<=0)throw new ArgumentOutOfRangeException(nameof(fixedHalfJumpSeconds));
        Policy=policy;StandardSongMovement=standardSongMovement;PlayerBeatOffset=playerBeatOffset;FixedHalfJumpSeconds=fixedHalfJumpSeconds;
    }
    public string CacheIdentity=>(StandardSongMovement?"normal:":"unknown:")+Policy.ToString(CultureInfo.InvariantCulture)+":"+
        BitConverter.SingleToInt32Bits(PlayerBeatOffset).ToString("X8",CultureInfo.InvariantCulture)+":"+
        BitConverter.SingleToInt32Bits(FixedHalfJumpSeconds).ToString("X8",CultureInfo.InvariantCulture);
    public bool Equals(JumpSettingsSnapshot? other)=>other!=null&&Policy==other.Policy&&StandardSongMovement==other.StandardSongMovement&&BitConverter.SingleToInt32Bits(PlayerBeatOffset)==BitConverter.SingleToInt32Bits(other.PlayerBeatOffset)
        &&BitConverter.SingleToInt32Bits(FixedHalfJumpSeconds)==BitConverter.SingleToInt32Bits(other.FixedHalfJumpSeconds);
    public override bool Equals(object? other)=>other is JumpSettingsSnapshot value&&Equals(value);
    public override int GetHashCode()=>Policy^(StandardSongMovement?16:0)^BitConverter.SingleToInt32Bits(PlayerBeatOffset)^BitConverter.SingleToInt32Bits(FixedHalfJumpSeconds);
}
