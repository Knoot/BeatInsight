using System;
using System.Linq;
using Newtonsoft.Json.Linq;

namespace BeatInsight.Formats;

internal static class NoodleWallEntry
{
    static bool Number(JToken? t,out float value)
    {
        value=0;if(t==null||(t.Type!=JTokenType.Integer&&t.Type!=JTokenType.Float))return false;
        value=(float)t;return !float.IsNaN(value)&&!float.IsInfinity(value);
    }
    static bool Optional(JObject data,string name,out float? value)
    {
        value=null;var token=data[name];if(token==null||token.Type==JTokenType.Null)return true;
        if(!Number(token,out float n))return false;value=n;return true;
    }
    // Project the working root only. The source JSON/fingerprint is retained.
    internal static bool Project(JObject root,JToken selected,int major,float bpm,JumpSettingsSnapshot? player)
    {
        if(major!=3||player==null||!player.StandardSongMovement||root["obstacles"] is not JArray walls)return false;
        if(new[]{"environment","_environment"}.Any(k=>!NoodleStaticProfile.IsEmptyTrackValue(root[k])))return false;
        foreach(string key in new[]{"customData","_customData"})
            if(root[key] is JObject metadata&&new[]{"environment","_environment"}.Any(k=>!NoodleStaticProfile.IsEmptyTrackValue(metadata[k])))return false;
        if(new[]{"njsEvents","_njsEvents","rotationEvents","_rotationEvents"}.Any(k=>root[k]!=null&&root[k]!.Type!=JTokenType.Null&&(!(root[k] is JArray a)||a.Count>0)))return false;
        if(root["bpmEvents"] is JToken tempo)
        {
            if(tempo is not JArray changes)return false;
            foreach(var change in changes)
                if(change is not JObject||!Number(change["b"],out float b)||b!=0||!Number(change["m"],out float m)||m!=bpm)return false;
        }
        if(!Number(selected["_noteJumpMovementSpeed"]??selected["noteJumpMovementSpeed"],out float speed)||speed<=0)return false;
        var offsetToken=selected["_noteJumpStartBeatOffset"]??selected["noteJumpStartBeatOffset"];
        float mapOffset=0;if(offsetToken!=null&&!Number(offsetToken,out mapOffset))return false;
        bool projected=false;
        foreach(var wall in walls)
        {
            if(wall is not JObject||NoodleFakeData.IsFake(wall,major)||wall["customData"] is not JObject custom||custom["animation"] is not JObject animation)continue;
            if(!Number(wall["b"],out float beat)||!Number(wall["d"],out float beats)||beats<=0)continue;
            if(!Optional(custom,"noteJumpMovementSpeed",out var objectSpeed)||!Optional(custom,"noteJumpStartBeatOffset",out var objectOffset))continue;
            try
            {
                var movement=WallJumpMovement.Resolve(player,bpm,speed,mapOffset,objectSpeed,objectOffset);
                float start=beat/bpm*60,duration=(beat+beats)/bpm*60-start;
                if(float.IsNaN(duration)||float.IsInfinity(duration)||duration<=0)continue;
                if(TryProject(custom,start,duration,movement))projected=true;
            }
            catch(NotSupportedException){/* Keep the original unsupported animation. */}
        }
        return projected;
    }
    internal static bool TryProject(JObject custom,float start,float duration,WallJumpMovement movement)
    {
        if(custom["animation"] is not JObject animation||float.IsNaN(start)||float.IsInfinity(start)||float.IsNaN(duration)||float.IsInfinity(duration)||duration<=0)return false;
        if(!NoodleVectorCurve.TryRead(animation["offsetPosition"],out var curve)||curve!.AlwaysZero||!curve.HasZeroZ)return false;
        if(animation.Properties().Any(p=>p.Name!="offsetPosition"&&p.Name!="dissolve"&&p.Name!="dissolveArrow"))return false;
        if(new[]{"localRotation","worldRotation","rotation","_localRotation","_rotation"}.Any(k=>custom[k]!=null&&custom[k]!.Type!=JTokenType.Null))return false;
        if(!NoodleStaticProfile.IsEmptyTrackValue(custom["track"])||custom["_track"]!=null)return false;
        if(custom["size"] is JArray size&&size.Count>2&&size[2].Type!=JTokenType.Null)return false;
        var window=WallContactInterval.AtPointPlane(movement,start,0,0,movement.NoteJumpSpeed*duration);
        float spawn=start-.5f-movement.HalfJumpSeconds;
        float normalized=(window.Start-spawn-.5f)/(movement.JumpSeconds+duration);
        if(!curve.IsSettledZeroAt(normalized-.00001f))return false;
        animation.Remove("offsetPosition");return true;
    }

}
