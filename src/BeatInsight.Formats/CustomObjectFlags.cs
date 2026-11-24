using System;
using System.Collections;
using Newtonsoft.Json.Linq;
using BeatInsight.WallAlert;

namespace BeatInsight.Formats;

// Read only during level preparation. No dependency on the custom-data assembly.
public static class CustomObjectFlags
{
    public static bool HasCustomWallGeometry(object item)
    {
        if(item.GetType().GetProperty("customData")?.GetValue(item) is not IDictionary data)return false;
        foreach(string key in new[]{"coordinates","_position","size","_scale","scale","worldRotation","_rotation","localRotation","_localRotation","animation","_animation","track","_track","uninteractable","_interactable"})
            if(data.Contains(key)&&data[key]!=null)
            {
                // The v3 consumer reads uninteractable; its legacy alias is ignored.
                if(key=="_interactable"&&Version.TryParse(item.GetType().GetProperty("version")?.GetValue(item)?.ToString(),out var sourceVersion)&&sourceVersion.Major==3)continue;
                if((key=="track"||key=="_track")&&NoodleStaticProfile.IsEmptyTrackValue(data[key]))
                {
                    string? version=item.GetType().GetProperty("version")?.GetValue(item)?.ToString();
                    if(version!=null&&(version.StartsWith("2.",StringComparison.Ordinal)||version.StartsWith("3.",StringComparison.Ordinal))&&key==NoodleStaticProfile.TrackKey(version.StartsWith("2.",StringComparison.Ordinal)))continue;
                }
                return true;
            }
        return false;
    }
    public static bool HasPlayerMotion(object item)
    {
        if(item.GetType().GetProperty("customEventDatas")?.GetValue(item) is IEnumerable events)
            foreach(var ev in events)
                if(ev?.GetType().GetProperty("eventType")?.GetValue(ev) is string name&&name=="AssignPlayerToTrack")return true;
        if(item.GetType().GetProperty("customData")?.GetValue(item) is not IDictionary data)return false;
        foreach(string key in new[]{"customEvents","_customEvents"})
            if(data.Contains(key)&&data[key]!=null)
                foreach(var ev in JToken.FromObject(data[key]))
                    if((string?)(ev["t"]??ev["_type"])=="AssignPlayerToTrack")return true;
        return false;
    }
    public static bool HasStaticTrackContext(object item)
    {
        if(item.GetType().GetProperty("customEventDatas")?.GetValue(item) is not IEnumerable events)return false;
        int major=0;
        foreach(var ev in events)
        {
            if(ev==null)return false;
            var type=ev.GetType();
            if(!Version.TryParse(type.GetProperty("version")?.GetValue(ev)?.ToString(),out var version)||version.Major!=2&&version.Major!=3)return false;
            if(major!=0&&major!=version.Major)return false;major=version.Major;
            if(type.GetProperty("customData")?.GetValue(ev) is not IDictionary custom)return false;
            var obj=new JObject {[major==2?"_type":"t"]=type.GetProperty("eventType")?.GetValue(ev) as string,
                [major==2?"_time":"b"]=0,[major==2?"_data":"d"]=JToken.FromObject(custom)};
            if(!NoodleVisualEvents.IsVisual(new JArray(obj),major))return false;
        }
        if(item.GetType().GetProperty("customData")?.GetValue(item) is not IDictionary data)return false;
        foreach(string key in new[]{"environment","_environment"})
            if(data.Contains(key)&&!NoodleStaticProfile.IsEmptyTrackValue(data[key]))return false;
        foreach(string key in new[]{"customEvents","_customEvents"})
            if(data.Contains(key)&&!NoodleStaticProfile.IsEmptyTrackValue(data[key])&&
                (major==0||key!=(major==2?"_customEvents":"customEvents")||!NoodleVisualEvents.IsVisual(JToken.FromObject(data[key]!),major)))return false;
        return true;
    }
    public static bool HasNoWallEntryEnvironment(object item)
    {
        if(item.GetType().GetProperty("customData")?.GetValue(item) is not IDictionary data)return false;
        foreach(string key in new[]{"environment","_environment"})
            if(data.Contains(key)&&!NoodleStaticProfile.IsEmptyTrackValue(data[key]))return false;
        return true;
    }
    public static NoodleWallShape StaticWall(object item,int lane,int layer,int width,int height,bool leftHanded=false,bool allowStaticTracks=false,WallJumpMovement? entryMovement=null)
    {
        var type=item.GetType();
        if(type.GetProperty("customData")==null)return new NoodleWallShape(lane,layer,width,height);
        string version=type.GetProperty("version")?.GetValue(item)?.ToString()??throw new NotSupportedException("Custom obstacle version is unavailable");
        int major=Version.Parse(version).Major;
        if(major!=2&&major!=3)throw new NotSupportedException("Custom obstacle version has not been validated");
        bool old=major==2;
        var custom=new JObject();
        if(type.GetProperty("customData")?.GetValue(item) is IDictionary data)
        foreach(DictionaryEntry entry in data)
        {
            string key=(string)entry.Key;
            if(entry.Value==null)continue;
            if(key==(old?"_interactable":"uninteractable"))
            {
                if(entry.Value is not bool interactiveFlag||(old?!interactiveFlag:interactiveFlag))throw new NotSupportedException("Non-interactive walls must be excluded before shape preparation");
                continue;
            }
            if(key==NoodleStaticProfile.TrackKey(old))
            {
                if(!NoodleStaticProfile.IsEmptyTrackValue(entry.Value))
                {
                    if(!allowStaticTracks)throw new NotSupportedException("Named runtime tracks need a confirmed static context");
                    NoodleStaticProfile.ValidateTrackAssignment(JToken.FromObject(entry.Value),true);
                }
                continue;
            }
            if(key=="_animation"||key=="animation")
            {
                if(!NoodleVisualEvents.IsNeutralObjectAnimation(key,JToken.FromObject(entry.Value),major,true))
                {
                    bool entryAccepted=false;
                    if(major==3&&key=="animation"&&entryMovement!=null)
                    {
                        var projected=JObject.FromObject(data);
                        if(NoodleWallEntry.TryProject(projected,(float)(type.GetProperty("time")?.GetValue(item)??float.NaN),
                            (float)(type.GetProperty("duration")?.GetValue(item)??float.NaN),entryMovement))
                            entryAccepted=NoodleVisualEvents.IsNeutralObjectAnimation(key,projected[key]!,major,true);
                    }
                    if(!entryAccepted)throw new NotSupportedException("Runtime wall animation changes geometry or is unvalidated");
                }
                continue;
            }
            if(key==(old?"_dissolve":"dissolve")||key==(old?"_dissolveArrow":"dissolveArrow"))
            {
                if(!NoodleVisualEvents.Points(JToken.FromObject(entry.Value)))throw new NotSupportedException("Invalid direct runtime opacity metadata");
                continue;
            }
            if(key==NoodleMetadataScope.Key(major))
            {
                NoodleMetadataScope.ValidateNested(JToken.FromObject(entry.Value));continue;
            }
            if(NoodleStaticProfile.IsNeutralMetadataName(key))
            {
                if(entry.Value is IEnumerable&&entry.Value is not string)throw new NotSupportedException("Structured runtime neutral metadata is unsupported: "+key);
                NoodleStaticProfile.ValidateNeutralMetadata(key,JToken.FromObject(entry.Value));continue;
            }
            bool accepted=key==(old?"_position":"coordinates")||key==(old?"_scale":"size")||key==(old?"_fake":"NE_fake")||key==(old?"_localRotation":"localRotation")||key==(old?"_rotation":"worldRotation")||key=="color"||key=="_color"||key=="spawnEffect"||key=="_disableSpawnEffect"||key=="disableDebris";
            if(!accepted)throw new NotSupportedException("Unsupported runtime obstacle property: "+key);
            custom[key]=JToken.FromObject(entry.Value);
        }
        // The loaded shape uses v3 fields regardless of the source file version.
        var root=new JObject {["x"]=lane,["y"]=layer,["w"]=width,["h"]=height,["customData"]=new JObject()};
        root["customData"]!["coordinates"]=custom[old?"_position":"coordinates"];
        root["customData"]!["size"]=custom[old?"_scale":"size"];
        root["customData"]!["localRotation"]=custom[old?"_localRotation":"localRotation"];
        root["customData"]!["worldRotation"]=custom[old?"_rotation":"worldRotation"];
        return NoodleWallAnalysis.Read(root,3,true,leftHanded);
    }
    public static bool IsNonInteractiveWall(object item)
    {
        var type=item.GetType();
        string? version=type.GetProperty("version")?.GetValue(item)?.ToString();
        if(version==null||type.GetProperty("customData")?.GetValue(item) is not IDictionary data)return false;
        if(version.StartsWith("2.",StringComparison.Ordinal))return data.Contains("_interactable")&&data["_interactable"] is false;
        return version.StartsWith("3.",StringComparison.Ordinal)&&data.Contains("uninteractable")&&data["uninteractable"] is true;
    }
    public static bool IsFake(object item)
    {
        var type=item.GetType();
        if(type.GetProperty("customData")?.GetValue(item) is not IDictionary data)return false;
        string? version=type.GetProperty("version")?.GetValue(item)?.ToString();
        string name=version!=null?(version.StartsWith("2.",StringComparison.Ordinal)?"_fake":"NE_fake"):
            data.Contains("NE_fake")?"NE_fake":"_fake";
        return data.Contains(name)&&data[name] is true;
    }
}
