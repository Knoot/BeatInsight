using System.Collections;
using System.Reflection;
using System.Runtime.Loader;
using Mono.Cecil;
using Newtonsoft.Json.Linq;

namespace BeatInsight.Compare;

static class NoodleDissolveChecks
{
    public static object Run(string gameRoot)
    {
        var audits=new List<object>();
        foreach(var pinned in new[]{("NoodleExtensions","FE50124E33631EED7ECB78EA1A19BB7EA64CC12974B0298DAD991672C2ABDA95"),("Heck","9E5163F09E122713543FB69F89799D1121FB712820E6B4350F7A30C5D790793C")})
        {
            string path=Path.Combine(gameRoot,"Plugins",pinned.Item1+".dll");
            string hash=Comparison.HashFile(path);
            if(hash!=pinned.Item2)throw new InvalidDataException("Reinspect changed dissolve consumer: "+pinned.Item1);
            using var module=ModuleDefinition.ReadModule(path);
            audits.Add(new {assembly=pinned.Item1,sha256=hash});
        }
        var heck=AssemblyLoadContext.Default.LoadFromAssemblyPath(Path.Combine(gameRoot,"Plugins/Heck.dll"));
        var noodle=AssemblyLoadContext.Default.LoadFromAssemblyPath(Path.Combine(gameRoot,"Plugins/NoodleExtensions.dll"));
        var trackType=heck.GetType("Heck.Animation.Track",true)!;
        var propertyType=heck.GetType("Heck.Animation.Property`1",true)!.MakeGenericType(typeof(float));
        var helperType=noodle.GetType("NoodleExtensions.Animation.AnimationHelper",true)!;
        var method=helperType.GetMethod("GetObjectOffset",BindingFlags.Public|BindingFlags.NonPublic|BindingFlags.Instance)!;
        var results=new List<object>();int cases=0,failed=0;
        foreach(string key in new[]{"dissolve","dissolveArrow"})foreach(float value in new[]{0f,.25f,1f})foreach(int count in new[]{1,2})
        {
            cases++;var tracks=Array.CreateInstance(trackType,count);
            for(int i=0;i<count;i++)
            {
                var track=Activator.CreateInstance(trackType,true)!;
                var property=Activator.CreateInstance(propertyType,true)!;
                propertyType.GetProperty("Value",BindingFlags.Public|BindingFlags.NonPublic|BindingFlags.Instance)!.SetValue(property,value);
                ((IDictionary)trackType.GetField("_properties",BindingFlags.NonPublic|BindingFlags.Instance)!.GetValue(track)!)[key]=property;
                tracks.SetValue(track,i);
            }
            object?[] args=new object?[method.GetParameters().Length];args[1]=tracks;args[2]=.5f;
            method.Invoke(Activator.CreateInstance(helperType,BindingFlags.Public|BindingFlags.NonPublic|BindingFlags.Instance,null,new object[]{false},null),args);
            float expected=count==1?value:value*value;
            bool success=args.Skip(3).Take(4).All(v=>v==null)&&args[9]==null
                &&Equals(args[key=="dissolve"?7:8],expected)&&args[key=="dissolve"?8:7]==null;
            if(!success)failed++;
            results.Add(new {key,value,count,expected,geometryUnchanged=args.Skip(3).Take(4).All(v=>v==null),interactable=args[9],success});
        }
        return new {schemaVersion=1,cases,failed,audits,results,gameStarted=false,referenceUsed=false,
            scope="actual installed AnimationHelper with explicit scalar track properties; visual outputs separate from geometry/interactable; event execution and Unity rendering not run"};
    }
}
