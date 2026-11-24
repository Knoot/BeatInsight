using System.Collections;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.Loader;
using Mono.Cecil;

namespace BeatInsight.Compare;

static class NoodleCutFlagChecks
{
    public static object Run(string gameRoot)
    {
        foreach(var pin in new[]{("Plugins/NoodleExtensions.dll","FE50124E33631EED7ECB78EA1A19BB7EA64CC12974B0298DAD991672C2ABDA95"),
            ("Plugins/Heck.dll","9E5163F09E122713543FB69F89799D1121FB712820E6B4350F7A30C5D790793C"),
            ("Plugins/CustomJSONData.dll","4F9F8EFD8012159D8995B6D936E3C21B6AA6A35E319686FA3E9B04C2B6331F7C"),
            ("Beat Saber_Data/Managed/Main.dll","A276C8A50ACFAD69A3DD3A82CDE08571C3262344702B9B014C89756771B4495E")})
            if(Comparison.HashFile(Path.Combine(gameRoot,pin.Item1))!=pin.Item2)throw new InvalidDataException("Reinspect changed cut flag consumer");
        GameLoader.Load(gameRoot,"""{"version":"3.3.0","colorNotes":[]}""",120,noodleExtensions:true);
        const BindingFlags flags=BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic;
        var main=AssemblyLoadContext.Default.LoadFromAssemblyName(new AssemblyName("Main"));
        var custom=AssemblyLoadContext.Default.LoadFromAssemblyName(new AssemblyName("CustomJSONData"));
        var noodle=AssemblyLoadContext.Default.LoadFromAssemblyName(new AssemblyName("NoodleExtensions"));
        var heck=AssemblyLoadContext.Default.LoadFromAssemblyName(new AssemblyName("Heck"));
        var metadataType=custom.GetType("CustomJSONData.CustomBeatmap.CustomData",true)!;
        var factory=custom.GetType("CustomJSONData.CustomBeatmap.CustomNoteData",true)!.GetMethod("CreateCustomBasicNoteData")!;var fp=factory.GetParameters();
        var dataCtor=noodle.GetType("NoodleExtensions.NoodleNoteData",true)!.GetConstructors(flags).Single();var cp=dataCtor.GetParameters();
        var contextCtor=heck.GetType("Heck.Deserialize.DeserializedData",true)!.GetConstructors(flags).Single();
        var consumerType=noodle.GetType("NoodleExtensions.HarmonyPatches.Objects.GameNoteCutNoodlifier",true)!;
        var consumer=RuntimeHelpers.GetUninitializedObject(consumerType);var skip=consumerType.GetMethod("SkipBadCuts",flags)!;
        var controllerType=main.GetType("GameNoteController",true)!;
        var noteField=main.GetType("NoteController",true)!.GetField("_noteData",flags)!;
        int cases=0,failed=0;var failures=new List<object>();
        foreach(int major in new[]{2,3})foreach(int mask in Enumerable.Range(0,8))foreach(int color in new[]{0,1})foreach(int direction in new[]{0,1,8})foreach(bool registered in new[]{false,true})
        {
            var metadata=(IDictionary)Activator.CreateInstance(metadataType)!;
            string[] fields={"disableBadCutDirection","disableBadCutSpeed","disableBadCutSaberType"};
            for(int i=0;i<3;i++)metadata[fields[i]]=(mask&(1<<i))!=0;
            var note=(NoteData)factory.Invoke(null,new object[]{1f,2f,1,0,Enum.ToObject(fp[4].ParameterType,1),Enum.ToObject(fp[5].ParameterType,color),Enum.ToObject(fp[6].ParameterType,direction),metadata,new Version(major,0)})!;
            var parsed=dataCtor.Invoke(new object[]{note,metadata,120f,Activator.CreateInstance(cp[3].ParameterType)!,Activator.CreateInstance(cp[4].ParameterType)!,major==2,false});
            var dictionaries=contextCtor.GetParameters().Select(p=>Activator.CreateInstance(p.ParameterType)!).ToArray();
            if(registered)((IDictionary)dictionaries[2])[note]=parsed;
            consumerType.GetField("_deserializedData",flags)!.SetValue(consumer,contextCtor.Invoke(dictionaries));
            var controller=RuntimeHelpers.GetUninitializedObject(controllerType);noteField.SetValue(controller,note);
            foreach(int ok in Enumerable.Range(0,8))
            {
                bool directionOk=(ok&1)!=0,speedOk=(ok&2)!=0,saberOk=(ok&4)!=0;
                bool expected=registered&&major==3&&(((mask&1)!=0&&!directionOk)||((mask&2)!=0&&!speedOk)||((mask&4)!=0&&!saberOk));
                bool actual=(bool)skip.Invoke(consumer,new object[]{controller,directionOk,speedOk,saberOk})!;
                bool pass=actual==expected&&note.time==1&&note.beat==2&&note.lineIndex==0&&(int)note.noteLineLayer==1&&(int)note.colorType==color&&(int)note.cutDirection==direction;
                cases++;if(!pass){failed++;if(failures.Count<12)failures.Add(new{major,mask,color,direction,registered,ok,expected,actual,time=note.time,beat=note.beat,lane=note.lineIndex,layer=(int)note.noteLineLayer,nativeColor=(int)note.colorType,nativeDirection=(int)note.cutDirection});}
            }
        }
        using var module=ModuleDefinition.ReadModule(Path.Combine(gameRoot,"Plugins/NoodleExtensions.dll"));
        IEnumerable<TypeDefinition> All(TypeDefinition t)=>new[]{t}.Concat(t.NestedTypes.SelectMany(All));
        var methods=module.Types.SelectMany(All).SelectMany(t=>t.Methods).Where(m=>m.HasBody).ToArray();
        var readers=new List<object>();
        foreach(string name in new[]{"DisableBadCutDirection","DisableBadCutSpeed","DisableBadCutSaberType"})
        {
            var users=methods.Where(m=>m.Body.Instructions.Any(i=>i.Operand is MethodReference call&&call.Name=="get_"+name)).ToArray();
            if(users.Length!=1||users[0].Name!="SkipBadCuts")throw new InvalidDataException("Cut flag consumer changed: "+name);
            readers.Add(new{name,consumer=users[0].FullName});
        }
        return new{schemaVersion=1,cases,failed,failures,readers,gameStarted=false,referenceUsed=false,
            scope="actual note constructors, context lookup and SkipBadCuts truth table with isolated controller note field; registered/missing context, both colors and arrow/dot directions; NoteData primitives unchanged; v2 ignored-field control; full cut dispatch, score/energy and Unity lifecycle not executed"};
    }
}

