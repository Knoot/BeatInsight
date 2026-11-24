using System.Collections;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.Loader;
using Mono.Cecil;
using UnityEngine;

namespace BeatInsight.Compare;

static class NoodleLocalRotationChecks
{
    public static object Run(string gameRoot)
    {
        foreach(var pin in new[]{("NoodleExtensions","FE50124E33631EED7ECB78EA1A19BB7EA64CC12974B0298DAD991672C2ABDA95"),
            ("Heck","9E5163F09E122713543FB69F89799D1121FB712820E6B4350F7A30C5D790793C")})
            if(Comparison.HashFile(Path.Combine(gameRoot,"Plugins",pin.Item1+".dll"))!=pin.Item2)
                throw new InvalidDataException("Reinspect changed local rotation endpoint: "+pin.Item1);
        GameLoader.Load(gameRoot,"""{"version":"3.3.0","colorNotes":[]}""",120,noodleExtensions:true);
        const BindingFlags flags=BindingFlags.Public|BindingFlags.NonPublic|BindingFlags.Instance;
        var heck=AssemblyLoadContext.Default.LoadFromAssemblyPath(Path.Combine(gameRoot,"Plugins/Heck.dll"));
        var noodle=AssemblyLoadContext.Default.LoadFromAssemblyPath(Path.Combine(gameRoot,"Plugins/NoodleExtensions.dll"));
        var curveType=heck.GetType("Heck.Animation.QuaternionPointDefinition",true)!;
        var baseType=curveType.BaseType!;
        var pointType=curveType.GetNestedType("PointData",BindingFlags.Public|BindingFlags.NonPublic)!;
        var pointCtor=pointType.GetConstructors(flags).Single();
        var pointsField=baseType.GetField("_points",flags)!;
        var animationType=noodle.GetType("NoodleExtensions.NoodleObjectData+AnimationObjectData",true)!;
        var localField=animationType.GetField("<LocalLocalRotation>k__BackingField",flags)!;
        var helperType=noodle.GetType("NoodleExtensions.Animation.AnimationHelper",true)!;
        var offset=helperType.GetMethod("GetObjectOffset",flags)!;
        var interpolate=baseType.GetMethods().Single(m=>m.Name=="Interpolate"&&m.GetParameters().Length==1);
        var results=new List<object>();int cases=0,failed=0;
        foreach(float finalTime in new[]{.125f,.375f,.49f})foreach(bool identity in new[]{false,true})
        foreach(bool mirror in new[]{false,true})foreach(float time in new[]{.5f,.75f,1f,2f})
        {
            var cached=identity?new Quaternion(0,0,0,1):new Quaternion(.2f,.3f,.4f,.8f);
            var curve=RuntimeHelpers.GetUninitializedObject(curveType);
            var points=(IList)Activator.CreateInstance(pointsField.FieldType)!;
            var parameters=pointCtor.GetParameters();
            object Point(Quaternion value,float t)=>pointCtor.Invoke(new object?[]{value,null,
                Array.CreateInstance(parameters[2].ParameterType.GetElementType()!,0),t,
                Array.CreateInstance(parameters[4].ParameterType.GetElementType()!,0),Enum.ToObject(parameters[5].ParameterType,0)});
            points.Add(Point(new Quaternion(.7f,.1f,.2f,.6f),0));points.Add(Point(cached,finalTime));pointsField.SetValue(curve,points);
            var sampled=(Quaternion)interpolate.Invoke(curve,new object[]{time})!;
            var animation=RuntimeHelpers.GetUninitializedObject(animationType);localField.SetValue(animation,curve);
            var helper=Activator.CreateInstance(helperType,flags,null,new object[]{mirror},null)!;
            object?[] args=new object?[offset.GetParameters().Length];args[0]=animation;args[2]=time;offset.Invoke(helper,args);
            var expected=new Quaternion(cached.x,mirror?-cached.y:cached.y,mirror?-cached.z:cached.z,cached.w);
            bool Same(Quaternion a,Quaternion b)=>a.x==b.x&&a.y==b.y&&a.z==b.z&&a.w==b.w;
            bool pass=Same(sampled,cached)&&args[6] is Quaternion actual&&Same(actual,expected)
                &&new[]{3,4,5,7,8,9}.All(i=>args[i]==null);
            cases++;if(!pass)failed++;results.Add(new{finalTime,identity,mirror,time,pass});
        }
        using var module=ModuleDefinition.ReadModule(Path.Combine(gameRoot,"Plugins/NoodleExtensions.dll"));
        var update=module.Types.Single(t=>t.Name=="NoteUpdateNoodlifier").Methods.Single(m=>m.Name=="Prefix");
        var calls=update.Body.Instructions.Where(i=>i.Operand is MethodReference).Select(i=>(MethodReference)i.Operand).ToArray();
        if(!calls.Any(m=>m.Name=="get_InternalLocalRotation")||!calls.Any(m=>m.Name=="set_localRotation")
            ||!calls.Any(m=>m.DeclaringType.FullName=="UnityEngine.Quaternion"&&m.Name=="op_Multiply"))
            throw new InvalidDataException("Local rotation composition changed");
        return new{schemaVersion=1,profileVersion=BeatInsight.Formats.LocalMap.AnalysisProfileVersion,cases,failed,results,gameStarted=false,referenceUsed=false,
            scope="actual quaternion endpoint clamp and AnimationHelper with explicitly cached quaternions, both hands and nonidentity controls; pinned constructor/composition IL audited; Euler conversion, in-flight Slerp and Unity Transform lifecycle not executed"};
    }
}
