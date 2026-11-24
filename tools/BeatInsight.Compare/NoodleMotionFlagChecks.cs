using System.Collections;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.Loader;
using Mono.Cecil;
using UnityEngine;

namespace BeatInsight.Compare;

static class NoodleMotionFlagChecks
{
    public static object Run(string gameRoot)
    {
        foreach(var pin in new[]{("Plugins/NoodleExtensions.dll","FE50124E33631EED7ECB78EA1A19BB7EA64CC12974B0298DAD991672C2ABDA95"),
            ("Plugins/Heck.dll","9E5163F09E122713543FB69F89799D1121FB712820E6B4350F7A30C5D790793C"),
            ("Plugins/CustomJSONData.dll","4F9F8EFD8012159D8995B6D936E3C21B6AA6A35E319686FA3E9B04C2B6331F7C"),
            ("Beat Saber_Data/Managed/Main.dll","A276C8A50ACFAD69A3DD3A82CDE08571C3262344702B9B014C89756771B4495E")})
            if(Comparison.HashFile(Path.Combine(gameRoot,pin.Item1))!=pin.Item2)throw new InvalidDataException("Reinspect changed motion flag consumer");
        GameLoader.Load(gameRoot,"""{"version":"3.3.0","colorNotes":[],"obstacles":[]}""",120,noodleExtensions:true);
        const BindingFlags flags=BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic;
        var main=AssemblyLoadContext.Default.LoadFromAssemblyName(new AssemblyName("Main"));
        var custom=AssemblyLoadContext.Default.Assemblies.Single(a=>a.GetName().Name=="CustomJSONData");
        var noodle=AssemblyLoadContext.Default.Assemblies.Single(a=>a.GetName().Name=="NoodleExtensions");
        var heck=AssemblyLoadContext.Default.LoadFromAssemblyName(new AssemblyName("Heck"));
        var metadataType=custom.GetType("CustomJSONData.CustomBeatmap.CustomData",true)!;
        var factory=custom.GetType("CustomJSONData.CustomBeatmap.CustomNoteData",true)!.GetMethod("CreateCustomBasicNoteData")!;
        var ctor=noodle.GetType("NoodleExtensions.NoodleNoteData",true)!.GetConstructors(flags).Single();var cp=ctor.GetParameters();
        var contextCtor=heck.GetType("Heck.Deserialize.DeserializedData",true)!.GetConstructors(flags).Single();
        var movementType=main.GetType("BeatmapObjectSpawnMovementData",true)!;
        var movement=RuntimeHelpers.GetUninitializedObject(movementType);
        movementType.GetField("_noteLinesCount",flags)!.SetValue(movement,4);
        movementType.GetField("_rightVec",flags)!.SetValue(movement,new Vector3(1,0,0));
        var heightType=main.GetType("PlayerHeightToJumpOffsetYProvider",true)!;
        var height=RuntimeHelpers.GetUninitializedObject(heightType);
        movementType.GetField("_jumpOffsetYProvider",flags)!.SetValue(movement,height);
        var managerType=noodle.GetType("NoodleExtensions.Managers.SpawnDataManager",true)!;
        var manager=RuntimeHelpers.GetUninitializedObject(managerType);
        managerType.GetField("_movementData",flags)!.SetValue(manager,movement);
        var spawn=managerType.GetMethod("GetJumpingNoteSpawnData",flags)!;
        var variableType=main.GetType("VariableMovementDataProvider",true)!;
        var variable=RuntimeHelpers.GetUninitializedObject(variableType);
        var calculateGravity=variableType.GetMethod("CalculateCurrentNoteJumpGravity",flags)!;
        var mirrorPostfix=noodle.GetType("NoodleExtensions.HarmonyPatches.ObjectProcessing.LeftHandedMirrorNoteData",true)!
            .GetMethod("FlipLineIndex",BindingFlags.Static|BindingFlags.Public|BindingFlags.NonPublic)!;
        var trackType=heck.GetType("Heck.Animation.Track",true)!;
        var pathType=heck.GetType("Heck.Animation.PathProperty`1",true)!.MakeGenericType(typeof(Vector3));
        var curveType=heck.GetType("Heck.Animation.Vector3PointDefinition",true)!;
        var helperType=noodle.GetType("NoodleExtensions.Animation.AnimationHelper",true)!;
        var getOffset=helperType.GetMethod("GetObjectOffset",flags)!;
        var results=new List<object>();int cases=0,failed=0,bombTrackSamples=0;

        foreach(bool bomb in new[]{false,true})foreach(int major in new[]{2,3})foreach(bool mirror in new[]{false,true})foreach(bool gravity in new[]{false,true})foreach(bool look in new[]{false,true})
        foreach(int layer in new[]{0,1,2})foreach(int rowLayer in new[]{0,1,2})foreach(bool coordinates in new[]{false,true})foreach(float offset in new[]{-.2f,0f,.25f})foreach(float halfJump in new[]{.5f,1f,2f})
        {
            cases++;var data=(IDictionary)Activator.CreateInstance(metadataType)!;
            data[major==2?"_disableNoteGravity":"disableNoteGravity"]=gravity;
            data[major==2?"_disableNoteLook":"disableNoteLook"]=look;
            if(coordinates)data[major==2?"_position":"coordinates"]=new List<object>{.25f,1.5f};
            var fp=factory.GetParameters();var note=(NoteData)factory.Invoke(null,new object[]{1f,2f,0,0,Enum.ToObject(fp[4].ParameterType,layer),
                Enum.ToObject(fp[5].ParameterType,bomb?-1:0),Enum.ToObject(fp[6].ParameterType,bomb?9:1),data,new Version(major,0)})!;
            if(mirror){note.Mirror(4);mirrorPostfix.Invoke(null,new object[]{note});}
            // The mirror postfix invalidates row caches. Supply the controlled
            // post-mirror row start explicitly, as in other isolated spawn checks.
            data["NE_startNoteLineLayer"]=(float)rowLayer;
            var trackRegistry=(IDictionary)Activator.CreateInstance(cp[4].ParameterType)!;
            object? boundTrack=null;
            if(bomb&&major==3)
            {
                data["track"]="fixture";
                var animation=(IDictionary)Activator.CreateInstance(metadataType)!;
                animation["offsetPosition"]=new List<object>{new List<object>{0f,4f,0f,0f},new List<object>{0f,0f,0f,.375f}};
                data["animation"]=animation;
                boundTrack=Activator.CreateInstance(trackType,true)!;trackRegistry["fixture"]=boundTrack;
                var property=Activator.CreateInstance(pathType,true)!;
                var interpolation=pathType.GetProperty("Interpolation",flags)!.GetValue(property)!;
                var points=Activator.CreateInstance(curveType,flags,null,new object[]{new List<object>{new List<object>{2f,2f,2f,0f},new List<object>{1f,1f,1f,.375f}}},null)!;
                interpolation.GetType().GetMethod("Init",flags)!.Invoke(interpolation,new[]{points});
                ((IDictionary)trackType.GetField("_pathProperties",flags)!.GetValue(boundTrack)!)["scale"]=property;
            }
            var parsed=ctor.Invoke(new object[]{note,data,120f,Activator.CreateInstance(cp[3].ParameterType)!,trackRegistry,major==2,mirror});
            var dictionaries=contextCtor.GetParameters().Select(p=>Activator.CreateInstance(p.ParameterType)!).ToArray();
            ((IDictionary)dictionaries[2])[note]=parsed;
            managerType.GetField("_deserializedData",flags)!.SetValue(manager,contextCtor.Invoke(dictionaries));
            heightType.GetField("_jumpOffsetY",flags)!.SetValue(height,offset);
            var spawnType=spawn.GetParameters()[1].ParameterType.GetElementType()!;
            object?[] args={note,Activator.CreateInstance(spawnType)};
            bool original=(bool)spawn.Invoke(manager,args)!;var output=args[1]!;
            Vector3 Vector(string name)=>(Vector3)spawnType.GetField(name)!.GetValue(output)!;
            var start=Vector("moveStartOffset");var moveEnd=Vector("moveEndOffset");var end=Vector("jumpEndOffset");
            float gravityBase=(float)spawnType.GetField("gravityBase")!.GetValue(output)!;
            float target=coordinates?1.5f:layer,begin=!bomb&&gravity?target:rowLayer;
            float x=coordinates?.45f:-.9f;if(mirror)x=-x;
            float highest=.525f*target+.858333f+offset;
            float expectedPeak=highest+(bomb&&gravity?.6f*(rowLayer-target):0);
            variableType.GetField("_halfJumpDuration",flags)!.SetValue(variable,halfJump);
            float actualGravity=(float)calculateGravity.Invoke(variable,new object[]{gravityBase})!;
            // The installed jump's scalar parabola at elapsed = halfJump. Native
            // gravity is calculated above; Transform/position setters are excluded.
            float peak=start.y+actualGravity*halfJump*halfJump-actualGravity*halfJump*halfJump*.5f;
            bool pass=!original&&(bool)parsed.GetType().GetProperty("DisableGravity",flags)!.GetValue(parsed)! ==gravity
                &&(bool)parsed.GetType().GetProperty("DisableLook",flags)!.GetValue(parsed)! ==look
                &&Math.Abs(start.x-x)<1e-5&&start==moveEnd&&Math.Abs(start.y-(.25f+.6f*begin))<1e-5
                &&Math.Abs(end.y-(.25f+.6f*rowLayer))<1e-5&&Math.Abs(peak-expectedPeak)<1e-5
                &&note.time==1&&(int)note.cutDirection==(bomb?9:1)&&note.lineIndex==(mirror?3:0);
            if(boundTrack!=null)
            {
                var tracks=parsed.GetType().GetProperty("Track",flags)!.GetValue(parsed)!;
                pass&=((IEnumerable)tracks).Cast<object>().Single()==boundTrack;
                var animation=parsed.GetType().GetProperty("AnimationObject",flags)!.GetValue(parsed);
                foreach(float time in new[]{.25f,.5f})
                {
                    var helper=Activator.CreateInstance(helperType,flags,null,new object[]{mirror},null)!;
                    object?[] offsets=new object?[getOffset.GetParameters().Length];offsets[0]=animation;offsets[1]=tracks;offsets[2]=time;
                    getOffset.Invoke(helper,offsets);
                    var position=(Vector3)offsets[3]!;var scale=(Vector3)offsets[5]!;
                    float normalized=Math.Min(time/.375f,1),expectedY=4*(1-normalized)*.6f,expectedScale=2-normalized;
                    pass&=Math.Abs(position.y-expectedY)<1e-5&&position.x==0&&position.z==0
                        &&Math.Abs(scale.x-(mirror?-expectedScale:expectedScale))<1e-5
                        &&Math.Abs(scale.y-expectedScale)<1e-5&&Math.Abs(scale.z-expectedScale)<1e-5
                        &&new[]{4,6,7,8,9}.All(i=>offsets[i]==null);
                    bombTrackSamples++;
                }
            }
            if(!pass)failed++;results.Add(new{bomb,major,mirror,gravity,look,layer,rowLayer,coordinates,offset,halfJump,startY=start.y,endY=end.y,gravityBase,actualGravity,peak,expectedPeak,pass});
        }
        using var module=ModuleDefinition.ReadModule(Path.Combine(gameRoot,"Plugins/NoodleExtensions.dll"));
        var lookMethod=module.Types.Single(t=>t.FullName=="NoodleExtensions.HarmonyPatches.Objects.NoteJumpNoodlifier").Methods.Single(m=>m.Name=="DoNoteLook");
        if(!lookMethod.Body.Instructions.Any(i=>i.Operand is MethodReference m&&m.Name=="get_DisableLook")
            ||!lookMethod.Body.Instructions.Any(i=>i.Operand is MethodReference m&&m.Name=="set_localRotation"))throw new InvalidDataException("Look flag branch changed");
        IEnumerable<TypeDefinition> All(TypeDefinition t)=>new[]{t}.Concat(t.NestedTypes.SelectMany(All));
        var methods=module.Types.SelectMany(All).SelectMany(t=>t.Methods).Where(m=>m.HasBody).ToArray();
        var readers=new List<object>();
        foreach(string flag in new[]{"DisableGravity","DisableLook"})
        {
            var consumers=methods.Where(m=>m.Body.Instructions.Any(i=>i.Operand is MethodReference call&&call.DeclaringType.FullName=="NoodleExtensions.NoodleBaseNoteData"&&call.Name=="get_"+flag)).ToArray();
            if(consumers.Length!=3)throw new InvalidDataException("Motion flag consumers changed: "+flag);
            readers.Add(new{flag,consumers=consumers.Select(m=>m.FullName).ToArray()});
        }
        return new{schemaVersion=1,profileVersion=BeatInsight.Formats.LocalMap.AnalysisProfileVersion,cases,failed,bombTrackSamples,readers,results,gameStarted=false,referenceUsed=false,
            scope="actual note constructors, mirror/postfix, deserialized lookup, GetJumpingNoteSpawnData and native gravity calculation with controlled post-mirror row layer, height and half-jump duration; independent scalar peak gold including bomb start/gravity mismatch; actual bomb track lookup, per-object entry plus neutral path scale at early/nominal times; flag consumers/look branch audited; Unity transforms and full jump lifecycle not executed"};
    }
}

