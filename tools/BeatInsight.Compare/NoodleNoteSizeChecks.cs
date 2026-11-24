using System.Collections;
using System.Reflection;
using System.Runtime.Loader;
using Mono.Cecil;
using Newtonsoft.Json.Linq;

namespace BeatInsight.Compare;

static class NoodleNoteSizeChecks
{
    public static object Run(string gameRoot)
    {
        string path=Path.Combine(gameRoot,"Plugins/NoodleExtensions.dll");
        if(Comparison.HashFile(path)!="FE50124E33631EED7ECB78EA1A19BB7EA64CC12974B0298DAD991672C2ABDA95")
            throw new InvalidDataException("Reinspect changed note size consumer");
        using var module=ModuleDefinition.ReadModule(path);
        IEnumerable<TypeDefinition> All(TypeDefinition t)=>new[]{t}.Concat(t.NestedTypes.SelectMany(All));
        var readers=module.Types.SelectMany(All).SelectMany(t=>t.Methods).Where(m=>m.HasBody&&m.Body.Instructions.Any(i=>i.OpCode.Name=="ldstr"&&i.Operand as string=="size")).ToArray();
        if(readers.Length!=1||readers[0].DeclaringType.FullName!="NoodleExtensions.NoodleObstacleData")
            throw new InvalidDataException("Size metadata has a new consumer; recheck note init/update assumptions");
        GameLoader.Load(gameRoot,"""{"version":"3.3.0","colorNotes":[],"obstacles":[]}""",120,noodleExtensions:true);
        var custom=AssemblyLoadContext.Default.Assemblies.Single(a=>a.GetName().Name=="CustomJSONData").GetType("CustomJSONData.CustomBeatmap.CustomData",true)!;
        var assembly=AssemblyLoadContext.Default.Assemblies.Single(a=>a.GetName().Name=="NoodleExtensions");
        var type=assembly.GetType("NoodleExtensions.NoodleNoteData",true)!;
        var ctor=type.GetConstructors(BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic).Single();
        var parameters=ctor.GetParameters();
        string[] fields={"StartX","StartY","ScaleX","ScaleY","ScaleZ","Njs","SpawnOffset","Fake","Uninteractable",
            "DisableBadCutDirection","DisableBadCutSaberType","DisableBadCutSpeed","DisableGravity","DisableLook"};
        PropertyInfo Property(string name)
        {
            for(Type? current=type;current!=null;current=current.BaseType)
                if(current.GetProperty(name,BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic|BindingFlags.DeclaredOnly) is PropertyInfo property)return property;
            throw new InvalidDataException("Changed note metadata property: "+name);
        }
        int cases=0,failed=0;var results=new List<object>();
        foreach(int color in new[]{0,1})foreach(int direction in new[]{0,1,8})foreach(bool mirror in new[]{false,true})foreach(bool explicitScale in new[]{false,true})
        {
            object Build(bool size)
            {
                string json=new JObject {["version"]="3.3.0",["colorNotes"]=new JArray(new JObject {["b"]=2,["x"]=1,["y"]=0,["c"]=color,["d"]=direction})}.ToString();
                var note=OracleBridge.GetItems(GameLoader.Load(gameRoot,json,120,noodleExtensions:true)).OfType<NoteData>().Single();
                if(mirror)note.Mirror(4);
                var metadata=(IDictionary)Activator.CreateInstance(custom)!;
                if(size)metadata["size"]=new List<object>{1f,1f,1f};
                if(explicitScale)metadata["scale"]=new List<object>{2f,1f,1f};
                return ctor.Invoke(new object[]{note,metadata,120f,Activator.CreateInstance(parameters[3].ParameterType)!,Activator.CreateInstance(parameters[4].ParameterType)!,false,mirror});
            }
            JObject Snapshot(object data)=>new(fields.Select(name=>new JProperty(name,Property(name).GetValue(data))));
            var baseline=Snapshot(Build(false));var withSize=Snapshot(Build(true));
            bool pass=JToken.DeepEquals(baseline,withSize)&&((float?)withSize["ScaleX"]==(explicitScale?2f:(float?)null));
            cases++;if(!pass)failed++;
            results.Add(new{color,direction,mirror,explicitScale,pass,baseline,withSize});
        }
        return new{cases,failed,sizeReaders=readers.Select(m=>m.FullName).ToArray(),results,
            scope="Installed real-note constructor pairs and whole-assembly size reader audit; existing init/update scale checks remain required; no Unity lifecycle"};
    }
}
