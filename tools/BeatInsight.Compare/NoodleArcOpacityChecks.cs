using System.Collections;
using BeatInsight.Formats;
using System.Reflection;
using System.Runtime.Loader;
using Newtonsoft.Json.Linq;

namespace BeatInsight.Compare;

static class NoodleArcOpacityChecks
{
    public static object Run(string gameRoot,string[]? kinds=null,string[]? variants=null)
    {
        foreach(var pinned in new[]{("NoodleExtensions","FE50124E33631EED7ECB78EA1A19BB7EA64CC12974B0298DAD991672C2ABDA95"),("Heck","9E5163F09E122713543FB69F89799D1121FB712820E6B4350F7A30C5D790793C"),("CustomJSONData","4F9F8EFD8012159D8995B6D936E3C21B6AA6A35E319686FA3E9B04C2B6331F7C")})
            if(Comparison.HashFile(Path.Combine(gameRoot,"Plugins",pinned.Item1+".dll"))!=pinned.Item2)
                throw new InvalidDataException("Reinspect changed arc opacity consumer: "+pinned.Item1);
        const BindingFlags flags=BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic;
        var game=GameLoader.Load(gameRoot,"""{"version":"3.3.0","colorNotes":[],"sliders":[{"b":1,"x":0,"y":0,"c":0,"d":1,"tb":2,"tx":1,"ty":0,"tc":1}]}""",120,noodleExtensions:true);
        var slider=OracleBridge.GetItems(game).OfType<SliderData>().Single();
        var customAssembly=AssemblyLoadContext.Default.Assemblies.Single(a=>a.GetName().Name=="CustomJSONData");
        var custom=customAssembly.GetType("CustomJSONData.CustomBeatmap.CustomData",true)!;
        var noodle=AssemblyLoadContext.Default.Assemblies.Single(a=>a.GetName().Name=="NoodleExtensions");
        var objectType=noodle.GetType("NoodleExtensions.NoodleObjectData",true)!;
        var helperType=noodle.GetType("NoodleExtensions.Animation.AnimationHelper",true)!;
        var helper=Activator.CreateInstance(helperType,flags,null,new object[]{false},null)!;
        var offset=helperType.GetMethod("GetObjectOffset",flags)!;
        var easingVariants=new List<string>{"easeLinear"};
        foreach(string family in new[]{"Quad","Cubic","Quart","Quint","Sine","Circ","Expo","Bounce"})
            foreach(string mode in new[]{"In","Out","InOut"})easingVariants.Add("ease"+mode+family);
        var opacityVariants=new[]{"absent","empty","direct","direct-arrow","animated","animated-arrow","ramp"}
            .Concat(easingVariants).Concat(easingVariants.Select(e=>"arrow-"+e)).ToArray();
        var results=new List<object>();int cases=0,failed=0;
        foreach(string kind in kinds??new[]{"Slider"})foreach(int major in new[]{2,3})foreach(string variant in variants??opacityVariants)
        {
            var type=noodle.GetType("NoodleExtensions.Noodle"+(kind=="Bomb"?"Note":kind)+"Data",true)!;
            var ctor=type.GetConstructors(flags).Single();var parameters=ctor.GetParameters();
            var metadata=(IDictionary)Activator.CreateInstance(custom)!;
            metadata[major==2?"_position":"coordinates"]=new List<object>{-1f,.25f};
            metadata["tailCoordinates"]=new List<object>{.5f,1f};
            bool eased=variant.StartsWith("ease",StringComparison.Ordinal)||variant.StartsWith("arrow-ease",StringComparison.Ordinal);
            string easing=variant.StartsWith("arrow-",StringComparison.Ordinal)?variant.Substring(6):variant;
            string key=major==2?"_dissolve":"dissolve";
            if(variant.EndsWith("arrow",StringComparison.Ordinal)||variant.StartsWith("arrow-",StringComparison.Ordinal))key+="Arrow";
            if(variant=="direct"||variant=="direct-arrow")metadata[key]=new List<object>{new List<object>{.5f,0f}};
            if(variant=="animated"||variant=="animated-arrow"||variant=="ramp"||eased)
            {
                var animation=(IDictionary)Activator.CreateInstance(custom)!;
                animation[key]=eased?new List<object>{new List<object>{.5f,0f,"easeInQuad"},new List<object>{1f,1f,easing}}:variant=="ramp"?new List<object>{new List<object>{.5f,0f},new List<object>{1f,1f}}:new List<object>{.5f};
                metadata[major==2?"_animation":"animation"]=animation;
            }
            if(variant=="empty")metadata[major==2?"_animation":"animation"]=Activator.CreateInstance(custom)!;
            bool scale=variant=="identity-scale"||variant=="identity-scale-frames"||variant=="active-scale";
            if(scale||variant=="unused-identity-scale")
            {
                var animation=(IDictionary)Activator.CreateInstance(custom)!;
                bool unused=variant=="unused-identity-scale";bool old=unused?major!=2:major==2;
                float factor=variant=="active-scale"?2f:1f;
                animation[old?"_scale":"scale"]=variant=="identity-scale-frames"?
                    new List<object>{new List<object>{1f,1f,1f,0f},new List<object>{1f,1f,1f,1f}}:new List<object>{factor,factor,factor};
                metadata[old?"_animation":"animation"]=animation;
            }
            string before=JToken.FromObject(metadata).ToString();
            object item=slider;
            if(kind=="Obstacle")
            {
                var oc=customAssembly.GetType("CustomJSONData.CustomBeatmap.CustomObstacleData",true)!.GetConstructors().Single();
                item=oc.Invoke(new object[]{1f,2f,4f,0,0,Enum.ToObject(oc.GetParameters()[5].ParameterType,2),1f,4,3,metadata,new Version(major,0)});
            }
            if(kind=="Note"||kind=="Bomb")
            {
                var factory=customAssembly.GetType("CustomJSONData.CustomBeatmap.CustomNoteData",true)!.GetMethod("CreateCustomBasicNoteData")!;
                var p=factory.GetParameters();
                item=factory.Invoke(null,new object[]{1f,2f,0,0,Enum.ToObject(p[4].ParameterType,0),Enum.ToObject(p[5].ParameterType,kind=="Bomb"?-1:0),Enum.ToObject(p[6].ParameterType,kind=="Bomb"?9:1),metadata,new Version(major,0)})!;
            }
            var data=ctor.Invoke(new object[]{item,metadata,120f,Activator.CreateInstance(parameters[3].ParameterType)!,Activator.CreateInstance(parameters[4].ParameterType)!,major==2,false});
            var animationObject=objectType.GetProperty("AnimationObject",flags)!.GetValue(data);
            bool geometry=(float?)objectType.GetProperty("StartX",flags)!.GetValue(data)==-1f
                &&(float?)objectType.GetProperty("StartY",flags)!.GetValue(data)==.25f
                &&(kind!="Slider"||(float?)type.GetProperty("TailStartX",flags)!.GetValue(data)==.5f
                    &&(float?)type.GetProperty("TailStartY",flags)!.GetValue(data)==1f)
                &&objectType.GetProperty("Fake",flags)!.GetValue(data)==null
                &&objectType.GetProperty("Uninteractable",flags)!.GetValue(data)==null;
            foreach(bool mirror in variants==null?new[]{false}:new[]{false,true})
            foreach(float time in variants==null?new[]{0f,.25f,.5f,1f}:new[]{-1f,0f,.25f,.5f,1f,2f})
            {
                cases++;object?[] args=new object?[offset.GetParameters().Length];args[0]=animationObject;args[2]=time;
                var receiver=mirror?Activator.CreateInstance(helperType,flags,null,new object[]{true},null)!:helper;
                offset.Invoke(receiver,args);
                bool animated=variant=="animated"||variant=="animated-arrow"||variant=="ramp";
                float? expected=animated?(variant=="ramp"?.5f+.5f*time:.5f):null;
                bool arrow=variant=="animated-arrow"||variant.StartsWith("arrow-",StringComparison.Ordinal);
                if(eased)
                {
                    NoodleVectorCurve.TryRead(new JArray(new JArray(.5f,0,0,0,"easeInQuad"),new JArray(1,0,0,1,easing)),out var curve);
                    expected=curve!.At(time).X;
                }
                bool offsets=args[3]==null&&args[4]==null&&args[6]==null;
                if(scale)
                {
                    object? vector=args[5];float expectedScale=variant=="active-scale"?2f:1f;
                    offsets=offsets&&vector!=null&&new[]{"x","y","z"}.All(axis=>(float)vector.GetType().GetField(axis)!.GetValue(vector)! == (mirror&&axis=="x"?-expectedScale:expectedScale));
                }
                else offsets=offsets&&args[5]==null;
                bool pass=geometry&&before==JToken.FromObject(metadata).ToString()&&offsets
                    &&args[9]==null&&(args[arrow?8:7] is float actual&&expected.HasValue?Math.Abs(actual-expected.Value)<.00001f:Equals(args[arrow?8:7],expected))&&args[arrow?7:8]==null;
                var scaleOffset=args[5]==null?null:new[]{"x","y","z"}.Select(axis=>(float)args[5]!.GetType().GetField(axis)!.GetValue(args[5])!).ToArray();
                if(!pass)failed++;results.Add(new {kind,major,variant,mirror,time,expected,scaleOffset,nominalCoordinatesUnchanged=geometry,offsetsMatch=offsets,negativeControl=variants!=null&&(variant=="active-scale"||kind!="Obstacle"&&variant!="unused-identity-scale"),pass});
            }
        }
        return new {schemaVersion=1,profileVersion=BeatInsight.Formats.LocalMap.AnalysisProfileVersion,cases,failed,results,gameStarted=false,referenceUsed=false,
            scope=variants==null?"installed Noodle object constructors, real scalar point parsing/interpolation and AnimationHelper; direct opacity is unused, local opacity affects only visual outputs; no Unity rendering/copy pipeline":
                "installed constructors and Vector3 interpolation, both handedness modes; canonical unit scale yields signed X reflection when mirrored; wall coverage admitted only with separate composition proof; note/bomb/arc canonical scale remains unsupported; no Unity lifecycle"};
    }
}
