using System.Collections;
using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.Loader;
using Mono.Cecil;
using Mono.Cecil.Cil;
using OpCodes=System.Reflection.Emit.OpCodes;
using UnityEngine;

namespace BeatInsight.Compare;

static class NoodleWallInteractionChecks
{
    delegate bool SelectWall(object data,ref Bounds bounds);
    public static object Run(string gameRoot,string? mapFolder=null)
    {
        foreach(var pin in new[]{("NoodleExtensions","FE50124E33631EED7ECB78EA1A19BB7EA64CC12974B0298DAD991672C2ABDA95"),
            ("CustomJSONData","4F9F8EFD8012159D8995B6D936E3C21B6AA6A35E319686FA3E9B04C2B6331F7C"),
            ("Heck","9E5163F09E122713543FB69F89799D1121FB712820E6B4350F7A30C5D790793C")})
            if(Comparison.HashFile(Path.Combine(gameRoot,"Plugins",pin.Item1+".dll"))!=pin.Item2)throw new InvalidDataException("Reinspect changed wall interaction consumer");
        GameLoader.Load(gameRoot,"""{"version":"3.3.0","colorNotes":[]}""",120,noodleExtensions:true);
        const BindingFlags flags=BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic;
        var noodle=AssemblyLoadContext.Default.LoadFromAssemblyName(new AssemblyName("NoodleExtensions"));
        var custom=AssemblyLoadContext.Default.LoadFromAssemblyName(new AssemblyName("CustomJSONData"));
        var dataType=noodle.GetType("NoodleExtensions.NoodleObjectData",true)!;
        using var module=ModuleDefinition.ReadModule(Path.Combine(gameRoot,"Plugins/NoodleExtensions.dll"));
        var postfix=module.Types.Single(t=>t.Name=="ObstacleInitNoodlifier").Methods.Single(m=>m.Name=="Postfix");
        var sequence=postfix.Body.Instructions.Where(i=>i.Offset>=0xd0&&i.Offset<0xfb).ToArray();
        if(!sequence.Select(i=>i.OpCode.Code).SequenceEqual(new[]{Code.Ldloc_0,Code.Brfalse_S,Code.Ldloc_0,Code.Callvirt,Code.Stloc_S,
            Code.Ldloca_S,Code.Call,Code.Brfalse_S,Code.Ldloca_S,Code.Call,Code.Brfalse_S,Code.Ldarg_S,Code.Call,Code.Call,Code.Br_S}))throw new InvalidDataException("Wall interaction branch changed");
        var add=postfix.Body.Instructions.Single(i=>i.Offset==0x102);
        if(add.Operand is not MethodReference call||call.Name!="AddActive"||call.DeclaringType.Name!="ManagedActiveObstacleTracker")throw new InvalidDataException("Active wall registration changed");
        // Execute the pinned conditional/bounds slice. Replace only its two exits
        // with bool returns: true means reach AddActive, false means bypass it.
        var dynamic=new DynamicMethod("InstalledWallInteraction",typeof(bool),new[]{typeof(object),typeof(Bounds).MakeByRefType()},typeof(NoodleWallInteractionChecks).Module,true);
        var il=dynamic.GetILGenerator();var flag=il.DeclareLocal(typeof(bool?));var active=il.DefineLabel();var ignored=il.DefineLabel();
        foreach(var i in sequence)
        {
            switch(i.OpCode.Code)
            {
                case Code.Ldloc_0:il.Emit(OpCodes.Ldarg_0);il.Emit(OpCodes.Castclass,dataType);break;
                case Code.Brfalse_S:if(((Instruction)i.Operand).Offset!=0xfb)throw new InvalidDataException("Changed wall branch target");il.Emit(OpCodes.Brfalse,active);break;
                case Code.Stloc_S:il.Emit(OpCodes.Stloc,flag);break;
                case Code.Ldloca_S:il.Emit(OpCodes.Ldloca,flag);break;
                case Code.Ldarg_S:if(((ParameterDefinition)i.Operand).Name!="____bounds")throw new InvalidDataException("Changed wall bounds input");il.Emit(OpCodes.Ldarg_1);break;
                case Code.Br_S:if(((Instruction)i.Operand).Offset!=0x107)throw new InvalidDataException("Changed wall bypass");il.Emit(OpCodes.Br,ignored);break;
                case Code.Callvirt:il.Emit(OpCodes.Callvirt,dataType.GetProperty("Uninteractable",flags)!.GetMethod!);break;
                case Code.Call:
                    var method=(MethodReference)i.Operand;
                    MethodInfo target=method.Name switch {
                        "get_HasValue"=>typeof(bool?).GetProperty("HasValue")!.GetMethod!,
                        "GetValueOrDefault"=>typeof(bool?).GetMethod("GetValueOrDefault",Type.EmptyTypes)!,
                        "get_zero"=>typeof(Vector3).GetProperty("zero")!.GetMethod!,
                        "set_size"=>typeof(Bounds).GetProperty("size")!.SetMethod!,
                        _=>throw new InvalidDataException("Changed wall interaction call")};
                    il.Emit(OpCodes.Call,target);break;
                default:throw new InvalidDataException("Unexpected wall interaction instruction");
            }
        }
        il.MarkLabel(active);il.Emit(OpCodes.Ldc_I4_1);il.Emit(OpCodes.Ret);
        il.MarkLabel(ignored);il.Emit(OpCodes.Ldc_I4_0);il.Emit(OpCodes.Ret);
        var select=(SelectWall)dynamic.CreateDelegate(typeof(SelectWall));
        var metadataType=custom.GetType("CustomJSONData.CustomBeatmap.CustomData",true)!;
        var obstacleCtor=custom.GetType("CustomJSONData.CustomBeatmap.CustomObstacleData",true)!.GetConstructors().Single();
        var parsedCtor=noodle.GetType("NoodleExtensions.NoodleObstacleData",true)!.GetConstructors(flags).Single();var cp=parsedCtor.GetParameters();
        int cases=0,failed=0;var results=new List<object>();
        foreach(int major in new[]{2,3})foreach(string state in new[]{"missing","null","false","true"})foreach(float width in new[]{1f,4f})
        {
            var metadata=(IDictionary)Activator.CreateInstance(metadataType)!;
            if(state!="missing")metadata[major==2?"_interactable":"uninteractable"]=state=="null"?null:state=="true";
            var obstacle=obstacleCtor.Invoke(new object[]{1f,2f,4f,0,0,Enum.ToObject(obstacleCtor.GetParameters()[5].ParameterType,2),1f,4,3,metadata,new Version(major,0)});
            var parsed=parsedCtor.Invoke(new object[]{obstacle,metadata,120f,Activator.CreateInstance(cp[3].ParameterType)!,Activator.CreateInstance(cp[4].ParameterType)!,major==2,false});
            bool expectedIgnored=major==3?state=="true":state=="false";
            var bounds=new Bounds(Vector3.zero,new Vector3(width,3,10));bool actual=select(parsed,ref bounds);
            bool pass=actual==!expectedIgnored&&bounds.size.Equals(expectedIgnored?Vector3.zero:new Vector3(width,3,10))
                &&BeatInsight.Formats.CustomObjectFlags.IsNonInteractiveWall(obstacle)==expectedIgnored;
            if(!expectedIgnored)
            {
                try{_ =BeatInsight.Formats.CustomObjectFlags.StaticWall(obstacle,0,2,4,3);}
                catch(NotSupportedException){pass=false;}
            }
            else
            {
                try{_ =BeatInsight.Formats.CustomObjectFlags.StaticWall(obstacle,0,2,4,3);pass=false;}
                catch(NotSupportedException){}
            }
            cases++;if(!pass)failed++;results.Add(new{major,state,width,active=actual,pass});
        }
        foreach(object? legacy in new object?[]{null,false,true,17f,new List<object>{false,true}})
        foreach(string canonical in new[]{"missing","false","true"})foreach(bool coordinates in new[]{false,true})foreach(bool mirror in new[]{false,true})
        {
            var metadata=(IDictionary)Activator.CreateInstance(metadataType)!;
            metadata["_interactable"]=legacy;
            if(canonical!="missing")metadata["uninteractable"]=canonical=="true";
            if(coordinates)metadata["coordinates"]=new List<object>{-1f,1.5f};
            var obstacle=obstacleCtor.Invoke(new object[]{1f,2f,4f,0,0,Enum.ToObject(obstacleCtor.GetParameters()[5].ParameterType,2),1f,4,3,metadata,new Version(3,3,0)});
            var parsed=parsedCtor.Invoke(new object[]{obstacle,metadata,120f,Activator.CreateInstance(cp[3].ParameterType)!,Activator.CreateInstance(cp[4].ParameterType)!,false,mirror});
            var bounds=new Bounds(Vector3.zero,new Vector3(4,3,10));bool wallActive=select(parsed,ref bounds);
            bool geometry=BeatInsight.Formats.CustomObjectFlags.HasCustomWallGeometry(obstacle);
            bool pass=wallActive==(canonical!="true")&&geometry==(coordinates||canonical!="missing")
                &&BeatInsight.Formats.CustomObjectFlags.IsNonInteractiveWall(obstacle)==(canonical=="true");
            cases++;if(!pass)failed++;results.Add(new{legacy=legacy==null?Newtonsoft.Json.Linq.JValue.CreateNull():Newtonsoft.Json.Linq.JToken.FromObject(legacy),canonical,coordinates,mirror,active=wallActive,geometry,pass});
        }
        var maps=new List<object>();
        if(mapFolder!=null)foreach(string file in Directory.GetFiles(mapFolder,"*.beatmap.dat"))
        {
            var root=Newtonsoft.Json.Linq.JObject.Parse(File.ReadAllText(file));
            if(((string?)root["_version"])?.StartsWith("2.",StringComparison.Ordinal)==true)
            {
                var legacySave=NoodleNativeData.Deserialize(gameRoot,root,"2");
                var legacyCollection=legacySave.GetType().GetProperty("obstacles")?.GetValue(legacySave)??legacySave.GetType().GetField("obstacles")?.GetValue(legacySave)
                    ??throw new InvalidDataException("Native v2 obstacle collection changed");
                var legacyWalls=((IEnumerable)legacyCollection).Cast<object>().ToArray();
                int sourceWalls=((Newtonsoft.Json.Linq.JArray)root["_obstacles"]!).Count;
                int declaredIgnored=((Newtonsoft.Json.Linq.JArray)root["_obstacles"]!).Count(w=>(bool?)w["_customData"]?["_interactable"]==false);
                int runtimeIgnored=0;
                foreach(var source in legacyWalls)
                {
                    var legacyMetadata=(IDictionary)source.GetType().GetProperty("customData")!.GetValue(source)!;
                    var obstacle=obstacleCtor.Invoke(new object[]{1f,2f,4f,0,0,Enum.ToObject(obstacleCtor.GetParameters()[5].ParameterType,2),1f,4,3,legacyMetadata,new Version((string)root["_version"]!)});
                    if(BeatInsight.Formats.CustomObjectFlags.IsNonInteractiveWall(obstacle))runtimeIgnored++;
                }
                bool legacyPass=sourceWalls==legacyWalls.Length&&declaredIgnored==runtimeIgnored;
                cases++;if(!legacyPass)failed++;maps.Add(new{file=Path.GetFileName(file),walls=legacyWalls.Length,declaredIgnored,runtimeIgnored,pass=legacyPass,
                    scope="Actual whole-map v2 custom deserializer and genuine per-wall metadata in controlled CustomObstacleData constructors; physical exclusion independently checked by pinned Noodle constructor/Postfix matrix. Not full v2 row pipeline or Unity bounds initialization for animated walls."});
                continue;
            }
            if((string?)root["version"]!="3.3.0")throw new InvalidDataException("Map fixture needs validated v3.3 format");
            var saved=NoodleNativeData.Deserialize(gameRoot,root,"3");
            var collection=saved.GetType().GetProperty("obstacles")?.GetValue(saved)??saved.GetType().GetField("obstacles")?.GetValue(saved)
                ??throw new InvalidDataException("Native v3 obstacle collection changed");
            var walls=((IEnumerable)collection).Cast<object>().ToArray();
            int aliases=0,geometryCount=0,inactive=0;
            foreach(var source in walls)
            {
                var metadata=(IDictionary)source.GetType().GetProperty("customData")!.GetValue(source)!;
                if(metadata.Contains("_interactable"))aliases++;
                var obstacle=obstacleCtor.Invoke(new object[]{1f,2f,4f,0,0,Enum.ToObject(obstacleCtor.GetParameters()[5].ParameterType,2),1f,4,3,metadata,new Version(3,3,0)});
                var parsed=parsedCtor.Invoke(new object[]{obstacle,metadata,120f,Activator.CreateInstance(cp[3].ParameterType)!,Activator.CreateInstance(cp[4].ParameterType)!,false,false});
                if(BeatInsight.Formats.CustomObjectFlags.HasCustomWallGeometry(obstacle))geometryCount++;
                var bounds=new Bounds(Vector3.zero,new Vector3(4,3,10));if(!select(parsed,ref bounds))inactive++;
            }
            bool pass=aliases>0&&geometryCount==0&&inactive==0;
            cases++;if(!pass)failed++;maps.Add(new{file=Path.GetFileName(file),walls=walls.Length,aliases,geometryCount,inactive,pass,
                scope="Genuine per-wall metadata from whole-map deserializer, controlled base shape; interaction and geometry classification only, not full map warning rendering"});
        }
        return new{schemaVersion=1,profileVersion=BeatInsight.Formats.LocalMap.AnalysisProfileVersion,cases,failed,results,maps,gameStarted=false,
            scope="actual obstacle constructors and extracted pinned Postfix interaction/bounds IL; active registration exit audited, Unity Transform initialization, full collision loop and live warning lifecycle not executed"};
    }
}
