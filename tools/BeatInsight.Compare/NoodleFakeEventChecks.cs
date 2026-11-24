using System.Collections;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.Loader;
using Mono.Cecil;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace BeatInsight.Compare;

static class NoodleFakeEventChecks
{
    public static object Run(string gameRoot,string? inventory=null)
    {
        const BindingFlags flags=BindingFlags.Public|BindingFlags.NonPublic|BindingFlags.Instance;
        const BindingFlags statics=BindingFlags.Public|BindingFlags.NonPublic|BindingFlags.Static;
        GameLoader.Load(gameRoot,"""{"version":"3.3.0","colorNotes":[]}""",120,noodleExtensions:true);
        foreach(var pin in new[]{("Heck","9E5163F09E122713543FB69F89799D1121FB712820E6B4350F7A30C5D790793C"),
            ("Chroma","7B0B532AED6C841C631686F44C739AACFFCAEE05F2E9A9C3AF516C2C4920BC37")})
            if(Comparison.HashFile(Path.Combine(gameRoot,"Plugins",pin.Item1+".dll"))!=pin.Item2)throw new InvalidDataException("Reinspect changed fake event provider");
        var heck=AssemblyLoadContext.Default.LoadFromAssemblyPath(Path.Combine(gameRoot,"Plugins/Heck.dll"));
        var custom=AssemblyLoadContext.Default.Assemblies.Single(a=>a.GetName().Name=="CustomJSONData");
        var trackType=heck.GetType("Heck.Animation.Track",true)!;
        // Replay literal property registrations without starting plugins or Unity.
        var registrations=new List<object>();
        foreach(string library in new[]{"Heck","NoodleExtensions","Chroma"})
        {
            using var module=ModuleDefinition.ReadModule(Path.Combine(gameRoot,"Plugins",library+".dll"));
            foreach(var method in module.Types.Single(t=>t.FullName==library+".Plugin").Methods.Where(m=>m.HasBody))
            foreach(var instruction in method.Body.Instructions)
            {
                if(instruction.Operand is not GenericInstanceMethod call||call.DeclaringType.FullName!="Heck.Animation.Track"||call.Name is not ("RegisterProperty" or "RegisterPathProperty"))continue;
                if(instruction.Previous.Operand is not string alias||instruction.Previous.Previous.Operand is not string name)throw new InvalidDataException("Property registration literals changed");
                Type type=call.GenericArguments.Single().FullName switch {"System.Single"=>typeof(float),"UnityEngine.Vector3"=>typeof(Vector3),"UnityEngine.Vector4"=>typeof(Vector4),"UnityEngine.Quaternion"=>typeof(Quaternion),_=>throw new InvalidDataException("Unvalidated registration type")};
                trackType.GetMethods(statics).Single(m=>m.Name==call.Name&&m.IsGenericMethodDefinition).MakeGenericMethod(type).Invoke(null,new object[]{name,alias});
                registrations.Add(new{library,kind=call.Name,name,alias});
            }
        }
        var parsedType=heck.GetType("Heck.HeckCoroutineEventData",true)!;
        var ctor=parsedType.GetConstructors(flags).Single();var cp=ctor.GetParameters();
        var eventType=custom.GetType("CustomJSONData.CustomBeatmap.CustomEventData",true)!;
        var ownerType=heck.GetType("Heck.Animation.Events.CoroutineEvent",true)!;
        var owner=RuntimeHelpers.GetUninitializedObject(ownerType);
        var main=AssemblyLoadContext.Default.LoadFromAssemblyPath(Path.Combine(gameRoot,"Beat Saber_Data/Managed/Main.dll"));
        if(Comparison.HashFile(Path.Combine(gameRoot,"Beat Saber_Data/Managed/Main.dll"))!="A276C8A50ACFAD69A3DD3A82CDE08571C3262344702B9B014C89756771B4495E")throw new InvalidDataException("Reinspect changed callback provider");
        var callbacksType=main.GetType("BeatmapCallbacksController",true)!;
        var callbacks=RuntimeHelpers.GetUninitializedObject(callbacksType);
        callbacksType.GetField("_songTime",flags)!.SetValue(callbacks,0f);
        ownerType.GetField("_beatmapCallbacksController",flags)!.SetValue(owner,callbacks);
        var bpm=DispatchProxy.Create(ownerType.GetField("_bpmController",flags)!.FieldType,typeof(ControlledBpmProxy));((ControlledBpmProxy)bpm).Bpm=120;
        ownerType.GetField("_bpmController",flags)!.SetValue(owner,bpm);
        var contextCtor=heck.GetType("Heck.Deserialize.DeserializedData",true)!.GetConstructors(flags).Single();
        var dictionaries=contextCtor.GetParameters().Select(p=>Activator.CreateInstance(p.ParameterType)!).ToArray();
        ownerType.GetField("_deserializedData",flags)!.SetValue(owner,contextCtor.Invoke(dictionaries));
        var dispatch=ownerType.GetMethod("StartEventCoroutine",flags)!;
        var helperType=AssemblyLoadContext.Default.Assemblies.Single(a=>a.GetName().Name=="NoodleExtensions").GetType("NoodleExtensions.Animation.AnimationHelper",true)!;
        var helper=Activator.CreateInstance(helperType,flags,null,new object[]{false},null)!;
        var offset=helperType.GetMethod("GetObjectOffset",flags)!;
        object Parse(object saved,IDictionary definitions,IDictionary tracks,float time,out object parsed)
        {
            var type=saved.GetType();string kind=(string)type.GetProperty("type")!.GetValue(saved)!;
            var metadata=type.GetProperty("customData")!.GetValue(saved)!;
            var ev=Activator.CreateInstance(eventType,new object[]{time,kind,metadata,new Version(3,3,0)})!;
            parsed=ctor.Invoke(new object[]{ev,definitions,tracks,false});return ev;
        }
        IEnumerable<object> Events(object saved)=>(IEnumerable<object>)saved.GetType().GetProperty("customEvents")!.GetValue(saved)!;
        int cases=0,failed=0;var results=new List<object>();
        foreach(bool named in new[]{false,true})foreach(bool duplicate in new[]{false,true})foreach(float? value in new float?[]{null,0,.5f,1,2})
        {
            var tracks=(IDictionary)Activator.CreateInstance(cp[2].ParameterType)!;
            var target=Activator.CreateInstance(trackType,true)!;var unrelated=Activator.CreateInstance(trackType,true)!;
            tracks["decorative"]=target;tracks["real"]=unrelated;
            var definitions=(IDictionary)Activator.CreateInstance(cp[1].ParameterType)!;
            var bound=Array.CreateInstance(trackType,1);bound.SetValue(target,0);
            var other=Array.CreateInstance(trackType,1);other.SetValue(unrelated,0);
            float? Sample(object list){object?[] args=new object?[offset.GetParameters().Length];args[1]=list;args[2]=.5f;offset.Invoke(helper,args);return(float?)args[9];}
            bool pass=true;
            // Seed, replace, clear or restore the same registered property through native parsing + dispatch.
            foreach(float? step in new float?[]{1,value,0,1})
            {
                var data=new JObject {["track"]=duplicate?new JArray("decorative","decorative"):new JValue("decorative"),["duration"]=0,
                    ["interactable"]=step.HasValue?(named?new JValue("curve"):new JArray(step.Value)):JValue.CreateNull()};
                var root=new JObject {["version"]="3.3.0",["colorNotes"]=new JArray(),["customData"]=new JObject {
                    ["pointDefinitions"]=new JObject {["curve"]=new JArray(step??0)},
                    ["customEvents"]=new JArray(new JObject {["b"]=0,["t"]="AnimateTrack",["d"]=data})}};
                var saved=NoodleNativeData.Deserialize(gameRoot,root,"3");
                var rootMetadata=(IDictionary)saved.GetType().GetProperty("customData")!.GetValue(saved)!;
                foreach(DictionaryEntry point in (IDictionary)rootMetadata["pointDefinitions"]!)definitions[point.Key]=point.Value;
                var ev=Parse(Events(saved).Single(),definitions,tracks,0,out var parsed);
                ((IDictionary)dictionaries[0])[ev]=parsed;
                dispatch.Invoke(owner,new[]{ev,Enum.ToObject(dispatch.GetParameters()[1].ParameterType,0)});
                pass&=Sample(bound)==step&&Sample(other)==null;
                ((IDictionary)dictionaries[0]).Remove(ev);
            }
            cases++;if(!pass)failed++;results.Add(new{named,duplicate,value,pass});
        }
        var controlledDispatch=ControlledCoroutineScheduler.CompileDispatch(dispatch);
        int dispatchedCases=0,dispatchStarts=0,dispatchStops=0;
        var animate=ownerType.GetMethod("AnimateTrackCoroutine",flags)!;
        var songTime=callbacksType.GetField("_songTime",flags)!;
        int timedCases=0,timedSamples=0,replacements=0;
        var timedFailures=new List<object>();
        foreach(float currentBpm in new[]{90f,120f,180f})foreach(float beats in new[]{.5f,2f})
        foreach(int repeat in new[]{0,2})foreach(bool named in new[]{false,true})
        foreach(bool delayed in new[]{false,true})foreach(string easing in new[]{"easeLinear","easeInQuad"})
        {
            var tracks=(IDictionary)Activator.CreateInstance(cp[2].ParameterType)!;
            var target=Activator.CreateInstance(trackType,true)!;var unrelated=Activator.CreateInstance(trackType,true)!;
            tracks["decorative"]=target;tracks["real"]=unrelated;
            var bound=Array.CreateInstance(trackType,1);bound.SetValue(target,0);
            var other=Array.CreateInstance(trackType,1);other.SetValue(unrelated,0);
            float? Sample(object list){object?[] args=new object?[offset.GetParameters().Length];args[1]=list;args[2]=.5f;offset.Invoke(helper,args);return(float?)args[9];}
            object? timedEvent=null;
            object Info(bool reverse,out object parsed,bool clear=false)
            {
                var curve=new JArray(new JArray(reverse?2:0,0),new JArray(reverse?0:2,1));
                var root=new JObject {["version"]="3.3.0",["colorNotes"]=new JArray(),["customData"]=new JObject {
                    ["pointDefinitions"]=new JObject {["curve"]=curve.DeepClone()},
                    ["customEvents"]=new JArray(new JObject {["b"]=0,["t"]="AnimateTrack",["d"]=new JObject {
                        ["track"]="decorative",["duration"]=beats,["repeat"]=reverse?0:repeat,["easing"]=easing,
                        ["interactable"]=clear?JValue.CreateNull():named?new JValue("curve"):curve}})}};
                var saved=NoodleNativeData.Deserialize(gameRoot,root,"3");
                var metadata=(IDictionary)saved.GetType().GetProperty("customData")!.GetValue(saved)!;
                var definitions=(IDictionary)Activator.CreateInstance(cp[1].ParameterType)!;
                foreach(DictionaryEntry entry in (IDictionary)metadata["pointDefinitions"]!)definitions[entry.Key]=entry.Value;
                timedEvent=Parse(Events(saved).Single(),definitions,tracks,0,out parsed);
                return((IEnumerable)parsedType.GetProperty("CoroutineInfos",flags)!.GetValue(parsed)!).Cast<object>().Single();
            }
            ((ControlledBpmProxy)bpm).Bpm=currentBpm;
            float start=3.25f,duration=60*beats/currentBpm;
            IEnumerator Iterator(object info,object parsed,float begin)
            {
                object Get(string name)=>info.GetType().GetProperty(name,flags)!.GetValue(info)!;
                return(IEnumerator)animate.Invoke(owner,new[]{Get("PointDefinition"),Get("Property"),Get("Track"),(object)duration,begin,
                    parsedType.GetProperty("Easing",flags)!.GetValue(parsed)!,parsedType.GetProperty("Repeat",flags)!.GetValue(parsed)!,false})!;
            }
            var info=Info(false,out var parsed);var iterator=Iterator(info,parsed,start);bool pass=true;
            foreach(float fraction in new[]{0f,.25f,.5f,.75f,.99f,1.01f,1.5f,2.75f,3.1f})
            {
                if(delayed&&fraction<.75f)continue;
                float time=start+fraction*duration;songTime.SetValue(callbacks,time);bool running=iterator.MoveNext();
                float elapsed=(time-start)/duration;
                float progress=elapsed>=repeat+1?1:elapsed-MathF.Floor(elapsed);
                float expected=2*(easing=="easeLinear"?progress:progress*progress);
                float? actual=Sample(bound);
                bool samplePass=actual.HasValue&&Math.Abs(actual.Value-expected)<.00002f&&Sample(other)==null&&running==(elapsed<repeat+1);
                timedSamples++;pass&=samplePass;
                if(!samplePass&&timedFailures.Count<12)timedFailures.Add(new{currentBpm,beats,repeat,named,delayed,easing,fraction,actual,expected,running});
            }
            // The host explicitly disposes the old iterator. Unity StopCoroutine is not executed.
            foreach(float replaceAt in new[]{.25f,.75f})
            {
                var old=Iterator(info,parsed,start);songTime.SetValue(callbacks,start+replaceAt*duration);old.MoveNext();((IDisposable)old).Dispose();
                var replacementInfo=Info(true,out var replacementParsed);
                pass&=ReferenceEquals(info.GetType().GetProperty("Property",flags)!.GetValue(info),replacementInfo.GetType().GetProperty("Property",flags)!.GetValue(replacementInfo));
                float begin=start+replaceAt*duration;var replacement=Iterator(replacementInfo,replacementParsed,begin);
                foreach(float fraction in new[]{0f,.25f,.5f,.75f,1.25f})
                {
                    float time=begin+fraction*duration;songTime.SetValue(callbacks,time);replacement.MoveNext();
                    float progress=Math.Min((time-begin)/duration,1);
                    float expected=2*(1-(easing=="easeLinear"?progress:progress*progress));
                    float? actual=Sample(bound);pass&=actual.HasValue&&Math.Abs(actual.Value-expected)<.00002f&&Sample(other)==null;
                    pass&=!old.MoveNext()&&Sample(bound)==actual;timedSamples++;
                }
                ((IDisposable)replacement).Dispose();replacements++;
            }
            using(var scheduler=new ControlledCoroutineScheduler())
            {
                var previousScheduler=ControlledCoroutineScheduler.Current;ControlledCoroutineScheduler.Current=scheduler;
                try
                {
                    void Dispatch(bool reverse,bool clear=false)
                    {
                        Info(reverse,out var parsedEvent,clear);((IDictionary)dictionaries[0])[timedEvent!]=parsedEvent;
                        try{controlledDispatch.Invoke(null,new[]{owner,timedEvent,Enum.ToObject(dispatch.GetParameters()[1].ParameterType,0)});}
                        finally{((IDictionary)dictionaries[0]).Remove(timedEvent!);}
                    }
                    songTime.SetValue(callbacks,0f);Dispatch(false);
                    pass&=scheduler.Count==1;
                    if(delayed)songTime.SetValue(callbacks,.25f*duration);
                    scheduler.Tick();float firstProgress=delayed?(easing=="easeLinear"?.25f:.0625f):0;
                    pass&=Math.Abs((Sample(bound)??-100)-2*firstProgress)<.00002f&&Sample(other)==null;
                    songTime.SetValue(callbacks,.5f*duration);scheduler.Tick();
                    float progress=easing=="easeLinear"?.5f:.25f;
                    pass&=Math.Abs((Sample(bound)??-100)-2*progress)<.00002f;
                    Dispatch(true);pass&=scheduler.Starts==2&&scheduler.Stops==1&&scheduler.Count==1;
                    scheduler.Tick();pass&=Math.Abs((Sample(bound)??-100)-2*(1-progress))<.00002f;
                    Dispatch(false,true);pass&=scheduler.Count==0&&scheduler.Stops==2&&Sample(bound)==null;
                    songTime.SetValue(callbacks,duration*(repeat+1)+.1f);Dispatch(false);
                    pass&=scheduler.Count==0&&scheduler.Starts==2&&Sample(bound)==2&&Sample(other)==null;
                    dispatchStarts+=scheduler.Starts;dispatchStops+=scheduler.Stops;dispatchedCases++;
                }
                finally{ControlledCoroutineScheduler.Current=previousScheduler;}
            }
            timedCases++;cases++;if(!pass)failed++;
        }
        int dispatchedPathCases=0,pathSamples=0;
        foreach(float currentBpm in new[]{90f,120f,180f})foreach(float beats in new[]{.5f,2f})foreach(bool named in new[]{false,true})
        {
            var tracks=(IDictionary)Activator.CreateInstance(cp[2].ParameterType)!;
            var target=Activator.CreateInstance(trackType,true)!;tracks["decorative"]=target;
            var definitions=(IDictionary)Activator.CreateInstance(cp[1].ParameterType)!;
            var bound=Array.CreateInstance(trackType,1);bound.SetValue(target,0);
            float? SamplePath(float local){object?[] args=new object?[offset.GetParameters().Length];args[1]=bound;args[2]=local;offset.Invoke(helper,args);return(float?)args[7];}
            using var scheduler=new ControlledCoroutineScheduler();var previousScheduler=ControlledCoroutineScheduler.Current;ControlledCoroutineScheduler.Current=scheduler;
            bool pass=true;
            try
            {
                void DispatchPath(JToken points,float duration)
                {
                    var root=new JObject {["version"]="3.3.0",["colorNotes"]=new JArray(),["customData"]=new JObject {
                        ["pointDefinitions"]=new JObject {["curve"]=points.DeepClone()},
                        ["customEvents"]=new JArray(new JObject {["b"]=0,["t"]="AssignPathAnimation",["d"]=new JObject {
                            ["track"]="decorative",["duration"]=duration,["dissolve"]=points.Type==JTokenType.Null?JValue.CreateNull():named?new JValue("curve"):points.DeepClone()}})}};
                    var saved=NoodleNativeData.Deserialize(gameRoot,root,"3");
                    var metadata=(IDictionary)saved.GetType().GetProperty("customData")!.GetValue(saved)!;
                    definitions.Clear();foreach(DictionaryEntry point in (IDictionary)metadata["pointDefinitions"]!)definitions[point.Key]=point.Value;
                    var ev=Parse(Events(saved).Single(),definitions,tracks,0,out var parsed);
                    ((IDictionary)dictionaries[0])[ev]=parsed;
                    try{controlledDispatch.Invoke(null,new[]{owner,ev,Enum.ToObject(dispatch.GetParameters()[1].ParameterType,1)});}
                    finally{((IDictionary)dictionaries[0]).Remove(ev);}
                }
                ((ControlledBpmProxy)bpm).Bpm=currentBpm;songTime.SetValue(callbacks,0f);
                DispatchPath(new JArray(1),0);pass&=SamplePath(.25f)==1;
                DispatchPath(new JArray(new JArray(0,0),new JArray(1,1)),beats);pass&=scheduler.Count==1;
                float seconds=beats*60/currentBpm;
                foreach(float progress in new[]{0f,.25f,.75f,1.1f})
                {
                    songTime.SetValue(callbacks,progress*seconds);scheduler.Tick();
                    foreach(float local in new[]{0f,.25f,.75f,1f})
                    {
                        float expected=1+(local-1)*Math.Min(progress,1);
                        pass&=Math.Abs((SamplePath(local)??-100)-expected)<.00002f;pathSamples++;
                    }
                }
                DispatchPath(JValue.CreateNull(),0);pass&=SamplePath(.25f)==null&&scheduler.Count==0&&scheduler.Starts==1;
            }
            finally{ControlledCoroutineScheduler.Current=previousScheduler;}
            dispatchedPathCases++;cases++;if(!pass)failed++;
        }
        var scaleTrackResults=new List<object>();int scaleTrackSamples=0;
        foreach(bool mirror in new[]{false,true})
        {
            var tracks=(IDictionary)Activator.CreateInstance(cp[2].ParameterType)!;var target=Activator.CreateInstance(trackType,true)!;tracks["size"]=target;
            var bound=Array.CreateInstance(trackType,1);bound.SetValue(target,0);
            var definitions=(IDictionary)Activator.CreateInstance(cp[1].ParameterType)!;
            var scaleHelper=Activator.CreateInstance(helperType,flags,null,new object[]{mirror},null)!;
            var root=JObject.Parse("""{"version":"3.3.0","colorNotes":[],"customData":{"customEvents":[{"b":474,"t":"AnimateTrack","d":{"track":"size","scale":[[1,1,1,0],[0.5,0.5,0.5,1]],"duration":1}},{"b":477,"t":"AnimateTrack","d":{"track":"size","scale":[[0.5,0.5,0.5,0],[1,1,1,1]],"duration":0.5}}]}}""");
            var saved=NoodleNativeData.Deserialize(gameRoot,root,"3");
            var scheduler=new ControlledCoroutineScheduler();var previousScheduler=ControlledCoroutineScheduler.Current;ControlledCoroutineScheduler.Current=scheduler;
            bool pass=true;var samples=new List<object>();
            try
            {
                ((ControlledBpmProxy)bpm).Bpm=174;int eventIndex=0;
                foreach(float beat in new[]{474f,474.5f,475f,476.5f,477f,477.25f,477.5f,478f,479f,479f})
                {
                    songTime.SetValue(callbacks,beat*60/174);
                    if(beat==474||beat==477)
                    {
                        var source=Events(saved).ElementAt(eventIndex);
                        var ev=Parse(source,definitions,tracks,beat*60/174,out var parsed);eventIndex++;
                        ((IDictionary)dictionaries[0])[ev]=parsed;
                        try{controlledDispatch.Invoke(null,new[]{owner,ev,Enum.ToObject(dispatch.GetParameters()[1].ParameterType,0)});}
                        finally{((IDictionary)dictionaries[0]).Remove(ev);}
                    }
                    scheduler.Tick();object?[] args=new object?[offset.GetParameters().Length];args[1]=bound;args[2]=.5f;offset.Invoke(scaleHelper,args);
                    var actual=args[5] is Vector3 value?new[]{value.x,value.y,value.z}:Array.Empty<float>();
                    float expected=beat<=475?1-.5f*(beat-474):beat<=477?.5f:beat<=477.5f?.5f+(beat-477):1;
                    pass&=actual.Length==3&&Math.Abs(actual[0]-(mirror?-expected:expected))<.0001f&&Math.Abs(actual[1]-expected)<.0001f&&Math.Abs(actual[2]-expected)<.0001f;
                    samples.Add(new{beat,expected,actual});scaleTrackSamples++;
                }
                pass&=scheduler.Count==0&&scheduler.Starts==2;
            }
            finally{scheduler.Dispose();ControlledCoroutineScheduler.Current=previousScheduler;}
            cases++;if(!pass)failed++;scaleTrackResults.Add(new{mirror,pass,samples,scope="Exact err candidate size event payload/timing at BPM 174; actual parser/dispatcher/Track/AnimationHelper, controlled scheduler. Scale vector including mirrored X sign; no note rotation/mesh/collider neutrality or support admission."});
        }
        var charts=new List<object>();
        if(inventory!=null)foreach(string id in new[]{"089","090","091","092"})
        {
            var request=JObject.Parse(File.ReadAllText(Path.Combine(inventory,id+".result.json")))["request"]!;
            var root=JObject.Parse(File.ReadAllText(Path.Combine((string)request["Folder"]!,(string)request["File"]!)));
            var saved=NoodleNativeData.Deserialize(gameRoot,root,"3");
            var tracks=(IDictionary)Activator.CreateInstance(cp[2].ParameterType)!;
            var graph=NoodleTrackInventory.Inspect(root);
            foreach(string name in graph.ReferencedTracks.Concat(graph.GameplayTracks).Distinct())tracks[name]=Activator.CreateInstance(trackType,true)!;
            var definitions=(IDictionary)Activator.CreateInstance(cp[1].ParameterType)!;
            var metadata=(IDictionary)saved.GetType().GetProperty("customData")!.GetValue(saved)!;
            if(metadata["pointDefinitions"] is IDictionary points)foreach(DictionaryEntry point in points)definitions[point.Key]=point.Value;
            int count=0,infos=0,empty=0;var types=new Dictionary<string,int>();var blocked=new List<object>();
            foreach(var ev in Events(saved))
            {
                count++;
                object parsed;
                try{Parse(ev,definitions,tracks,0,out parsed);}
                catch(TargetInvocationException e)when(e.InnerException is System.Security.SecurityException security&&security.StackTrace?.Contains("UnityEngine.Quaternion.Internal_FromEulerRad",StringComparison.Ordinal)==true)
                {blocked.Add(new{index=count-1,reason="Native Unity quaternion construction requires game runtime"});continue;}
                var entries=((IEnumerable)parsedType.GetProperty("CoroutineInfos",flags)!.GetValue(parsed)!).Cast<object>().ToArray();
                infos+=entries.Length;if(entries.Length==0)empty++;
                foreach(var entry in entries)
                {
                    var property=entry.GetType().GetProperty("Property",flags)!.GetValue(entry)!;
                    string name=property.GetType().Name+"<"+property.GetType().GenericTypeArguments.Single().Name+">";
                    types[name]=types.GetValueOrDefault(name)+1;
                    if(!tracks.Values.Cast<object>().Any(t=>ReferenceEquals(t,entry.GetType().GetProperty("Track",flags)!.GetValue(entry))))throw new InvalidDataException("Parsed event resolved a foreign track");
                }
            }
            bool pass=count==root["customData"]!["customEvents"]!.Count();cases++;if(!pass)failed++;
            charts.Add(new{id,count,parsed=count-blocked.Count,blocked,infos,empty,types,pass});
        }
        return new{cases,failed,timedCases,timedSamples,replacements,dispatchedCases,dispatchStarts,dispatchStops,dispatchedPathCases,pathSamples,scaleTrackSamples,scaleTrackResults,timedFailures,registrations,results,charts,
            scope="Installed whole-map custom event JSON deserialization and Heck constructor with replayed Heck/Noodle/Chroma property registrations; scalar zero-duration actual dispatch plus parsed nonzero-duration actual coroutine driven by controlled callback time, explicit host disposal/replacement, same Track read by AnimationHelper and unrelated-track control. Duration conversion follows separately audited IL; timed dispatch also executes StartEventCoroutine IL with only MonoBehaviour StartCoroutine/StopCoroutine redirected to a controlled scheduler; native Unity scheduling remains excluded. Local charts attempt every event; native Unity quaternion construction is explicitly blocked. No chart coroutine execution, complete processor pipeline or environment lifecycle."};
    }
}
