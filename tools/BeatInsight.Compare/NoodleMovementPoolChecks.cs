using System.Reflection;
using System.Runtime.CompilerServices;

namespace BeatInsight.Compare;

static class NoodleMovementPoolChecks
{
    const BindingFlags flags=BindingFlags.Public|BindingFlags.NonPublic|BindingFlags.Instance|BindingFlags.Static;
    static FieldInfo Field(Type type,string name){for(Type? t=type;t!=null;t=t.BaseType)if(t.GetField(name,flags|BindingFlags.DeclaredOnly) is FieldInfo field)return field;throw new MissingFieldException(type.FullName,name);}
    public static object Run(object template,BeatmapObjectData first,BeatmapObjectData second)
    {
        var wrapperType=template.GetType();var poolType=wrapperType.GetNestedType("Pool",flags)!;
        var pool=Activator.CreateInstance(poolType,true)!;var baseType=poolType.BaseType!.BaseType!;
        var construct=baseType.GetMethod("Construct",flags)!;
        var zenject=construct.GetParameters()[1].ParameterType.Assembly;
        var container=Activator.CreateInstance(construct.GetParameters()[1].ParameterType)!;
        var factoryType=zenject.GetType("Zenject.PlaceholderFactory`1",true)!.MakeGenericType(wrapperType);
        var factory=Activator.CreateInstance(factoryType)!;
        var constructor=wrapperType.GetConstructors(flags).Single();var parameters=constructor.GetParameters();
        var settings=RuntimeHelpers.GetUninitializedObject(parameters[1].ParameterType);
        parameters[1].ParameterType.GetField("noteJumpValue",flags)!.SetValue(settings,Field(wrapperType,"_noteJumpStartBeatOffset").GetValue(template));
        parameters[1].ParameterType.GetField("noteJumpValueType",flags)!.SetValue(settings,Field(wrapperType,"_noteJumpValueType").GetValue(template));
        parameters[1].ParameterType.GetField("beatsPerMinute",flags)!.SetValue(settings,60f/(float)Field(wrapperType,"_oneBeatDuration").GetValue(template)!);
        var initialized=RuntimeHelpers.GetUninitializedObject(parameters[2].ParameterType);
        Field(parameters[2].ParameterType,"<MovementData>k__BackingField").SetValue(initialized,Field(wrapperType,"_movementData").GetValue(template));
        var sourceContext=Field(wrapperType,"_deserializedData").GetValue(template)!;
        string[] dictionaries={"CustomEventCustomDatas","EventCustomDatas","ObjectCustomDatas"};
        var contextTypeData=sourceContext.GetType();var contextCtor=contextTypeData.GetConstructors(flags).Single();
        var injectedContext=contextCtor.Invoke(dictionaries.Select(name=>contextTypeData.GetProperty(name,flags)!.GetValue(sourceContext)).ToArray());
        var inputs=new[]{Field(wrapperType,"_original").GetValue(template),settings,initialized,injectedContext};
        var bind=container.GetType().GetMethods(flags).Single(m=>m.Name=="BindInstance"&&m.IsGenericMethodDefinition);
        for(int i=0;i<inputs.Length;i++)
        {
            var binder=bind.MakeGenericMethod(parameters[i].ParameterType).Invoke(container,new[]{inputs[i]})!;
            if(i==3)binder.GetType().GetMethod("WithId",flags)!.Invoke(binder,new object[]{"NoodleExtensions"});
        }
        var instantiate=container.GetType().GetMethods(flags).Single(m=>m.Name=="Instantiate"&&!m.IsGenericMethod&&m.GetParameters().Length==1&&m.GetParameters()[0].ParameterType==typeof(Type));
        var created=new HashSet<object>(ReferenceEqualityComparer.Instance);int factoryCalls=0,failed=0,checks=0;
        Func<object> producer=()=>
        {
            var result=instantiate.Invoke(container,new object[]{wrapperType})!;
            created.Add(result);factoryCalls++;
            foreach(string name in new[]{"_original","_movementData","_deserializedData"}){if(!ReferenceEquals(Field(wrapperType,name).GetValue(result),(name=="_deserializedData"?injectedContext:Field(wrapperType,name).GetValue(template))))failed++;checks++;}
            foreach(string name in new[]{"_noteJumpStartBeatOffset","_noteJumpValueType","_oneBeatDuration"}){if(!Equals(Field(wrapperType,name).GetValue(result),Field(wrapperType,name).GetValue(template)))failed++;checks++;}
            return result;
        };
        var methodProviderType=zenject.GetType("Zenject.MethodProvider`1",true)!.MakeGenericType(wrapperType);
        var providerCtor=methodProviderType.GetConstructors(flags).Single();var delegateType=providerCtor.GetParameters()[0].ParameterType;
        var contextType=delegateType.GenericTypeArguments[0];var parameter=System.Linq.Expressions.Expression.Parameter(contextType,"context");
        var body=System.Linq.Expressions.Expression.Convert(System.Linq.Expressions.Expression.Invoke(System.Linq.Expressions.Expression.Constant(producer)),wrapperType);
        var callback=System.Linq.Expressions.Expression.Lambda(delegateType,body,parameter).Compile();
        var provider=providerCtor.Invoke(new[]{(object)callback,container});
        var context=Activator.CreateInstance(contextType,new[]{container,(object)wrapperType})!;
        factoryType.BaseType!.GetMethod("Construct",flags)!.Invoke(factory,new[]{provider,context});
        construct.Invoke(pool,new object?[]{factory,container,null});
        var spawn=poolType.GetMethod("Spawn",flags)!;var despawn=baseType.GetMethod("Despawn",flags)!;
        int Count()=> (int)Field(poolType,"_activeCount").GetValue(pool)!;
        int Inactive()=> (int)Field(poolType,"_inactiveItems").GetValue(pool)!.GetType().GetProperty("Count")!.GetValue(Field(poolType,"_inactiveItems").GetValue(pool))!;
        var active=new List<object>();
        foreach(var key in new[]{first,second,first,second})
        {
            var item=spawn.Invoke(pool,new object[]{key})!;active.Add(item);
            if(!created.Contains(item)||active.Distinct(ReferenceEqualityComparer.Instance).Count()!=active.Count||Count()!=active.Count||Count()+Inactive()!=created.Count)failed++;
            checks++;
        }
        foreach(var item in active)despawn.Invoke(pool,new[]{item});
        if(Count()!=0||Inactive()!=created.Count||created.Count<4)failed++;checks++;
        int allocations=factoryCalls;
        var overrides=wrapperType.GetFields(flags).Where(f=>f.Name.EndsWith("Override",StringComparison.Ordinal)).ToArray();
        for(int i=0;i<3;i++)
        {
            var item=spawn.Invoke(pool,new object[]{second.GetCopy()})!;
            if(!created.Contains(item)||overrides.Any(f=>f.GetValue(item)!=null)||Count()!=1||factoryCalls!=allocations)failed++;
            despawn.Invoke(pool,new[]{item});checks++;
        }
        var replacement=contextCtor.Invoke(contextCtor.GetParameters().Select(p=>Activator.CreateInstance(p.ParameterType)).ToArray());
        var remap=contextTypeData.GetMethod("Remap",flags)!;remap.Invoke(injectedContext,new[]{replacement});
        var old=spawn.Invoke(pool,new object[]{first})!;
        if(overrides.Any(f=>f.GetValue(old)!=null))failed++;checks++;
        despawn.Invoke(pool,new[]{old});
        var newKey=second.GetCopy();var oldObjects=(System.Collections.IDictionary)contextTypeData.GetProperty("ObjectCustomDatas",flags)!.GetValue(sourceContext)!;
        contextTypeData.GetMethod("RegisterNewObject",flags)!.Invoke(replacement,new[]{newKey,oldObjects[second]});
        wrapperType.GetMethod("InitObject",flags)!.Invoke(template,new object[]{second});
        var next=spawn.Invoke(pool,new object[]{newKey})!;
        foreach(var field in overrides){if(!Equals(field.GetValue(next),field.GetValue(template)))failed++;checks++;}
        if(!ReferenceEquals(Field(wrapperType,"_deserializedData").GetValue(next),injectedContext)||factoryCalls!=allocations)failed++;checks++;
        despawn.Invoke(pool,new[]{next});
        if(Count()!=0||Inactive()!=created.Count)failed++;checks++;
        return new{failed,checks,factoryCalls,created=created.Count,active=Count(),inactive=Inactive(),scope="Real Zenject cold pool allocation/growth/reuse and actual NoodleMovementDataProvider constructor; actual Zenject PlaceholderFactory/MethodProvider and DiContainer.Instantiate resolve four constructor bindings including NoodleExtensions ID. Context Remap with cached providers drops old key and resolves newly registered key without allocation; controlled initialization inputs; game installer/scene pool binding and Unity pool lifetime excluded."};
    }
}
