using System.Collections;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.Loader;
using Newtonsoft.Json.Linq;
using BeatInsight.Core;
using BeatInsight.Formats;
using UnityEngine;

namespace BeatInsight.Compare;

static class NoodleVisualTrackChecks
{
    const BindingFlags flags=BindingFlags.Public|BindingFlags.NonPublic|BindingFlags.Instance|BindingFlags.Static;
    internal static IEnumerable<(string Name,JObject Root,float Bpm)> Fixtures()
    {
        using var stream=typeof(NoodleVisualTrackChecks).Assembly.GetManifestResourceStream("BeatInsight.IsolatedVisualTrack.json")!;
        using var reader=new StreamReader(stream);var source=JObject.Parse(reader.ReadToEnd());
        foreach(bool arcTrack in new[]{false,true})foreach(bool arrays in new[]{false,true})foreach(bool constant in new[]{false,true})foreach(int repeat in new[]{0,2})
        {
            var root=(JObject)source.DeepClone();var data=root["customData"]!["customEvents"]![2]!["d"]!;
            if(arcTrack)root["sliders"]![0]!["customData"]=new JObject {["track"]="notes"};
            if(arrays)
            {
                foreach(var note in root["colorNotes"]!)note["customData"]!["track"]=new JArray("notes","other");
                data["track"]=new JArray("notes","other");
            }
            if(constant)data["color"]=new JArray(.25,.5,2,.75);
            data["repeat"]=repeat;
            yield return ($"isolated-visual-arc={arcTrack};array={arrays};constant={constant};repeat={repeat}",root,120);
        }
        var arcOnly=(JObject)source.DeepClone();
        foreach(var note in arcOnly["colorNotes"]!)((JObject)note["customData"]!).Remove("track");
        arcOnly["sliders"]![0]!["customData"]=new JObject {["track"]="notes"};
        yield return ("isolated-visual-arc-only-control",arcOnly,120);
        var opacity=(JObject)source.DeepClone();opacity["sliders"]![0]!["customData"]=new JObject {["track"]="notes"};
        opacity["customData"]!["customEvents"]![2]!["d"]!["color"]=new JArray(.25,.5,2,.75);
        opacity["customData"]!["customEvents"]![2]!["d"]!["dissolve"]=new JArray(.25);
        opacity["customData"]!["customEvents"]![2]!["d"]!["dissolveArrow"]=new JArray(.75);
        yield return ("isolated-visual-opacity;constant=True",opacity,120);
        var control=(JObject)source.DeepClone();control["customData"]!["customEvents"]![2]!["d"]!["offsetPosition"]=new JArray(9,8,7);
        yield return ("isolated-visual-geometry-control",control,120);
    }
    internal static JObject ProjectReal(MapCase request)
    {
        var root=JObject.Parse(File.ReadAllText(LocalMap.SafePath(request.Folder,request.File)));
        if(!((string?)root["version"]??"").StartsWith("3.",StringComparison.Ordinal)
            ||root["bombNotes"] is JArray {Count:>0}||root["burstSliders"] is JArray {Count:>0})
            throw new NotSupportedException("Real visual-track probe supports ordinary v3 notes/arcs only");
        _=LocalMap.Load(request,AnalysisFeatures.Average|AnalysisFeatures.Peak|AnalysisFeatures.Median|AnalysisFeatures.FirstNotes|AnalysisFeatures.Density,includeSwingWindowWalls:false);
        var names=new HashSet<string>(StringComparer.Ordinal);
        foreach(string collection in new[]{"colorNotes","sliders"})
        foreach(var item in root[collection]??new JArray())
        {
            var target=item["customData"]?["track"];
            if(target is JArray a)foreach(var name in a)names.Add(name.Value<string>()!);
            else if(target?.Type==JTokenType.String)names.Add(target.Value<string>()!);
        }
        if(names.Count==0)throw new InvalidDataException("Real visual-track source has no isolated gameplay track");
        var events=(JArray)root["customData"]!["customEvents"]!;
        root["customData"]!["customEvents"]=new JArray(events.Where(e=>e["t"]?.Value<string>()=="AnimateTrack"
            &&e["d"] is JObject data&&data["track"] is JToken target
            &&(target is JArray a?a.Any(n=>n.Type==JTokenType.String&&names.Contains((string)n!)):target.Type==JTokenType.String&&names.Contains(target.Value<string>()!)))
            .Select(e=>e.DeepClone()));
        root["obstacles"]=new JArray();
        return root;
    }
    internal static JObject WithoutAssignments(JObject source)
    {
        var copy=(JObject)source.DeepClone();
        foreach(string collection in new[]{"colorNotes","sliders"})
        foreach(var item in copy[collection]??new JArray())
            (item["customData"] as JObject)?.Remove("track");
        return copy;
    }
    internal static object CompareProduction(MapCase request)
    {
        const AnalysisFeatures features=AnalysisFeatures.Average|AnalysisFeatures.Peak|AnalysisFeatures.Median|AnalysisFeatures.FirstNotes|AnalysisFeatures.Density;
        var json=File.ReadAllText(LocalMap.SafePath(request.Folder,request.File));var root=JObject.Parse(json);
        var selected=SelectedMapAnalyzer.Load(new SelectedMapRequest("visual-probe",request.Characteristic,request.Difficulty,
            request.Difficulty switch {"Normal"=>1,"Expert"=>3,"ExpertPlus"=>4,_=>throw new InvalidDataException("Unvalidated real visual fixture difficulty")},
            LocalMap.SafePath(request.Folder,request.File),features:features));
        var info=LocalMap.Info(request.Folder);if(info["audio"] is not JObject)info["audio"]=new JObject();
        info["audio"]!["songDuration"]=selected.Duration;
        LoadedMap Load(JObject source)=>LocalMap.LoadDocuments(new MapDocuments(info.ToString(),source.ToString(),file:request.File),request.Characteristic,request.Difficulty,features,includeSwingWindowWalls:false);
        var actual=Load(root);var without=Load(WithoutAssignments(root));
        bool same=JToken.DeepEquals(JObject.FromObject(actual.Input),JObject.FromObject(without.Input));
        var expected=CompatibleAnalysis.Analyze(without.Input,reproduceKnownDefects:false,features:features);
        same&=JToken.DeepEquals(JObject.FromObject(expected),JObject.FromObject(selected.Corrected))
            &&selected.MedianLeft==WindowStatistics.ActiveWindowMedian(expected.LeftBins)&&selected.MedianRight==WindowStatistics.ActiveWindowMedian(expected.RightBins);
        return new{failed=same?0:1,mapSha256=Comparison.Hash(json),notes=actual.Input.Notes.Length,
            scope="Production notes-only input and menu metrics versus the same real map with only gameplay track assignments removed. Static coordinates/arcs/tempo remain unchanged. Native color offset evidence is separate; no reference-mod correctness claim."};
    }
    internal static object RunReal(object heckData,object noodleData,float initialBpm)
    {
        var heck=heckData.GetType().Assembly;
        var noodle=AssemblyLoadContext.Default.Assemblies.Single(a=>a.GetName().Name=="NoodleExtensions");
        var main=AssemblyLoadContext.Default.Assemblies.Single(a=>a.GetName().Name=="Main");
        var ownerType=heck.GetType("Heck.Animation.Events.CoroutineEvent",true)!;
        var callbacksType=main.GetType("BeatmapCallbacksController",true)!;
        var callbacks=RuntimeHelpers.GetUninitializedObject(callbacksType);var clock=callbacksType.GetField("_songTime",flags)!;
        var owner=RuntimeHelpers.GetUninitializedObject(ownerType);
        ownerType.GetField("_beatmapCallbacksController",flags)!.SetValue(owner,callbacks);
        var bpm=DispatchProxy.Create(ownerType.GetField("_bpmController",flags)!.FieldType,typeof(ControlledBpmProxy));((ControlledBpmProxy)bpm).Bpm=initialBpm;
        ownerType.GetField("_bpmController",flags)!.SetValue(owner,bpm);ownerType.GetField("_deserializedData",flags)!.SetValue(owner,heckData);
        var dispatch=ownerType.GetMethod("StartEventCoroutine",flags)!;var compiled=ControlledCoroutineScheduler.CompileDispatch(dispatch);
        var events=((IDictionary)heckData.GetType().GetProperty("CustomEventCustomDatas",flags)!.GetValue(heckData)!).Keys.Cast<BeatmapDataItem>().OrderBy(e=>e.time).ToArray();
        var objects=(IDictionary)noodleData.GetType().GetProperty("ObjectCustomDatas",flags)!.GetValue(noodleData)!;
        var entries=new List<DictionaryEntry>();foreach(DictionaryEntry entry in objects)entries.Add(entry);
        var helperType=noodle.GetType("NoodleExtensions.Animation.AnimationHelper",true)!;var offset=helperType.GetMethod("GetObjectOffset",flags)!;
        var helpers=new[]{false,true}.Select(left=>Activator.CreateInstance(helperType,flags,null,new object[]{left},null)!).ToArray();
        int failed=events.Length==0?1:0,samples=0,colorSamples=0,pending=0;
        var tracks=new HashSet<object>(ReferenceEqualityComparer.Instance);
        foreach(var entry in entries)
            if(entry.Value!.GetType().GetProperty("Track",flags)!.GetValue(entry.Value) is IEnumerable assigned)
                foreach(var track in assigned)tracks.Add(track);
        using var scheduler=new ControlledCoroutineScheduler();var previous=ControlledCoroutineScheduler.Current;ControlledCoroutineScheduler.Current=scheduler;
        try
        {
            int next=0;
            foreach(var entry in entries.OrderBy(e=>((BeatmapDataItem)e.Key).time))
            {
                float time=((BeatmapDataItem)entry.Key).time;
                while(next<events.Length&&events[next].time<=time)
                {clock.SetValue(callbacks,events[next].time);compiled.Invoke(null,new[]{owner,events[next],Enum.ToObject(dispatch.GetParameters()[1].ParameterType,0)});scheduler.Tick();next++;}
                clock.SetValue(callbacks,time);scheduler.Tick();
                foreach(var helper in helpers)
                {
                    object?[] args=new object?[10];args[0]=entry.Value!.GetType().GetProperty("AnimationObject",flags)!.GetValue(entry.Value);
                    args[1]=entry.Value.GetType().GetProperty("Track",flags)!.GetValue(entry.Value);args[2]=.5f;
                    offset.Invoke(helper,args);if(args.Skip(3).Any(a=>a!=null))failed++;samples++;
                }
                foreach(var track in tracks)
                foreach(DictionaryEntry p in (IDictionary)track.GetType().GetField("_properties",flags)!.GetValue(track)!)
                {
                    var value=p.Value!.GetType().GetProperty("Value",flags)!.GetValue(p.Value);
                    if(value==null)continue;
                    if((string)p.Key!="color"||value is not Vector4 color||new[]{color.x,color.y,color.z,color.w}.Any(v=>!float.IsFinite(v)))failed++;
                    else colorSamples++;
                }
            }
            pending=scheduler.Count;if(colorSamples==0)failed++;
        }
        finally{scheduler.Dispose();ControlledCoroutineScheduler.Current=previous;}
        return new{failed,samples,colorSamples,objects=objects.Count,events=events.Length,tracks=tracks.Count,pendingBeforeDispose=pending,
            pendingAfterDispose=scheduler.Count,contextRestored=ReferenceEquals(ControlledCoroutineScheduler.Current,previous),
            scope="Real note/arc source and isolated color events through installed converters/rows, deserialized shared tracks, timed iterator dispatch and AnimationHelper at nominal object times, both handedness modes. Unrelated scene events and walls excluded from this probe. No native Unity scheduling, environment construction or full pose."};
    }
    internal static object Run(string gameRoot,object heckData,object noodleData,bool geometryControl,bool constant,bool opacity=false)
    {
        var heck=heckData.GetType().Assembly;
        var noodle=AssemblyLoadContext.Default.Assemblies.Single(a=>a.GetName().Name=="NoodleExtensions");
        var main=AssemblyLoadContext.Default.Assemblies.Single(a=>a.GetName().Name=="Main");
        var ownerType=heck.GetType("Heck.Animation.Events.CoroutineEvent",true)!;
        var callbacksType=main.GetType("BeatmapCallbacksController",true)!;
        var callbacks=RuntimeHelpers.GetUninitializedObject(callbacksType);var clock=callbacksType.GetField("_songTime",flags)!;
        var owner=RuntimeHelpers.GetUninitializedObject(ownerType);
        ownerType.GetField("_beatmapCallbacksController",flags)!.SetValue(owner,callbacks);
        var bpm=DispatchProxy.Create(ownerType.GetField("_bpmController",flags)!.FieldType,typeof(ControlledBpmProxy));
        ((ControlledBpmProxy)bpm).Bpm=120;
        ownerType.GetField("_bpmController",flags)!.SetValue(owner,bpm);
        ownerType.GetField("_deserializedData",flags)!.SetValue(owner,heckData);
        var dispatch=ownerType.GetMethod("StartEventCoroutine",flags)!;
        var compiled=ControlledCoroutineScheduler.CompileDispatch(dispatch);
        var events=((IDictionary)heckData.GetType().GetProperty("CustomEventCustomDatas",flags)!.GetValue(heckData)!).Keys.Cast<BeatmapDataItem>().OrderBy(e=>e.time).ToArray();
        var objects=(IDictionary)noodleData.GetType().GetProperty("ObjectCustomDatas",flags)!.GetValue(noodleData)!;
        var noteEntries=new List<DictionaryEntry>();foreach(DictionaryEntry entry in objects)if(entry.Key is NoteData)noteEntries.Add(entry);
        var entries=noteEntries.ToArray();
        int failed=entries.Length==2&&events.Length==2?0:1,samples=0,colorSamples=0,identityChecks=0,positionControls=0;
        var helperType=noodle.GetType("NoodleExtensions.Animation.AnimationHelper",true)!;
        var offset=helperType.GetMethod("GetObjectOffset",flags)!;
        if(offset.GetParameters().Length!=10||offset.GetParameters()[3].Name!="positionOffset"||offset.GetParameters()[9].Name!="cuttable")
            throw new InvalidDataException("Animation helper offset ABI changed");
        var noteTracks=new HashSet<object>(ReferenceEqualityComparer.Instance);
        foreach(DictionaryEntry entry in objects)
        {
            var tracks=entry.Value!.GetType().GetProperty("Track",flags)!.GetValue(entry.Value) as IEnumerable;
            if(tracks!=null)foreach(var track in tracks)noteTracks.Add(track);
        }
        if(noteTracks.Count==0)failed++;
        var parsedEvents=(IDictionary)heckData.GetType().GetProperty("CustomEventCustomDatas",flags)!.GetValue(heckData)!;
        int colorInfos=0,sceneInfos=0,scalarInfos=0;
        foreach(DictionaryEntry entry in parsedEvents)
        foreach(var info in (IEnumerable)entry.Value!.GetType().GetProperty("CoroutineInfos",flags)!.GetValue(entry.Value)!)
        {
            var track=info!.GetType().GetProperty("Track",flags)!.GetValue(info)!;
            var property=info.GetType().GetProperty("Property",flags)!.GetValue(info)!;
            bool color=property.GetType().GenericTypeArguments.Single()==typeof(Vector4);
            if(color){if(!noteTracks.Contains(track))failed++;else identityChecks++;colorInfos++;}
            else if(opacity&&noteTracks.Contains(track)&&property.GetType().GenericTypeArguments.Single()==typeof(float))scalarInfos++;
            else if(!geometryControl){if(noteTracks.Contains(track))failed++;sceneInfos++;}
        }
        if(colorInfos!=noteTracks.Count||!geometryControl&&sceneInfos!=1||scalarInfos!=(opacity?2*noteTracks.Count:0))failed++;
        using var scheduler=new ControlledCoroutineScheduler();var previous=ControlledCoroutineScheduler.Current;ControlledCoroutineScheduler.Current=scheduler;
        try
        {
            clock.SetValue(callbacks,0f);
            foreach(var ev in events)compiled.Invoke(null,new[]{owner,ev,Enum.ToObject(dispatch.GetParameters()[1].ParameterType,0)});
            foreach(float time in new[]{0f,.25f,.5f,.75f,1.25f,3.25f})
            {
                clock.SetValue(callbacks,time);scheduler.Tick();
                foreach(var track in noteTracks)
                {
                    var properties=(IDictionary)track.GetType().GetField("_properties",flags)!.GetValue(track)!;
                    if(!properties.Contains("color")){failed++;continue;}
                    var property=properties["color"]!;
                    if(property.GetType().GetProperty("Value",flags)!.GetValue(property) is not Vector4 color
                        ||new[]{color.x,color.y,color.z,color.w}.Any(v=>!float.IsFinite(v)))failed++;
                    else
                    {
                        colorSamples++;
                        if(constant)
                        {if(color.x!=.25f||color.y!=.5f||color.z!=2||color.w!=.75f)failed++;}
                        else if(time<=.75f)
                        {if(Math.Abs(color.x-(.25f+.5f*time))>1e-5||Math.Abs(color.y-(.5f-.25f*time))>1e-5
                            ||Math.Abs(color.z-2*time)>1e-5||Math.Abs(color.w-(1-.5f*time))>1e-5)failed++;}
                    }
                    foreach(DictionaryEntry p in properties)
                        if(!geometryControl&&(string)p.Key!="color"&&!(opacity&&(string)p.Key is "dissolve" or "dissolveArrow")&&p.Value!.GetType().GetProperty("Value",flags)!.GetValue(p.Value)!=null)failed++;
                }
                foreach(bool left in new[]{false,true})
                {
                    var helper=Activator.CreateInstance(helperType,flags,null,new object[]{left},null)!;
                    foreach(DictionaryEntry entry in objects)
                    {
                        object?[] args=new object?[10];
                        args[0]=entry.Value!.GetType().GetProperty("AnimationObject",flags)!.GetValue(entry.Value);
                        args[1]=entry.Value.GetType().GetProperty("Track",flags)!.GetValue(entry.Value);args[2]=.5f;
                        offset.Invoke(helper,args);
                        if(geometryControl&&entry.Key is NoteData)
                        {
                            if(args[3] is not Vector3 p||Math.Abs(p.x-(left?-5.4f:5.4f))>1e-5||Math.Abs(p.y-4.8f)>1e-5||Math.Abs(p.z-4.2f)>1e-5)failed++;else positionControls++;
                            if(args.Skip(4).Any(a=>a!=null))failed++;
                        }
                        else if(opacity)
                        {
                            if(new[]{3,4,5,6,9}.Any(i=>args[i]!=null)||args[7] is not float d||d!=.25f||args[8] is not float a||a!=.75f)failed++;
                        }
                        else if(args.Skip(3).Any(a=>a!=null))failed++;
                        samples++;
                    }
                }
            }
            if(scheduler.Count!=0)failed++;
        }
        finally{ControlledCoroutineScheduler.Current=previous;}
        return new{failed,samples,colorSamples,identityChecks,colorInfos,sceneInfos,scalarInfos,positionControls,geometryControl,opacity,
            scope="Actual custom loader/rows and Heck/Noodle/Chroma map bindings, native parsed Vector4 color events and AnimationHelper for notes/arcs in both handedness modes. "
                +"Unity scheduling is replaced by an explicit iterator scheduler. Parent/environment GameObjects are not constructed; admission excludes shared scene/material/parent/player tracks. "
                +"Geometry control writes a nonzero native offset and is rejected by the production gate. No full scene or collider pose proof."};
    }
}
