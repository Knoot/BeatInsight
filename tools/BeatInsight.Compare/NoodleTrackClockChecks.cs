using System.Collections;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.Loader;
using BeatInsight.Formats;
using Mono.Cecil;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace BeatInsight.Compare;

// Diagnostic state model only: does not authorize track geometry in map analysis.
static class NoodleTrackClockChecks
{
    sealed class Clock
    {
        float start;readonly float duration;int repeats;bool skip,done;readonly bool nonLazy;
        readonly NoodleVectorCurve points,easing;public NoodleVectorCurve.Value Value;
        public Clock(float start,float duration,int repeats,bool nonLazy,NoodleVectorCurve points,NoodleVectorCurve easing)
        {this.start=start;this.duration=duration;this.repeats=repeats;this.nonLazy=nonLazy;this.points=points;this.easing=easing;}
        public bool Step(float songTime,float lastFrame)
        {
            if(done)return false;
            while(repeats>=0)
            {
                float elapsed=songTime-start;
                if(!skip)
                {
                    float progress=Math.Min(elapsed/duration,1);
                    float eased=easing.At(progress).X;
                    Value=points.At(eased);
                    skip=!nonLazy&&eased>=lastFrame;
                }
                if(elapsed<duration)
                {
                    if(repeats==0&&skip){done=true;return false;}
                    return true;
                }
                repeats--;start+=duration;skip=false;
            }
            done=true;return false;
        }
    }
    public static object Run(string gameRoot)
    {
        foreach(var pin in new[]{("Plugins/Heck.dll","9E5163F09E122713543FB69F89799D1121FB712820E6B4350F7A30C5D790793C"),
            ("Beat Saber_Data/Managed/Main.dll","A276C8A50ACFAD69A3DD3A82CDE08571C3262344702B9B014C89756771B4495E")})
            if(Comparison.HashFile(Path.Combine(gameRoot,pin.Item1))!=pin.Item2)throw new InvalidDataException("Reinspect changed event clock provider");
        const BindingFlags flags=BindingFlags.Public|BindingFlags.NonPublic|BindingFlags.Instance;
        var heck=AssemblyLoadContext.Default.LoadFromAssemblyPath(Path.Combine(gameRoot,"Plugins/Heck.dll"));
        var ownerType=heck.GetType("Heck.Animation.Events.CoroutineEvent",true)!;
        var callbacksType=AssemblyLoadContext.Default.LoadFromAssemblyPath(Path.Combine(gameRoot,"Beat Saber_Data/Managed/Main.dll")).GetType("BeatmapCallbacksController",true)!;
        var callbacks=RuntimeHelpers.GetUninitializedObject(callbacksType);
        var songTimeField=callbacksType.GetField("_songTime",flags)!;
        var owner=RuntimeHelpers.GetUninitializedObject(ownerType);
        ownerType.GetField("_beatmapCallbacksController",flags)!.SetValue(owner,callbacks);
        var coroutine=ownerType.GetMethod("AnimateTrackCoroutine",flags)!;
        var propertyType=heck.GetType("Heck.Animation.Property`1",true)!.MakeGenericType(typeof(Vector3));
        var trackType=heck.GetType("Heck.Animation.Track",true)!;
        var curveType=heck.GetType("Heck.Animation.Vector3PointDefinition",true)!;
        var functionType=heck.GetType("Heck.Animation.Functions",true)!;
        var names=new List<string>{"easeLinear"};
        foreach(string family in new[]{"Quad","Cubic","Quart","Quint","Sine","Circ","Expo","Bounce"})
            foreach(string mode in new[]{"In","Out","InOut"})names.Add("ease"+mode+family);
        object Convert(JToken token)=>token is JArray a?a.Select(Convert).ToList():token.Type==JTokenType.String?(object)token.Value<string>()!:(float)token;
        int cases=0,steps=0,failed=0;var failures=new List<object>();
        foreach(float duration in new[]{.25f,.8f,2.5f})foreach(float start in new[]{0f,3.25f})
        foreach(int repeat in new[]{0,1,2})foreach(bool nonLazy in new[]{false,true})
        foreach(bool early in new[]{false,true})foreach(string easingName in names)foreach(bool jump in new[]{false,true})
        {
            float last=early?.375f:1;
            var points=new JArray(new JArray(0,1,-2,0),new JArray(2,3,0,last,"easeOutSine"));
            NoodleVectorCurve.TryRead(points,out var ownPoints);
            NoodleVectorCurve.TryRead(new JArray(new JArray(0,0,0,0),new JArray(1,0,0,1,easingName)),out var ownEasing);
            var clock=new Clock(start,duration,repeat,nonLazy,ownPoints!,ownEasing!);
            var property=Activator.CreateInstance(propertyType,true)!;
            var nativePoints=Activator.CreateInstance(curveType,flags,null,new[]{Convert(points)},null)!;
            var track=Activator.CreateInstance(trackType,true)!;
            var iterator=(IEnumerator)coroutine.Invoke(owner,new object[]{nativePoints,property,track,duration,start,Enum.Parse(functionType,easingName),repeat,nonLazy})!;
            float[] sample=jump?new[]{0f,.1f,1.8f,3.5f,4f}:new[]{0f,.1f,.375f,.5f,.9f,1f,1.1f,1.5f,2f,2.1f,2.5f,3f,4f};
            bool casePass=true;
            foreach(float fraction in sample)
            {
                float time=start+fraction*duration;songTimeField.SetValue(callbacks,time);
                bool running=iterator.MoveNext(),expectedRunning=clock.Step(time,last);
                var actual=(Vector3)propertyType.GetProperty("Value",flags)!.GetValue(property)!;
                var expected=clock.Value;
                bool pass=running==expectedRunning&&Math.Abs(actual.x-expected.X)<1e-5&&Math.Abs(actual.y-expected.Y)<1e-5&&Math.Abs(actual.z-expected.Z)<1e-5;
                steps++;casePass&=pass;
                if(!pass&&failures.Count<15)failures.Add(new{duration,start,repeat,nonLazy,early,easingName,jump,time,running,expectedRunning,actual=new[]{actual.x,actual.y,actual.z},expected=new[]{expected.X,expected.Y,expected.Z}});
            }
            cases++;if(!casePass)failed++;
        }
        int settledSamples=0;
        var settledPoints=JArray.Parse("[[0,1,0,0.125],[0,0,0,0.374]]");
        NoodleVectorCurve.TryRead(settledPoints,out var ownSettled);
        foreach(float bpm in new[]{90f,120f,260f})foreach(float eventBeat in new[]{0f,331.583f})
        foreach(float progress in new[]{.25f,.5f,.573f,.9f,1.25f,3f})foreach(bool delayed in new[]{false,true})
        foreach(string easingName in names.Where(n=>!n.EndsWith("Bounce",StringComparison.Ordinal)))
        {
            float start=eventBeat/bpm*60,duration=60*1.6f/bpm;
            float time=start+duration*progress;
            var property=Activator.CreateInstance(propertyType,true)!;
            var points=Activator.CreateInstance(curveType,flags,null,new[]{Convert(settledPoints)},null)!;
            var track=Activator.CreateInstance(trackType,true)!;
            var iterator=(IEnumerator)coroutine.Invoke(owner,new object[]{points,property,track,duration,start,Enum.Parse(functionType,easingName),0,false})!;
            if(!delayed){songTimeField.SetValue(callbacks,start);iterator.MoveNext();}
            songTimeField.SetValue(callbacks,time);iterator.MoveNext();
            var actual=(Vector3)propertyType.GetProperty("Value",flags)!.GetValue(property)!;
            float normalized=(time-start)/duration;
            NoodleVectorCurve.TryRead(new JArray(new JArray(0,0,0,0),new JArray(1,0,0,1,easingName)),out var easing);
            float eased=easing!.At(Math.Min(normalized,1)).X;
            bool admitted=ownSettled!.IsSettledZeroAt(easingName=="easeLinear"?eased:eased-.00001f);
            bool pass=admitted?actual.x==0&&actual.y==0&&actual.z==0:actual.y>0;
            cases++;settledSamples++;if(!pass)failed++;
        }
        using var module=ModuleDefinition.ReadModule(Path.Combine(gameRoot,"Plugins/Heck.dll"));
        var startMethod=module.Types.Single(t=>t.Name=="CoroutineEvent").Methods.Single(m=>m.Name=="StartEventCoroutine");
        var instructions=startMethod.Body.Instructions;
        var durationCall=instructions.Single(i=>i.Operand is MethodReference m&&m.Name=="get_Duration");
        if(durationCall.Previous.Previous.OpCode.Code!=Mono.Cecil.Cil.Code.Ldc_R4||(float)durationCall.Previous.Previous.Operand!=60
            ||durationCall.Next.OpCode.Code!=Mono.Cecil.Cil.Code.Mul
            ||!instructions.Any(i=>i.Operand is MethodReference m&&m.Name=="get_currentBpm"&&i.Next.OpCode.Code==Mono.Cecil.Cil.Code.Div))throw new InvalidDataException("Duration conversion changed");
        return new{schemaVersion=1,cases,steps,settledSamples,failed,failures,gameStarted=false,referenceUsed=false,
            scope="actual AnimateTrack coroutine driven manually by controlled callback songTime, real vector parsing/easing/property updates, repeated and late frames; duration conversion IL audited; dispatch/cancellation, zero-duration fast path, BPM changes, path bindings and Unity lifecycle not executed; diagnostic model only"};
    }
}
