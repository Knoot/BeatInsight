using Mono.Cecil;
using Mono.Cecil.Cil;

namespace BeatInsight.Compare;

static class PreviewExtraction
{
    public static void Adapt(ModuleDefinition module, TypeDefinition root)
    {
        var aliases = new Dictionary<string,Type> {
            ["BeatmapKey"]=typeof(OracleKey),
            ["BeatmapLevel"]=typeof(OracleLevel),
            ["IReadonlyBeatmapData"]=typeof(OracleData),
            ["GameplayModifiers"]=typeof(object)
        };
        TypeReference Map(TypeReference t)
        {
            if(aliases.TryGetValue(t.Name,out var type) && (t.Name=="GameplayModifiers"||t.Namespace.EndsWith(".Compatibility",StringComparison.Ordinal)))return module.ImportReference(type);
            if(t is GenericInstanceType g){var n=new GenericInstanceType(Map(g.ElementType));foreach(var a in g.GenericArguments)n.GenericArguments.Add(Map(a));return n;}
            if(t is ByReferenceType b)return new ByReferenceType(Map(b.ElementType));
            if(t is ArrayType a2)return new ArrayType(Map(a2.ElementType),a2.Rank);
            return t;
        }
        MethodReference Method(MethodReference m)
        {
            if(m is MethodDefinition)return m;
            if(m is GenericInstanceMethod g){var n=new GenericInstanceMethod(Method(g.ElementMethod));foreach(var a in g.GenericArguments)n.GenericArguments.Add(Map(a));return n;}
            var r=new MethodReference(m.Name,Map(m.ReturnType),Map(m.DeclaringType)){HasThis=m.HasThis,ExplicitThis=m.ExplicitThis,CallingConvention=m.CallingConvention};
            foreach(var g2 in m.GenericParameters)r.GenericParameters.Add(new GenericParameter(g2.Name,r));
            foreach(var p in m.Parameters)r.Parameters.Add(new ParameterDefinition(Map(p.ParameterType)));
            return r;
        }
        MethodReference Bridge(string name)=>module.ImportReference(typeof(PreviewBridge).GetMethod(name)!);
        IEnumerable<TypeDefinition> Types(TypeDefinition t)=>new[]{t}.Concat(t.NestedTypes.SelectMany(Types));
        foreach(var t in Types(root))
        {
            foreach(var f in t.Fields)f.FieldType=Map(f.FieldType);
            foreach(var m in t.Methods)
            {
                m.ReturnType=Map(m.ReturnType);foreach(var p in m.Parameters)p.ParameterType=Map(p.ParameterType);
                if(!m.HasBody)continue;
                foreach(var v in m.Body.Variables)v.VariableType=Map(v.VariableType);
                foreach(var i in m.Body.Instructions)
                {
                    if(i.Operand is MethodReference mr)
                    {
                        if(mr.Name=="1quTQFhQ3aepkynXPNo1icm2hzGMSss3beFc2Xt9Sand")
                        {
                            var g=new GenericInstanceMethod(Bridge(nameof(PreviewBridge.GetRecords)));
                            g.GenericArguments.Add(((GenericInstanceType)mr.ReturnType).GenericArguments[0]);i.Operand=g;i.OpCode=OpCodes.Call;
                        }
                        else if(mr.DeclaringType.Name=="BeatmapKey"&&mr.DeclaringType.Namespace.EndsWith(".Compatibility",StringComparison.Ordinal)&&mr.Name=="op_Equality")i.Operand=Bridge(nameof(PreviewBridge.KeysEqual));
                        else if(mr.DeclaringType.Name=="BeatmapLevel"&&mr.DeclaringType.Namespace.EndsWith(".Compatibility",StringComparison.Ordinal)&&mr.Name=="get_songDuration")i.Operand=module.ImportReference(typeof(OracleLevel).GetProperty("songDuration")!.GetMethod!);
                        else if(mr.DeclaringType.Name=="IReadonlyBeatmapData"&&mr.DeclaringType.Namespace.EndsWith(".Compatibility",StringComparison.Ordinal)&&mr.Name=="get_allBeatmapDataItems")i.Operand=module.ImportReference(typeof(OracleData).GetProperty("allBeatmapDataItems")!.GetMethod!);
                        else if(mr.DeclaringType.Name=="Beatmap"&&mr.Name=="GetBeatmapData")i.Operand=Bridge(nameof(PreviewBridge.GetData));
                        else if(mr.DeclaringType.Name=="BeatSaber"&&mr.Name=="get_gameplayModifiers")i.Operand=Bridge(nameof(PreviewBridge.GetModifiers));
                        else if(mr.Name=="get_instance"&&mr.DeclaringType.FullName.Contains("MonoBehaviourSingleton"))i.Operand=Bridge(nameof(PreviewBridge.GetStore));
                        else i.Operand=Method(mr);
                    }
                    else if(i.Operand is FieldReference f && f is not FieldDefinition)i.Operand=new FieldReference(f.Name,Map(f.FieldType),Map(f.DeclaringType));
                    else if(i.Operand is TypeReference tr)i.Operand=Map(tr);
                }
            }
        }
    }
}
