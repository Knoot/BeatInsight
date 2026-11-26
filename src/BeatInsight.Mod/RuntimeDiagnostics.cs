using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using HarmonyLib;
using IPA.Utilities.Async;
using Newtonsoft.Json;
using UnityEngine;

namespace BeatInsight.Mod;

internal static class RuntimeDiagnostics
{
    const int Limit=512;
    const int TransitionSampleLimit=12;
    const int NoteSampleLimit=3;
    const int NotePoseLimit=12;
    static readonly Stopwatch clock=new Stopwatch();
    static readonly HashSet<MethodBase> installed=new();
    static readonly HashSet<Assembly> identified=new();
    static readonly Dictionary<(Type Type,string Name),MemberInfo?> members=new();
    static Harmony? patches;
    static bool active;
    static int mainThread,emitted;
    static int transitionSamples;
    static long sequence,attempt;
    static string session="",map="";
    static object? attemptData;
    static readonly List<WeakReference<object>> watched=new();
    static readonly List<(WeakReference<NoteController> Controller,WeakReference<NoteData> Data)> sampledNotes=new();
    static int noteSamples,notePoseSamples;
    static bool rowPatchesSampled;
    static CancellationTokenSource? samples;
    internal static long AttemptId=>Interlocked.Read(ref attempt);
    internal static bool CanRecord=>active&&Plugin.Config.DiagnosticLogging&&Volatile.Read(ref emitted)<Limit;

    internal static void Enable()
    {
        active=true;mainThread=Thread.CurrentThread.ManagedThreadId;session=Guid.NewGuid().ToString("N");
        clock.Restart();patches=new Harmony("arevu.beatinsight.diagnostics");
        Record("diagnostics.enabled",new{schema=1,limit=Limit,transitionSampleLimit=TransitionSampleLimit,noteSampleLimit=NoteSampleLimit,notePoseLimit=NotePoseLimit,assembly=typeof(Plugin).Assembly.GetName().Version?.ToString()});
        Identify(typeof(Plugin).Assembly);
        Identify(typeof(BeatInsight.Core.CompatibleAnalysis).Assembly);
        Identify(typeof(BeatInsight.Formats.LocalMap).Assembly);
        Identify(typeof(BeatInsight.WallAlert.SquatWarningSequence).Assembly);
        Discover();CaptureRotationMath();
    }
    // One bounded sample per diagnostics enable. No scene objects, mutations,
    // gameplay callbacks or per-frame work; native Unity supplies the results.
    static void CaptureRotationMath()
    {
        if(!CanRecord)return;
        try
        {
            Identify(typeof(Quaternion).Assembly);
            long started=clock.ElapsedMilliseconds;
            var angles=new[]{
                new Vector3(0,0,0),new Vector3(.75f,0,0),new Vector3(0,.75f,0),new Vector3(0,0,.75f),
                new Vector3(-5,0,0),new Vector3(30,0,0),new Vector3(0,30,0),new Vector3(0,0,30),
                new Vector3(90,0,0),new Vector3(0,90,0),new Vector3(0,0,90),
                new Vector3(180,0,0),new Vector3(0,180,0),new Vector3(0,0,180),
                new Vector3(360,0,0),new Vector3(0,360,0),new Vector3(0,0,360),
                new Vector3(0,-30,0),new Vector3(0,-360,0),new Vector3(0,720,0),
                new Vector3(12.25f,23.75f,34.5f),new Vector3(-15.5f,-25.25f,35.125f),
                new Vector3(45,90,-30),new Vector3(180,90,270)};
            float[] V(Vector3 v)=>new[]{v.x,v.y,v.z};
            float[] Q(Quaternion q)=>new[]{q.x,q.y,q.z,q.w};
            var rows=new List<object>(angles.Length);
            foreach(var input in angles)
            {
                var vector=Quaternion.Euler(input);var scalar=Quaternion.Euler(input.x,input.y,input.z);
                rows.Add(new{degrees=V(input),vector=Q(vector),scalar=Q(scalar),inverse=Q(Quaternion.Inverse(vector)),
                    right=V(vector*Vector3.right),up=V(vector*Vector3.up),forward=V(vector*Vector3.forward)});
            }
            Record("rotation.math",new{schema=1,unity=Application.unityVersion,count=rows.Count,elapsedMs=clock.ElapsedMilliseconds-started,rows,
                scope="native Unity Euler overloads and inverse; managed quaternion-vector product; no scene/player pose or map admission"});
        }
        catch(Exception e){Record("rotation.math.unavailable",new{error=e.GetType().Name});}
    }
    internal static void BeginAttempt(string key,object data)
    {
        try
        {
            ResetSamples();map=key;attemptData=data;Interlocked.Increment(ref attempt);Interlocked.Exchange(ref emitted,0);
            Discover();Record("attempt.begin",new{map,reason="selection",settings=data});
        }
        catch(Exception e){Record("diagnostics.error",new{operation="begin",error=e.GetType().Name});}
    }
    internal static void RestartAttempt()
    {
        try
        {
            ResetSamples();Interlocked.Increment(ref attempt);Interlocked.Exchange(ref emitted,0);
            Record("attempt.begin",new{map,reason="restart",settings=attemptData});
        }
        catch(Exception e){Record("diagnostics.error",new{operation="restart",error=e.GetType().Name});}
    }
    internal static void Record(string kind,object? data=null,long? attemptId=null)
    {
        if(!CanRecord)return;
        try
        {
            int count=Interlocked.Increment(ref emitted);if(count>Limit)return;
            int thread=Thread.CurrentThread.ManagedThreadId;
            int frame=-1;if(thread==mainThread){try{frame=Time.frameCount;}catch{/* A diagnostic event may arrive during teardown. */}}
            Plugin.Log.Info("BI-DIAG "+JsonConvert.SerializeObject(new{schema=1,session,seq=Interlocked.Increment(ref sequence),ms=clock.ElapsedMilliseconds,
                attempt=attemptId??AttemptId,thread,frame,
                kind=count==Limit?"diagnostics.limit":kind,data=count==Limit?(object)new{limit=Limit}:data}));
        }
        catch{/* Diagnostics must not change gameplay or history recording. */}
    }
    static void Identify(Assembly assembly)
    {
        if(!CanRecord||!identified.Add(assembly))return;
        try
        {
            using var sha=SHA256.Create();string hash=BitConverter.ToString(sha.ComputeHash(File.ReadAllBytes(assembly.Location))).Replace("-","");
            Record("assembly",new{name=assembly.GetName().Name,version=assembly.GetName().Version?.ToString(),sha256=hash});
        }
        catch(Exception e){Record("assembly.unavailable",new{name=assembly.GetName().Name,error=e.GetType().Name});}
    }
    internal static void Discover()
    {
        if(!CanRecord||patches==null)return;
        Install();
    }
    // Only bounded scene/event boundaries. Never hook Update or obstacle playback.
    static void Install()
    {
        Hook("Main","VRCenterAdjust","OnEnable");
        Hook("Main","VRCenterAdjust","Start");
        Hook("Main","VRCenterAdjust","OnDisable");
        Hook("Main","VRCenterAdjust","SetRoomTransformOffset");
        Hook("Main","VRCenterAdjust","ResetRoom");
        Hook("Main","VRRenderingParamsSetup","OnEnable");
        Hook("Main","SettingsApplicatorSO","NotifyRoomTransformOffsetWasUpdated");
        Hook("Main","PlayerTransforms","Awake");
        Hook("Main","NoteController","Init");
        Hook("Zenject","Zenject.MonoKernel","Initialize");
        Hook("BGLib.AppFlow","GameScenesManager","ActivatePresentedSceneRootObjects");
        Hook("BGLib.AppFlow","GameScenesManager","SetActiveRootObjectsInScenes");
        Hook("NoodleExtensions","NoodleExtensions.Managers.NoodlePlayerTransformManager","Initialize");
        Hook("NoodleExtensions","NoodleExtensions.Managers.NoodlePlayerTransformManager","Create");
        Hook("NoodleExtensions","NoodleExtensions.Animation.PlayerTrack","Construct");
        Hook("NoodleExtensions","NoodleExtensions.Animation.PlayerTrack","AssignTrack");
        Hook("NoodleExtensions","NoodleExtensions.Animation.PlayerTrack","Start");
        Hook("NoodleExtensions","NoodleExtensions.Animation.PlayerTrack","OnDidPauseEvent");
        Hook("NoodleExtensions","NoodleExtensions.Animation.PlayerTrack","OnDidResumeEvent");
        Hook("NoodleExtensions","NoodleExtensions.Animation.AssignPlayerToTrack","Callback");
    }
    static void Hook(string assembly,string type,string name)
    {
        var provider=AppDomain.CurrentDomain.GetAssemblies().FirstOrDefault(a=>a.GetName().Name==assembly);
        if(provider==null)return;
        try
        {
            var target=provider.GetType(type)?.GetMethods(BindingFlags.Public|BindingFlags.NonPublic|BindingFlags.Instance|BindingFlags.Static|BindingFlags.DeclaredOnly).SingleOrDefault(m=>m.Name==name);
            if(target==null){Record("hook.unavailable",new{assembly,type,name});return;}
            if(installed.Contains(target))return;
            if(type=="NoteController"&&name=="Init")
            {
                // Typed postfix: no __args array or boxing on every spawned note.
                patches!.Patch(target,postfix:new HarmonyMethod(AccessTools.Method(typeof(RuntimeDiagnostics),nameof(NoteInitialized))){priority=Priority.Last});
                installed.Add(target);Identify(provider);Record("hook.installed",new{assembly,type,name,noteSampleLimit=NoteSampleLimit});return;
            }
            var prefix=AccessTools.Method(typeof(RuntimeDiagnostics),target.IsStatic?nameof(BeforeStatic):nameof(Before));
            var postfix=AccessTools.Method(typeof(RuntimeDiagnostics),target.ReturnType!=typeof(void)?nameof(AfterResult):target.IsStatic?nameof(AfterStatic):nameof(After));
            patches!.Patch(target,new HarmonyMethod(prefix),new HarmonyMethod(postfix));installed.Add(target);Identify(provider);
            Record("hook.installed",new{assembly,type,name});
        }
        catch(Exception e){Record("hook.error",new{assembly,type,name,error=e.GetType().Name,message=e.Message});}
    }
    static void Before(object __instance,MethodBase __originalMethod,object[] __args)=>Boundary("before",__instance,__originalMethod,__args,null);
    static void After(object __instance,MethodBase __originalMethod,object[] __args)=>Boundary("after",__instance,__originalMethod,__args,null);
    static void BeforeStatic(MethodBase __originalMethod,object[] __args)=>Boundary("before",null,__originalMethod,__args,null);
    static void AfterStatic(MethodBase __originalMethod,object[] __args)=>Boundary("after",null,__originalMethod,__args,null);
    static void AfterResult(object __instance,MethodBase __originalMethod,object[] __args,object __result)=>Boundary("after",__instance,__originalMethod,__args,__result);
    static void NoteInitialized(NoteController __instance)
    {
        if(!CanRecord||Volatile.Read(ref noteSamples)>=NoteSampleLimit||!RuntimeCapture.HasActiveAttempt
            ||Thread.CurrentThread.ManagedThreadId!=mainThread)return;
        try
        {
            if(!(__instance is GameNoteController)||__instance.noteData==null||(int)__instance.noteData.scoringType<=0)return;
            var data=__instance.noteData;
            sampledNotes.Add((new WeakReference<NoteController>(__instance),new WeakReference<NoteData>(data)));noteSamples++;
            RecordNotePose("post-init-observer",__instance,noteSamples);
        }
        catch(Exception e){Record("note.pose.error",new{reason="post-init-observer",error=e.GetType().Name});}
    }
    static object? NoteMetadata(object? value,int depth=0)
    {
        if(value is string text&&text.Length>256)return new{value=text.Substring(0,256),length=text.Length,truncated=true};
        var simple=Simple(value);if(simple!=null||value==null)return simple;
        if(depth>=4)return new{type=value.GetType().Name,truncated=true};
        if(value is IDictionary map)
        {
            var fields=new Dictionary<string,object?>();
            foreach(string key in new[]{"track","_track","animation","_animation","scale","_scale","size","worldRotation","_rotation","localRotation","_localRotation","disableNoteGravity","_disableNoteGravity","disableNoteLook","_disableNoteLook"})
                if(map.Contains(key))fields[key]=NoteMetadata(map[key],depth+1);
            return fields;
        }
        if(value is IList list)return new{count=list.Count,values=list.Cast<object?>().Take(4).Select(v=>NoteMetadata(v,depth+1)).ToArray(),truncated=list.Count>4};
        return new{type=value.GetType().Name};
    }
    static object? NoteBoxes(object? source)
    {
        if(!(source is IEnumerable boxes))return null;
        var result=new List<object>();int seen=0;
        foreach(var value in boxes)
        {
            if(seen++>=2)break;
            if(!(value is BoxCuttableBySaber cuttable)||cuttable==null)continue;
            var collider=Read(cuttable,"_collider") as BoxCollider;
            object? bounds=null;
            if(collider!=null){var b=collider.bounds;bounds=new{center=Simple(b.center),size=Simple(b.size),enabled=collider.enabled,active=collider.gameObject.activeInHierarchy};}
            result.Add(new{canBeCut=cuttable.canBeCut,cuttable.radius,center=Simple(cuttable.colliderCenter),size=Simple(cuttable.colliderSize),
                pose=Pose(cuttable.transform),bounds});
        }
        return result;
    }
    static object NoteSnapshot(NoteController controller)
    {
        var data=controller.noteData;
        var wrapper=Read(controller,"_wrapperGO") as GameObject;
        var clockSource=Read(controller,"_audioTimeSyncController");
        return new{controllerId=controller.GetInstanceID(),dataId=RuntimeHelpers.GetHashCode(data),
            data=new{data.time,data.beat,data.lineIndex,layer=(int)data.noteLineLayer,color=(int)data.colorType,direction=(int)data.cutDirection,
                scoring=(int)data.scoringType,gameplay=(int)data.gameplayType,metadata=NoteMetadata(Read(data,"customData"))},
            songTime=Simple(Read(clockSource,"songTime")),controller.uniformScale,controller.hidden,
            visualModifier=Simple(Read(controller,"noteVisualModifierType")),root=Pose(controller.transform),visual=Pose(controller.noteTransform),
            wrapper=wrapper==null?null:Pose(wrapper.transform),bigBoxes=NoteBoxes(Read(controller,"_bigCuttableBySaberList")),smallBoxes=NoteBoxes(Read(controller,"_smallCuttableBySaberList"))};
    }
    static void RecordNotePose(string reason,NoteController controller,int? ordinal=null)
    {
        if(!CanRecord||notePoseSamples>=NotePoseLimit)return;
        notePoseSamples++;
        long started=Stopwatch.GetTimestamp();var snapshot=NoteSnapshot(controller);
        double snapshotMs=(Stopwatch.GetTimestamp()-started)*1000.0/Stopwatch.Frequency;
        Record("note.pose",new{reason,ordinal,snapshotMs,snapshot});
    }
    static void SampleNotes(string reason)
    {
        if(!CanRecord||notePoseSamples>=NotePoseLimit||!RuntimeCapture.HasActiveAttempt||Thread.CurrentThread.ManagedThreadId!=mainThread)return;
        for(int i=sampledNotes.Count-1;i>=0&&notePoseSamples<NotePoseLimit;i--)
        {
            var entry=sampledNotes[i];
            if(!entry.Controller.TryGetTarget(out var controller)||controller==null){sampledNotes.RemoveAt(i);continue;}
            if(!entry.Data.TryGetTarget(out var data)||!ReferenceEquals(controller.noteData,data))
            {Record("note.pose.skipped",new{reason,controllerId=controller.GetInstanceID(),cause="pool-reused-or-data-collected"});sampledNotes.RemoveAt(i);continue;}
            try{RecordNotePose(reason,controller);}
            catch(Exception e){Record("note.pose.error",new{reason,error=e.GetType().Name});}
        }
    }
    static void Boundary(string phase,object? instance,MethodBase method,object[] args,object? result)
    {
        if(!CanRecord)return;
        try
        {
            if(instance!=null&&method.Name!="OnDisable"&&(instance is VRCenterAdjust||method.DeclaringType?.FullName?.Contains("PlayerTrack")==true||method.DeclaringType?.Name=="PlayerTransforms"||method.DeclaringType?.Name=="NoodlePlayerTransformManager"))Watch(instance);
            Record("lifecycle",new{phase,method=method.DeclaringType?.FullName+"::"+method.Name,
                instance=Snapshot(instance),args=args.Take(4).Select(Argument).ToArray(),result=result==null?null:Snapshot(result)});
            if(Thread.CurrentThread.ManagedThreadId==mainThread&&method.DeclaringType?.FullName=="NoodleExtensions.Animation.PlayerTrack"
                &&transitionSamples<TransitionSampleLimit
                &&(method.Name is "OnDidPauseEvent" or "OnDidResumeEvent"||phase=="after"&&method.Name=="AssignTrack"))
            {
                transitionSamples++;
                TakeSample("player-"+method.Name+"-"+phase);
            }
        }
        catch(Exception e){Record("snapshot.error",new{phase,method=method.Name,error=e.GetType().Name});}
    }
    static object? Read(object? value,string name)
    {
        if(value==null)return null;
        var key=(value.GetType(),name);
        MemberInfo? member;
        lock(members)
        {
            if(!members.TryGetValue(key,out member))
            {
                // Missing optional members are normal; Harmony lookup would log each miss.
                for(Type? type=key.Item1;type!=null&&member==null;type=type.BaseType)
                {
                    const BindingFlags flags=BindingFlags.Public|BindingFlags.NonPublic|BindingFlags.Instance|BindingFlags.DeclaredOnly;
                    member=type.GetField(name,flags);
                    if(member==null)
                    {
                        var property=type.GetProperty(name,flags);
                        if(property?.GetIndexParameters().Length==0)member=property;
                    }
                }
                members[key]=member;
            }
        }
        return member is FieldInfo field?field.GetValue(value):member is PropertyInfo propertyInfo?propertyInfo.GetValue(value):null;
    }
    static object? Simple(object? value)
    {
        if(value==null||value is string||value is bool||value is int||value is float||value is double)return value;
        if(value is Enum)return value.ToString();
        if(value is Vector3 v)return new[]{v.x,v.y,v.z};
        if(value is Quaternion q)return new[]{q.x,q.y,q.z,q.w};
        if(value.GetType().FullName=="Unity.Mathematics.float3")return new[]{Read(value,"x"),Read(value,"y"),Read(value,"z")};
        return null;
    }
    static object? Argument(object? value)
    {
        var simple=Simple(value);if(simple!=null||value==null)return simple;
        if(value is IList list&&list.Count<=12&&list.Cast<object?>().All(x=>x is string))return list.Cast<string>().ToArray();
        var custom=Read(value,"customData") as IDictionary;
        if(custom!=null)
        {
            var fields=new Dictionary<string,object?>();
            foreach(string key in new[]{"track","_track","target","_target","duration","_duration"})
                if(custom.Contains(key))fields[key]=Simple(custom[key])??(custom[key] is IList names?names.Cast<object?>().Take(4).Select(Simple).ToArray():null);
            return new{type=value.GetType().FullName,time=Read(value,"time"),eventType=Read(value,"eventType"),fields};
        }
        return new{type=value.GetType().FullName,id=RuntimeHelpers.GetHashCode(value)};
    }
    static object? Pose(Transform? transform)
    {
        if(transform==null)return null;
        var matrix=transform.localToWorldMatrix;
        var path=new List<object>();for(var cursor=transform;cursor!=null&&path.Count<6;cursor=cursor.parent)path.Add(new{name=cursor.name,id=cursor.GetInstanceID()});
        return new{id=transform.GetInstanceID(),scene=transform.gameObject.scene.name,active=transform.gameObject.activeInHierarchy,path,
            localPosition=Simple(transform.localPosition),localRotation=Simple(transform.localRotation),localScale=Simple(transform.localScale),
            world=new[]{matrix.m00,matrix.m01,matrix.m02,matrix.m03,matrix.m10,matrix.m11,matrix.m12,matrix.m13,matrix.m20,matrix.m21,matrix.m22,matrix.m23,matrix.m30,matrix.m31,matrix.m32,matrix.m33}};
    }
    static object? Snapshot(object? value)
    {
        if(value==null)return null;
        var fields=new Dictionary<string,object?>();
        foreach(string name in new[]{"_v2","_hasInitialized","_target","_startPos","_startLocalRot","_sceneType"})
        {var field=Read(value,name);if(field!=null)fields[name]=Simple(field);}
        var settings=Read(value,"_settingsManager");var room=Read(Read(settings,"settings"),"room");
        if(room!=null)fields["room"]=new{center=Simple(Read(room,"center")),rotation=Simple(Read(room,"rotation"))};
        var applicator=Read(value,"_applicator");if(applicator!=null)fields["applicator"]=applicator.GetType().FullName;
        if(Read(value,"roomTransformOffsetDidUpdateEvent") is Delegate subscribers)fields["roomSubscribers"]=subscribers.GetInvocationList().Length;
        var track=Read(value,"_track");
        if(track!=null)
        {
            var properties=new Dictionary<string,object?>();var map=Read(track,"_properties") as IDictionary;
            foreach(string name in new[]{"offsetPosition","offsetWorldRotation","position","localPosition","rotation","localRotation","scale"})
                if(map!=null&&map.Contains(name))properties[name]=Simple(Read(map[name],"Value"));
            fields["track"]=new{id=RuntimeHelpers.GetHashCode(track),properties};
        }
        object? pose=null;
        if(Thread.CurrentThread.ManagedThreadId==mainThread)
        {
            if(value is Component component){pose=Pose(component.transform);if(component is Behaviour behaviour)fields["enabled"]=behaviour.enabled;}
            else if(value is GameObject go)pose=Pose(go.transform);
            foreach(string name in new[]{"_rootTransform","_headTransform","_leftHandTransform","_rightHandTransform","_originTransform","_originParentTransform"})
                if(Read(value,name) is Transform t)fields[name]=Pose(t);
            if(Read(value,"_transformController") is Behaviour controller&&controller!=null)
                fields["controller"]=new{type=controller.GetType().FullName,id=controller.GetInstanceID(),enabled=controller.enabled,pose=Pose(controller.transform)};
        }
        return new{type=value.GetType().FullName,id=RuntimeHelpers.GetHashCode(value),fields,pose};
    }
    static void Watch(object instance)
    {
        foreach(var weak in watched)if(weak.TryGetTarget(out var target)&&ReferenceEquals(target,instance))return;
        if(watched.Count<32)watched.Add(new WeakReference<object>(instance));
    }
    static void ResetSamples()
    {
        samples?.Cancel();samples?.Dispose();samples=null;watched.Clear();transitionSamples=0;sampledNotes.Clear();noteSamples=0;notePoseSamples=0;rowPatchesSampled=false;
    }
    internal static void SongStart()
    {
        if(!CanRecord)return;
        try
        {
            CaptureRowPatches();
            TakeSample("song-start");
            samples?.Cancel();samples?.Dispose();samples=new CancellationTokenSource();
            SampleLater(250,AttemptId,samples.Token);SampleLater(1500,AttemptId,samples.Token);
        }
        catch(Exception e){Record("sample.error",new{error=e.GetType().Name});}
    }
    // Read patch metadata once after map construction, before music. This is
    // registration evidence, not an execution trace or a change to patch order.
    static void CaptureRowPatches()
    {
        if(!CanRecord||rowPatchesSampled)return;
        rowPatchesSampled=true;
        try
        {
            var processor=typeof(NoteData).Assembly.GetType("BeatmapObjectsInTimeRowProcessor");
            var rows=new List<object>();
            foreach(string name in new[]{"HandleCurrentTimeSliceAllNotesAndSlidersDidFinishTimeSlice","HandleCurrentTimeSliceColorNotesDidFinishTimeSlice"})
            {
                var method=processor?.GetMethod(name,BindingFlags.Public|BindingFlags.NonPublic|BindingFlags.Instance);
                var info=method==null?null:Harmony.GetPatchInfo(method);
                object[] Describe(IEnumerable<Patch>? values)=>values==null?Array.Empty<object>():values.Take(32).Select(p=>(object)new
                {
                    p.owner,p.priority,p.index,p.before,p.after,
                    assembly=p.PatchMethod.DeclaringType?.Assembly.GetName().Name,
                    type=p.PatchMethod.DeclaringType?.FullName,method=p.PatchMethod.Name
                }).ToArray();
                rows.Add(new{name,available=method!=null,prefixes=Describe(info?.Prefixes),postfixes=Describe(info?.Postfixes),transpilers=Describe(info?.Transpilers)});
            }
            Record("rows.patches",new{schema=1,rows,scope="registration metadata; no patch invocation or reordering"});
        }
        catch(Exception e){Record("rows.patches.unavailable",new{error=e.GetType().Name});}
    }
    static async void SampleLater(int delay,long id,CancellationToken cancellation)
    {
        try
        {
            await Task.Delay(delay,cancellation).ConfigureAwait(false);
            if(!active||id!=AttemptId||cancellation.IsCancellationRequested)return;
            await UnityMainThreadTaskScheduler.Factory.StartNew(()=>
            {
                if(active&&id==AttemptId&&!cancellation.IsCancellationRequested)TakeSample("song-start+"+delay+"ms");
            }).ConfigureAwait(false);
        }
        catch(OperationCanceledException){ }
        catch(Exception e){Record("sample.error",new{error=e.GetType().Name},id);}
    }
    static void TakeSample(string reason)
    {
        if(!CanRecord)return;
        var nodes=new List<object>();
        foreach(var weak in watched)
            if(weak.TryGetTarget(out var value))
            {
                try{if(value is UnityEngine.Object unity&&unity==null)continue;nodes.Add(Snapshot(value)!);}
                catch(Exception e){nodes.Add(new{type=value.GetType().FullName,error=e.GetType().Name});}
            }
        Record("pose.sample",new{reason,nodes});
        SampleNotes(reason);
    }
    internal static void Disable()
    {
        Record("diagnostics.disabled",new{hooks=installed.Count});active=false;
        try{patches?.UnpatchSelf();}catch(Exception e){Plugin.Log.Warn("Diagnostic unpatch failed: "+e.Message);}
        finally{ResetSamples();patches=null;installed.Clear();identified.Clear();lock(members)members.Clear();attemptData=null;map="";clock.Stop();}
    }
}
