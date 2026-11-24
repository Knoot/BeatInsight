using System.Collections;
using System.Reflection;
using System.Runtime.Loader;
using BeatInsight.Formats;
using Mono.Cecil;
using Newtonsoft.Json.Linq;

namespace BeatInsight.Compare;

static class NoodlePositionChecks
{
    public static object Run(string gameRoot)
    {
        foreach(var pin in new[]{("NoodleExtensions","FE50124E33631EED7ECB78EA1A19BB7EA64CC12974B0298DAD991672C2ABDA95"),
            ("Heck","9E5163F09E122713543FB69F89799D1121FB712820E6B4350F7A30C5D790793C"),
            ("CustomJSONData","4F9F8EFD8012159D8995B6D936E3C21B6AA6A35E319686FA3E9B04C2B6331F7C")})
            if(Comparison.HashFile(Path.Combine(gameRoot,"Plugins",pin.Item1+".dll"))!=pin.Item2)throw new InvalidDataException("Reinspect changed position consumer: "+pin.Item1);
        GameLoader.Load(gameRoot,"""{"version":"3.3.0","colorNotes":[],"obstacles":[]}""",120,noodleExtensions:true);
        const BindingFlags flags=BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic;
        var custom=AssemblyLoadContext.Default.Assemblies.Single(a=>a.GetName().Name=="CustomJSONData");
        var metadataType=custom.GetType("CustomJSONData.CustomBeatmap.CustomData",true)!;
        var extension=AssemblyLoadContext.Default.Assemblies.Single(a=>a.GetName().Name=="NoodleExtensions");
        var baseType=extension.GetType("NoodleExtensions.NoodleObjectData",true)!;
        var helperType=extension.GetType("NoodleExtensions.Animation.AnimationHelper",true)!;
        var getOffset=helperType.GetMethod("GetObjectOffset",flags)!;
        var curves=new List<(string Name,JArray Points)>{("zero",JArray.Parse("[0,0,0]")),("zero-frames",JArray.Parse("[[0,0,0,0],[0,0,0,1]]")),
            ("entry-y",JArray.Parse("[[0,0.8,0,0],[0,0,0,0.4]]")),("entry-xyz",JArray.Parse("[[1,2,-3,0],[0,0,0,0.25]]")),
            ("late",JArray.Parse("[[0,1,0,0],[0,0,0,0.75]]")),("constant",JArray.Parse("[1,2,3]")),
            ("linear",JArray.Parse("[[1,2,3,0],[3,4,5,1]]")),("multi",JArray.Parse("[[1,0,0,0],[0,1,0,0.2],[0,0,0,0.4]]"))};
        var easings=new List<string>{"easeLinear"};
        foreach(string family in new[]{"Quad","Cubic","Quart","Quint","Sine","Circ","Expo","Bounce"})
            foreach(string mode in new[]{"In","Out","InOut"})easings.Add("ease"+mode+family);
        foreach(string easing in easings)
        {
            curves.Add((easing,new JArray(new JArray(1,2,-3,0),new JArray(3,4,5,1,easing))));
            curves.Add((easing+"-entry",new JArray(new JArray(1,2,-3,0),new JArray(0,0,0,.4f,easing))));
        }
        curves.Add(("destination-easing",JArray.Parse("[[1,2,-3,0,\"easeInQuad\"],[3,4,5,1,\"easeOutCubic\"]]")));
        object ConvertPoint(JToken token)=>token is JArray array?array.Select(ConvertPoint).ToList():token.Type==JTokenType.String?(object)token.Value<string>()!:(float)token;
        var results=new List<object>();int cases=0,failed=0;
        foreach(string kind in new[]{"Note","Bomb","Obstacle","Slider"})foreach(int major in new[]{2,3})foreach(var (name,points) in curves)foreach(bool named in new[]{false,true})
        {
            if(!NoodleVectorCurve.TryRead(points,out var own))throw new InvalidDataException("Valid controlled curve rejected");
            var metadata=Activator.CreateInstance(metadataType)!;var animation=Activator.CreateInstance(metadataType)!;
            ((IDictionary)animation)[major==2?"_position":"offsetPosition"]=named?(object)"curve":ConvertPoint(points);
            ((IDictionary)metadata)[major==2?"_animation":"animation"]=animation;
            var type=extension.GetType("NoodleExtensions.Noodle"+(kind=="Bomb"?"Note":kind)+"Data",true)!;
            var ctor=type.GetConstructors(flags).Single();var parameters=ctor.GetParameters();object item;
            if(kind=="Obstacle")
            {
                var factory=custom.GetType("CustomJSONData.CustomBeatmap.CustomObstacleData",true)!.GetConstructors().Single();
                item=factory.Invoke(new object[]{1f,2f,4f,0,0,Enum.ToObject(factory.GetParameters()[5].ParameterType,2),1f,4,3,metadata,new Version(major,0)});
            }
            else if(kind=="Slider")
            {
                var map=GameLoader.Load(gameRoot,"""{"version":"3.3.0","colorNotes":[],"sliders":[{"b":1,"x":0,"y":0,"c":0,"d":1,"tb":2,"tx":1,"ty":0,"tc":1}]}""",120,noodleExtensions:true);
                item=OracleBridge.GetItems(map).OfType<SliderData>().Single();
            }
            else
            {
                var factory=custom.GetType("CustomJSONData.CustomBeatmap.CustomNoteData",true)!.GetMethod("CreateCustomBasicNoteData")!;var p=factory.GetParameters();
                item=factory.Invoke(null,new object[]{1f,2f,0,0,Enum.ToObject(p[4].ParameterType,0),Enum.ToObject(p[5].ParameterType,kind=="Bomb"?-1:0),
                    Enum.ToObject(p[6].ParameterType,kind=="Bomb"?9:1),metadata,new Version(major,0)})!;
            }
            var definitions=(IDictionary)Activator.CreateInstance(parameters[3].ParameterType)!;
            definitions["curve"]=ConvertPoint(points);definitions["Curve"]=ConvertPoint(new JArray(9,9,9));
            var parsed=ctor.Invoke(new object[]{item,metadata,120f,definitions,Activator.CreateInstance(parameters[4].ParameterType)!,major==2,false});
            var objectAnimation=baseType.GetProperty("AnimationObject",flags)!.GetValue(parsed);
            foreach(bool mirror in new[]{false,true})foreach(float time in new[]{-1f,0f,.1f,.2f,.25f,.4f,.49f,.5f,1f,2f})
            {
                cases++;var helper=Activator.CreateInstance(helperType,flags,null,new object[]{mirror},null)!;
                object?[] args=new object?[getOffset.GetParameters().Length];args[0]=objectAnimation;args[2]=time;getOffset.Invoke(helper,args);
                var vector=args[3];var expected=own!.At(time);
                float Read(string axis)=>(float)vector!.GetType().GetField(axis)!.GetValue(vector)!;
                bool pass=vector!=null&&Math.Abs(Read("x")-(mirror?-expected.X:expected.X)*.6f)<1e-5
                    &&Math.Abs(Read("y")-expected.Y*.6f)<1e-5&&Math.Abs(Read("z")-expected.Z*.6f)<1e-5&&args.Skip(4).All(v=>v==null);
                if(!pass)failed++;results.Add(new{kind,major,name,named,mirror,time,own!.AlwaysZero,own.RestoredBeforeNote,pass});
            }
        }
        using var module=ModuleDefinition.ReadModule(Path.Combine(gameRoot,"Plugins/NoodleExtensions.dll"));
        var update=module.Types.Single(t=>t.FullName=="NoodleExtensions.HarmonyPatches.Objects.NoteUpdateNoodlifier").Methods.Single(m=>m.Name=="Prefix");
        var clock=update.Body.Instructions.Single(i=>i.Operand is MethodReference m&&m.DeclaringType.FullName=="IAudioTimeSource"&&m.Name=="get_songTime");
        var codes=new List<Mono.Cecil.Cil.Code>();for(var i=clock.Next;i!=null;i=i.Next){codes.Add(i.OpCode.Code);if(i.OpCode.Code==Mono.Cecil.Cil.Code.Div)break;}
        if(!codes.SequenceEqual(new[]{Mono.Cecil.Cil.Code.Ldarg_2,Mono.Cecil.Cil.Code.Callvirt,Mono.Cecil.Cil.Code.Ldloc_S,Mono.Cecil.Cil.Code.Ldc_R4,
            Mono.Cecil.Cil.Code.Mul,Mono.Cecil.Cil.Code.Sub,Mono.Cecil.Cil.Code.Sub,Mono.Cecil.Cil.Code.Ldloc_S,Mono.Cecil.Cil.Code.Div})
            ||(float)clock.Next.Next.Next.Next.Operand!=.5f)throw new InvalidDataException("Normalized note-time expression changed");
        return new{schemaVersion=1,profileVersion=BeatInsight.Formats.LocalMap.AnalysisProfileVersion,cases,failed,easingCount=easings.Count,normalizedNoteTime=.5f,results,gameStarted=false,referenceUsed=false,
            scope="installed constructors, case-sensitive named point lookup, vector interpolation and AnimationHelper against independent vector/easing evaluation in both handedness modes; destination-frame easing and normalized clock IL audited; Unity lifecycle and physical pose not executed"};
    }
}
