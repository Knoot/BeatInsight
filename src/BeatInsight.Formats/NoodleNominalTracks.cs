using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json.Linq;

namespace BeatInsight.Formats;

// All admissible endpoints agree at nominal object time. Their path blends
// therefore remain neutral without guessing event dispatch/frame timing.
internal static class NoodleNominalTracks
{
    static bool Number(JToken? token,float min,float max)
    {
        if(token==null||(token.Type!=JTokenType.Integer&&token.Type!=JTokenType.Float))return false;
        float value=(float)token;return !float.IsNaN(value)&&!float.IsInfinity(value)&&value>=min&&value<=max;
    }
    internal static bool Assigned(JToken? value,HashSet<string> tracks)
        =>value is JArray array?array.Any(t=>t.Type==JTokenType.String&&tracks.Contains((string)t!)):
            value?.Type==JTokenType.String&&tracks.Contains((string)value!);
    sealed class OffsetEvent
    {
        internal float Beat,Duration;internal NoodleVectorEasing.Kind Easing;internal NoodleVectorCurve Curve=null!;
    }
    internal static bool TryRead(JToken value,int major,JObject root,float bpm,bool notes,out HashSet<string> affected)
    {
        affected=new HashSet<string>(StringComparer.Ordinal);
        if(major!=3||value is not JArray events)return false;
        var offsets=new Dictionary<string,List<OffsetEvent>>(StringComparer.Ordinal);
        bool moving=false;
        foreach(var ev in events)
        {
            if(NoodleVisualEvents.IsVisual(new JArray(ev.DeepClone()),major))continue;
            if(ev is not JObject obj||obj["d"] is not JObject data||!Number(obj["b"],0,1000000))return false;
            if(obj.Properties().Any(p=>p.Name!="b"&&p.Name!="t"&&p.Name!="d"))return false;
            string? type=obj["t"]?.Type==JTokenType.String?(string?)obj["t"]:null;
            if(type!="AnimateTrack"&&type!="AssignPathAnimation")return false;
            var target=data["track"];
            if(target==null||NoodleStaticProfile.IsEmptyTrackValue(target))return false;
            try{NoodleStaticProfile.ValidateTrackAssignment(target,true);}catch(NotSupportedException){return false;}
            var names=target is JArray a?a.Select(t=>(string)t!).ToArray():new[]{(string)target!};
            if(names.Distinct(StringComparer.Ordinal).Count()!=names.Length)return false;
            if(data["duration"]!=null&&!Number(data["duration"],0,1000000))return false;
            if(data["easing"]!=null&&(data["easing"]!.Type!=JTokenType.String||!NoodleVectorEasing.TryRead((string)data["easing"]!,out _)))return false;
            bool geometry=false;
            foreach(var property in data.Properties())
            {
                string key=property.Name;
                if(key=="track")continue;
                if(key=="duration"){if(!Number(property.Value,0,1000000))return false;continue;}
                if(key=="easing")
                {if(property.Value.Type!=JTokenType.String||!NoodleVectorEasing.TryRead((string)property.Value!,out _))return false;continue;}
                if(key=="dissolve"||key=="dissolveArrow")
                {if(!NoodleVisualEvents.Points(property.Value))return false;continue;}
                if(key!="offsetPosition"&&(key!="scale"||type!="AssignPathAnimation"))return false;
                if(!NoodleVectorCurve.TryRead(property.Value,out var curve))return false;
                if(key=="offsetPosition")
                {
                    if(type=="AssignPathAnimation")
                    {if(!(curve!.AlwaysZero||curve.RestoredBeforeNote))return false;}
                    else
                    {
                        float duration=data["duration"]==null?0:(float)data["duration"]!;
                        bool active=!curve!.AlwaysZero;
                        var easing=NoodleVectorEasing.Kind.Linear;
                        if(data["easing"]!=null)NoodleVectorEasing.TryRead((string)data["easing"]!,out easing);
                        if(active&&(duration<=0||!curve.At(1).IsZero||easing>=NoodleVectorEasing.Kind.InBounce))return false;
                        moving|=active;
                        foreach(string name in names)
                        {
                            if(!offsets.TryGetValue(name,out var list))offsets.Add(name,list=new List<OffsetEvent>());
                            list.Add(new OffsetEvent {Beat=(float)obj["b"]!,Duration=duration,Easing=easing,Curve=curve});
                        }
                    }
                }
                else
                {
                    var end=curve!.At(.5f);
                    if(!curve.SettledBeforeNote||end.X!=1||end.Y!=1||end.Z!=1)return false;
                }
                geometry=true;
            }
            if(!geometry)return false;
            affected.UnionWith(names);
        }
        if(!moving)return true;
        // First song-time profile: constant BPM, linear event clock, no repeats
        // or overlapping writes. Each bound track must independently be zero.
        if(!Number(new JValue(bpm),.001f,1000000))return false;
        if(root["bpmEvents"] is JToken tempo)
        {
            if(tempo is not JArray changes)return false;
            foreach(var change in changes)
                if(change is not JObject||!Number(change["b"],0,0)||!Number(change["m"],.001f,1000000)||(float)change["m"]! !=bpm)return false;
        }
        foreach(var list in offsets.Values)
        {
            if(list.Any(e=>e.Duration>0&&(!Number(new JValue(60*e.Duration/bpm),.000001f,1000000000))))return false;
            list.Sort((a,b)=>a.Beat.CompareTo(b.Beat));
            for(int i=1;i<list.Count;i++)
                if(list[i].Beat<=list[i-1].Beat+list[i-1].Duration)return false;
        }
        if(!notes)return true;
        foreach(var note in (root["colorNotes"]??new JArray()).Concat(root["bombNotes"]??new JArray()))
        {
            if(note is not JObject obj||obj["customData"] is JToken custom&&custom.Type!=JTokenType.Null&&custom is not JObject)return false;
            var assignment=(obj["customData"] as JObject)?["track"];
            if(assignment==null||IsEmpty(assignment))continue;
            var names=assignment is JArray a?a.Where(t=>t.Type==JTokenType.String).Select(t=>(string)t!):
                assignment.Type==JTokenType.String?new[]{(string)assignment!}:Array.Empty<string>();
            if(!Number(note["b"],0,1000000))return false;
            float time=(float)note["b"]!/bpm*60;
            foreach(string name in names)
            {
                if(!offsets.TryGetValue(name,out var list))continue;
                float beat=(float)note["b"]!;
                int low=0,high=list.Count;
                while(low<high){int mid=low+(high-low)/2;if(list[mid].Beat<=beat)low=mid+1;else high=mid;}
                if(low==0)continue;
                var ev=list[low-1];
                if(ev.Curve.AlwaysZero)continue;
                float start=ev.Beat/bpm*60,duration=60*ev.Duration/bpm;
                float progress=(time-start)/duration;
                float eased=NoodleVectorEasing.Apply(ev.Easing,Math.Min(progress,1));
                // Leave room for independently measured float differences in
                // nonlinear evaluators; this is not a frame-time guarantee.
                if(ev.Easing!=NoodleVectorEasing.Kind.Linear)eased-=.00001f;
                if(!ev.Curve.IsSettledZeroAt(eased))return false;
            }
        }
        return true;
    }
    static bool IsEmpty(JToken token)=>token.Type==JTokenType.Null||token is JArray a&&a.Count==0;
}
