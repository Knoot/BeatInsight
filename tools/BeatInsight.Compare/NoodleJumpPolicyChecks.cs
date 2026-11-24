using System.Collections;
using BeatInsight.Formats;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.Loader;
using UnityEngine;

namespace BeatInsight.Compare;

static class NoodleJumpPolicyChecks
{
    static float HalfBeats(float njs,float bpm,float offset)
    {
        float beats=4,step=njs*(60/bpm);
        while(step*beats>18-.001f)beats*=.5f;
        return Math.Max(.25f,beats+offset);
    }
    public static object Run(string gameRoot)
    {
        foreach(var pin in new[]{("Beat Saber_Data/Managed/Main.dll","A276C8A50ACFAD69A3DD3A82CDE08571C3262344702B9B014C89756771B4495E"),
            ("Beat Saber_Data/Managed/DataModels.dll","B95112A1A581F52070195B419600591C98ADFB5E225DCB49C819F69B61C703B8"),
            ("Beat Saber_Data/Managed/Core.dll","BBCBE0D57CDCD3B5DD8E84AC9A59744A4B6C828ECD9BCE72D973C83B18D17097"),
            ("Plugins/NoodleExtensions.dll","FE50124E33631EED7ECB78EA1A19BB7EA64CC12974B0298DAD991672C2ABDA95"),
            ("Plugins/Heck.dll","9E5163F09E122713543FB69F89799D1121FB712820E6B4350F7A30C5D790793C"),
            ("Plugins/CustomJSONData.dll","4F9F8EFD8012159D8995B6D936E3C21B6AA6A35E319686FA3E9B04C2B6331F7C")})
            if(Comparison.HashFile(Path.Combine(gameRoot,pin.Item1))!=pin.Item2)throw new InvalidDataException("Reinspect changed jump policy provider");
        const BindingFlags flags=BindingFlags.Public|BindingFlags.NonPublic|BindingFlags.Instance;
        var main=AssemblyLoadContext.Default.LoadFromAssemblyPath(Path.Combine(gameRoot,"Beat Saber_Data/Managed/Main.dll"));
        var core=AssemblyLoadContext.Default.LoadFromAssemblyPath(Path.Combine(gameRoot,"Beat Saber_Data/Managed/Core.dll"));
        var noodle=AssemblyLoadContext.Default.LoadFromAssemblyPath(Path.Combine(gameRoot,"Plugins/NoodleExtensions.dll"));
        var heck=AssemblyLoadContext.Default.LoadFromAssemblyPath(Path.Combine(gameRoot,"Plugins/Heck.dll"));
        var custom=AssemblyLoadContext.Default.LoadFromAssemblyPath(Path.Combine(gameRoot,"Plugins/CustomJSONData.dll"));
        var selector=main.GetType("BeatmapObjectSpawnControllerHelpers",true)!.GetMethod("GetNoteJumpValues")!;
        var settingsType=selector.GetParameters()[0].ParameterType;
        var providerType=main.GetType("VariableMovementDataProvider",true)!;
        var init=providerType.GetMethod("Init",flags)!;
        var calculate=core.GetType("CoreMathUtils",true)!.GetMethod("CalculateHalfJumpDurationInBeats")!;
        var metadataType=custom.GetType("CustomJSONData.CustomBeatmap.CustomData",true)!;
        var obstacleCtor=custom.GetType("CustomJSONData.CustomBeatmap.CustomObstacleData",true)!.GetConstructors().Single();
        var parsedCtor=noodle.GetType("NoodleExtensions.NoodleObstacleData",true)!.GetConstructors(flags).Single();var pc=parsedCtor.GetParameters();
        var contextCtor=heck.GetType("Heck.Deserialize.DeserializedData",true)!.GetConstructors(flags).Single();
        var wrapperType=noodle.GetType("NoodleExtensions.Managers.NoodleMovementDataProvider",true)!;
        var movementType=main.GetType("BeatmapObjectSpawnMovementData",true)!;
        var initObject=wrapperType.GetMethod("InitObject",flags)!;
        var results=new List<object>();int cases=0,failed=0,resets=0,resolverComparisons=0;
        foreach(int mode in new[]{0,1})foreach(float mapOffset in new[]{-1f,0f,2f})foreach(float playerOffset in new[]{-1f,0f,1f})
        foreach(float fixedDuration in new[]{.25f,.5f,1f})foreach(float bpm in new[]{90f,120f,260f})foreach(float speed in new[]{10f,20f,30f})
        {
            var settings=RuntimeHelpers.GetUninitializedObject(settingsType);
            var modeProperty=settingsType.GetProperty("noteJumpDurationTypeSettings",flags)!;
            settingsType.GetField("_noteJumpDurationTypeSettings",flags)!.SetValue(settings,Enum.ToObject(modeProperty.PropertyType,mode));
            settingsType.GetField("_noteJumpStartBeatOffset",flags)!.SetValue(settings,playerOffset);
            settingsType.GetField("_noteJumpFixedDuration",flags)!.SetValue(settings,fixedDuration);
            object?[] selection={settings,mapOffset,null,null};selector.Invoke(null,selection);
            int selectedMode=Convert.ToInt32(selection[2]);float selected=(float)selection[3]!;
            float expectedValue=mode==0?mapOffset+playerOffset:fixedDuration;
            var original=RuntimeHelpers.GetUninitializedObject(providerType);
            object[] initArgs=init.GetParameters().Select(p=>p.Name switch {
                "startHalfJumpDurationInBeats"=>(object)4f,"maxHalfJumpDistance"=>18f,"noteJumpMovementSpeed"=>speed,"bpm"=>bpm,"minRelativeNoteJumpSpeed"=>0f,
                "noteJumpValueType"=>selection[2]!,"noteJumpValue"=>selected,"centerPosition"=>new Vector3(0,0,0),"forwardVector"=>new Vector3(0,0,1),
                _=>throw new InvalidDataException("Movement Init parameter changed: "+p.Name)}).ToArray();
            init.Invoke(original,initArgs);
            float Read(object obj,string name)=>(float)obj.GetType().GetProperty(name,flags)!.GetValue(obj)!;
            float expectedHalf=mode==0?HalfBeats(speed,bpm,expectedValue)*(60/bpm):expectedValue;
            float actualHalf=Read(original,"halfJumpDuration");
            float nativeBeats=(float)calculate.Invoke(null,new object[]{4f,18f,speed,60/bpm,mapOffset+playerOffset})!;
            bool pass=selectedMode==(mode==0?1:2)&&selected==expectedValue&&Math.Abs(actualHalf-expectedHalf)<1e-5
                &&Math.Abs(Read(original,"jumpDuration")-2*expectedHalf)<1e-5
                &&Math.Abs(nativeBeats-HalfBeats(speed,bpm,mapOffset+playerOffset))<1e-5;
            var snapshot=new JumpSettingsSnapshot(mode,playerOffset,fixedDuration);
            var own=WallJumpMovement.Resolve(snapshot,bpm,speed,mapOffset);
            pass&=Math.Abs(own.HalfJumpSeconds-actualHalf)<1e-5&&Math.Abs(own.JumpSeconds-Read(original,"jumpDuration"))<1e-5&&own.NoteJumpSpeed==Read(original,"noteJumpSpeed")&&Math.Abs(own.JumpDistance-Read(original,"jumpDistance"))<1e-5;
            resolverComparisons++;
            cases++;if(!pass)failed++;
            foreach(string variant in new[]{"none","speed","offset","both"})
            {
                var metadata=(IDictionary)Activator.CreateInstance(metadataType)!;
                bool hasSpeed=variant=="speed"||variant=="both",hasOffset=variant=="offset"||variant=="both";
                if(hasSpeed)metadata["noteJumpMovementSpeed"]=24f;
                if(hasOffset)metadata["noteJumpStartBeatOffset"]=.75f;
                var obstacle=obstacleCtor.Invoke(new object[]{10f,20f,2f,0,2,Enum.ToObject(obstacleCtor.GetParameters()[5].ParameterType,2),1f,3,3,metadata,new Version(3,0)});
                var parsed=parsedCtor.Invoke(new object[]{obstacle,metadata,bpm,Activator.CreateInstance(pc[3].ParameterType)!,Activator.CreateInstance(pc[4].ParameterType)!,false,false});
                var dictionaries=contextCtor.GetParameters().Select(p=>Activator.CreateInstance(p.ParameterType)!).ToArray();
                ((IDictionary)dictionaries[2])[obstacle]=parsed;
                var context=contextCtor.Invoke(dictionaries);
                var movement=RuntimeHelpers.GetUninitializedObject(movementType);
                movementType.GetField("_startHalfJumpDurationInBeats",flags)!.SetValue(movement,4f);
                movementType.GetField("_maxHalfJumpDistance",flags)!.SetValue(movement,18f);
                var wrapper=RuntimeHelpers.GetUninitializedObject(wrapperType);
                foreach(var pair in new (string Field,object Value)[]{("_original",original),("_deserializedData",context),("_movementData",movement),
                    ("_oneBeatDuration",60/bpm),("_noteJumpStartBeatOffset",selected),("_noteJumpValueType",selection[2]!)})
                    wrapperType.GetField(pair.Field,flags)!.SetValue(wrapper,pair.Value);
                initObject.Invoke(wrapper,new[]{obstacle});
                float effectiveSpeed=hasSpeed?24:speed,effectiveValue=hasOffset?.75f:selected;
                float expected=variant=="none"?expectedHalf:mode==0?HalfBeats(effectiveSpeed,bpm,effectiveValue)*(60/bpm):effectiveValue;
                bool overridePass=Math.Abs(Read(wrapper,"halfJumpDuration")-expected)<1e-5
                    &&Math.Abs(Read(wrapper,"jumpDuration")-2*expected)<1e-5&&Read(wrapper,"noteJumpSpeed")==effectiveSpeed;
                var ownOverride=WallJumpMovement.Resolve(snapshot,bpm,speed,mapOffset,hasSpeed?24f:(float?)null,hasOffset?.75f:(float?)null);
                overridePass&=Math.Abs(ownOverride.HalfJumpSeconds-Read(wrapper,"halfJumpDuration"))<1e-5
                    &&Math.Abs(ownOverride.JumpSeconds-Read(wrapper,"jumpDuration"))<1e-5&&ownOverride.NoteJumpSpeed==Read(wrapper,"noteJumpSpeed")&&Math.Abs(ownOverride.JumpDistance-Read(wrapper,"jumpDistance"))<1e-5;
                resolverComparisons++;
                // Reuse the same wrapper for an object with no custom overrides.
                ((IDictionary)dictionaries[2]).Remove(obstacle);initObject.Invoke(wrapper,new[]{obstacle});
                overridePass&=Math.Abs(Read(wrapper,"halfJumpDuration")-expectedHalf)<1e-5&&Math.Abs(Read(wrapper,"jumpDuration")-2*expectedHalf)<1e-5&&Read(wrapper,"noteJumpSpeed")==speed;
                resets++;cases++;if(!overridePass)failed++;
                if(!overridePass&&results.Count<15)results.Add(new{mode,mapOffset,playerOffset,fixedDuration,bpm,speed,variant,expected});
            }
        }
        int boundaryCases=0;
        foreach(float bpm in new[]{90f,120f,260f})foreach(float beats in new[]{1f,2f,4f})
        {
            float threshold=(18-.001f)/(beats*(60/bpm));
            foreach(float speed in new[]{MathF.BitDecrement(threshold),threshold,MathF.BitIncrement(threshold)})
            {
                float native=(float)calculate.Invoke(null,new object[]{4f,18f,speed,60/bpm,0f})!;
                var own=WallJumpMovement.Resolve(new JumpSettingsSnapshot(0,0,.5f),bpm,speed,0);
                bool pass=native==HalfBeats(speed,bpm,0)&&own.HalfJumpSeconds==native*(60/bpm);
                resolverComparisons++;
                cases++;boundaryCases++;if(!pass){failed++;if(results.Count<15)results.Add(new{bpm,beats,speed,native,expected=HalfBeats(speed,bpm,0)});}
            }
        }
        return new{schemaVersion=1,cases,failed,resets,boundaryCases,resolverComparisons,failures=results,gameStarted=false,referenceUsed=false,
            scope="actual GetNoteJumpValues with isolated settings, Core half-jump math, VariableMovementDataProvider.Init and Noodle InitObject overrides/reset; map/player offsets and fixed policy; no real user settings, variable speed events, gameplay modifiers, collision interval integration; production constant-speed resolver compared against native results"};
    }
}
