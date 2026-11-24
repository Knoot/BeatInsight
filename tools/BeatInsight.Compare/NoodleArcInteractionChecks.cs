using System.Collections;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.Loader;
using Mono.Cecil;

namespace BeatInsight.Compare;

static class NoodleArcInteractionChecks
{
    public static object Run(string gameRoot)
    {
        var audits=new List<object>();
        foreach(var pinned in new[]{("NoodleExtensions","FE50124E33631EED7ECB78EA1A19BB7EA64CC12974B0298DAD991672C2ABDA95"),("Heck","9E5163F09E122713543FB69F89799D1121FB712820E6B4350F7A30C5D790793C"),("CustomJSONData","4F9F8EFD8012159D8995B6D936E3C21B6AA6A35E319686FA3E9B04C2B6331F7C")})
        {
            string hash=Comparison.HashFile(Path.Combine(gameRoot,"Plugins",pinned.Item1+".dll"));
            if(hash!=pinned.Item2)throw new InvalidDataException("Reinspect changed arc interaction consumer: "+pinned.Item1);
            audits.Add(new{assembly=pinned.Item1,sha256=hash});
        }
        using var module=ModuleDefinition.ReadModule(Path.Combine(gameRoot,"Plugins/NoodleExtensions.dll"));
        IEnumerable<TypeDefinition> All(TypeDefinition t)=>new[]{t}.Concat(t.NestedTypes.SelectMany(All));
        var methods=module.Types.SelectMany(All).SelectMany(t=>t.Methods).Where(m=>m.HasBody).ToArray();
        var readers=methods.Where(m=>m.Body.Instructions.Any(i=>i.Operand is MethodReference r&&r.DeclaringType.FullName=="NoodleExtensions.NoodleObjectData"&&r.Name=="get_Uninteractable")).ToArray();
        var cutCalls=methods.Where(m=>m.Body.Instructions.Any(i=>i.Operand is MethodReference r&&r.DeclaringType.FullName=="NoodleExtensions.Managers.FakePatchesManager"&&r.Name=="GetCuttable")).ToArray();
        if(readers.Length!=3||readers.Any(m=>!(m.DeclaringType.FullName=="NoodleExtensions.NoodleObjectData"&&m.IsConstructor
            ||m.DeclaringType.FullName=="NoodleExtensions.Managers.FakePatchesManager"&&m.Name=="GetCuttable"
            ||m.DeclaringType.FullName=="NoodleExtensions.HarmonyPatches.Objects.ObstacleInitNoodlifier")))
            throw new InvalidDataException("Reinspect changed Uninteractable consumers");
        if(cutCalls.Length!=3||cutCalls.Any(m=>m.DeclaringType.FullName!="NoodleExtensions.HarmonyPatches.FakeNotes.MiscFakePatches"))
            throw new InvalidDataException("Reinspect changed GetCuttable call sites: "+string.Join("; ",cutCalls.Select(m=>m.FullName)));
        var game=GameLoader.Load(gameRoot,"""{"version":"3.3.0","colorNotes":[{"b":1,"x":0,"y":0,"c":0,"d":1}],"sliders":[{"b":1,"x":0,"y":0,"c":0,"d":1,"tb":2,"tx":1,"ty":0,"tc":1}]}""",120,noodleExtensions:true);
        var slider=OracleBridge.GetItems(game).OfType<SliderData>().Single();
        const BindingFlags flags=BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic;
        var custom=AssemblyLoadContext.Default.Assemblies.Single(a=>a.GetName().Name=="CustomJSONData");
        var metadataType=custom.GetType("CustomJSONData.CustomBeatmap.CustomData",true)!;
        var noodle=AssemblyLoadContext.Default.Assemblies.Single(a=>a.GetName().Name=="NoodleExtensions");
        var heck=AssemblyLoadContext.Default.LoadFromAssemblyPath(Path.Combine(gameRoot,"Plugins/Heck.dll"));
        var objectType=noodle.GetType("NoodleExtensions.NoodleObjectData",true)!;
        var sliderCtor=noodle.GetType("NoodleExtensions.NoodleSliderData",true)!.GetConstructors(flags).Single();
        var noteCtor=noodle.GetType("NoodleExtensions.NoodleNoteData",true)!.GetConstructors(flags).Single();
        var noteFactory=custom.GetType("CustomJSONData.CustomBeatmap.CustomNoteData",true)!.GetMethod("CreateCustomBasicNoteData")!;
        var fp=noteFactory.GetParameters();
        var deserialized=heck.GetType("Heck.Deserialize.DeserializedData",true)!;
        var deserializeCtor=deserialized.GetConstructors(flags).Single();
        var managerType=noodle.GetType("NoodleExtensions.Managers.FakePatchesManager",true)!;
        var cuttable=managerType.GetMethod("GetCuttable",flags)!;
        var results=new List<object>();int cases=0,failed=0;
        foreach(int major in new[]{2,3})foreach(bool? arcFlag in new bool?[]{null,false,true})foreach(bool headUninteractable in new[]{false,true})
        {
            cases++;var arcMetadata=(IDictionary)Activator.CreateInstance(metadataType)!;
            arcMetadata[major==2?"_interactable":"uninteractable"]=arcFlag;
            var noteMetadata=(IDictionary)Activator.CreateInstance(metadataType)!;
            noteMetadata[major==2?"_interactable":"uninteractable"]=major==2?!headUninteractable:headUninteractable;
            var head=noteFactory.Invoke(null,new object[]{.5f,1f,0,0,Enum.ToObject(fp[4].ParameterType,0),Enum.ToObject(fp[5].ParameterType,0),Enum.ToObject(fp[6].ParameterType,1),noteMetadata,new Version(major,0)})!;
            object Parse(ConstructorInfo ctor,object item,IDictionary metadata)
            {
                var p=ctor.GetParameters();
                return ctor.Invoke(new object[]{item,metadata,120f,Activator.CreateInstance(p[3].ParameterType)!,Activator.CreateInstance(p[4].ParameterType)!,major==2,false});
            }
            var arcData=Parse(sliderCtor,slider,arcMetadata);var headData=Parse(noteCtor,head,noteMetadata);
            var dictionaries=deserializeCtor.GetParameters().Select(p=>Activator.CreateInstance(p.ParameterType)!).ToArray();
            ((IDictionary)dictionaries[2])[slider]=arcData;((IDictionary)dictionaries[2])[head]=headData;
            var context=deserializeCtor.Invoke(dictionaries);
            // Construct just the receiver for the pure managed getter, without registering Harmony delegates.
            var manager=RuntimeHelpers.GetUninitializedObject(managerType);
            managerType.GetField("_deserializedData",flags)!.SetValue(manager,context);
            bool canCut=(bool)cuttable.Invoke(manager,new[]{head})!;
            bool arcCanCut=(bool)cuttable.Invoke(manager,new object[]{slider})!;
            bool? parsed=(bool?)objectType.GetProperty("Uninteractable",flags)!.GetValue(arcData);
            bool? expected=arcFlag.HasValue?(major==2?!arcFlag.Value:arcFlag.Value):null;
            bool pass=parsed==expected&&canCut==!headUninteractable&&arcCanCut==!(expected??false)&&objectType.GetProperty("Fake",flags)!.GetValue(arcData)==null;
            if(!pass)failed++;results.Add(new{major,arcFlag,headUninteractable,parsed,canCut,arcCanCut,pass});
        }
        return new{schemaVersion=1,profileVersion=BeatInsight.Formats.LocalMap.AnalysisProfileVersion,cases,failed,audits,
            consumers=readers.Select(m=>m.FullName).ToArray(),cuttableCallSites=cutCalls.Select(m=>m.FullName).ToArray(),results,
            gameStarted=false,referenceUsed=false,scope="actual arc/note constructors, Heck deserialized lookup and separate GetCuttable for arc/head; arc interaction is active but does not propagate to physical head; pinned IL audit; no Unity lifecycle"};
    }
}
