using System.Reflection;
using System.Runtime.Loader;
using BeatInsight.Formats;
using Newtonsoft.Json.Linq;
namespace BeatInsight.Compare;

static class NoodleBurstChecks
{
    public static object Run(string gameRoot)
    {
        string path=Path.Combine(gameRoot,"Beat Saber_Data/Managed/BGLib.UnityExtension.dll");
        if(Comparison.HashFile(path)!="A4D506858549A81448637567F3E646217BCB82A044E0625471C31C650BDF3AF6")throw new InvalidDataException("Reinspect changed signed-line-angle helper");
        var assembly=AssemblyLoadContext.Default.LoadFromAssemblyPath(path);
        var method=assembly.GetType("Vector2Extensions",true)!.GetMethod("SignedAngleToLine")!;
        var vector=method.GetParameters()[0].ParameterType;
        var directionMethod=typeof(NoteData).Assembly.GetType("NoteCutDirectionExtensions",true)!.GetMethod("Direction")!;
        var vectors=new List<(float X,float Y)>{(0,0),(1e-20f,0),(-1,-2),(1,2)};
        foreach(float angle in new[]{0f,10,30,39.99f,40,40.01f,45,60,90,135,180,270,330})
            vectors.Add(((float)Math.Sin(angle*Math.PI/180),(float)-Math.Cos(angle*Math.PI/180)));
        GameLoader.Load(gameRoot,"""{"version":"3.3.0","colorNotes":[],"obstacles":[]}""",120,noodleExtensions:true);
        var customAssembly=AssemblyLoadContext.Default.Assemblies.Single(a=>a.GetName().Name=="CustomJSONData");
        var customType=customAssembly.GetType("CustomJSONData.CustomBeatmap.CustomData",true)!;
        var customFactory=customAssembly.GetType("CustomJSONData.CustomBeatmap.CustomSliderData",true)!.GetMethod("CreateCustomBurstSliderData")!;
        var errors=new List<object>();int cases=0;
        foreach(int direction in Enumerable.Range(0,9))
        {
            var nativeDirection=directionMethod.Invoke(null,new[]{Enum.ToObject(directionMethod.GetParameters()[0].ParameterType,direction)})!;
            foreach(var point in vectors)
            {
                cases++;var line=Activator.CreateInstance(vector,new object[]{point.X,point.Y})!;
                float native=(float)method.Invoke(null,new[]{nativeDirection,line})!;
                float own=NoodleBurstGeometry.Angle(direction,point.X,point.Y);
                if(Math.Abs(native-own)>1e-4||(Math.Abs(native)<=40)!=(Math.Abs(own)<=40))errors.Add(new {direction,point.X,point.Y,native,own});
            }
            var root=new JObject {["version"]="3.3.0",["colorNotes"]=new JArray(new JObject {["b"]=2,["x"]=0,["y"]=0,["c"]=0,["d"]=direction}),
                ["burstSliders"]=new JArray(new JObject {["b"]=2,["x"]=0,["y"]=0,["c"]=0,["d"]=direction,["tb"]=3,["tx"]=1,["ty"]=2,["sc"]=4,["s"]=1})};
            var raw=GameLoader.Load(gameRoot,root.ToString(),120);var slider=OracleBridge.GetItems(raw).OfType<SliderData>().Single();
            cases++;if((int)OracleBridge.GetItems(raw).OfType<NoteData>().Single().scoringType!=4)errors.Add(new {direction,error="Installed burst head role changed"});
            cases++;if((int)slider.tailCutDirection!=8||slider.sliceCount!=4)errors.Add(new {direction,error="Installed burst converter changed tail direction or slice count"});
            var parameters=customFactory.GetParameters();var args=new object?[parameters.Length];
            for(int i=0;i<args.Length;i++)args[i]=parameters[i].ParameterType.IsValueType?Activator.CreateInstance(parameters[i].ParameterType):null;
            args[7]=Enum.ToObject(parameters[7].ParameterType,direction);args[8]=1f;args[10]=1;args[13]=4;args[14]=1f;args[15]=Activator.CreateInstance(customType);args[16]=new Version(3,3,0);
            var custom=(SliderData)customFactory.Invoke(null,args)!;
            cases++;if((int)custom.tailCutDirection!=8||custom.sliceCount!=4)errors.Add(new {direction,error="Installed custom burst factory changed tail direction or slice count"});
        }
        return new {schemaVersion=1,profileVersion=BeatInsight.Formats.LocalMap.AnalysisProfileVersion,cases,failed=errors.Count,referenceUsed=false,gameStarted=false,
            scope="installed managed angle/direction primitives and both vanilla/custom burst factory fields; independent full-map gold tested in Core; complete Noodle row patch lifecycle not executed",errors};
    }
}
