using System.Collections;
using System.Reflection;
using System.Reflection.Emit;
using Mono.Cecil.Cil;
using EmitCodes=System.Reflection.Emit.OpCodes;
using System.Runtime.CompilerServices;
using System.Runtime.Loader;
using Mono.Cecil;
using UnityEngine;

namespace BeatInsight.Compare;

// Evidence for a future admission rule, not a geometry projection.
static class NoodlePlayerTrackChecks
{
    public static object Run(string gameRoot)
    {
        foreach(var pin in new[]{("NoodleExtensions","FE50124E33631EED7ECB78EA1A19BB7EA64CC12974B0298DAD991672C2ABDA95"),
            ("Heck","9E5163F09E122713543FB69F89799D1121FB712820E6B4350F7A30C5D790793C"),
            ("CustomJSONData","4F9F8EFD8012159D8995B6D936E3C21B6AA6A35E319686FA3E9B04C2B6331F7C")})
            if(Comparison.HashFile(Path.Combine(gameRoot,"Plugins",pin.Item1+".dll"))!=pin.Item2)throw new InvalidDataException("Reinspect changed player track consumer");
        const BindingFlags flags=BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic;
        using var module=ModuleDefinition.ReadModule(Path.Combine(gameRoot,"Plugins/Heck.dll"));
        var update=module.Types.Single(t=>t.Name=="TransformController").Methods.Single(m=>m.Name=="UpdatePos");
        var consumed=update.Body.Instructions.Where(i=>i.Operand is string).Select(i=>(string)i.Operand).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray();
        if(!consumed.SequenceEqual(new[]{"localPosition","localRotation","position","rotation","scale"}))throw new InvalidDataException("Transform property readers changed");
        using var playerModule=ModuleDefinition.ReadModule(Path.Combine(gameRoot,"Plugins/NoodleExtensions.dll"));
        var playerUpdate=playerModule.Types.Single(t=>t.Name=="PlayerTrack").Methods.Single(m=>m.Name=="Update");
        var playerKeys=playerUpdate.Body.Instructions.Where(i=>i.Operand is string).Select(i=>(string)i.Operand).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray();
        if(!playerKeys.Contains("offsetPosition")||!playerKeys.Contains("offsetWorldRotation"))throw new InvalidDataException("Player offset consumer changed");
        var expression=playerUpdate.Body.Instructions.Where(i=>i.Offset>=0xe7&&i.Offset<=0x104).ToArray();
        if(!expression.Select(i=>i.OpCode.Code).SequenceEqual(new[]{Code.Ldloc_2,Code.Ldloc_S,Code.Ldc_R4,Code.Call,Code.Ldarg_0,Code.Ldfld,Code.Call,Code.Call,Code.Stloc_3})
            ||expression[5].Operand is not FieldReference startField||startField.Name!="_startPos")throw new InvalidDataException("Player offset expression changed");
        var dynamic=new DynamicMethod("InstalledPlayerOffset",typeof(Vector3),new[]{typeof(Vector3),typeof(Vector3),typeof(Quaternion)},typeof(NoodlePlayerTrackChecks).Module,true);
        var il=dynamic.GetILGenerator();
        foreach(var instruction in expression)
        {
            switch(instruction.OpCode.Code)
            {
                case Code.Ldloc_2:il.Emit(EmitCodes.Ldarg_2);break;
                case Code.Ldloc_S:if(((VariableDefinition)instruction.Operand).Index!=11)throw new InvalidDataException("Player offset temporary changed");il.Emit(EmitCodes.Ldarg_0);break;
                case Code.Ldc_R4:il.Emit(EmitCodes.Ldc_R4,(float)instruction.Operand);break;
                case Code.Ldarg_0:break; // _startPos receiver/field become explicit value input.
                case Code.Ldfld:il.Emit(EmitCodes.Ldarg_1);break;
                case Code.Call:
                    var method=(MethodReference)instruction.Operand;
                    MethodInfo target=method.DeclaringType.Name=="Quaternion"
                        ?typeof(Quaternion).GetMethod("op_Multiply",new[]{typeof(Quaternion),typeof(Vector3)})!
                        :typeof(Vector3).GetMethod(method.Name,method.Parameters.Count==2&&method.Parameters[1].ParameterType.FullName=="System.Single"?new[]{typeof(Vector3),typeof(float)}:new[]{typeof(Vector3),typeof(Vector3)})!;
                    il.Emit(EmitCodes.Call,target);break;
                case Code.Stloc_3:break;
                default:throw new InvalidDataException("Unexpected player offset instruction");
            }
        }
        il.Emit(EmitCodes.Ret);var applyOffset=(Func<Vector3,Vector3,Quaternion,Vector3>)dynamic.CreateDelegate(typeof(Func<Vector3,Vector3,Quaternion,Vector3>));
        GameLoader.Load(gameRoot,"""{"version":"3.3.0","colorNotes":[]}""",120,noodleExtensions:true);
        var heck=AssemblyLoadContext.Default.LoadFromAssemblyName(new AssemblyName("Heck"));
        var noodle=AssemblyLoadContext.Default.LoadFromAssemblyName(new AssemblyName("NoodleExtensions"));
        var custom=AssemblyLoadContext.Default.LoadFromAssemblyName(new AssemblyName("CustomJSONData"));
        var trackType=heck.GetType("Heck.Animation.Track",true)!;
        var controllerType=heck.GetType("Heck.Animation.Transform.TransformController",true)!;
        var propertyType=heck.GetType("Heck.Animation.Property`1",true)!.MakeGenericType(typeof(Vector3));
        var metadataType=custom.GetType("CustomJSONData.CustomBeatmap.CustomData",true)!;
        var parsedType=noodle.GetType("NoodleExtensions.NoodlePlayerTrackEventData",true)!;
        var ctor=parsedType.GetConstructors(flags).Single();
        var trackListType=typeof(List<>).MakeGenericType(trackType);
        int cases=0,failed=0,updateCases=0,parseCases=0,offsetCases=0;var failures=new List<object>();
        foreach(bool mirror in new[]{false,true})foreach(int count in new[]{0,1,2,4})
        foreach(string key in new[]{"offsetPosition","definitePosition","dissolve"})foreach(float component in new[]{0f,7f})
        {
            var controller=RuntimeHelpers.GetUninitializedObject(controllerType);
            controllerType.GetField("_leftHanded",flags)!.SetValue(controller,mirror);
            controllerType.GetField("_v2",flags)!.SetValue(controller,false);
            int notifications=0;Action notify=()=>notifications++;
            foreach(string name in new[]{"PositionUpdated","RotationUpdated","ScaleUpdated"})controllerType.GetEvent(name,flags)!.AddEventHandler(controller,notify);
            var tracks=(IList)Activator.CreateInstance(trackListType)!;
            for(int i=0;i<count;i++)
            {
                var track=Activator.CreateInstance(trackType,true)!;
                var valueType=key=="dissolve"?heck.GetType("Heck.Animation.Property`1",true)!.MakeGenericType(typeof(float)):propertyType;
                var property=Activator.CreateInstance(valueType,true)!;
                valueType.GetProperty("Value",flags)!.SetValue(property,key=="dissolve"?(object)component:new Vector3(component,component,component));
                ((IDictionary)trackType.GetField("_properties",flags)!.GetValue(track)!)[key]=property;tracks.Add(track);
            }
            controllerType.GetProperty("Tracks",flags)!.SetValue(controller,tracks);
            controllerType.GetMethod("UpdatePos",flags)!.Invoke(controller,null);
            bool pass=notifications==0;cases++;updateCases++;if(!pass){failed++;failures.Add(new{kind="unconsumed-property",mirror,count,key,component,notifications});}
        }
        var mirrorVector=heck.GetType("Heck.MirrorExtensions",true)!.GetMethods(BindingFlags.Static|BindingFlags.Public|BindingFlags.NonPublic)
            .Single(m=>m.Name=="Mirror"&&m.ReturnType==typeof(Vector3)&&m.GetParameters().Length==2&&m.GetParameters()[1].ParameterType==typeof(bool));
        foreach(bool mirror in new[]{false,true})foreach(var start in new[]{new Vector3(0,0,0),new Vector3(-2,1,5),new Vector3(3,-4,-2)})
        foreach(float amount in new[]{0f,1f,7f})foreach(int axis in new[]{0,1,2})
        {
            var offset=new Vector3(axis==0?amount:0,axis==1?amount:0,axis==2?amount:0);
            var reflected=(Vector3)mirrorVector.Invoke(null,new object[]{offset,mirror})!;
            var actual=applyOffset(reflected,start,new Quaternion(0,0,0,1));
            float x=start.x+(mirror?-offset.x:offset.x)*.6f,y=start.y+offset.y*.6f,z=start.z+offset.z*.6f;
            bool pass=Math.Abs(actual.x-x)<1e-5&&Math.Abs(actual.y-y)<1e-5&&Math.Abs(actual.z-z)<1e-5;
            cases++;offsetCases++;if(!pass){failed++;failures.Add(new{kind="player-offset-expression",mirror,amount,axis});}
        }
        foreach(int major in new[]{2,3})foreach(bool explicitTarget in new[]{false,true})
        {
            var tracks=(IDictionary)Activator.CreateInstance(ctor.GetParameters()[1].ParameterType)!;
            var selected=Activator.CreateInstance(trackType,true)!;var other=Activator.CreateInstance(trackType,true)!;tracks["player"]=selected;tracks["Player"]=other;
            var metadata=(IDictionary)Activator.CreateInstance(metadataType)!;metadata[major==2?"_track":"track"]="player";
            var targetProperty=parsedType.GetProperty("PlayerObject",flags)!;
            string defaultName=Enum.GetName(targetProperty.PropertyType,Enum.ToObject(targetProperty.PropertyType,0))!;
            if(explicitTarget)metadata[major==2?"_target":"target"]=defaultName;
            var parsed=ctor.Invoke(new object[]{metadata,tracks,major==2});
            bool pass=ReferenceEquals(parsedType.GetProperty("Track",flags)!.GetValue(parsed),selected)&&Convert.ToInt32(targetProperty.GetValue(parsed))==0;
            cases++;parseCases++;if(!pass){failed++;failures.Add(new{kind="player-event-constructor",major,explicitTarget});}
        }
        var lifecycle=Newtonsoft.Json.Linq.JObject.FromObject(NoodlePlayerLifecycleChecks.Run(gameRoot));
        cases+=(int)lifecycle["cases"]!;failed+=(int)lifecycle["failed"]!;
        return new{schemaVersion=1,cases,updateCases,parseCases,offsetCases,failed,failures,consumedProperties=consumed,playerConsumedProperties=playerKeys,lifecycle,
            gameStarted=false,menuAdmissionChanged=false,
            scope="actual v3 TransformController.UpdatePos on uninitialized managed receiver with only unconsumed track properties, no transform callbacks; separate pinned PlayerTrack.Update offset expression with actual mirror/vector operations and identity world rotation; actual v2/v3 player event constructors and case-sensitive target lookup; lifecycle report covers enable gate, target routing and hierarchy model; component creation, native parenting, manager initialization, AssignTrack/Start, pause and multiplayer lifecycle not executed; no new menu admission"};
    }
}
