using System.Reflection;
using System.Runtime.Loader;
using Newtonsoft.Json.Linq;
namespace BeatInsight.Compare;

static class NoodleArcChecks
{
    public static object Run(string gameRoot)
    {
        const BindingFlags flags=BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic;
        var game=GameLoader.Load(gameRoot,"""{"version":"3.3.0","colorNotes":[],"sliders":[{"b":1,"x":0,"y":0,"c":0,"d":1,"tb":2,"tx":1,"ty":0,"tc":1}]}""",120,noodleExtensions:true);
        var slider=OracleBridge.GetItems(game).OfType<SliderData>().Single();
        var customAssembly=AssemblyLoadContext.Default.Assemblies.Single(a=>a.GetName().Name=="CustomJSONData");
        var customType=customAssembly.GetType("CustomJSONData.CustomBeatmap.CustomData",true)!;
        var assembly=AssemblyLoadContext.Default.Assemblies.Single(a=>a.GetName().Name=="NoodleExtensions");
        var type=assembly.GetType("NoodleExtensions.NoodleSliderData",true)!;
        var baseType=assembly.GetType("NoodleExtensions.NoodleObjectData",true)!;
        var constants=assembly.GetType("NoodleExtensions.NoodleController",true)!;
        var sharedConstants=AssemblyLoadContext.Default.Assemblies.Single(a=>a.GetName().Name=="Heck").GetType("Heck.HeckController",true)!;
        if((string?)constants.GetField("TAIL_NOTE_OFFSET",BindingFlags.Static|BindingFlags.Public|BindingFlags.NonPublic)!.GetValue(null)!="tailCoordinates"||(string?)sharedConstants.GetField("V2_POSITION",BindingFlags.Static|BindingFlags.Public|BindingFlags.NonPublic)!.GetValue(null)!="_position")throw new InvalidDataException("Reinspect changed arc coordinate keys");
        var ctor=type.GetConstructors(flags).Single();var parameters=ctor.GetParameters();
        var results=new List<object>();int failed=0,cases=0;
        var fixtures=new (string Name,List<object?>? Head,List<object?>? Tail,float? HX,float? HY,float? TX,float? TY)[]{
            ("none",null,null,null,null,null,null),
            ("head",new(){-1f,1.5f},null,-1,1.5f,null,null),
            ("tail",null,new(){-.5f,2f},null,null,-.5f,2),
            ("partial",new(){null,.25f},new(){.5f,null},null,.25f,.5f,null),
            ("short",new(){1f},new(){null},1,null,null,null),
            ("empty",new(),new(),null,null,null,null)
        };
        foreach(int major in new[]{2,3})foreach(var fixture in fixtures)
        {
            cases++;var metadata=(System.Collections.IDictionary)Activator.CreateInstance(customType)!;
            if(fixture.Head!=null)metadata[major==2?"_position":"coordinates"]=fixture.Head;
            if(fixture.Tail!=null)metadata["tailCoordinates"]=fixture.Tail;
            string before=JToken.FromObject(metadata).ToString();
            var data=ctor.Invoke(new object[]{slider,metadata,120f,Activator.CreateInstance(parameters[3].ParameterType)!,Activator.CreateInstance(parameters[4].ParameterType)!,major==2,false});
            float? hx=(float?)baseType.GetProperty("StartX",flags)!.GetValue(data),hy=(float?)baseType.GetProperty("StartY",flags)!.GetValue(data),
                tx=(float?)type.GetProperty("TailStartX",flags)!.GetValue(data),ty=(float?)type.GetProperty("TailStartY",flags)!.GetValue(data);
            bool pass=hx==fixture.HX&&hy==fixture.HY&&tx==fixture.TX&&ty==fixture.TY&&before==JToken.FromObject(metadata).ToString();
            if(!pass)failed++;results.Add(new {major,fixture=fixture.Name,status=pass?"pass":"mismatch",hx,hy,tx,ty});
        }
        return new {schemaVersion=1,profileVersion=BeatInsight.Formats.LocalMap.AnalysisProfileVersion,cases,failed,referenceUsed=false,gameStarted=false,
            scope="installed NoodleSliderData constructor with actual native slider and independent coordinate gold; canonical unmirrored metadata only, full runtime row/visual lifecycle not executed",results};
    }
}
