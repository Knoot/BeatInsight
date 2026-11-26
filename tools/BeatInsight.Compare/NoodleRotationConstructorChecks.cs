using System.Collections;
using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.CompilerServices;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace BeatInsight.Compare;

// Euler is a controlled host edge: outputs are opaque controls or captured
// game samples; this host does not execute Unity's native conversion.
static class NoodleRotationConstructorChecks
{
    const BindingFlags flags=BindingFlags.Public|BindingFlags.NonPublic|BindingFlags.Instance|BindingFlags.Static;
    sealed class EulerContext
    {
        internal readonly Dictionary<(float,float,float),Quaternion> Outputs=new();
        internal readonly List<(float,float,float)> Calls=new(),Expected=new();
    }
    static EulerContext? current;
    static int failureSinkCalls;
    public static Quaternion EulerVector(Vector3 value)
    {
        var key=(value.x,value.y,value.z);var context=current??throw new InvalidDataException("Euler escaped constructor probe");
        if(!context.Outputs.TryGetValue(key,out var output))throw new InvalidDataException("Unexpected native constructor Euler input: "+key);
        context.Calls.Add(key);return output;
    }
    public static Quaternion EulerThree(float x,float y,float z)=>EulerVector(new Vector3(x,y,z));
    public static void DeserializeFailure(object logger,Exception error,BeatmapObjectData item,float bpm)
    {failureSinkCalls++;throw new InvalidDataException("Native rotation constructor rejected source metadata",error);}
    static object Get(object value,string name)=>value.GetType().GetProperty(name,flags)?.GetValue(value)??value.GetType().GetField(name,flags)?.GetValue(value)
        ??throw new InvalidDataException("Rotation source member changed: "+name);
    internal static object Run(string gameRoot,RotationMathCapture? capture=null)
    {
        if(current!=null)throw new InvalidOperationException("Nested constructor rotation probe");
        failureSinkCalls=0;
        var noodle=AppDomain.CurrentDomain.GetAssemblies().Single(a=>a.GetName().Name=="NoodleExtensions");
        var custom=AppDomain.CurrentDomain.GetAssemblies().Single(a=>a.GetName().Name=="CustomJSONData");
        var game=AppDomain.CurrentDomain.GetAssemblies().Single(a=>a.GetName().Name=="DataModels");
        var noteType=noodle.GetType("NoodleExtensions.NoodleNoteData",true)!;
        var constructors=new Dictionary<ConstructorInfo,DynamicMethod>();var constructorAudits=new List<object>();int eulerSites=0,baseSites=0;
        var quaternionReader=custom.GetType("CustomJSONData.CustomBeatmap.CustomData",true)!.GetMethod("GetQuaternion",flags,null,new[]{typeof(string)},null)!;
        int readerEulerSites=0;
        var compiledReader=InstalledTranspilerCompiler.Compile(quaternionReader,typeof(HeckMapChecks).GetMethod(nameof(HeckMapChecks.Identity))!,(code,operand)=>
        {
            if(operand is MethodInfo m&&m.DeclaringType==typeof(Quaternion)&&m.Name=="Euler")
            {
                string name=m.GetParameters().Length==1?nameof(EulerVector):m.GetParameters().Length==3?nameof(EulerThree):throw new InvalidDataException("Unknown reader Euler signature");
                readerEulerSites++;return(OpCodes.Call,typeof(NoodleRotationConstructorChecks).GetMethod(name)!);
            }
            return(code,operand);
        },exceptionBlocks:true);
        if(readerEulerSites==0)throw new InvalidDataException("Quaternion metadata reader host edge changed");
        var hierarchy=new Stack<Type>();
        for(var t=noteType;t!=null&&t.Assembly==noodle;t=t.BaseType)hierarchy.Push(t);
        while(hierarchy.Count>0)
        {
            var type=hierarchy.Pop();var ctor=type.GetConstructors(flags).Single(c=>!c.IsStatic&&c.GetParameters().Length>2);
            int euler=0,bases=0;
            var compiled=InstalledTranspilerCompiler.Compile(ctor,typeof(HeckMapChecks).GetMethod(nameof(HeckMapChecks.Identity))!,(code,operand)=>
            {
                if(operand is ConstructorInfo parent&&constructors.TryGetValue(parent,out var body))
                {bases++;return(OpCodes.Call,body);}
                if(Equals(operand,quaternionReader))return(OpCodes.Call,compiledReader.Method);
                if(operand is MethodInfo m&&m.DeclaringType==typeof(Quaternion)&&m.Name=="Euler")
                {
                    string name=m.GetParameters().Length==1?nameof(EulerVector):m.GetParameters().Length==3?nameof(EulerThree):throw new InvalidDataException("Unknown Euler signature");
                    euler++;return(OpCodes.Call,typeof(NoodleRotationConstructorChecks).GetMethod(name)!);
                }
                if(operand is MethodInfo failure&&failure.DeclaringType?.FullName=="Heck.Deserialize.DeserializerExtensions"&&failure.Name=="DeserializeFailure")
                {
                    if(failure.GetParameters().Length!=4||failure.ReturnType!=typeof(void))throw new InvalidDataException("Constructor failure sink changed");
                    return(OpCodes.Call,typeof(NoodleRotationConstructorChecks).GetMethod(nameof(DeserializeFailure))!);
                }
                return(code,operand);
            },exceptionBlocks:true);
            constructors.Add(ctor,compiled.Method);eulerSites+=euler;baseSites+=bases;
            constructorAudits.Add(new{type=type.FullName,compiled.Original,compiled.Patched,eulerSites=euler,baseSites=bases});
        }
        if(constructors.Count!=3||eulerSites==0||baseSites!=2)throw new InvalidDataException("Native rotation constructor chain changed");
        var noteCtor=noteType.GetConstructors(flags).Single(c=>!c.IsStatic);var parameters=noteCtor.GetParameters();
        var converterPatch=custom.GetType("CustomJSONData.HarmonyPatches.ConvertersCustomify",true)!;
        var results=new List<object>();int failed=0,parsed=0,eulerCalls=0,negativeControls=0,recoveryControls=0;
        var controlMap=GameLoader.Load(gameRoot,"""{"version":"3.3.0","rotationEvents":[{"b":0,"e":0,"r":45}],"colorNotes":[{"b":1,"x":0,"y":1,"c":0,"d":1}]}""",120,noodleExtensions:true);
        var controlNote=OracleBridge.GetItems(controlMap).OfType<NoteData>().Single();
        foreach(var mode in new[]{(Major:2,Scalar:false),(Major:2,Scalar:true),(Major:3,Scalar:false)})
        {
            int major=mode.Major;string ns=major==2?"BeatmapDataLoaderVersion2_6_0AndEarlier":"BeatmapDataLoaderVersion3";
            var converterType=game.GetType(ns+".BeatmapDataLoader+ColorNoteConverter",true)!;
            var converter=InstalledTranspilerCompiler.Compile(converterType.GetMethod("Convert",flags)!,converterPatch.GetMethod(major==2?"ColorNoteConvertV2_6_0AndEarlier":"ColorNoteConvertV3",flags)!).Method;
            var bpmCtor=game.GetType("BpmTimeProcessor",true)!.GetConstructors(flags).Single(c=>c.GetParameters().Length==2&&c.GetParameters()[1].ParameterType.GenericTypeArguments[0].Name==(major==2?"EventData":"BpmChangeEventData"));
            var rotationCtor=game.GetType("RotationTimeProcessor",true)!.GetConstructors(flags).Single(c=>c.GetParameters().Length==1&&c.GetParameters()[0].ParameterType.GenericTypeArguments[0].Name==(major==2?"EventData":"RotationEventData"));
            JToken? invalidRotation=null;
            (NoteData Note,object Data) Parse(NoteData source,Quaternion? world,Quaternion? local,bool left)
            {
                if(current!=null)throw new InvalidOperationException("Nested Euler source parse");
                string key=major==2?"_customData":"customData";
                var worldAngles=world.HasValue&&capture!=null?capture.Angles(world.Value,mode.Scalar):mode.Scalar?new Vector3(0,23.75f,0):new Vector3(12.25f,23.75f,34.5f);
                var localAngles=local.HasValue&&capture!=null?capture.Angles(local.Value):new Vector3(-15.5f,-25.25f,35.125f);
                var metadata=new JObject();
                if(world.HasValue)metadata[major==2?"_rotation":"worldRotation"]=mode.Scalar?new JValue(worldAngles.y):new JArray(worldAngles.x,worldAngles.y,worldAngles.z);
                if(local.HasValue)metadata[major==2?"_localRotation":"localRotation"]=new JArray(localAngles.x,localAngles.y,localAngles.z);
                if(invalidRotation!=null)metadata[major==2?"_rotation":"worldRotation"]=invalidRotation.DeepClone();
                var json=major==2?new JObject{["_version"]="2.6.0",["_notes"]=new JArray(new JObject{["_time"]=source.beat,["_lineIndex"]=source.lineIndex,["_lineLayer"]=(int)source.noteLineLayer,["_type"]=(int)source.colorType,["_cutDirection"]=(int)source.cutDirection,[key]=metadata}),
                    ["_events"]=new JArray(new JObject{["_time"]=0,["_type"]=14,["_value"]=6},new JObject{["_time"]=2,["_type"]=15,["_value"]=3})}:
                    new JObject{["version"]="3.3.0",["colorNotes"]=new JArray(new JObject{["b"]=source.beat,["x"]=source.lineIndex,["y"]=(int)source.noteLineLayer,["c"]=(int)source.colorType,["d"]=(int)source.cutDirection,[key]=metadata}),
                    ["rotationEvents"]=new JArray(new JObject{["b"]=0,["e"]=0,["r"]=45},new JObject{["b"]=2,["e"]=1,["r"]=-15})};
                var saved=NoodleNativeData.Deserialize(gameRoot,json,major.ToString());
                var events=Get(saved,major==2?"events":"bpmEvents");
                var instance=Activator.CreateInstance(converterType,flags,null,new[]{bpmCtor.Invoke(new[]{(object)120f,events}),rotationCtor.Invoke(new[]{Get(saved,major==2?"events":"rotationEvents")})},null)!;
                var raw=((IEnumerable)Get(saved,major==2?"notes":"colorNotes")).Cast<object>().Single();
                var note=(NoteData)converter.Invoke(null,new[]{instance,raw})!;
                var nativeMetadata=Get(note,"customData");var data=RuntimeHelpers.GetUninitializedObject(noteType);
                var context=new EulerContext();
                void Register(Vector3 angles,Quaternion value){var tuple=(angles.x,angles.y,angles.z);context.Outputs[tuple]=value;context.Expected.Add(tuple);}
                if(world.HasValue)Register(worldAngles,world.Value);
                if(local.HasValue)Register(localAngles,local.Value);
                current=context;
                try
                {
                    constructors[noteCtor].Invoke(null,new[]{data,(object)note,nativeMetadata,120f,Activator.CreateInstance(parameters[3].ParameterType)!,Activator.CreateInstance(parameters[4].ParameterType)!,major==2,left});
                    if(!context.Calls.OrderBy(v=>v).SequenceEqual(context.Expected.OrderBy(v=>v)))
                        throw new InvalidDataException("Native rotation constructor changed Euler gate or call ownership");
                    parsed++;eulerCalls+=context.Calls.Count;
                    return(note,data);
                }
                finally{current=null;}
            }
            var report=JObject.FromObject(NoteWorldRotationCompositionChecks.Run(gameRoot,Parse,capture?.Worlds(mode.Scalar),capture?.Locals()));
            failed+=(int)report["failed"]!;results.Add(new{major,scalarWorld=mode.Scalar,composition=report});
            foreach(bool left in new[]{false,true})
            {
                int before=failureSinkCalls;bool rejected=false;invalidRotation=new JValue("invalid-rotation-control");
                try{Parse(controlNote,null,null,left);}
                catch(TargetInvocationException)
                {rejected=failureSinkCalls==before+1;}
                finally{invalidRotation=null;}
                if(!rejected||current!=null)throw new InvalidDataException("Malformed constructor rotation was accepted or left host state behind");
                negativeControls++;
            }
            var recovered=Parse(controlNote,null,null,false);
            if(noteType.GetProperty("WorldRotationQuaternion",flags)!.GetValue(recovered.Data)!=null
                ||noteType.GetProperty("LocalRotationQuaternion",flags)!.GetValue(recovered.Data)!=null||current!=null)
                throw new InvalidDataException("Constructor rotation state leaked after rejected metadata");
            recoveryControls++;
        }
        return new{cases=parsed,failed,eulerCalls,eulerSites,readerEulerSites,baseSites,negativeControls,recoveryControls,failureSinkCalls,
            eulerOrigin=capture==null?"opaque-control":"captured-native-samples",unity=capture?.Unity,constructorAudits,results,contextCleared=current==null,
            scope="Actual custom JSON v2/v3 parsing and color-note conversion, emitted complete native NoodleNoteData/base constructor chain and NoteInit rotation prefix; no backing-field injection. Euler receives exact source argument controls; eulerOrigin distinguishes opaque controls from replayed native samples captured in game. Failure logging is a throwing host sink, checked with malformed input and recovery controls. Native Euler is not executed in this host; runtime delegate registration, Transform and full scene/player pose are not executed; no production admission."};
    }
}
