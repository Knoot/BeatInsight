using System;
using Newtonsoft.Json.Linq;

namespace BeatInsight.Formats;

// Track events remain cutout-only. Object curves separately allow zero offsets
// and verified nominal-note entry motion; unknown fields never become visual-only.
internal static class NoodleVisualEvents
{
    static bool Number(JToken? value,double min,double max)
        =>value!=null&&(value.Type==JTokenType.Integer||value.Type==JTokenType.Float)
            &&!double.IsNaN((double)value)&&(double)value>=min&&(double)value<=max;
    static bool Easing(JToken value)=>value.Type==JTokenType.String&&NoodleVectorEasing.TryRead((string)value!,out _);
    internal static bool Points(JToken value)
    {
        if(value is not JArray points||points.Count==0)return false;
        if(points.Count==1&&Number(points[0],0,1))return true;
        double previous=-1;
        foreach(var point in points)
        {
            if(point is not JArray frame||(frame.Count!=2&&frame.Count!=3)||!Number(frame[0],0,1)||!Number(frame[1],0,1))return false;
            if(frame.Count==3&&(frame[2].Type!=JTokenType.String||!NoodleVectorEasing.TryRead((string)frame[2]!,out _)))return false;
            double time=(double)frame[1];if(time<previous)return false;previous=time;
        }
        return true;
    }
    internal static bool IdentityScale(JToken value)
    {
        if(value is not JArray points||points.Count==0)return false;
        bool One(JToken component)=>Number(component,1,1);
        if(points.Count==3&&One(points[0])&&One(points[1])&&One(points[2]))return true;
        double previous=-1;
        foreach(var point in points)
        {
            if(point is not JArray frame||frame.Count!=4||!One(frame[0])||!One(frame[1])||!One(frame[2])||!Number(frame[3],0,1))return false;
            double time=(double)frame[3];if(time<=previous)return false;previous=time;
        }
        return true;
    }
    internal static bool IsIdentityScaleAnimation(JToken value,int syntaxMajor)
        =>value is JObject animation&&animation.Count==1&&animation[syntaxMajor==2?"_scale":"scale"] is JToken scale&&IdentityScale(scale);
    internal static bool IsGeometryNeutralAnimation(JToken value,int major,bool wallIdentityScale=false)
        =>IsSupportedAnimation(value,major,wallIdentityScale,false);
    internal static bool IsNominalNoteEntryAnimation(JToken value,int major)
        =>major==3&&IsSupportedAnimation(value,major,false,true);
    internal static bool IsNominalBombEntryAnimation(JToken value,int major)
        =>major==3&&IsSupportedAnimation(value,major,false,true,false);
    static bool IsSupportedAnimation(JToken value,int major,bool wallIdentityScale,bool noteEntryOffset,bool allowRotation=true)
    {
        if(major!=2&&major!=3||value is not JObject animation)return false;
        foreach(var property in animation.Properties())
        {
            if(property.Name==(major==2?"_position":"offsetPosition"))
            {
                if(!NoodleVectorCurve.TryRead(property.Value,out var curve)||!(curve!.AlwaysZero||noteEntryOffset&&curve.RestoredBeforeNote))return false;
                continue;
            }
            if(allowRotation&&noteEntryOffset&&major==3&&property.Name=="localRotation")
            {
                // Validate Euler frame syntax only. Quaternion interpolation is
                // not vector interpolation; admission relies on the zero endpoint
                // being reached before the nominal note time.
                if(!NoodleVectorCurve.TryRead(property.Value,out var rotation)||!rotation!.RestoredBeforeNote)return false;
                continue;
            }
            // A wall's local collision bounds are symmetric on X, so the helper's
            // handedness reflection preserves coverage for a unit scale curve.
            if(wallIdentityScale&&property.Name==(major==2?"_scale":"scale"))
            {if(!IdentityScale(property.Value))return false;continue;}
            if(property.Name!=(major==2?"_dissolve":"dissolve")&&property.Name!=(major==2?"_dissolveArrow":"dissolveArrow"))return false;
            if(!Points(property.Value))return false;
        }
        return true;
    }
    internal static bool IsNeutralObjectAnimation(string key,JToken value,int major,bool wallIdentityScale=false)
        =>key==(major==2?"_animation":"animation")?IsGeometryNeutralAnimation(value,major,wallIdentityScale):
            key==(major==2?"animation":"_animation")&&IsIdentityScaleAnimation(value,major==2?3:2);
    internal static bool IsVisual(JToken value,int major)
    {
        if(major!=2&&major!=3||value is not JArray events)return false;
        bool old=major==2;
        foreach(var ev in events)
        {
            if(ev is not JObject obj)return false;
            string type=old?"_type":"t",data=old?"_data":"d",time=old?"_time":"b";
            if(obj[type]?.Type!=JTokenType.String||((string?)obj[type]!="AnimateTrack"&&(major!=3||(string?)obj[type]!="AssignPathAnimation"))||!Number(obj[time],0,double.MaxValue)||obj[data] is not JObject fields)return false;
            foreach(var property in obj.Properties())if(property.Name!=type&&property.Name!=data&&property.Name!=time)return false;
            string track=old?"_track":"track",duration=old?"_duration":"duration",easing=old?"_easing":"easing";
            var target=fields[track];
            if(target==null||NoodleStaticProfile.IsEmptyTrackValue(target))return false;
            try{NoodleStaticProfile.ValidateTrackAssignment(target,true);}catch(NotSupportedException){return false;}
            bool opacity=false;
            foreach(var property in fields.Properties())
            {
                if(property.Name==track)continue;
                if(property.Name==duration){if(!Number(property.Value,0,1000000))return false;continue;}
                if(property.Name==easing){if(!Easing(property.Value))return false;continue;}
                if(property.Name==(old?"_dissolve":"dissolve")||property.Name==(old?"_dissolveArrow":"dissolveArrow"))
                {if(!Points(property.Value))return false;opacity=true;continue;}
                return false;
            }
            if(!opacity)return false;
        }
        return true;
    }
}

