using System.Reflection;
using System.Runtime.Loader;
using BeatInsight.Formats;
using Newtonsoft.Json.Linq;
namespace BeatInsight.Compare;

static class NoodleMirrorChecks
{
    public static object Run(string gameRoot)
    {
        const BindingFlags flags=BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic;
        GameLoader.Load(gameRoot,"""{"version":"3.3.0","colorNotes":[],"obstacles":[]}""",120,noodleExtensions:true);
        var customAssembly=AssemblyLoadContext.Default.Assemblies.Single(a=>a.GetName().Name=="CustomJSONData");
        var customType=customAssembly.GetType("CustomJSONData.CustomBeatmap.CustomData",true)!;
        var obstacleType=customAssembly.GetType("CustomJSONData.CustomBeatmap.CustomObstacleData",true)!;
        var obstacleCtor=obstacleType.GetConstructors().Single();
        var noodleAssembly=AssemblyLoadContext.Default.Assemblies.Single(a=>a.GetName().Name=="NoodleExtensions");
        var noodleType=noodleAssembly.GetType("NoodleExtensions.NoodleObstacleData",true)!;
        var noodleBase=noodleAssembly.GetType("NoodleExtensions.NoodleObjectData",true)!;
        var constructor=noodleType.GetConstructors(flags).Single();var parameters=constructor.GetParameters();
        var results=new List<object>();int failed=0,cases=0;
        var fixtures=new (string Name,int Lane,int Width,float? X,float? CustomWidth,float Right,float Left)[]{
            (Name:"base",Lane:0,Width:1,X:(float?)null,CustomWidth:(float?)null,Right:0f,Left:3f),
            ("position",0,1,(float?)-.5f,(float?)null,1.5f,1.5f),
            ("width-only",1,1,(float?)null,(float?)2,1f,0f),
            ("both",0,1,(float?)-1.5f,(float?)2.5f,.5f,1f),
            ("partial-position",1,1,(float?)null,(float?).5f,1f,1.5f),
            ("zero-width",1,1,(float?)null,(float?)0,1f,2f),
            ("outside",0,1,(float?)3,(float?)1.25f,5f,-2.25f),
            ("extended-left",-4,1,(float?)-1.5f,(float?)2,.5f,1.5f),
            ("extended-right",4,1,(float?)-1.5f,(float?)2,.5f,1.5f),
            ("extended-encoded",1000,1,(float?)-1.5f,(float?)2,.5f,1.5f),
            ("bare-left",-1,1,null,null,-1,4),
            ("bare-right",4,1,null,null,4,-1),
            ("bare-wide",-1,4,null,null,-1,1),
            ("bare-literal",1000,1,null,null,1000,-997)
        };
        foreach(int major in new[]{2,3})foreach(bool mirror in new[]{false,true})foreach(int layer in new[]{2,7})foreach(var fixture in fixtures)
        {
            cases++;var errors=new List<string>();
            var metadata=(System.Collections.IDictionary)Activator.CreateInstance(customType)!;
            if(fixture.X.HasValue||fixture.Name=="partial-position")metadata[major==2?"_position":"coordinates"]=new List<object?>{fixture.X.HasValue?(object)fixture.X.Value:null,1.5f};
            metadata[major==2?"_scale":"size"]=new List<object?>{fixture.CustomWidth.HasValue?(object)fixture.CustomWidth.Value:null,1f};
            var obstacle=(ObstacleData)obstacleCtor.Invoke(new object[]{1f,2f,4f,0,fixture.Lane,Enum.ToObject(obstacleCtor.GetParameters()[5].ParameterType,layer),1f,fixture.Width,3,metadata,new Version(major,0)});
            string before=JToken.FromObject(metadata).ToString();
            if(mirror)obstacle.Mirror(4);
            if(obstacle.lineIndex!=(mirror?4-fixture.Width-fixture.Lane:fixture.Lane))errors.Add("Installed base mirror differs from manual gold");
            var native=constructor.Invoke(new object[]{obstacle,metadata,120f,Activator.CreateInstance(parameters[3].ParameterType)!,Activator.CreateInstance(parameters[4].ParameterType)!,major==2,mirror});
            var start=(float?)noodleBase.GetProperty("StartX",flags)!.GetValue(native);
            float actual=start.HasValue?start.Value+2:obstacle.lineIndex;
            float nativeWidth=(float?)noodleType.GetProperty("Width",flags)!.GetValue(native)??obstacle.width;
            var own=CustomObjectFlags.StaticWall(obstacle,obstacle.lineIndex,(int)obstacle.lineLayer,obstacle.width,obstacle.height,mirror);
            float expected=mirror?fixture.Left:fixture.Right;
            if(actual!=expected||own.X!=expected||nativeWidth!=own.Width)errors.Add("Installed constructor or own projection differs from independent gold");
            bool positioned=fixture.X.HasValue||fixture.Name=="partial-position";
            if((float?)noodleBase.GetProperty("StartY",flags)!.GetValue(native)!=(positioned?1.5f:(float?)null)||own.Y!=(positioned?1.5f:layer)||own.Height!=1)errors.Add("Explicit position and height did not replace the base layer");
            if(before!=JToken.FromObject(metadata).ToString())errors.Add("Mirror processing mutated source metadata");
            if(layer==2&&fixture.Name.StartsWith("bare-",StringComparison.Ordinal))
            {
                var wall=major==2?new JObject {["_time"]=2,["_duration"]=2,["_lineIndex"]=fixture.Lane,["_width"]=fixture.Width,["_type"]=1}:
                    new JObject {["b"]=2,["d"]=2,["x"]=fixture.Lane,["y"]=2,["w"]=fixture.Width,["h"]=3};
                var root=new JObject {[major==2?"_version":"version"]=major==2?"2.6.0":"3.3.0",[major==2?"_notes":"colorNotes"]=new JArray(),[major==2?"_obstacles":"obstacles"]=new JArray(wall)};
                var loaded=OracleBridge.GetItems(GameLoader.Load(gameRoot,root.ToString(),120,noodleExtensions:true)).OfType<ObstacleData>().Single();
                if(mirror)loaded.Mirror(4);
                if(loaded.lineIndex!=expected||loaded.width!=fixture.Width)errors.Add("Installed raw loader did not preserve bare lane and width");
            }
            if(errors.Count>0)failed++;results.Add(new {major,mirror,layer,fixture=fixture.Name,expected,actual,projected=own.X,errors});
        }
        string heckPath=Path.Combine(gameRoot,"Plugins/Heck.dll");
        string hash=Comparison.HashFile(heckPath);
        if(hash!="9E5163F09E122713543FB69F89799D1121FB712820E6B4350F7A30C5D790793C")throw new InvalidDataException("Reinspect changed quaternion mirror contract");
        var heck=AssemblyLoadContext.Default.Assemblies.Single(a=>a.GetName().Name=="Heck");
        var method=heck.GetType("Heck.MirrorExtensions",true)!.GetMethods().Single(m=>m.Name=="Mirror"&&m.GetParameters().Length==2&&m.GetParameters()[0].ParameterType.FullName=="UnityEngine.Quaternion");
        var quaternionType=method.GetParameters()[0].ParameterType;
        var q=Activator.CreateInstance(quaternionType)!;
        quaternionType.GetField("y")!.SetValue(q,.25f);quaternionType.GetField("z")!.SetValue(q,.5f);quaternionType.GetField("w")!.SetValue(q,.75f);
        foreach(bool mirror in new[]{false,true})
        {
            cases++;var reflected=method.Invoke(null,new[]{q,(object)mirror})!;
            if((float)quaternionType.GetField("y")!.GetValue(reflected)!!=(mirror?-.25f:.25f)||(float)quaternionType.GetField("z")!.GetValue(reflected)!!=(mirror?-.5f:.5f)||(float)quaternionType.GetField("w")!.GetValue(reflected)! !=.75f)failed++;
        }
        return new {schemaVersion=1,profileVersion=BeatInsight.Formats.LocalMap.AnalysisProfileVersion,cases,failed,heckSha256=hash,referenceUsed=false,gameStarted=false,
            scope="installed ObstacleData.Mirror, actual NoodleObstacleData constructor without rotation inputs, managed quaternion mirror and independent manual gold; full runtime lifecycle not executed",results};
    }
}
