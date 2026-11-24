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
    static CancellationTokenSource? samples;
    internal static long AttemptId=>Interlocked.Read(ref attempt);
    internal static bool CanRecord=>active&&Plugin.Config.DiagnosticLogging&&Volatile.Read(ref emitted)<Limit;

    internal static void Enable()
    {
        active=true;mainThread=Thread.CurrentThread.ManagedThreadId;session=Guid.NewGuid().ToString("N");
        clock.Restart();patches=new Harmony("arevu.beatinsight.diagnostics");
        Record("diagnostics.enabled",new{schema=1,limit=Limit,transitionSampleLimit=TransitionSampleLimit,assembly=typeof(Plugin).Assembly.GetName().Version?.ToString()});
        Identify(typeof(Plugin).Assembly);Discover();
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
        samples?.Cancel();samples?.Dispose();samples=null;watched.Clear();transitionSamples=0;
    }
    internal static void SongStart()
    {
        if(!CanRecord)return;
        try
        {
            TakeSample("song-start");
            samples?.Cancel();samples?.Dispose();samples=new CancellationTokenSource();
            SampleLater(250,AttemptId,samples.Token);SampleLater(1500,AttemptId,samples.Token);
        }
        catch(Exception e){Record("sample.error",new{error=e.GetType().Name});}
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
    }
    internal static void Disable()
    {
        Record("diagnostics.disabled",new{hooks=installed.Count});active=false;
        try{patches?.UnpatchSelf();}catch(Exception e){Plugin.Log.Warn("Diagnostic unpatch failed: "+e.Message);}
        finally{ResetSamples();patches=null;installed.Clear();identified.Clear();lock(members)members.Clear();attemptData=null;map="";clock.Stop();}
    }
}
