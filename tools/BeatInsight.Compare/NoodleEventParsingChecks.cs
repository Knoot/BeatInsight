using System.Collections;
using System.Reflection;
using System.Runtime.Loader;
using BeatInsight.Formats;
using Mono.Cecil;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace BeatInsight.Compare;

static class NoodleEventParsingChecks
{
    public static object Run(string gameRoot)
    {
        foreach(var pin in new[]{("Heck","9E5163F09E122713543FB69F89799D1121FB712820E6B4350F7A30C5D790793C"),
            ("NoodleExtensions","FE50124E33631EED7ECB78EA1A19BB7EA64CC12974B0298DAD991672C2ABDA95"),
            ("CustomJSONData","4F9F8EFD8012159D8995B6D936E3C21B6AA6A35E319686FA3E9B04C2B6331F7C")})
            if(Comparison.HashFile(Path.Combine(gameRoot,"Plugins",pin.Item1+".dll"))!=pin.Item2)throw new InvalidDataException("Reinspect changed event parser: "+pin.Item1);
        const BindingFlags instance=BindingFlags.Public|BindingFlags.NonPublic|BindingFlags.Instance;
        const BindingFlags statics=BindingFlags.Public|BindingFlags.NonPublic|BindingFlags.Static;
        var heck=AssemblyLoadContext.Default.LoadFromAssemblyPath(Path.Combine(gameRoot,"Plugins/Heck.dll"));
        var custom=AssemblyLoadContext.Default.LoadFromAssemblyPath(Path.Combine(gameRoot,"Plugins/CustomJSONData.dll"));
        var trackType=heck.GetType("Heck.Animation.Track",true)!;
        var metadataType=custom.GetType("CustomJSONData.CustomBeatmap.CustomData",true)!;
        var eventType=custom.GetType("CustomJSONData.CustomBeatmap.CustomEventData",true)!;
        var parsedType=heck.GetType("Heck.HeckCoroutineEventData",true)!;
        var ctor=parsedType.GetConstructors(instance).Single();var cp=ctor.GetParameters();
        var registrations=new List<object>();
        foreach(string assembly in new[]{"Heck","NoodleExtensions"})
        {
            using var module=ModuleDefinition.ReadModule(Path.Combine(gameRoot,"Plugins",assembly+".dll"));
            foreach(var method in module.Types.Single(t=>t.FullName==assembly+".Plugin").Methods.Where(m=>m.HasBody))
            foreach(var i in method.Body.Instructions)
            {
                if(i.Operand is not GenericInstanceMethod call||call.DeclaringType.FullName!="Heck.Animation.Track"||call.Name is not ("RegisterProperty" or "RegisterPathProperty"))continue;
                if(i.Previous.Operand is not string alias||i.Previous.Previous.Operand is not string name)throw new InvalidDataException("Registration literals changed");
                Type valueType=call.GenericArguments.Single().FullName switch {"UnityEngine.Vector3"=>typeof(Vector3),"UnityEngine.Quaternion"=>typeof(Quaternion),"System.Single"=>typeof(float),_=>throw new InvalidDataException("Unvalidated property type")};
                trackType.GetMethods(statics).Single(m=>m.Name==call.Name&&m.IsGenericMethodDefinition).MakeGenericMethod(valueType).Invoke(null,new object[]{name,alias});
                registrations.Add(new{assembly,kind=call.Name,name,alias,type=valueType.Name});
            }
        }
        object Convert(JToken t)=>t is JArray a?a.Select(Convert).ToList():t.Type==JTokenType.String?(object)t.Value<string>()!:t.Type==JTokenType.Null?null!:(float)t;
        object Metadata(JObject obj){var result=Activator.CreateInstance(metadataType)!;foreach(var p in obj.Properties())((IDictionary)result)[p.Name]=Convert(p.Value);return result;}
        var rows=new List<object>();int cases=0,failed=0;
        foreach(int major in new[]{2,3})foreach(string kind in new[]{"AnimateTrack","AssignPathAnimation"})
        foreach(string property in kind=="AnimateTrack"?new[]{"offsetPosition","dissolve"}:new[]{"offsetPosition","scale","dissolve"})
        foreach(bool named in new[]{false,true})foreach(string target in new[]{"single","multiple","duplicate","case-sensitive"})
        {
            bool old=major==2;string key=old?property=="offsetPosition"?"_position":"_"+property:property;
            var curve=property=="dissolve"?JArray.Parse("[[0.25,0],[1,1]]"):JArray.Parse("[[1,2,3,0],[3,4,5,1]]");
            JToken assignment=target=="single"?new JValue("fixture"):target=="case-sensitive"?new JValue("Fixture"):
                target=="duplicate"?new JArray("fixture","fixture"):new JArray("fixture","other");
            var fields=new JObject {[old?"_track":"track"]=assignment,[old?"_duration":"duration"]=2.5,[old?"_easing":"easing"]="easeOutCubic",[key]=named?new JValue("curve"):curve.DeepClone()};
            if(!old)fields["repeat"]=2;
            var tracks=(IDictionary)Activator.CreateInstance(cp[2].ParameterType)!;
            foreach(string name in new[]{"fixture","other","Fixture"})tracks[name]=Activator.CreateInstance(trackType,true)!;
            var definitions=(IDictionary)Activator.CreateInstance(cp[1].ParameterType)!;definitions["curve"]=Convert(curve);definitions["Curve"]=Convert(new JArray(9,9,9));
            var metadata=Metadata(fields);string before=JToken.FromObject(metadata).ToString();
            var ev=Activator.CreateInstance(eventType,new object[]{1f,kind,metadata,new Version(major,0)})!;
            var parsed=ctor.Invoke(new object[]{ev,definitions,tracks,old});
            var infos=((IEnumerable)parsedType.GetProperty("CoroutineInfos",instance)!.GetValue(parsed)!).Cast<object>().ToArray();
            int targets=target is "multiple" or "duplicate"?2:1;
            int fanout=old&&kind=="AnimateTrack"&&property=="offsetPosition"?2:1;
            bool pass=infos.Length==targets*fanout&&(float)parsedType.GetProperty("Duration",instance)!.GetValue(parsed)! == 2.5f
                &&(int)parsedType.GetProperty("Repeat",instance)!.GetValue(parsed)! == (old?0:2)
                &&parsedType.GetProperty("Easing",instance)!.GetValue(parsed)!.ToString()=="easeOutCubic"&&before==JToken.FromObject(metadata).ToString();
            foreach(var info in infos)
            {
                var t=info.GetType();var point=t.GetProperty("PointDefinition",instance)!.GetValue(info)!;
                var track=t.GetProperty("Track",instance)!.GetValue(info)!;
                string[] expectedTracks=target=="single"?new[]{"fixture"}:target=="case-sensitive"?new[]{"Fixture"}:target=="duplicate"?new[]{"fixture","fixture"}:new[]{"fixture","other"};
                pass&=expectedTracks.Any(n=>ReferenceEquals(track,tracks[n]));
                var sampled=point.GetType().GetMethods(instance).Single(m=>m.Name=="Interpolate"&&m.GetParameters().Length==1).Invoke(point,new object[]{.5f});
                pass&=property=="dissolve"?sampled is float f&&Math.Abs(f-.625f)<1e-5:sampled is Vector3 v&&v.x==2&&v.y==3&&v.z==4;
            }
            if(target=="duplicate"||target=="multiple")
            {
                object Property(object info)=>info.GetType().GetProperty("Property",instance)!.GetValue(info)!;
                pass&=ReferenceEquals(Property(infos[0]),Property(infos[fanout]))==(target=="duplicate");
            }
            cases++;if(!pass)failed++;rows.Add(new{major,kind,property,named,target,fanout,infoCount=infos.Length,pass});
        }
        foreach(int major in new[]{2,3})foreach(string variant in new[]{"invalid-point-type","clear","defaults"})
        {
            bool old=major==2;string key=old?"_position":"offsetPosition";
            var fields=new JObject {[old?"_track":"track"]="fixture",[key]=variant=="clear"?JValue.CreateNull():variant=="defaults"?JArray.Parse("[1,2,3]"):new JValue(17)};
            var tracks=(IDictionary)Activator.CreateInstance(cp[2].ParameterType)!;tracks["fixture"]=Activator.CreateInstance(trackType,true)!;
            var definitions=(IDictionary)Activator.CreateInstance(cp[1].ParameterType)!;definitions["curve"]=new List<object>{1f,2f,3f};
            var ev=Activator.CreateInstance(eventType,new object[]{1f,"AnimateTrack",Metadata(fields),new Version(major,0)})!;
            bool rejected=false,pass=false;string? exceptionType=null;
            try
            {
                var parsed=ctor.Invoke(new object[]{ev,definitions,tracks,old});
                var infos=((IEnumerable)parsedType.GetProperty("CoroutineInfos",instance)!.GetValue(parsed)!).Cast<object>().ToArray();
                pass=variant=="clear"?infos.Length>0&&infos.All(i=>i.GetType().GetProperty("PointDefinition",instance)!.GetValue(i)==null):variant=="defaults"
                    &&(float)parsedType.GetProperty("Duration",instance)!.GetValue(parsed)! == 0
                    &&(int)parsedType.GetProperty("Repeat",instance)!.GetValue(parsed)! == 0
                    &&parsedType.GetProperty("Easing",instance)!.GetValue(parsed)!.ToString()=="easeLinear";
            }
            catch(TargetInvocationException e){rejected=true;exceptionType=e.InnerException?.GetType().Name;pass=variant=="invalid-point-type"&&e.InnerException is InvalidOperationException;}
            cases++;if(!pass)failed++;rows.Add(new{major,variant,rejected,exceptionType,pass});
        }
        var extensions=heck.GetType("Heck.Animation.AnimationExtensions",true)!;
        var resolve=extensions.GetMethods(statics).Single(m=>m.Name=="GetTrackArray"&&m.GetParameters()[2].ParameterType==typeof(bool));
        foreach(int major in new[]{2,3})foreach(string variant in new[]{"missing","null","unknown","wrong-case","wrong-alias","empty","invalid-list"})
        {
            bool old=major==2;var fields=new JObject();string key=old?"_track":"track";
            if(variant=="null")fields[key]=null;
            if(variant=="unknown")fields[key]="absent";
            if(variant=="wrong-case")fields[key]="FIXTURE";
            if(variant=="wrong-alias")fields[old?"track":"_track"]="fixture";
            if(variant=="empty")fields[key]=new JArray();
            if(variant=="invalid-list")fields[key]=new JArray("fixture",3);
            var tracks=(IDictionary)Activator.CreateInstance(cp[2].ParameterType)!;tracks["fixture"]=Activator.CreateInstance(trackType,true)!;
            bool rejected=false;int count=-1;string? resolutionException=null;
            try{count=((IEnumerable)resolve.Invoke(null,new[]{Metadata(fields),tracks,(object)old})!).Cast<object>().Count();}
            catch(TargetInvocationException e){rejected=true;resolutionException=e.InnerException?.GetType().Name;}
            string expectedException=variant=="unknown"||variant=="wrong-case"?"InvalidOperationException":variant=="invalid-list"?"InvalidCastException":"JsonNotDefinedException";
            bool pass=variant=="empty"?!rejected&&count==0:rejected&&resolutionException==expectedException;
            cases++;if(!pass)failed++;rows.Add(new{major,variant,rejected,resolutionException,count,pass});
        }
        using var provider=ModuleDefinition.ReadModule(Path.Combine(gameRoot,"Plugins/Heck.dll"));
        var pointLookup=provider.Types.Single(t=>t.FullName=="Heck.Animation.AnimationExtensions").Methods.Single(m=>m.Name=="GetPointData");
        var loggerCall=pointLookup.Body.Instructions.Single(i=>i.Operand is MethodReference m&&m.DeclaringType.FullName=="IPA.Logging.Logger"&&m.Name=="Error");
        if(loggerCall.Next.OpCode.Code!=Mono.Cecil.Cil.Code.Ldnull||loggerCall.Next.Next.OpCode.Code!=Mono.Cecil.Cil.Code.Ret)
            throw new InvalidDataException("Missing point definition fallback changed");
        return new{schemaVersion=1,cases,failed,registrations,missingPointDefinition="pinned IL: log error then return null; logger branch not executed",rows,gameStarted=false,referenceUsed=false,
            scope="actual event constructor, canonical field conversion, inline/named point parsing and track lookup; native property registrations replayed from pinned plugin IL without plugin startup; duration/easing/repeat, alias fanout, duplicate/case-sensitive targets; full map custom-event loading, Chroma registrations and Unity scheduling not executed; diagnostic only"};
    }
}
