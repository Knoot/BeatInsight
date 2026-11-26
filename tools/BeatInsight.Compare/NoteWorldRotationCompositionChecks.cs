using System.Collections;
using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.CompilerServices;
using UnityEngine;
using Q=System.Numerics.Quaternion;

namespace BeatInsight.Compare;

static class NoteWorldRotationCompositionChecks
{
    const BindingFlags flags=BindingFlags.Public|BindingFlags.NonPublic|BindingFlags.Instance|BindingFlags.Static;
    sealed class Context
    {
        internal object Controller=null!;
        internal Transform Transform=null!;
        internal Quaternion Pose;
        internal int Writes,Reads,InverseCalls;
    }
    static Context? current;
    public static Transform TransformFor(Component controller)
    {
        var c=current!;if(!ReferenceEquals(c.Controller,controller))throw new InvalidDataException("Unexpected rotation controller");return c.Transform;
    }
    public static Quaternion ReadPose(Transform transform)
    {var c=current!;if(!ReferenceEquals(c.Transform,transform))throw new InvalidDataException("Unexpected rotation read");c.Reads++;return c.Pose;}
    public static void WritePose(Transform transform,Quaternion rotation)
    {var c=current!;if(!ReferenceEquals(c.Transform,transform))throw new InvalidDataException("Unexpected rotation write");c.Writes++;c.Pose=rotation;}
    public static Quaternion Inverse(Quaternion value)
    {
        current!.InverseCalls++;
        float norm=value.x*value.x+value.y*value.y+value.z*value.z+value.w*value.w;
        return new Quaternion(-value.x/norm,-value.y/norm,-value.z/norm,value.w/norm);
    }
    public static IEnumerable RotationPrefix(IEnumerable source)
    {
        var instructions=source.Cast<object>().ToArray();var instructionType=instructions[0].GetType();
        var operand=instructionType.GetField("operand")!;var opcode=instructionType.GetField("opcode")!;
        int stop=Array.FindIndex(instructions,i=>operand.GetValue(i) is MethodInfo m&&m.DeclaringType?.FullName=="NoodleExtensions.NoodleObjectData"&&m.Name=="get_ScaleX");
        if(stop<1||(OpCode)opcode.GetValue(instructions[stop-1])! != OpCodes.Ldloc_0)
            throw new InvalidDataException("Note rotation prefix boundary changed");
        // Keep the branch target's labels while replacing its first stack load.
        opcode.SetValue(instructions[stop-1],OpCodes.Ret);operand.SetValue(instructions[stop-1],null);
        return instructions.Take(stop).ToArray();
    }
    static FieldInfo Field(Type type,string name)
    {
        for(Type? t=type;t!=null;t=t.BaseType)if(t.GetField(name,flags|BindingFlags.DeclaredOnly) is FieldInfo f)return f;
        throw new MissingFieldException(type.FullName,name);
    }
    static Quaternion Axis(int axis,float degrees)
    {
        double r=degrees*Math.PI/360;float s=(float)Math.Sin(r),c=(float)Math.Cos(r);
        return new Quaternion(axis==0?s:0,axis==1?s:0,axis==2?s:0,c);
    }
    static Q Numerics(Quaternion q)=>new(q.x,q.y,q.z,q.w);
    static bool Close(Quaternion actual,Q expected)
    {
        var a=Numerics(actual);return Math.Min((a-expected).Length(),(a+expected).Length())<.00001;
    }
    internal static object Run(string gameRoot,Func<NoteData,Quaternion?,Quaternion?,bool,(NoteData Note,object Data)>? parsedFactory=null,
        Quaternion?[]? worldFixtures=null,Quaternion?[]? localFixtures=null)
    {
        if(current!=null)throw new InvalidOperationException("Nested note rotation probe");
        var map=GameLoader.Load(gameRoot,"""{"version":"3.3.0","rotationEvents":[{"b":0,"e":0,"r":45},{"b":2,"e":1,"r":-15}],"colorNotes":[{"b":0,"x":0,"y":1,"c":0,"d":1},{"b":1,"x":0,"y":1,"c":0,"d":1},{"b":2,"x":0,"y":1,"c":0,"d":1},{"b":2.001,"x":0,"y":1,"c":0,"d":1},{"b":4,"x":0,"y":1,"c":0,"d":1}]}""",120,noodleExtensions:true);
        var noodle=AppDomain.CurrentDomain.GetAssemblies().Single(a=>a.GetName().Name=="NoodleExtensions");
        var heck=AppDomain.CurrentDomain.GetAssemblies().Single(a=>a.GetName().Name=="Heck");
        var initType=noodle.GetType("NoodleExtensions.HarmonyPatches.Objects.NoteInitNoodlifier",true)!;
        var method=initType.GetMethod("Postfix",flags)!;
        int transformAdapters=0,inverseAdapters=0,readAdapters=0,writeAdapters=0;
        var compiled=InstalledTranspilerCompiler.Compile(method,typeof(NoteWorldRotationCompositionChecks).GetMethod(nameof(RotationPrefix))!,(code,operand)=>
        {
            if(operand is not MethodInfo m)return(code,operand);
            string? replace=null;
            if(m.DeclaringType==typeof(Component)&&m.Name=="get_transform"){transformAdapters++;replace=nameof(TransformFor);}
            else if(m.DeclaringType==typeof(Quaternion)&&m.Name=="Inverse"){inverseAdapters++;replace=nameof(Inverse);}
            else if(m.DeclaringType==typeof(Transform)&&m.Name=="get_localRotation"){readAdapters++;replace=nameof(ReadPose);}
            else if(m.DeclaringType==typeof(Transform)&&m.Name=="set_localRotation"){writeAdapters++;replace=nameof(WritePose);}
            return replace==null?(code,operand):(OpCodes.Call,typeof(NoteWorldRotationCompositionChecks).GetMethod(replace)!);
        });
        if(transformAdapters!=1||inverseAdapters!=1||readAdapters!=1||writeAdapters!=2)throw new InvalidDataException("Note rotation host edges changed");
        var dataType=initType.GetConstructors(flags).Single(c=>!c.IsStatic).GetParameters()[0].ParameterType;
        var dataCtor=dataType.GetConstructors(flags).Single(c=>!c.IsStatic);
        var data=dataCtor.Invoke(dataCtor.GetParameters().Select(p=>Activator.CreateInstance(p.ParameterType)).ToArray());
        var objects=(IDictionary)dataType.GetProperty("ObjectCustomDatas",flags)!.GetValue(data)!;
        // The full constructor also registers a runtime delegate. This prefix
        // fixture supplies only the native field it reads, without detours.
        var receiver=RuntimeHelpers.GetUninitializedObject(initType);
        Field(initType,"_deserializedData").SetValue(receiver,data);
        var noteCtor=noodle.GetType("NoodleExtensions.NoodleNoteData",true)!.GetConstructors(flags).Single(c=>!c.IsStatic);
        var p=noteCtor.GetParameters();
        var custom=AppDomain.CurrentDomain.GetAssemblies().Single(a=>a.GetName().Name=="CustomJSONData");
        var noteFactory=custom.GetType("CustomJSONData.CustomBeatmap.CustomNoteData",true)!.GetMethod("CreateCustomBasicNoteData",flags)!;
        var controllerType=method.GetParameters()[0].ParameterType;
        if(controllerType.IsAbstract)controllerType=controllerType.Assembly.GetType("GameNoteController",true)!;
        var controller=RuntimeHelpers.GetUninitializedObject(controllerType);
        var movement=RuntimeHelpers.GetUninitializedObject(method.GetParameters()[2].ParameterType);
        var jumpType=Field(movement.GetType(),"_jump").FieldType;var floorType=Field(movement.GetType(),"_floorMovement").FieldType;
        var jump=RuntimeHelpers.GetUninitializedObject(jumpType);var floor=RuntimeHelpers.GetUninitializedObject(floorType);
        Field(movement.GetType(),"_jump").SetValue(movement,jump);Field(movement.GetType(),"_floorMovement").SetValue(movement,floor);
        var transform=(Transform)RuntimeHelpers.GetUninitializedObject(typeof(Transform));
        var mirror=heck.GetType("Heck.MirrorExtensions",true)!.GetMethods(flags).Single(m=>m.Name=="Mirror"&&m.ReturnType==typeof(Quaternion)&&m.GetParameters().Length==2);
        Quaternion?[] worlds=worldFixtures??new Quaternion?[]{null,new Quaternion(0,0,0,1),Axis(0,30),Axis(1,90),Axis(2,45)};
        Quaternion?[] locals=localFixtures??new Quaternion?[]{null,new Quaternion(0,0,0,1),Axis(0,-20),Axis(2,17)};
        int cases=0,failed=0,missingControls=0,noncommuting=0;var failures=new List<object>();
        var notes=OracleBridge.GetItems(map).OfType<NoteData>().ToArray();int[] sceneGold={45,45,45,30,30};
        if(notes.Length!=5)throw new InvalidDataException("Rotation composition source notes changed");
        try
        {
            for(int n=0;n<notes.Length;n++)
            {
                var source=notes[n];if(source.rotation!=sceneGold[n])throw new InvalidDataException("Native scene rotation input differs from gold");
                var metadata=Activator.CreateInstance(p[1].ParameterType)!;
                var note=(NoteData)noteFactory.Invoke(null,new object[]{source.time,source.beat,source.rotation,source.lineIndex,source.noteLineLayer,source.colorType,source.cutDirection,metadata,new Version(3,3,0)})!;
                var noteData=noteCtor.Invoke(new[]{(object)note,metadata,120f,Activator.CreateInstance(p[3].ParameterType)!,Activator.CreateInstance(p[4].ParameterType)!,false,false});
                objects.Clear();objects.Add(note,noteData);
                foreach(var worldInput in worlds)foreach(var localInput in locals)foreach(bool left in new[]{false,true})foreach(bool tilted in new[]{false,true})
                {
                    var activeNote=note;var activeData=noteData;
                    if(parsedFactory!=null)
                    {
                        var parsed=parsedFactory(source,worldInput,localInput,left);activeNote=parsed.Note;activeData=parsed.Data;
                        objects.Clear();objects.Add(activeNote,activeData);
                        if(activeNote.rotation!=source.rotation||activeNote.time!=source.time||activeNote.beat!=source.beat)
                            throw new InvalidDataException("Parsed rotation fixture changed native scene/time inputs");
                    }
                    Quaternion? Mirror(Quaternion? q)=>q.HasValue?(Quaternion)mirror.Invoke(null,new object[]{q.Value,left})!:null;
                    var world=Mirror(worldInput);var local=Mirror(localInput);
                    if(parsedFactory==null)
                    {
                        Field(activeData.GetType(),"<WorldRotationQuaternion>k__BackingField").SetValue(activeData,world);
                        Field(activeData.GetType(),"<LocalRotationQuaternion>k__BackingField").SetValue(activeData,local);
                    }
                    var scene=Axis(1,left?-note.rotation:note.rotation);
                    var initial=scene*(tilted?Axis(2,-35):new Quaternion(0,0,0,1));
                    foreach(var target in new[]{jump,floor})
                    {
                        Field(target.GetType(),"_worldRotation").SetValue(target,scene);
                        var inverse=Q.Inverse(Numerics(scene));Field(target.GetType(),"_inverseWorldRotation").SetValue(target,new Quaternion(inverse.X,inverse.Y,inverse.Z,inverse.W));
                    }
                    current=new Context{Controller=controller,Transform=transform,Pose=initial};
                    compiled.Method.Invoke(null,new[]{receiver,controller,(object)activeNote,movement,Activator.CreateInstance(method.GetParameters()[3].ParameterType)!,1f,left});
                    Q GoldMirror(Quaternion q)=>left?new Q(q.x,-q.y,-q.z,q.w):Numerics(q);
                    var wg=worldInput.HasValue?GoldMirror(worldInput.Value):Numerics(scene);
                    var lg=localInput.HasValue?GoldMirror(localInput.Value):Q.Identity;
                    var poseGold=worldInput.HasValue?wg*lg:localInput.HasValue?Numerics(initial)*lg:Numerics(initial);
                    bool pass=Close(current.Pose,poseGold)&&current.Writes==(world.HasValue||local.HasValue?1:0)
                        &&current.Reads==(!world.HasValue&&local.HasValue?1:0)&&current.InverseCalls==(world.HasValue?1:0);
                    foreach(var target in new[]{jump,floor})
                        pass&=Close((Quaternion)Field(target.GetType(),"_worldRotation").GetValue(target)!,wg)
                            &&Close((Quaternion)Field(target.GetType(),"_inverseWorldRotation").GetValue(target)!,Q.Inverse(wg));
                    if(world.HasValue&&local.HasValue&&(wg*lg-lg*wg).Length()>.01f)noncommuting++;
                    cases++;if(!pass){failed++;failures.Add(new{n,left,tilted,world=world.HasValue,local=local.HasValue});}
                    current.Writes=current.Reads=current.InverseCalls=0;
                    compiled.Method.Invoke(null,new[]{receiver,controller,activeNote.GetCopy(),movement,Activator.CreateInstance(method.GetParameters()[3].ParameterType)!,1f,left});
                    if(current.Writes!=0||current.Reads!=0||current.InverseCalls!=0)throw new InvalidDataException("Unregistered note reached rotation host");
                    missingControls++;
                }
            }
        }
        finally{current=null;objects.Clear();}
        if(noncommuting==0)throw new InvalidDataException("Composition matrix has no ordering counterexamples");
        return new{cases,failed,missingControls,noncommuting,parsedConstructors=parsedFactory!=null,failures,compiled.Original,compiled.Patched,
            transformAdapters,inverseAdapters,readAdapters,writeAdapters,contextCleared=current==null,
            scope="Native loaded rotation events/converter inputs and actual NoteInit rotation prefix: native nullable gates, scene override selection and real NoteJump/NoteFloorMovement field writes, managed Unity quaternion product and native Heck mirror. "
                +(parsedFactory==null?"Quaternion inputs are analytic premises injected after data construction. ":"Quaternion inputs come through emitted native constructors with an explicit Euler host edge whose output provenance is recorded by the caller; no backing-field injection. ")
                +"Component/Transform and Inverse are explicit controlled host edges. Prefix stops before scale/movement remainder. No native Euler, real Transform, full frame/collider pose or admission proof."};
    }
}
