using System.Reflection;
using System.Runtime.Loader;
using BeatInsight.WallAlert;
using Mono.Cecil;
using Newtonsoft.Json.Linq;

namespace BeatInsight.Compare;

static class VanillaWallChecks
{
    public static object Run(string gameRoot,string? mapFolder=null)
    {
        // Execute the installed pure numeric methods, without Harmony or Unity startup.
        MethodInfo Extract(string relative,string typeName,string methodName,bool intArgument=false)
        {
            using var module=ModuleDefinition.ReadModule(Path.Combine(gameRoot,relative));
            var source=module.Types.Single(t=>t.Name==typeName);
            var method=source.Methods.Single(m=>m.Name==methodName&&(!intArgument||m.Parameters[0].ParameterType.FullName=="System.Int32"));
            foreach(var t in module.Types.ToArray())
            {
                if(t!=source&&t.Name!="<Module>"&&t.Name!="NoteLineLayer"){module.Types.Remove(t);continue;}
                t.CustomAttributes.Clear();
            }
            foreach(var m in source.Methods.ToArray())if(m!=method)source.Methods.Remove(m);
            source.Fields.Clear();source.Properties.Clear();source.Events.Clear();source.NestedTypes.Clear();
            source.IsPublic=true;method.IsPublic=true;method.CustomAttributes.Clear();
            module.Assembly.Name.Name+=".BeatInsight.VanillaWallProbe";
            module.Assembly.CustomAttributes.Clear();module.CustomAttributes.Clear();module.Resources.Clear();
            using var stream=new MemoryStream();module.Write(stream);stream.Position=0;
            return AssemblyLoadContext.Default.LoadFromStream(stream).GetType(source.FullName,true)!.GetMethod(methodName)!;
        }
        var convert=Extract("Beat Saber_Data/Managed/DataModels.dll","BeatmapTypeConverters","ConvertNoteLineLayer",true);
        var layerY=Extract("Beat Saber_Data/Managed/BeatmapCore.dll","StaticBeatmapObjectSpawnMovementData","LineYPosForLineLayer");
        var preserve=Extract("Plugins/MappingExtensions.dll","BeatmapTypeConvertersConvertNoteLineLayerPatch","Postfix");
        int cases=0,failed=0;var results=new List<object>();
        foreach(int raw in new[]{-1001,-1,0,1,2,3,4,5,999,1000})
        foreach(bool installed in new[]{false,true})
        {
            object layer=convert.Invoke(null,new object[]{raw})!;
            if(installed)
            {
                object?[] args={layer,raw};preserve.Invoke(null,args);layer=args[0]!;
            }
            float y=(float)layerY.Invoke(null,new[]{layer})!;
            bool expected=y>.25f;
            string json=new JObject { ["version"]="4.1.0",["obstacles"]=new JArray(new JObject {["b"]=2}),
                ["obstaclesData"]=new JArray(new JObject {["x"]=1,["y"]=raw,["w"]=2,["h"]=5,["d"]=1})}.ToString();
            bool menu=WallAlertAdapter.Count(json,120,preserveExtendedLayers:installed)==1;
            bool runtime=new SquatWarningSequence().Observe(2,3,1,1,Convert.ToInt32(layer),2,5);
            cases++;if(menu!=expected||runtime!=expected)failed++;
            results.Add(new{raw,installed,loaded=Convert.ToInt32(layer),y,expected,menu,runtime});
        }
        var maps=new List<object>();
        if(mapFolder!=null)
        foreach(var file in Directory.GetFiles(mapFolder,"*.beatmap.dat"))
        {
            var json=File.ReadAllText(file);var root=JObject.Parse(json);var sequence=new SquatWarningSequence();int runtime=0;
            foreach(var wall in ((JArray)root["obstacles"]!).OrderBy(w=>(float)w["b"]!))
            {
                var data=root["obstaclesData"]![(int?)wall["i"]??0]!;
                float beat=(float)wall["b"]!,duration=(float?)data["d"]??0;
                if(sequence.Observe(beat,beat+duration,duration,(int?)data["x"]??0,(int?)data["y"]??0,(int?)data["w"]??0,(int?)data["h"]??0))runtime++;
            }
            int menu=WallAlertAdapter.Count(json,135,preserveExtendedLayers:true);
            cases++;if(menu!=runtime)failed++;
            maps.Add(new{file=Path.GetFileName(file),menu,runtime});
        }
        return new{cases,failed,results,maps,scope="Installed numeric conversion/spawn IL; runtime sequence and menu; no game process"};
    }
}
