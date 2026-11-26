using System.Collections;
using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.Loader;

namespace BeatInsight.Compare;

static class InstalledTranspilerCompiler
{
    internal static (DynamicMethod Method,int Original,int Patched) Compile(MethodBase original,MethodInfo patch,
        Func<OpCode,object?,(OpCode Code,object? Operand)>? rewrite=null,bool exceptionBlocks=false,object? patchInstance=null,IReadOnlyList<MethodInfo>? followingPatches=null)
        {
            var harmony=AssemblyLoadContext.Default.LoadFromAssemblyName(new AssemblyName("0Harmony"));
            var reader=harmony.GetType("HarmonyLib.PatchProcessor",true)!.GetMethods().Single(m=>m.Name=="GetOriginalInstructions"&&m.GetParameters().Length==2&&m.GetParameters()[1].ParameterType==typeof(ILGenerator));
            var instructionType=harmony.GetType("HarmonyLib.CodeInstruction",true)!;
            var opcode=instructionType.GetField("opcode")!;var operand=instructionType.GetField("operand")!;
            var labels=instructionType.GetField("labels")!;var blocks=instructionType.GetField("blocks")!;
            if(!exceptionBlocks&&original.GetMethodBody()!.ExceptionHandlingClauses.Count!=0)throw new InvalidDataException("Converter exception handling changed");
            var parameters=(original.IsStatic?Array.Empty<Type>():new[]{original.DeclaringType!}).Concat(original.GetParameters().Select(p=>p.ParameterType)).ToArray();
            var method=new DynamicMethod("InstalledCustom"+original.DeclaringType!.Name,original is MethodInfo info?info.ReturnType:typeof(void),parameters,typeof(InstalledTranspilerCompiler).Module,true);
            var il=method.GetILGenerator();
            // Harmony's reader declares the original locals on this generator.
            // Duplicating them shifts LocalBuilder indices used by transpilers.
            var source=(IEnumerable)reader.Invoke(null,new object[]{original,il})!;
            int before=source.Cast<object>().Count();
            source=(IEnumerable)patch.Invoke(patchInstance,new object[]{source})!;
            if(followingPatches!=null)foreach(var following in followingPatches)
                source=(IEnumerable)following.Invoke(null,new object[]{source})!;
            var instructions=source.Cast<object>().ToArray();
            foreach(var instruction in instructions)
            {
                var boundaries=((IEnumerable)blocks.GetValue(instruction)!).Cast<object>().ToArray();
                if(!exceptionBlocks&&boundaries.Length!=0)throw new InvalidDataException("Converter exception blocks changed");
                foreach(Label label in (IEnumerable)labels.GetValue(instruction)!)il.MarkLabel(label);
                foreach(var boundary in boundaries)
                {
                    string kind=boundary.GetType().GetField("blockType")!.GetValue(boundary)!.ToString()!;
                    switch(kind)
                    {
                        case "BeginExceptionBlock":il.BeginExceptionBlock();break;
                        case "BeginFinallyBlock":il.BeginFinallyBlock();break;
                        case "BeginCatchBlock":il.BeginCatchBlock((Type)boundary.GetType().GetField("catchType")!.GetValue(boundary)!);break;
                        case "EndExceptionBlock":break;
                        default:throw new InvalidDataException("Unvalidated loader exception boundary: "+kind);
                    }
                }
                var code=(OpCode)opcode.GetValue(instruction)!;var value=operand.GetValue(instruction);
                if(rewrite!=null)(code,value)=rewrite(code,value);
                bool end=boundaries.Any(b=>b.GetType().GetField("blockType")!.GetValue(b)!.ToString()=="EndExceptionBlock");
                if(code==OpCodes.Endfinally&&end){il.EndExceptionBlock();continue;}
                switch(value)
                {
                    case null:il.Emit(code);break;
                    case Label label:il.Emit(code,label);break;
                    case Label[] targets:il.Emit(code,targets);break;
                    case LocalBuilder local:il.Emit(code,local);break;
                    case MethodInfo target:il.Emit(code,target);break;
                    case ConstructorInfo target:il.Emit(code,target);break;
                    case FieldInfo field:il.Emit(code,field);break;
                    case Type type:il.Emit(code,type);break;
                    case string text:il.Emit(code,text);break;
                    case float number:il.Emit(code,number);break;
                    case double number:il.Emit(code,number);break;
                    case long number:il.Emit(code,number);break;
                    case byte number:il.Emit(code,number);break;
                    case sbyte number:il.Emit(code,number);break;
                    case short number:il.Emit(code,number);break;
                    case int number:
                        if(code.OperandType==OperandType.ShortInlineVar)il.Emit(code,(byte)number);
                        else if(code.OperandType==OperandType.InlineVar)il.Emit(code,(short)number);
                        else il.Emit(code,number);
                        break;
                    default:throw new InvalidDataException("Unvalidated converter operand: "+value.GetType().Name);
                }
                if(end)il.EndExceptionBlock();
            }
            return(method,before,instructions.Length);
        }
}
