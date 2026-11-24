using System.Collections;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.Loader;
using BeatInsight.Formats;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace BeatInsight.Compare;

// Independent diagnostic model; never authorizes geometry in the menu.
static class NoodlePathAnimationChecks
{
    sealed class Model
    {
        public NoodleVectorCurve? Current,Previous;public float Time;
        public void Init(NoodleVectorCurve? next){Previous=Current;Current=next;Time=0;}
        public void Finish()=>Previous=null;
        public NoodleVectorCurve.Value? At(float local)
        {
            if(Current==null)return null;
            var b=Current.At(local);if(Previous==null)return b;
            var a=Previous.At(local);return new(a.X+(b.X-a.X)*Time,a.Y+(b.Y-a.Y)*Time,a.Z+(b.Z-a.Z)*Time);
        }
    }
    public static object Run(string gameRoot)
    {
        foreach(var pin in new[]{("Plugins/Heck.dll","9E5163F09E122713543FB69F89799D1121FB712820E6B4350F7A30C5D790793C"),
            ("Plugins/NoodleExtensions.dll","FE50124E33631EED7ECB78EA1A19BB7EA64CC12974B0298DAD991672C2ABDA95"),
            ("Beat Saber_Data/Managed/Main.dll","A276C8A50ACFAD69A3DD3A82CDE08571C3262344702B9B014C89756771B4495E")})
            if(Comparison.HashFile(Path.Combine(gameRoot,pin.Item1))!=pin.Item2)throw new InvalidDataException("Reinspect changed path animation provider");
        const BindingFlags flags=BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic;
        var heck=AssemblyLoadContext.Default.LoadFromAssemblyPath(Path.Combine(gameRoot,"Plugins/Heck.dll"));
        var noodle=AssemblyLoadContext.Default.LoadFromAssemblyPath(Path.Combine(gameRoot,"Plugins/NoodleExtensions.dll"));
        var main=AssemblyLoadContext.Default.LoadFromAssemblyPath(Path.Combine(gameRoot,"Beat Saber_Data/Managed/Main.dll"));
        var pathType=heck.GetType("Heck.Animation.PathProperty`1",true)!.MakeGenericType(typeof(Vector3));
        var propertyType=heck.GetType("Heck.Animation.Property`1",true)!.MakeGenericType(typeof(Vector3));
        var curveType=heck.GetType("Heck.Animation.Vector3PointDefinition",true)!;
        var trackType=heck.GetType("Heck.Animation.Track",true)!;
        var helperType=noodle.GetType("NoodleExtensions.Animation.AnimationHelper",true)!;
        var offset=helperType.GetMethod("GetObjectOffset",flags)!;
        var ownerType=heck.GetType("Heck.Animation.Events.CoroutineEvent",true)!;
        var owner=RuntimeHelpers.GetUninitializedObject(ownerType);
        var callbacksType=main.GetType("BeatmapCallbacksController",true)!;
        var callbacks=RuntimeHelpers.GetUninitializedObject(callbacksType);
        var songTime=callbacksType.GetField("_songTime",flags)!;
        ownerType.GetField("_beatmapCallbacksController",flags)!.SetValue(owner,callbacks);
        var coroutine=ownerType.GetMethod("AssignPathAnimationCoroutine",flags)!;
        var animate=ownerType.GetMethod("AnimateTrackCoroutine",flags)!;
        var names=new List<string>{"easeLinear"};
        foreach(string family in new[]{"Quad","Cubic","Quart","Quint","Sine","Circ","Expo","Bounce"})
            foreach(string mode in new[]{"In","Out","InOut"})names.Add("ease"+mode+family);
        var pointsA=JArray.Parse("[[1,2,3,0],[3,4,5,1]]");
        var pointsB=JArray.Parse("[[2,1,3,0],[1,1,1,0.375,\"easeOutCubic\"]]");
        var pointsC=JArray.Parse("[[4,5,6,0],[1,2,3,1]]");
        NoodleVectorCurve.TryRead(pointsA,out var ownA);NoodleVectorCurve.TryRead(pointsB,out var ownB);NoodleVectorCurve.TryRead(pointsC,out var ownC);
        object Convert(JToken token)=>token is JArray a?a.Select(Convert).ToList():token.Type==JTokenType.String?(object)token.Value<string>()!:(float)token;
        object Native(JArray points)=>Activator.CreateInstance(curveType,flags,null,new[]{Convert(points)},null)!;
        var nativeA=Native(pointsA);var nativeB=Native(pointsB);var nativeC=Native(pointsC);
        int cases=0,samples=0,failed=0,interruptions=0,compositions=0;var failures=new List<object>();
        int scalarPathCases=0;
        var scalarPathType=heck.GetType("Heck.Animation.PathProperty`1",true)!.MakeGenericType(typeof(float));
        var scalarCurveType=heck.GetType("Heck.Animation.FloatPointDefinition",true)!;
        var scalarRamp=Activator.CreateInstance(scalarCurveType,flags,null,new object[]{new List<object>{new List<object>{0f,0f},new List<object>{1f,1f}}},null)!;
        var scalarOne=Activator.CreateInstance(scalarCurveType,flags,null,new object[]{new List<object>{1f}},null)!;
        foreach(string key in new[]{"dissolve","dissolveArrow"})foreach(bool mirror in new[]{false,true})foreach(float blend in new[]{0f,.5f,1f})
        {
            var property=Activator.CreateInstance(scalarPathType,true)!;var interpolation=scalarPathType.GetProperty("Interpolation",flags)!.GetValue(property)!;
            var t=interpolation.GetType();t.GetMethod("Init",flags)!.Invoke(interpolation,new[]{scalarOne});t.GetMethod("Finish",flags)!.Invoke(interpolation,null);
            t.GetMethod("Init",flags)!.Invoke(interpolation,new[]{scalarRamp});t.GetProperty("Time",flags)!.SetValue(interpolation,blend);
            var track=Activator.CreateInstance(trackType,true)!;((IDictionary)trackType.GetField("_pathProperties",flags)!.GetValue(track)!)[key]=property;
            var tracks=Array.CreateInstance(trackType,1);tracks.SetValue(track,0);var helper=Activator.CreateInstance(helperType,flags,null,new object[]{mirror},null)!;
            foreach(float local in new[]{0f,.25f,.5f,.75f,1f})
            {
                float expected=1+(local-1)*blend;object?[] args=new object?[offset.GetParameters().Length];args[1]=tracks;args[2]=local;offset.Invoke(helper,args);
                int output=key=="dissolve"?7:8;bool pass=args[output] is float actual&&Math.Abs(actual-expected)<1e-5&&Enumerable.Range(3,7).Where(i=>i!=output).All(i=>args[i]==null);
                cases++;scalarPathCases++;if(!pass){failed++;if(failures.Count<12)failures.Add(new{kind="scalar-path-opacity",key,mirror,blend,local,expected});}
            }
        }
        foreach(string key in new[]{"offsetPosition","scale"})foreach(bool mirror in new[]{false,true})
        foreach(bool initial in new[]{false,true})foreach(float duration in new[]{.25f,.8f,2.5f})
        foreach(bool jump in new[]{false,true})foreach(string easingName in names)
        {
            var property=Activator.CreateInstance(pathType,true)!;
            var interpolation=pathType.GetProperty("Interpolation",flags)!.GetValue(property)!;
            var type=interpolation.GetType();var init=type.GetMethod("Init",flags)!;var finish=type.GetMethod("Finish",flags)!;
            var interpolate=type.GetMethod("Interpolate",flags)!;var timeProperty=type.GetProperty("Time",flags)!;
            var model=new Model();
            if(!initial){init.Invoke(interpolation,new[]{nativeA});finish.Invoke(interpolation,null);model.Init(ownA);model.Finish();}
            init.Invoke(interpolation,new[]{nativeB});model.Init(ownB);
            var track=Activator.CreateInstance(trackType,true)!;
            ((IDictionary)trackType.GetField("_pathProperties",flags)!.GetValue(track)!)[key]=property;
            var tracks=Array.CreateInstance(trackType,1);tracks.SetValue(track,0);
            var helper=Activator.CreateInstance(helperType,flags,null,new object[]{mirror},null)!;
            NoodleVectorCurve.TryRead(new JArray(new JArray(0,0,0,0),new JArray(1,0,0,1,easingName)),out var easing);
            float start=3.25f;bool done=false,reachedEnd=false,casePass=true;
            var iterator=(IEnumerator)coroutine.Invoke(owner,new object[]{interpolation,duration,start,Enum.Parse(coroutine.GetParameters()[3].ParameterType,easingName)})!;
            var animatedProperty=Activator.CreateInstance(propertyType,true)!;
            var propertyMap=(IDictionary)trackType.GetField("_properties",flags)!.GetValue(track)!;
            var animated=(IEnumerator)animate.Invoke(owner,new object[]{nativeC,animatedProperty,track,duration,start,
                Enum.Parse(animate.GetParameters()[5].ParameterType,easingName),0,true})!;
            foreach(float fraction in jump?new[]{0f,.2f,1.7f,2f}:new[]{0f,.1f,.375f,.5f,.9f,1f,1.1f,2f})
            {
                float time=start+fraction*duration;songTime.SetValue(callbacks,time);
                animated.MoveNext();
                var animatedExpected=ownC!.At(easing!.At(Math.Min((time-start)/duration,1)).X);
                bool running=iterator.MoveNext();bool expectedRunning=!done&&!reachedEnd;
                if(!done)
                {
                    if(reachedEnd){model.Finish();done=true;}
                    else {float elapsed=time-start;model.Time=easing!.At(Math.Min(elapsed/duration,1)).X;reachedEnd=elapsed>=duration;}
                }
                casePass&=running==expectedRunning&&Math.Abs((float)timeProperty.GetValue(interpolation)!-model.Time)<1e-5;
                foreach(float local in new[]{-1f,0f,.125f,.375f,.5f,1f,2f})
                {
                    var expected=model.At(local)!.Value;var actual=(Vector3)interpolate.Invoke(interpolation,new object[]{local})!;
                    object?[] args=new object?[offset.GetParameters().Length];args[1]=tracks;args[2]=local;offset.Invoke(helper,args);
                    int output=key=="scale"?5:3;float factor=key=="scale"?1:.6f;
                    var rendered=(Vector3)args[output]!;
                    bool pass=Math.Abs(actual.x-expected.X)<1e-5&&Math.Abs(actual.y-expected.Y)<1e-5&&Math.Abs(actual.z-expected.Z)<1e-5
                        &&Math.Abs(rendered.x-(mirror?-expected.X:expected.X)*factor)<1e-5
                        &&Math.Abs(rendered.y-expected.Y*factor)<1e-5&&Math.Abs(rendered.z-expected.Z*factor)<1e-5
                        &&Enumerable.Range(3,7).Where(i=>i!=output).All(i=>args[i]==null);
                    propertyMap[key]=animatedProperty;
                    object?[] combined=new object?[offset.GetParameters().Length];combined[1]=tracks;combined[2]=local;offset.Invoke(helper,combined);
                    propertyMap.Remove(key);
                    var combinedActual=(Vector3)combined[output]!;
                    float Combine(float a,float b)=>key=="scale"?a*b:(a+b)*.6f;
                    bool compositionPass=Math.Abs(combinedActual.x-(mirror?-1:1)*Combine(expected.X,animatedExpected.X))<2e-5
                        &&Math.Abs(combinedActual.y-Combine(expected.Y,animatedExpected.Y))<2e-5
                        &&Math.Abs(combinedActual.z-Combine(expected.Z,animatedExpected.Z))<2e-5
                        &&Enumerable.Range(3,7).Where(i=>i!=output).All(i=>combined[i]==null);
                    if(!compositionPass&&failures.Count<10)failures.Add(new{stage="composition",key,mirror,initial,duration,jump,easingName,time,local});
                    compositions++;casePass&=compositionPass;
                    samples++;casePass&=pass;
                    if(!pass&&failures.Count<10)failures.Add(new{key,mirror,initial,duration,jump,easingName,time,local});
                }
            }
            // Replacing an unfinished blend keeps the previous target curve,
            // not a snapshot of its currently blended output.
            init.Invoke(interpolation,new[]{nativeA});init.Invoke(interpolation,new[]{nativeB});timeProperty.SetValue(interpolation,.25f);
            init.Invoke(interpolation,new[]{nativeC});model.Init(ownB);model.Init(ownC);
            var interrupted=(Vector3)interpolate.Invoke(interpolation,new object[]{.5f})!;
            var expectedInterrupted=ownB!.At(.5f);
            casePass&=Math.Abs(interrupted.x-expectedInterrupted.X)<1e-5&&Math.Abs(interrupted.y-expectedInterrupted.Y)<1e-5&&Math.Abs(interrupted.z-expectedInterrupted.Z)<1e-5;
            init.Invoke(interpolation,new object?[]{null});casePass&=interpolate.Invoke(interpolation,new object[]{.5f})==null;
            finish.Invoke(interpolation,null);casePass&=interpolate.Invoke(interpolation,new object[]{.5f})==null;
            init.Invoke(interpolation,new[]{nativeA});var restored=(Vector3)interpolate.Invoke(interpolation,new object[]{.5f})!;
            casePass&=Math.Abs(restored.x-ownA!.At(.5f).X)<1e-5&&Math.Abs(restored.y-ownA.At(.5f).Y)<1e-5&&Math.Abs(restored.z-ownA.At(.5f).Z)<1e-5;
            interruptions++;cases++;if(!casePass)failed++;
        }
        int neutralSamples=0;
        foreach(string key in new[]{"offsetPosition","scale"})foreach(bool mirror in new[]{false,true})
        foreach(float blend in new[]{-.25f,0f,.125f,.5f,1f,1.25f})foreach(float local in new[]{.5f,.75f,1f})
        {
            float end=key=="scale"?1:0;
            var a=new JArray(new JArray(1,2,3,0),new JArray(end,end,end,.25));
            var b=new JArray(new JArray(4,5,6,0),new JArray(end,end,end,.49));
            var property=Activator.CreateInstance(pathType,true)!;
            var interpolation=pathType.GetProperty("Interpolation",flags)!.GetValue(property)!;
            var type=interpolation.GetType();
            type.GetMethod("Init",flags)!.Invoke(interpolation,new[]{Native(a)});
            type.GetMethod("Init",flags)!.Invoke(interpolation,new[]{Native(b)});
            type.GetProperty("Time",flags)!.SetValue(interpolation,blend);
            var track=Activator.CreateInstance(trackType,true)!;
            ((IDictionary)trackType.GetField("_pathProperties",flags)!.GetValue(track)!)[key]=property;
            var tracks=Array.CreateInstance(trackType,1);tracks.SetValue(track,0);
            var helper=Activator.CreateInstance(helperType,flags,null,new object[]{mirror},null)!;
            object?[] args=new object?[offset.GetParameters().Length];args[1]=tracks;args[2]=local;offset.Invoke(helper,args);
            var actual=(Vector3)args[key=="scale"?5:3]!;
            bool pass=actual.x==(mirror?-end:end)&&actual.y==end&&actual.z==end;
            cases++;neutralSamples++;if(!pass)failed++;
        }
        return new{schemaVersion=1,cases,scalarPathCases,samples,compositions,interruptions,neutralSamples,failed,failures,gameStarted=false,referenceUsed=false,
            scope="actual AssignPathAnimation coroutine, Init/Finish and vector transition, path property binding and AnimationHelper at independent song/object times, actual AnimateTrack composition; cached parsed vector definitions, replacement/clear/restore and both hands; JSON event parsing, dispatch ordering, quaternion transitions and Unity lifecycle not executed; diagnostic only"};
    }
}
