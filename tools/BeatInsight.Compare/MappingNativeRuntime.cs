using System.Reflection;
using System.Runtime.Loader;
using System.Security.Cryptography;
using Mono.Cecil;
using Mono.Cecil.Cil;
using UnityEngine;

namespace BeatInsight.Compare;

// Local in-memory adaptation of the installed loader. Never packaged or saved.
// Only state consumed by our comparison is adapted: encoded layers/directions,
// the row column lookup and physical offsets used by the two-note rotation.
public static class MappingNativeRuntime
{
    public static bool Enabled;
    public static int CustomFieldAddressAdapters;
    public static void Initialize(string gameRoot,bool copyMirrorProbe=false,bool noodleRowProbe=false,bool noodleConstructorProbe=false,bool customIndexProbe=false)
    {
        AssemblyLoadContext.Default.Resolving+=(_,name)=>
        {
            foreach(string dir in new[]{"Beat Saber_Data/Managed","Plugins","Libs"})
            {
                string file=Path.Combine(gameRoot,dir,name.Name+".dll");
                if(File.Exists(file))return AssemblyLoadContext.Default.LoadFromAssemblyPath(Path.GetFullPath(file));
            }
            return null;
        };
        Load("BeatmapCore","22AFA4502CBBF85A4265F20A91E983AAD45ADEC3B3A9D45C4AB4C406D5DA02D1");
        Load("DataModels","B95112A1A581F52070195B419600591C98ADFB5E225DCB49C819F69B61C703B8");
        if(copyMirrorProbe)
        {
            using var module=ModuleDefinition.ReadModule(Path.Combine(gameRoot,"Plugins/CustomJSONData.dll"));
            if(!NativeFieldAccessorChecks.Enabled)foreach(var binding in new[]{("CustomBeatmapData","_beatmapDataItemsPerTypeAndId","_beatmapDataItemsPerTypeAccessor"),
                ("CustomBeatmapDataSortedListForTypeAndIds","_items","_itemsAccessor")})
            {
                CustomFieldAddressAdapters++;
                var type=module.Types.Single(t=>t.Name==binding.Item1);
                var ctor=type.Methods.Single(m=>m.Name==".ctor");
                var target=(FieldReference)ctor.Body.Instructions.Single(i=>i.OpCode==OpCodes.Ldfld&&i.Operand is FieldReference f&&f.Name==binding.Item2).Operand;
                var accessor=type.Fields.Single(f=>f.Name==binding.Item3);
                var signature=(GenericInstanceType)accessor.FieldType;
                var method=new MethodDefinition("BeatInsightProbeFieldAddress",Mono.Cecil.MethodAttributes.Public|Mono.Cecil.MethodAttributes.Static,new ByReferenceType(signature.GenericArguments[1]));
                method.Parameters.Add(new ParameterDefinition(new ByReferenceType(signature.GenericArguments[0])));type.Methods.Add(method);
                var il=method.Body.GetILProcessor();il.Append(il.Create(OpCodes.Ldarg_0));il.Append(il.Create(OpCodes.Ldind_Ref));il.Append(il.Create(OpCodes.Ldflda,target));il.Append(il.Create(OpCodes.Ret));
                var delegateCtor=new MethodReference(".ctor",module.TypeSystem.Void,accessor.FieldType){HasThis=true};
                delegateCtor.Parameters.Add(new ParameterDefinition(module.TypeSystem.Object));delegateCtor.Parameters.Add(new ParameterDefinition(module.TypeSystem.IntPtr));
                var initializer=type.Methods.Single(m=>m.Name==".cctor");initializer.Body.Instructions.Clear();
                il=initializer.Body.GetILProcessor();il.Append(il.Create(OpCodes.Ldnull));il.Append(il.Create(OpCodes.Ldftn,method));
                il.Append(il.Create(OpCodes.Newobj,delegateCtor));il.Append(il.Create(OpCodes.Stsfld,accessor));il.Append(il.Create(OpCodes.Ret));
            }
            using var stream=new MemoryStream();module.Write(stream);stream.Position=0;
            AssemblyLoadContext.Default.LoadFromStream(stream);
        }
        void Load(string name,string hash)
        {
            if(AssemblyLoadContext.Default.Assemblies.Any(a=>a.GetName().Name==name))throw new InvalidOperationException("Initialize the native adapter before loading "+name);
            string path=Path.Combine(gameRoot,"Beat Saber_Data/Managed",name+".dll");
            if(Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path)))!=hash)throw new InvalidDataException("Reinspect changed game assembly: "+name);
            using var resolver=new DefaultAssemblyResolver();
            resolver.AddSearchDirectory(Path.Combine(gameRoot,"Beat Saber_Data/Managed"));
            resolver.AddSearchDirectory(Path.GetDirectoryName(typeof(object).Assembly.Location)!);
            using var module=ModuleDefinition.ReadModule(path,new ReaderParameters {AssemblyResolver=resolver});
            IEnumerable<TypeDefinition> All(TypeDefinition t)=>new[]{t}.Concat(t.NestedTypes.SelectMany(All));
            foreach(var type in module.Types.SelectMany(All))foreach(var method in type.Methods.Where(m=>m.HasBody))
            {
                if(customIndexProbe&&type.Name=="BeatmapData"&&method.Name is "AddBeatmapObjectData" or "InsertBeatmapEventData")
                {
                    var il=method.Body.GetILProcessor();var first=method.Body.Instructions[0];
                    foreach(var instruction in new[]{il.Create(OpCodes.Ldarg_0),il.Create(OpCodes.Ldarg_1),
                        il.Create(method.Name=="InsertBeatmapEventData"?OpCodes.Ldc_I4_1:OpCodes.Ldc_I4_0),
                        il.Create(OpCodes.Call,module.ImportReference(typeof(CustomIndexBridge).GetMethod(nameof(CustomIndexBridge.Add))!))})il.InsertBefore(first,instruction);
                }
                if(noodleConstructorProbe&&type.Name=="BeatmapData"&&method.Name==".ctor"&&method.Parameters.Count==1)
                {
                    var il=method.Body.GetILProcessor();
                    foreach(var ret in method.Body.Instructions.Where(i=>i.OpCode==OpCodes.Ret).ToArray())
                    {
                        ret.OpCode=OpCodes.Ldarg_0;ret.Operand=null;
                        var argument=il.Create(OpCodes.Ldarg_1);il.InsertAfter(ret,argument);
                        var call=il.Create(OpCodes.Call,module.ImportReference(typeof(NoodleRowBridge).GetMethod(nameof(NoodleRowBridge.Install))!));
                        il.InsertAfter(argument,call);il.InsertAfter(call,il.Create(OpCodes.Ret));
                    }
                }
                if(copyMirrorProbe&&type.Name=="BeatmapData"&&method.Name=="GetFilteredCopy")
                {
                    var il=method.Body.GetILProcessor();var first=method.Body.Instructions[0];
                    var result=new VariableDefinition(module.TypeSystem.Object);method.Body.Variables.Add(result);method.Body.InitLocals=true;
                    foreach(var instruction in new[]{il.Create(OpCodes.Ldarg_0),il.Create(OpCodes.Ldarg_1),il.Create(OpCodes.Ldloca,result),
                        il.Create(OpCodes.Call,module.ImportReference(typeof(NoodleCopyMirrorBridge).GetMethod(nameof(NoodleCopyMirrorBridge.FilteredCopy))!)),
                        il.Create(OpCodes.Brtrue,first),il.Create(OpCodes.Ldloc,result),il.Create(OpCodes.Castclass,method.ReturnType),il.Create(OpCodes.Ret)})
                        il.InsertBefore(first,instruction);
                }
                if(copyMirrorProbe&&type.Name=="BeatmapDataSortedListForTypeAndIds`1"&&method.Name=="InsertItem")
                {
                    var il=method.Body.GetILProcessor();var first=method.Body.Instructions[0];
                    var result=new VariableDefinition(module.TypeSystem.Object);method.Body.Variables.Add(result);method.Body.InitLocals=true;
                    foreach(var instruction in new[]{il.Create(OpCodes.Ldarg_0),il.Create(OpCodes.Ldarg_1),il.Create(OpCodes.Ldloca,result),
                        il.Create(OpCodes.Call,module.ImportReference(typeof(NoodleCopyMirrorBridge).GetMethod(nameof(NoodleCopyMirrorBridge.Insert))!)),
                        il.Create(OpCodes.Brtrue,first),il.Create(OpCodes.Ldloc,result),il.Create(OpCodes.Castclass,method.ReturnType),il.Create(OpCodes.Ret)})
                        il.InsertBefore(first,instruction);
                }
                if(copyMirrorProbe&&(type.Name is "NoteData" or "SliderData")&&method.Name=="Mirror")
                {
                    var il=method.Body.GetILProcessor();
                    foreach(var ret in method.Body.Instructions.Where(i=>i.OpCode==OpCodes.Ret).ToArray())
                    {
                        ret.OpCode=OpCodes.Ldarg_0;ret.Operand=null;
                        var call=il.Create(OpCodes.Call,module.ImportReference(typeof(NoodleCopyMirrorBridge).GetMethod(nameof(NoodleCopyMirrorBridge.Mirrored))!));
                        il.InsertAfter(ret,call);il.InsertAfter(call,il.Create(OpCodes.Ret));
                    }
                }
                string? hook=null;
                if(type.Name=="BeatmapTypeConverters")hook=method.Name switch {"ConvertNoteLineLayer"=>nameof(PreserveLayer),"ConvertNoteCutDirection"=>nameof(PreserveDirection),_=>null};
                if(type.Name=="ObstacleConverter")hook=method.Name switch {"GetHeightForObstacleType"=>nameof(LegacyHeight),"GetLayerForObstacleType"=>nameof(LegacyLayer),"GetNoteLineLayer"=>nameof(PreserveObstacleLayer),_=>null};
                if(type.Name=="NoteCutDirectionExtensions"&&method.Name=="RotationAngle")hook=nameof(Rotation);
                if(type.Name=="StaticBeatmapObjectSpawnMovementData"&&method.Name=="LineYPosForLineLayer")hook=nameof(LayerY);
                if(hook!=null&&method.Parameters.Count==1)
                {
                    var il=method.Body.GetILProcessor();
                    foreach(var ret in method.Body.Instructions.Where(i=>i.OpCode==OpCodes.Ret).ToArray())
                    {
                        // Preserve branch targets that pointed directly to ret.
                        ret.OpCode=OpCodes.Ldarg_0;
                        var call=il.Create(OpCodes.Call,module.ImportReference(typeof(MappingNativeRuntime).GetMethod(hook)!));
                        il.InsertAfter(ret,call);il.InsertAfter(call,il.Create(OpCodes.Ret));
                    }
                }
                if(!noodleRowProbe&&type.Name=="BeatmapObjectsInTimeRowProcessor"&&method.Name=="HandleCurrentTimeSliceAllNotesAndSlidersDidFinishTimeSlice")
                {
                    var il=method.Body.GetILProcessor();
                    var matches=method.Body.Instructions.Where(i=>i.OpCode==OpCodes.Ldelem_Ref&&i.Previous?.Operand is MethodReference r&&r.Name=="get_lineIndex").ToArray();
                    if(matches.Length!=1)throw new InvalidDataException("Native column lookup changed");
                    il.InsertBefore(matches[0],il.Create(OpCodes.Call,module.ImportReference(typeof(MappingNativeRuntime).GetMethod(nameof(Column))!)));
                }
                if(type.Name=="StaticBeatmapObjectSpawnMovementData"&&method.Name=="Get2DNoteOffset")Prefix(method,module,nameof(Offset),3);
                if(type.Name=="NoteCutDirectionExtensions"&&method.Name=="Direction")Prefix(method,module,nameof(Direction),1);
                if(name=="DataModels"&&type.FullName.StartsWith("BeatmapDataLoaderVersion",StringComparison.Ordinal)&&
                    method.Name=="Convert"&&!method.IsStatic&&method.Parameters.Count==1&&!method.Parameters[0].ParameterType.IsValueType&&
                    method.ReturnType.FullName is "BeatmapObjectData" or "NoteData" or "ObstacleData" or "SliderData")
                {
                    var il=method.Body.GetILProcessor();
                    foreach(var ret in method.Body.Instructions.Where(i=>i.OpCode==OpCodes.Ret).ToArray())
                    {
                        ret.OpCode=OpCodes.Ldarg_1;
                        var call=il.Create(OpCodes.Call,module.ImportReference(typeof(NativeFakeRegistry).GetMethod(nameof(NativeFakeRegistry.Converted))!));
                        var cast=il.Create(OpCodes.Castclass,method.ReturnType);
                        il.InsertAfter(ret,call);il.InsertAfter(call,cast);il.InsertAfter(cast,il.Create(OpCodes.Ret));
                    }
                }
                if(noodleRowProbe&&type.Name=="BeatmapObjectsInTimeRowProcessor"&&method.Name is "HandleCurrentTimeSliceAllNotesAndSlidersDidFinishTimeSlice" or "HandleCurrentTimeSliceColorNotesDidFinishTimeSlice")
                {
                    var il=method.Body.GetILProcessor();var first=method.Body.Instructions[0];
                    foreach(var instruction in new[]{il.Create(OpCodes.Ldarg_0),il.Create(OpCodes.Ldarg_1),il.Create(OpCodes.Ldarg_2),
                        il.Create(method.Name=="HandleCurrentTimeSliceAllNotesAndSlidersDidFinishTimeSlice"?OpCodes.Ldc_I4_1:OpCodes.Ldc_I4_0),
                        il.Create(OpCodes.Call,module.ImportReference(typeof(NoodleRowBridge).GetMethod(nameof(NoodleRowBridge.Process))!)),il.Create(OpCodes.Brfalse,first),il.Create(OpCodes.Ret)})
                        il.InsertBefore(first,instruction);
                }
                foreach(var instruction in method.Body.Instructions)
                    if(instruction.OpCode.OperandType==OperandType.ShortInlineBrTarget)
                        instruction.OpCode=(OpCode)typeof(OpCodes).GetField(instruction.OpCode.Code.ToString().Replace("_S",""))!.GetValue(null)!;
            }
            using var stream=new MemoryStream();module.Write(stream);stream.Position=0;
            AssemblyLoadContext.Default.LoadFromStream(stream);
        }
    }
    static void Prefix(MethodDefinition method,ModuleDefinition module,string hook,int arguments)
    {
        var il=method.Body.GetILProcessor();var first=method.Body.Instructions[0];
        il.InsertBefore(first,il.Create(OpCodes.Ldsfld,module.ImportReference(typeof(MappingNativeRuntime).GetField(nameof(Enabled))!)));
        il.InsertBefore(first,il.Create(OpCodes.Brfalse,first));
        for(int i=0;i<arguments;i++)il.InsertBefore(first,il.Create(OpCodes.Ldarg,method.Parameters[i]));
        il.InsertBefore(first,il.Create(OpCodes.Call,module.ImportReference(typeof(MappingNativeRuntime).GetMethod(hook)!)));
        il.InsertBefore(first,il.Create(OpCodes.Ret));
    }
    public static int Column(int raw)=>Enabled?Math.Clamp(raw,0,3):raw;
    public static int PreserveLayer(int native,int raw)=>Enabled&&(raw<0||raw>2)?raw:native;
    public static int PreserveObstacleLayer(int native,int raw)=>Enabled&&raw>2?raw:native;
    public static int PreserveDirection(int native,int raw)=>Enabled&&IsExtended(raw)?raw:native;
    public static bool IsExtended(int d)=>d is >=1000 and <=1360 or >=2000 and <=2360;
    public static float Rotation(float native,int raw)=>Enabled&&IsExtended(raw)?(raw>=2000?2000:1000)-raw:native;
    public static int LegacyHeight(int native,int raw)
    {
        if(!Enabled||raw<1000||raw>4005000)return native;
        int height=raw>=4001?(raw-4001)/1000:raw-1000;
        return (int)(height/1000f*5*1000+1000);
    }
    public static int LegacyLayer(int native,int raw)=>!Enabled||raw<1000||raw>4005000?native:(int)((raw>=4001?(raw-4001)%1000:0)/750f*5*1000+1334);
    public static Vector2 Offset(int x,int count,int y)
    {
        float horizontal=x>=1000||x<=-1000?-(count-1f)*.5f+(x<=-1000?x+2000f:x)*.6f/1000:(x-(count-1)*.5f)*.6f;
        float vertical=LayerY(y==0?.25f:y==1?.85f:1.45f,y);
        return new Vector2(horizontal,vertical);
    }
    public static float LayerY(float native,int raw)
    {
        if(!Enabled||raw>=0&&raw<=2)return native;
        float step=1.45f-.85f;
        return raw>=1000||raw<=-1000?.85f-step-step+raw*step/1000:.85f-step+raw*step;
    }
    public static Vector2 Direction(int raw)
    {
        float angle=IsExtended(raw)?(raw>=2000?2000:1000)-raw:raw switch {0=>180,1=>0,2=>-90,3=>90,4=>-135,5=>135,6=>-45,7=>45,_=>180};
        double radians=angle*Math.PI/180;
        return new Vector2((float)Math.Sin(radians),-(float)Math.Cos(radians));
    }
}
