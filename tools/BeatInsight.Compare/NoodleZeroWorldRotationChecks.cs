using System.Reflection;
using System.Runtime.Loader;
using System.Runtime.CompilerServices;
using System.Reflection.Emit;
using Mono.Cecil.Cil;
using EmitCodes=System.Reflection.Emit.OpCodes;
using Mono.Cecil;
using UnityEngine;

namespace BeatInsight.Compare;

static class NoodleZeroWorldRotationChecks
{
    public static object Run(string gameRoot)
    {
        foreach(var pin in new[]{("NoodleExtensions","FE50124E33631EED7ECB78EA1A19BB7EA64CC12974B0298DAD991672C2ABDA95"),
            ("Heck","9E5163F09E122713543FB69F89799D1121FB712820E6B4350F7A30C5D790793C")})
            if(Comparison.HashFile(Path.Combine(gameRoot,"Plugins",pin.Item1+".dll"))!=pin.Item2)throw new InvalidDataException("Reinspect zero world rotation consumer");
        using var module=ModuleDefinition.ReadModule(Path.Combine(gameRoot,"Plugins/NoodleExtensions.dll"));
        var objectType=module.Types.Single(t=>t.Name=="NoodleObjectData");
        var constructor=objectType.Methods.Single(m=>m.IsConstructor&&m.Parameters.Count>2);
        var calls=constructor.Body.Instructions.Where(i=>i.Operand is MethodReference).Select(i=>(MethodReference)i.Operand).ToArray();
        if(!constructor.Body.Instructions.Any(i=>Equals(i.Operand,"worldRotation"))
            ||!calls.Any(m=>m.DeclaringType.FullName=="UnityEngine.Quaternion"&&m.Name=="Euler")
            ||!calls.Any(m=>m.DeclaringType.FullName=="Heck.MirrorExtensions"&&m.Name=="Mirror"))throw new InvalidDataException("Rotation constructor changed");
        var init=module.Types.Single(t=>t.Name=="NoteInitNoodlifier").Methods.Single(m=>m.Name=="Postfix");
        string[] fields={"NoteJump::_worldRotation","NoteJump::_inverseWorldRotation","NoteFloorMovement::_worldRotation","NoteFloorMovement::_inverseWorldRotation"};
        foreach(string field in fields)
            if(!init.Body.Instructions.Any(i=>i.Operand is FieldReference f&&f.DeclaringType.Name+"::"+f.Name==field))throw new InvalidDataException("World rotation propagation changed");
        var arcInit=module.Types.Single(t=>t.Name=="SliderInitNoodlifier").Methods.Single(m=>m.Name=="Postfix");
        var arcSlice=arcInit.Body.Instructions.Where(i=>i.Offset>=0x64&&i.Offset<=0x70).ToArray();
        if(!arcSlice.Select(i=>i.OpCode.Code).SequenceEqual(new[]{Code.Ldarg_2,Code.Ldloc_S,Code.Stfld,Code.Ldloc_S,Code.Ldloc_S,Code.Call})
            ||arcSlice[2].Operand is not FieldReference arcField||arcField.DeclaringType.Name!="SliderMovement"||arcField.Name!="_worldRotation"
            ||arcSlice[5].Operand is not MethodReference compose||compose.DeclaringType.FullName!="UnityEngine.Quaternion"||compose.Name!="op_Multiply")throw new InvalidDataException("Arc world rotation propagation changed");
        var movementType=AssemblyLoadContext.Default.LoadFromAssemblyName(new AssemblyName("Main")).GetType("SliderMovement",true)!;
        var worldField=movementType.GetField("_worldRotation",BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic)!;
        // Execute the controller's field write and quaternion product only.
        // Object/local inputs replace its verified temporaries; no Transform calls.
        var dynamic=new DynamicMethod("InstalledArcWorldRotation",typeof(Quaternion),new[]{typeof(object),typeof(Quaternion),typeof(Quaternion)},typeof(NoodleZeroWorldRotationChecks).Module,true);
        var il=dynamic.GetILGenerator();
        foreach(var instruction in arcSlice)
        {
            switch(instruction.OpCode.Code)
            {
                case Code.Ldarg_2:il.Emit(EmitCodes.Ldarg_0);il.Emit(EmitCodes.Castclass,movementType);break;
                case Code.Ldloc_S:
                    int index=((VariableDefinition)instruction.Operand).Index;
                    if(index!=6&&index!=4)throw new InvalidDataException("Arc rotation temporaries changed");
                    il.Emit(index==6?EmitCodes.Ldarg_1:EmitCodes.Ldarg_2);break;
                case Code.Stfld:il.Emit(EmitCodes.Stfld,worldField);break;
                case Code.Call:il.Emit(EmitCodes.Call,typeof(Quaternion).GetMethod("op_Multiply",new[]{typeof(Quaternion),typeof(Quaternion)})!);break;
                default:throw new InvalidDataException("Unexpected arc rotation instruction");
            }
        }
        il.Emit(EmitCodes.Ret);var arcApply=(Func<object,Quaternion,Quaternion,Quaternion>)dynamic.CreateDelegate(typeof(Func<object,Quaternion,Quaternion,Quaternion>));
        var movement=RuntimeHelpers.GetUninitializedObject(movementType);
        GameLoader.Load(gameRoot,"""{"version":"3.3.0","colorNotes":[]}""",120,noodleExtensions:true);
        var heck=AssemblyLoadContext.Default.LoadFromAssemblyName(new AssemblyName("Heck"));
        var mirror=heck.GetType("Heck.MirrorExtensions",true)!.GetMethods(BindingFlags.Static|BindingFlags.Public|BindingFlags.NonPublic)
            .Single(m=>m.Name=="Mirror"&&m.ReturnType==typeof(Quaternion)&&m.GetParameters().Length==2&&m.GetParameters()[1].ParameterType==typeof(bool));
        int cases=0,failed=0,arcCases=0;
        foreach(bool leftHanded in new[]{false,true})foreach(int direction in Enumerable.Range(0,9))foreach(float tilt in new[]{-45f,0f,17f,90f})
        {
            var identity=(Quaternion)mirror.Invoke(null,new object[]{new Quaternion(0,0,0,1),leftHanded})!;
            float radians=(direction*45+tilt)*(float)Math.PI/360;var local=new Quaternion(0,0,(float)Math.Sin(radians),(float)Math.Cos(radians));
            var composed=identity*local;
            if(identity.x!=0||identity.y!=0||identity.z!=0||Math.Abs(identity.w)!=1||composed.x!=local.x||composed.y!=local.y||composed.z!=local.z||composed.w!=local.w)failed++;
            cases++;
            var arcComposed=arcApply(movement,identity,local);var stored=(Quaternion)worldField.GetValue(movement)!;
            if(!stored.Equals(identity)||!arcComposed.Equals(local))failed++;
            arcCases++;
        }
        var composition=Newtonsoft.Json.Linq.JObject.FromObject(NoteWorldRotationCompositionChecks.Run(gameRoot));
        failed+=(int)composition["failed"]!;
        var constructorComposition=Newtonsoft.Json.Linq.JObject.FromObject(NoodleRotationConstructorChecks.Run(gameRoot));
        failed+=(int)constructorComposition["failed"]!;
        var capturedComposition=Newtonsoft.Json.Linq.JObject.FromObject(NoodleRotationConstructorChecks.Run(gameRoot,RotationMathCapture.Load(gameRoot)));
        failed+=(int)capturedComposition["failed"]!;
        return new{schemaVersion=1,profileVersion=BeatInsight.Formats.LocalMap.AnalysisProfileVersion,cases,arcCases,failed,composition,constructorComposition,capturedComposition,gameStarted=false,
            scope="pinned constructor/controller IL audit, actual Heck quaternion mirror and Unity managed quaternion multiplication for identity; Euler(0,0,0) identity is a mathematical premise, native Unity Euler is not executed in this host; capturedComposition replays game-captured Euler samples; extracted arc field-write/product IL executed, full spawn rotation and Transform lifecycle not executed; production requires no rotation events and a supported flat characteristic"};
    }
}
