using System.Reflection;
using System.Runtime.Loader;
using System.Security.Cryptography;
using Mono.Cecil;
using Mono.Cecil.Cil;
using BeatInsight.WallAlert;
using BeatInsight.Formats;
using BeatInsight.Core;
using Newtonsoft.Json.Linq;

namespace BeatInsight.Compare;

static class MappingWallChecks
{
    public static object Run(string gameRoot,string? libraryRequests=null)
    {
        string path=Path.Combine(gameRoot,"Plugins/MappingExtensions.dll");
        string hash=Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path)));
        if(hash!="1EC0B4EDEEF56C721CD09954D4F251F69A62E6459550615142FAEB6856DA0E4F")
            throw new InvalidDataException("Mapping Extensions binary changed; reinspect the obstacle conversion patches.");
        // Execute the installed pure numeric postfixes in memory. No activation,
        // Harmony setup, Unity calls, or arithmetic rewriting is needed here.
        using var module=ModuleDefinition.ReadModule(path);
        string[] names={"BeatmapDataLoaderObstacleConverterGetHeightForObstacleTypePatch","BeatmapDataLoaderObstacleConverterGetLayerForObstacleTypePatch",
            "NoteCutDirectionExtensionsRotationAnglePatch","StaticBeatmapObjectSpawnMovementDataLineYPosForLineLayerPatch","StaticBeatmapObjectSpawnMovementDataGet2DNoteOffsetPatch"};
        // Resolve local field tokens before pruning their declaring types.
        foreach(var type in module.Types.Where(t=>names.Contains(t.Name)))
            foreach(var method in type.Methods.Where(m=>m.HasBody))_ = method.Body.Instructions.ToArray();
        foreach(var type in module.Types.ToArray())
        {
            if(!names.Contains(type.Name)&&type.Name!="<Module>"){module.Types.Remove(type);continue;}
            type.CustomAttributes.Clear();type.IsPublic=type.Name!="<Module>";
            foreach(var method in type.Methods.ToArray())
            {
                if(method.Name!="Postfix"){type.Methods.Remove(method);continue;}
                method.CustomAttributes.Clear();method.IsPublic=true;
                foreach(var instruction in method.Body.Instructions)
                    if(instruction.OpCode==OpCodes.Ldsfld&&instruction.Operand is FieldReference field&&field.Name=="active")
                    {instruction.OpCode=OpCodes.Ldc_I4_1;instruction.Operand=null;}
            }
        }
        module.Assembly.Name.Name+=".BeatInsight.WallOracle";
        module.Assembly.CustomAttributes.Clear();module.CustomAttributes.Clear();module.Resources.Clear();
        foreach(var t in module.Types)foreach(var m in t.Methods.Where(m=>m.HasBody))foreach(var i in m.Body.Instructions)
            if(i.Operand==null&&i.OpCode.OperandType!=OperandType.InlineNone)throw new InvalidDataException("Missing oracle operand: "+t.Name+" "+i.OpCode);
        using var stream=new MemoryStream();module.Write(stream);stream.Position=0;
        var assembly=AssemblyLoadContext.Default.LoadFromStream(stream);
        var methods=names.Select(n=>assembly.GetType("MappingExtensions.HarmonyPatches."+n,true)!.GetMethod("Postfix")!).ToArray();
        var own=typeof(WallAlertAdapter).Assembly.GetType("BeatInsight.WallAlert.MappingWallAnalysis",true)!
            .GetMethod("LegacyShape",BindingFlags.NonPublic|BindingFlags.Static)!;
        var random=new Random(314159);
        int[] types=new[]{0,1,1000,1001,4000,4001,4002,1004251,4005000}
            .Concat(Enumerable.Range(0,1000).Select(_=>random.Next(1000,4005001))).ToArray();
        var differences=new List<object>();
        foreach(int type in types)
        {
            int Native(MethodInfo method,int fallback)
            {
                object?[] args={fallback,Enum.ToObject(method.GetParameters()[1].ParameterType,type)};
                method.Invoke(null,args);return (int)args[0]!;
            }
            int height=Native(methods[0],type==1?3:5),layer=Native(methods[1],type==1?2:0);
            var actual=((int Layer,int Height))own.Invoke(null,new object[]{type})!;
            if(actual.Layer!=layer||actual.Height!=height)differences.Add(new {type,layer,height,actual});
        }
        int angleCases=0,coordinateCases=0;
        foreach(int direction in Enumerable.Range(1000,361).Concat(Enumerable.Range(2000,361)))
        {
            object?[] args={0f,Enum.ToObject(methods[2].GetParameters()[1].ParameterType,direction)};
            methods[2].Invoke(null,args);angleCases++;
            if((float)args[0]! != MappingNotes.Angle(direction))differences.Add(new {direction,error="Physical note angle differs from extension IL"});
        }
        bool previous=MappingNativeRuntime.Enabled;MappingNativeRuntime.Enabled=true;
        try
        {
            foreach(int x in new[]{-3001,-2000,-1001,-1000,-999,-5,-1,0,1,2,3,4,999,1000,1001,1500,2000,3333,9999})
            foreach(int y in new[]{-2000,-1000,-999,-1,0,1,2,3,999,1000,1500,2000,3333,9999})
            {
                float nativeY=y==0?.25f:y==1?.85f:1.45f;
                object?[] layerArgs={nativeY,Enum.ToObject(methods[3].GetParameters()[1].ParameterType,y)};
                methods[3].Invoke(null,layerArgs);nativeY=(float)layerArgs[0]!;
                object?[] offsetArgs={new UnityEngine.Vector2((x-1.5f)*.6f,nativeY),x,4,Enum.ToObject(methods[4].GetParameters()[3].ParameterType,y)};
                methods[4].Invoke(null,offsetArgs);var offset=(UnityEngine.Vector2)offsetArgs[0]!;coordinateCases++;
                if(Math.Abs(offset.x-MappingNotes.X(x))>1e-5||Math.Abs(nativeY-MappingNotes.Y(y))>1e-5)
                    differences.Add(new {x,y,error="Physical position differs from extension IL"});
            }
        }
        finally{MappingNativeRuntime.Enabled=previous;}
        int libraryMenuCases=0,supported=0,unsupported=0,squatOnlyCases=0;
        if(libraryRequests!=null)foreach(var token in JArray.Parse(File.ReadAllText(libraryRequests)))
        {
            var request=token.ToObject<MapCase>()!;libraryMenuCases++;
            int rank=Array.IndexOf(new[]{"Easy","Normal","Hard","Expert","ExpertPlus"},request.Difficulty);
            SelectedMapRequest Menu(AnalysisFeatures features)=>new("synthetic-mapping",request.Characteristic,request.Difficulty,rank,LocalMap.SafePath(request.Folder,request.File),features:features);
            try
            {
                var map=LocalMap.Load(request);
                var menu=SelectedMapAnalyzer.Load(Menu(AnalysisFeatures.Average|AnalysisFeatures.Peak|AnalysisFeatures.Median|AnalysisFeatures.FirstNotes|AnalysisFeatures.Density|AnalysisFeatures.Squats));
                if(menu.SquatError!=null||menu.SquatCount!=WallAlertAdapter.Count(map.Json,map.Bpm,true))differences.Add(new {request,error="Menu squat result differs"});
                supported++;
            }
            catch(Exception e){differences.Add(new {request,error=e.Message});}
            try
            {
                var menu=SelectedMapAnalyzer.Load(Menu(AnalysisFeatures.Squats));
                if(menu.SquatError!=null||menu.SquatCount==null)differences.Add(new {request,error=menu.SquatError??"Missing squat-only result"});
                var hidden=LocalMap.Load(request,AnalysisFeatures.Squats);
                if(hidden.Input.Notes.Length!=0||hidden.AudioJson!=null)differences.Add(new {request,error="Hidden notes or timing were loaded"});
                squatOnlyCases++;
            }
            catch(Exception e){differences.Add(new {request,error=e.Message});}
        }
        return new {schemaVersion=1,profileVersion=BeatInsight.Formats.LocalMap.AnalysisProfileVersion,cases=types.Length,angleCases,coordinateCases,libraryMenuCases,supported,unsupported,squatOnlyCases,failed=differences.Count,extensionSha256=hash,
            gameStarted=false,userHistoryRead=false,scope="installed Mapping Extensions obstacle, angle and physical position IL; activation flag forced only in local numeric extraction",differences};
    }
}
