using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.Loader;
using Mono.Cecil;
using Mono.Cecil.Cil;
using Newtonsoft.Json.Linq;
using UnityEngine;
using Matrix=System.Numerics.Matrix4x4;
using Vector=System.Numerics.Vector3;
using EmitCodes=System.Reflection.Emit.OpCodes;

namespace BeatInsight.Compare;

static class NoodleNoteScaleChecks
{
    delegate void Cut(object? unused,int color,int direction,int saber,float speed,Vector3 vector,float tolerance,
        out bool directionOK,out bool speedOK,out bool colorOK,out float deviation,out float angle);

    public static object Run(string gameRoot,string hierarchyPath)
    {
        const string bundleHash="2351F6BC889B478CE14FDD2B9BB55D65ED29AD4DAFF03B8A9D44EB0419496A6A";
        var hierarchy=JObject.Parse(File.ReadAllText(hierarchyPath));
        string bundle=Directory.GetFiles(Path.Combine(gameRoot,"Beat Saber_Data/StreamingAssets/aa/StandaloneWindows64"),"maincore_assets_all_*.bundle").Single();
        if(Comparison.HashFile(bundle)!=bundleHash||!string.Equals((string?)hierarchy["bundleSha256"],bundleHash,StringComparison.OrdinalIgnoreCase)
            ||(int?)hierarchy["schemaVersion"]!=2||(string?)hierarchy["readerVersion"]!="1.25.4")
            throw new InvalidDataException("Reinspect changed note prefab asset contract");
        foreach(string name in new[]{"NormalGameNote","ProModeGameNote","BombNote","BurstSliderHeadNote","BurstSliderNote"})
        {
            var prefab=hierarchy["prefabs"]!.Single(p=>(string?)p["prefab"]==name);
            if((bool?)prefab["cutTransformIsDirectChild"]!=true||(int?)prefab["movementLinkCount"]!=2
                ||(string?)prefab["root"]!["id"]==(string?)prefab["cutAndRotatedChild"]!["id"])
                throw new InvalidDataException("Note prefab root/child contract changed: "+name);
            int colliderCount=name=="BombNote"?1:name=="ProModeGameNote"?6:2;
            int rendererCount=name=="BombNote"?0:name=="BurstSliderNote"?1:3;
            if(prefab["cutColliders"] is not JArray colliders||colliders.Count!=colliderCount
                ||prefab["visualRenderers"] is not JArray renderers||renderers.Count!=rendererCount
                ||colliders.Concat(renderers).Any(a=>(string?)a["cutTransformId"]!=(string?)prefab["cutAndRotatedChild"]!["id"]))
                throw new InvalidDataException("Note cut/visual attachment contract changed: "+name);
        }
        foreach(var pin in new[]{("Main","A276C8A50ACFAD69A3DD3A82CDE08571C3262344702B9B014C89756771B4495E"),
            ("GameplayCore","C45267116BB2AE61104E994AC24E9A86378055A3FEDB62B6872EEE51C2FA8C7C")})
            if(Comparison.HashFile(Path.Combine(gameRoot,"Beat Saber_Data/Managed",pin.Item1+".dll"))!=pin.Item2)
                throw new InvalidDataException("Reinspect changed note scale consumer: "+pin.Item1);

        using var main=ModuleDefinition.ReadModule(Path.Combine(gameRoot,"Beat Saber_Data/Managed/Main.dll"));
        using var core=ModuleDefinition.ReadModule(Path.Combine(gameRoot,"Beat Saber_Data/Managed/GameplayCore.dll"));
        using var noodle=ModuleDefinition.ReadModule(Path.Combine(gameRoot,"Plugins/NoodleExtensions.dll"));
        MethodDefinition Method(ModuleDefinition module,string type,string name)=>module.Types.Single(t=>t.FullName==type).Methods.Single(m=>m.Name==name);
        static bool Call(Instruction i,string type,string name)=>i.Operand is MethodReference m&&m.DeclaringType.FullName==type&&m.Name==name;
        static bool Field(Instruction i,string type,string name)=>i.Operand is FieldReference f&&f.DeclaringType.FullName==type&&f.Name==name;
        int Local(Instruction i)=>i.OpCode.Code switch
        {Code.Ldloc_0 or Code.Stloc_0=>0,Code.Ldloc_1 or Code.Stloc_1=>1,Code.Ldloc_2 or Code.Stloc_2=>2,Code.Ldloc_3 or Code.Stloc_3=>3,
            Code.Ldloc or Code.Ldloc_S or Code.Stloc or Code.Stloc_S=>((VariableDefinition)i.Operand).Index,_=>-1};
        foreach(string type in new[]{"NoteInitNoodlifier","NoteUpdateNoodlifier"})
        {
            var method=Method(noodle,"NoodleExtensions.HarmonyPatches.Objects."+type,type=="NoteInitNoodlifier"?"Postfix":"Prefix");
            var transform=method.Body.Instructions.Single(i=>Call(i,"UnityEngine.Component","get_transform"));
            int receiver=Local(transform.Next);
            var write=method.Body.Instructions.Single(i=>Call(i,"UnityEngine.Transform","set_localScale"));
            if(receiver<0||transform.Previous.OpCode.Code!=Code.Ldarg_1)throw new InvalidDataException("Unknown note scale transform source");
            var load=write;
            for(int n=0;n<(type=="NoteInitNoodlifier"?2:6);n++)load=load.Previous;
            if(Local(load)!=receiver||!load.OpCode.Name.StartsWith("ldloc",StringComparison.Ordinal))throw new InvalidDataException("Note scale receiver changed");
            if(type=="NoteUpdateNoodlifier"&&(!Call(write.Previous,"UnityEngine.Vector3","Scale")
                ||!Call(write.Previous.Previous.Previous.Previous,"NoodleExtensions.NoodleObjectData","get_InternalScale")))
                throw new InvalidDataException("Note animated scale composition changed");
        }
        var jump=Method(main,"NoteJump","ManualUpdate");
        var rotations=jump.Body.Instructions.Where(i=>Call(i,"UnityEngine.Transform","set_localRotation")).ToArray();
        if(rotations.Length!=2||jump.Body.Instructions.Count(i=>Field(i,"NoteJump","_rotatedObject"))!=3)
            throw new InvalidDataException("Note jump rotation target changed");
        var handle=Method(main,"GameNoteController","HandleCut");
        if(!handle.Body.Instructions.Any(i=>Field(i,"NoteController","_noteTransform"))
            ||!handle.Body.Instructions.Any(i=>Call(i,"NoteBasicCutInfoHelper","GetBasicCutInfo")))
            throw new InvalidDataException("Note cut transform consumer changed");

        var helper=JObject.FromObject(NoodleArcOpacityChecks.Run(gameRoot,new[]{"Note"},new[]{"identity-scale","identity-scale-frames"}));
        if((int)helper["failed"]! !=0)throw new InvalidDataException("Installed note scale helper failed");
        var jumpPatch=NoodleJumpPatchChecks.Run(main,noodle);
        var initialRotation=JObject.FromObject(NoodleInitialRotationChecks.Run(gameRoot,main));
        var cutMethod=Method(core,"NoteBasicCutInfoHelper","GetBasicCutInfo");
        var cut=CompileCutSuffix(cutMethod,out int copied);
        var rows=new List<object>();int cases=0,failed=0,changed=0;
        foreach(int major in new[]{2,3})foreach(bool mirror in new[]{false,true})
        {
            var offsets=helper["results"]!.Where(r=>(int)r["major"]! ==major&&(bool)r["mirror"]! ==mirror).ToArray();
            if(offsets.Length!=12||offsets.Any(r=>r["scaleOffset"]==null||r["scaleOffset"]!.Type!=JTokenType.Array))
                throw new InvalidDataException("Incomplete signed-scale time coverage");
            float sx=(float)offsets[0]["scaleOffset"]![0]!;
            if(offsets.Any(r=>(float)r["scaleOffset"]![0]! !=sx||(float)r["scaleOffset"]![1]! !=1||(float)r["scaleOffset"]![2]! !=1))
                throw new InvalidDataException("Nonconstant identity scale helper output");
            foreach(int direction in Enumerable.Range(0,9))foreach(float tilt in new[]{-22.5f,0f,15f})
            foreach(float yaw in new[]{-30f,0f,90f})foreach(float uniform in new[]{.6f,1f})
            {
                // RotationAngle is the installed nominal direction mapping. The extra
                // tilt is a controlled child angle, not an asserted row-processor result.
                float angle=((NoteCutDirection)direction).RotationAngle()+tilt;
                float radians=angle*MathF.PI/180,yawRadians=yaw*MathF.PI/180;
                var matrix=Matrix.CreateScale(uniform)*Matrix.CreateRotationZ(radians)*Matrix.CreateScale(sx,1,1)*Matrix.CreateRotationY(yawRadians);
                if(!Matrix.Invert(matrix,out var inverse))throw new InvalidDataException("Singular controlled transform");
                bool pass=true;int count=0;
                foreach(float delta in new[]{-61f,-59f,-1f,0f,1f,59f,61f})foreach(float depth in new[]{0f,20f})
                {
                    float d=delta*MathF.PI/180;
                    var local=new Vector(MathF.Sin(d),-MathF.Cos(d),depth);
                    // Independent forward gold: scalar rotations, root reflection,
                    // then root yaw. Matrix4x4 is used only for the inverse path.
                    float x=(MathF.Cos(radians)*local.X-MathF.Sin(radians)*local.Y)*uniform*sx;
                    float y=(MathF.Sin(radians)*local.X+MathF.Cos(radians)*local.Y)*uniform;
                    float z=local.Z*uniform;
                    var world=new Vector(MathF.Cos(yawRadians)*x+MathF.Sin(yawRadians)*z,y,-MathF.Sin(yawRadians)*x+MathF.Cos(yawRadians)*z);
                    var actual=Vector.TransformNormal(world,inverse);
                    cut(null,0,direction,0,3,new Vector3(actual.X,actual.Y,actual.Z),60,out var directionOK,out var speedOK,out var colorOK,out var deviation,out var cutAngle);
                    bool expected=direction==8||(depth==0&&Math.Abs(delta)<60);
                    bool okay=directionOK==expected&&speedOK&&colorOK&&Math.Abs(actual.X-local.X)<1e-4&&Math.Abs(actual.Y-local.Y)<1e-4
                        &&Math.Abs(actual.Z-local.Z)<1e-4&&Math.Abs(cutAngle-(-90+delta))<.001f
                        &&Math.Abs(deviation-(depth==0?delta:90))<.001f;
                    cases++;count++;if(!okay){failed++;pass=false;}
                }
                if(mirror&&Math.Abs(MathF.Sin(radians))>1e-4)changed++;
                rows.Add(new{major,mirror,direction,tilt,yaw,uniform,scaleX=sx,cases=count,pass});
            }
        }
        // A right-facing child arrow under root reflection points left. Moving
        // that reflection below the child rotation reverses the accepted cut.
        var childRotation=Matrix.CreateRotationZ(MathF.PI*.5f);
        var reflectedChild=childRotation*Matrix.CreateScale(-1,1,1);
        var wrongOrderModel=Matrix.CreateScale(-1,1,1)*childRotation;
        Matrix.Invert(reflectedChild,out var reflectedInverse);Matrix.Invert(wrongOrderModel,out var wrongOrderInverse);
        var reflectedRightArrow=new Vector(-1,0,0);
        var correctLocal=Vector.TransformNormal(reflectedRightArrow,reflectedInverse);
        var wrongLocal=Vector.TransformNormal(reflectedRightArrow,wrongOrderInverse);
        cut(null,0,3,0,3,new Vector3(correctLocal.X,correctLocal.Y,correctLocal.Z),60,out bool correct,out _,out _,out _,out _);
        cut(null,0,3,0,3,new Vector3(wrongLocal.X,wrongLocal.Y,wrongLocal.Z),60,out bool wrongOrder,out _,out _,out _,out _);
        cases++;if(!correct||wrongOrder)failed++;
        foreach(int direction in new[]{1,8})foreach(var boundary in new[]{(X:1f,Y:0f,Tolerance:90f),(X:0f,Y:-1f,Tolerance:0f)})
        {
            cut(null,0,direction,0,3,new Vector3(boundary.X,boundary.Y,0),boundary.Tolerance,out var directionOK,out _,out _,out _,out _);
            cases++;if(directionOK!=(direction==8))failed++;
        }
        foreach(float speed in new[]{2f,2.01f})foreach(int saber in new[]{0,1})
        {
            cut(null,0,1,saber,speed,new Vector3(0,-1,0),60,out var directionOK,out var speedOK,out var colorOK,out _,out _);
            cases++;if(speedOK!=(speed>2)||!directionOK||colorOK!=(saber==0))failed++;
        }
        return new{schemaVersion=1,profileVersion=BeatInsight.Formats.LocalMap.AnalysisProfileVersion,cases=cases+(int)initialRotation["cases"]!,failed=failed+(int)initialRotation["failed"]!,
            cutCases=cases,helperCases=(int)helper["cases"]!,prefabCount=5,bundleSha256=bundleHash,
            copiedCutInstructions=copied,noncommutingConfigurations=changed,wrongOrderRejected=correct&&!wrongOrder,
            cutColliderCount=13,visualRendererCount=10,jumpPatch,initialRotation,results=rows,
            gameStarted=false,referenceUsed=false,
            scope="installed scale helper; pinned prefab hierarchy and consumer audit; installed cut IL after inverse-vector call with scalar MathF bindings; independent forward gold and managed matrix inverse, controlled child tilt/yaw/uniform scale; native Unity Transform, live note look, colliders and arrow mesh not executed"};
    }

    static Cut CompileCutSuffix(MethodDefinition method,out int copied)
    {
        var instructions=method.Body.Instructions;
        string[] parameters={"noteTransform","colorType","cutDirection","saberType","saberBladeSpeed","cutDirVec","cutAngleTolerance",
            "directionOK","speedOK","saberTypeOK","cutDirDeviation","cutDirAngle"};
        if(!method.IsStatic||!method.Parameters.Select(p=>p.Name).SequenceEqual(parameters)||method.Body.ExceptionHandlers.Count!=0)
            throw new InvalidDataException("Cut scalar ABI changed");
        if(instructions.Count<5||instructions[0].OpCode.Code!=Code.Ldarg_0||instructions[1].Operand is not ParameterDefinition vector||vector.Name!="cutDirVec"
            ||instructions[2].Operand is not MethodReference inverse||inverse.DeclaringType.FullName!="UnityEngine.Transform"||inverse.Name!="InverseTransformVector"
            ||instructions[3].OpCode.Code!=Code.Starg_S)
            throw new InvalidDataException("Cut inverse-vector prefix changed");
        var dynamic=new DynamicMethod("InstalledNoteCutScalarSuffix",typeof(void),new[]{typeof(object),typeof(int),typeof(int),typeof(int),typeof(float),typeof(Vector3),typeof(float),
            typeof(bool).MakeByRefType(),typeof(bool).MakeByRefType(),typeof(bool).MakeByRefType(),typeof(float).MakeByRefType(),typeof(float).MakeByRefType()},typeof(NoodleNoteScaleChecks).Module,true);
        var il=dynamic.GetILGenerator();
        foreach(var variable in method.Body.Variables)
        {
            if(variable.VariableType.FullName!="System.Boolean")throw new InvalidDataException("Unexpected cut local");
            il.DeclareLocal(typeof(bool));
        }
        var suffix=instructions.Skip(4).ToArray();var labels=suffix.ToDictionary(i=>i,_=>il.DefineLabel());
        copied=0;
        foreach(var instruction in suffix)
        {
            il.MarkLabel(labels[instruction]);
            var opcode=(System.Reflection.Emit.OpCode)typeof(EmitCodes).GetField(instruction.OpCode.Code.ToString())!.GetValue(null)!;
            switch(instruction.Operand)
            {
                case null:il.Emit(opcode);break;
                case float value:il.Emit(opcode,value);break;
                case ParameterDefinition parameter:
                    if(instruction.OpCode.OperandType==Mono.Cecil.Cil.OperandType.ShortInlineVar)il.Emit(opcode,(byte)parameter.Index);
                    else il.Emit(opcode,(short)parameter.Index);
                    break;
                case Instruction target:il.Emit(opcode,labels[target]);break;
                case FieldReference field when field.DeclaringType.FullName=="UnityEngine.Vector3":
                    il.Emit(opcode,typeof(Vector3).GetField(field.Name)!);break;
                case MethodReference call when call.DeclaringType.FullName=="UnityEngine.Mathf"&&call.Name is "Abs" or "Atan2":
                    il.Emit(opcode,typeof(MathF).GetMethod(call.Name,Enumerable.Repeat(typeof(float),call.Parameters.Count).ToArray())!);break;
                case MethodReference call when call.DeclaringType.FullName=="SaberTypeExtensions"&&call.Name=="MatchesColorType":
                    string assemblyName=call.DeclaringType.Scope is AssemblyNameReference reference?reference.Name:method.Module.Assembly.Name.Name;
                    var assembly=AssemblyLoadContext.Default.LoadFromAssemblyName(new AssemblyName(assemblyName));
                    il.Emit(opcode,assembly.GetType("SaberTypeExtensions")!.GetMethod("MatchesColorType")!);break;
                default:throw new InvalidDataException("Unvalidated cut suffix instruction: "+instruction);
            }
            copied++;
        }
        return (Cut)dynamic.CreateDelegate(typeof(Cut));
    }
}
