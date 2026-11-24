using System.Collections;
using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.Loader;
using Mono.Cecil;
using Mono.Cecil.Cil;
using EmitCodes=System.Reflection.Emit.OpCodes;

namespace BeatInsight.Compare;

static class NoodleInitialRotationChecks
{
    public static object Run(string gameRoot,ModuleDefinition main)
    {
        const BindingFlags flags=BindingFlags.Public|BindingFlags.NonPublic|BindingFlags.Instance;
        var raw=GameLoader.Load(gameRoot,"""{"version":"3.3.0","colorNotes":[],"obstacles":[]}""",120,noodleExtensions:true);
        var custom=AssemblyLoadContext.Default.Assemblies.Single(a=>a.GetName().Name=="CustomJSONData");
        var extension=AssemblyLoadContext.Default.Assemblies.Single(a=>a.GetName().Name=="NoodleExtensions");
        var metadataType=custom.GetType("CustomJSONData.CustomBeatmap.CustomData",true)!;
        var factory=custom.GetType("CustomJSONData.CustomBeatmap.CustomNoteData",true)!.GetMethod("CreateCustomBasicNoteData")!;
        var constructor=extension.GetType("NoodleExtensions.NoodleNoteData",true)!.GetConstructors(flags).Single();
        var constructorParameters=constructor.GetParameters();
        var mirrorPostfix=extension.GetType("NoodleExtensions.HarmonyPatches.ObjectProcessing.LeftHandedMirrorNoteData",true)!
            .GetMethod("FlipLineIndex",BindingFlags.Static|BindingFlags.NonPublic|BindingFlags.Public)!;
        var processorType=extension.GetType("NoodleExtensions.HarmonyPatches.ObjectProcessing.NoodleBeatmapObjectsInTimeRowProcessor",true)!;
        var baseProcessor=typeof(NoteData).Assembly.GetType("BeatmapObjectsInTimeRowProcessor",true)!;
        var pair=baseProcessor.GetMethod("HandlePerColorTypeTimeSliceContainerDidFinishTimeSlice",flags)!;
        var all=extension.GetType("NoodleExtensions.HarmonyPatches.ObjectProcessing.ProcessNotesNoodleDataInTimeRow",true)!
            .GetMethod("ProcessAllNotesInTimeRowPatch",BindingFlags.Static|BindingFlags.NonPublic)!;
        object Slice(Type itemType,NoteData[] notes)
        {
            var type=baseProcessor.GetNestedType("TimeSliceContainer`1",flags)!.MakeGenericType(itemType);
            var slice=Activator.CreateInstance(type,flags,null,new object[]{4},null)!;
            foreach(var note in notes)type.GetMethod("AddWithoutNotifications",flags)!.Invoke(slice,new object[]{note});
            return slice;
        }
        var initialize=CompileAngle(main,out int instructions);
        AuditForwarding(main);
        int[] mirrored={0,1,3,2,5,4,7,6,8};
        float[] rotations={-180,0,-90,90,-135,135,-45,45,0};
        var results=new List<object>();int cases=0,failed=0,orderControls=0,cacheEvictionControls=0;
        foreach(int major in new[]{2,3})foreach(bool mirror in new[]{false,true})
        foreach(int direction in Enumerable.Range(0,9))foreach(string companion in new[]{"same","dot","different"})
        foreach(var position in new[]{(X:0,Y:1),(X:1,Y:2),(X:2,Y:1),(X:1,Y:0)})
        foreach(bool coordinates in new[]{false,true})foreach(bool reverse in new[]{false,true})foreach(float? sourceAngle in new float?[]{null,17,-45})
        {
            int mate=companion=="same"?direction:companion=="dot"?8:(direction+1)%8;
            var sourceDirections=new[]{direction,mate};var sourceX=new[]{0,position.X};var sourceY=new[]{0,position.Y};
            var metadata=new object[2];var notes=new NoteData[2];
            for(int n=0;n<2;n++)
            {
                metadata[n]=Activator.CreateInstance(metadataType)!;var dictionary=(IDictionary)metadata[n];
                if(coordinates)dictionary[major==2?"_position":"coordinates"]=new List<object>{sourceX[n]-1f,sourceY[n]+.25f};
                if(major==2&&sourceAngle.HasValue)dictionary["_cutDirection"]=sourceAngle.Value;
                var p=factory.GetParameters();
                notes[n]=(NoteData)factory.Invoke(null,new object[]{1f,2f,0,sourceX[n],Enum.ToObject(p[4].ParameterType,sourceY[n]),
                    Enum.ToObject(p[5].ParameterType,0),Enum.ToObject(p[6].ParameterType,sourceDirections[n]),metadata[n],new Version(major,major==2?6:3,0)})!;
                if(major==3)notes[n].SetCutDirectionAngleOffset(sourceAngle??0);
            }
            var ordered=reverse?notes.Reverse().ToArray():notes;
            var processor=Activator.CreateInstance(processorType,flags,null,new[]{(object)4,raw},null)!;
            all.Invoke(null,new[]{processor,(object)4,Slice(typeof(BeatmapDataItem),ordered)});
            pair.Invoke(processor,new[]{Slice(typeof(NoteData),ordered),(object)100f});
            var expectedOffsets=Gold(sourceDirections,sourceX,sourceY,reverse,major==3?sourceAngle??0:0);
            var errors=new List<string>();
            for(int n=0;n<2;n++)
            {
                if(Math.Abs(notes[n].cutDirectionAngleOffset-expectedOffsets[n])>1e-4)errors.Add("Prepared row differs from independent angle gold");
                if(mirror)
                {
                    // Force every cache key to exist so an absent-key assertion
                    // cannot pass without exercising the installed invalidation.
                    var cached=(IDictionary)metadata[n];
                    foreach(string key in new[]{"NE_flipYSide","NE_flipLineIndex","NE_startNoteLineLayer","NE_tailStartNoteLineLayer"})cached[key]=.5f;
                    notes[n].Mirror(4);
                    mirrorPostfix.Invoke(null,new object[]{notes[n]});
                    cacheEvictionControls++;
                }
                int expectedDirection=mirror?mirrored[sourceDirections[n]]:sourceDirections[n];
                float expectedOffset=expectedOffsets[n]*(mirror?-1:1);
                if(major==2&&sourceAngle.HasValue)
                {
                    expectedDirection=sourceDirections[n]==8?8:1;expectedOffset=sourceAngle.Value*(mirror?-1:1);
                    if(Math.Abs(expectedOffset-expectedOffsets[n]*(mirror?-1:1))>.001f)orderControls++;
                }
                constructor.Invoke(new object[]{notes[n],metadata[n],120f,Activator.CreateInstance(constructorParameters[3].ParameterType)!,
                    Activator.CreateInstance(constructorParameters[4].ParameterType)!,major==2,mirror});
                float actual=initialize(notes[n]);float expected=rotations[expectedDirection]+expectedOffset;
                if((int)notes[n].cutDirection!=expectedDirection||Math.Abs(notes[n].cutDirectionAngleOffset-expectedOffset)>1e-4||Math.Abs(actual-expected)>1e-4)
                    errors.Add("Post-mirror constructor or controller rotation differs from gold");
                if(notes[n].lineIndex!=(mirror?3-sourceX[n]:sourceX[n])||(int)notes[n].colorType!=(mirror?1:0))errors.Add("Mirrored identity differs from gold");
                var dictionary=(IDictionary)metadata[n];
                if(coordinates)
                {
                    var point=(IList)dictionary[major==2?"_position":"coordinates"]!;
                    float expectedX=mirror?-sourceX[n]:sourceX[n]-1f;
                    if(Convert.ToSingle(point[0])!=expectedX||Convert.ToSingle(point[1])!=sourceY[n]+.25f)errors.Add("Custom coordinates reflected incorrectly");
                }
                if(mirror&&new[]{"NE_flipYSide","NE_flipLineIndex","NE_startNoteLineLayer","NE_tailStartNoteLineLayer"}.Any(dictionary.Contains))
                    errors.Add("Mirrored row cache was retained");
            }
            cases++;if(errors.Count>0)failed++;
            results.Add(new{major,mirror,direction,companion,position.X,position.Y,coordinates,reverse,sourceAngle,
                directions=notes.Select(n=>(int)n.cutDirection).ToArray(),offsets=notes.Select(n=>n.cutDirectionAngleOffset).ToArray(),
                controllerAngles=notes.Select(initialize).ToArray(),errors});
        }
        if(orderControls==0)throw new InvalidDataException("No legacy constructor-after-row controls were exercised");
        return new{cases,failed,copiedAngleInstructions=instructions,constructorAfterRowControls=orderControls,cacheEvictionControls,results,
            scope="actual custom note factory, Noodle all-note row patch and per-color pair processor, NoteData.Mirror plus explicit installed postfix, NoodleNoteData constructor and extracted controller angle IL; independent atan2 line-angle gold; whole-map mirror/copy and native Quaternion/Transform lifecycle not executed"};
    }

    static float[] Gold(int[] directions,int[] x,int[] y,bool reverse,float original)
    {
        var result=new[]{original,original};int first=reverse?1:0,second=1-first;
        if(directions[first]!=directions[second]&&directions[first]!=8&&directions[second]!=8)return result;
        int reference=directions[first]==8?second:first,other=1-reference;
        (double X,double Y)[] vectors={(0,1),(0,-1),(-1,0),(1,0),(-1,1),(1,1),(-1,-1),(1,-1),(0,1)};
        var vector=vectors[directions[reference]];double dx=x[other]-x[reference],dy=y[other]-y[reference];
        double dot=vector.X*dx+vector.Y*dy,cross=vector.X*dy-vector.Y*dx;
        double forward=Math.Atan2(cross,dot)*180/Math.PI,backward=Math.Atan2(-cross,-dot)*180/Math.PI;
        float offset=(float)(Math.Abs(forward)<Math.Abs(backward)?forward:backward);
        if(directions[reference]==8&&directions[other]==8){result[reference]=result[other]=offset;return result;}
        if(Math.Abs(offset)>40)return result;
        result[reference]=offset;result[other]=offset+(directions[other]==8&&directions[reference]>3?45:0);
        return result;
    }

    static Func<NoteData,float> CompileAngle(ModuleDefinition main,out int copied)
    {
        var method=main.Types.Single(t=>t.FullName=="GameNoteController").Methods.Single(m=>m.Name=="Init");
        var direction=method.Body.Instructions.Single(i=>i.Operand is MethodReference m&&m.DeclaringType.FullName=="NoteData"&&m.Name=="get_cutDirection");
        var body=new List<Instruction>();for(var i=direction.Previous;i!=null;i=i.Next){body.Add(i);if(i.OpCode.Code==Code.Add)break;}
        if(body.Count!=6)throw new InvalidDataException("Controller end angle expression changed");
        var dynamic=new DynamicMethod("InstalledNoteEndAngle",typeof(float),new[]{typeof(NoteData)},typeof(NoodleInitialRotationChecks).Module,true);
        var il=dynamic.GetILGenerator();
        foreach(var instruction in body)
        {
            if(instruction.OpCode.Code==Code.Ldarg_1)il.Emit(EmitCodes.Ldarg_0);
            else if(instruction.OpCode.Code==Code.Add)il.Emit(EmitCodes.Add);
            else if(instruction.Operand is MethodReference call&&call.DeclaringType.FullName=="NoteData"&&call.Name is "get_cutDirection" or "get_cutDirectionAngleOffset")
                il.Emit(EmitCodes.Callvirt,typeof(NoteData).GetProperty(call.Name[4..])!.GetMethod!);
            else if(instruction.Operand is MethodReference rotation&&rotation.DeclaringType.FullName=="NoteCutDirectionExtensions"&&rotation.Name=="RotationAngle")
                il.Emit(EmitCodes.Call,typeof(NoteCutDirectionExtensions).GetMethod("RotationAngle")!);
            else throw new InvalidDataException("Unvalidated controller angle instruction");
        }
        il.Emit(EmitCodes.Ret);copied=body.Count;return (Func<NoteData,float>)dynamic.CreateDelegate(typeof(Func<NoteData,float>));
    }

    static void AuditForwarding(ModuleDefinition main)
    {
        foreach(var link in new[]{("NoteController","NoteMovement"),("NoteMovement","NoteJump")})
        {
            var method=main.Types.Single(t=>t.FullName==link.Item1).Methods.Single(m=>m.Name=="Init");
            var call=method.Body.Instructions.Single(i=>i.Operand is MethodReference m&&m.DeclaringType.FullName==link.Item2&&m.Name=="Init");
            if(!method.Parameters.Any(p=>p.Name=="endRotation"))throw new InvalidDataException("Missing forwarded end rotation");
            var load=call.Previous.Previous.Previous;
            bool forwarded=load.OpCode.Code==Code.Ldarg_3&&method.Parameters[2].Name=="endRotation"
                ||load.Operand is ParameterDefinition p&&p.Name=="endRotation";
            if(!forwarded)throw new InvalidDataException("End rotation forwarding changed: "+link.Item1);
        }
        var jump=main.Types.Single(t=>t.FullName=="NoteJump").Methods.Single(m=>m.Name=="Init");
        var end=jump.Body.Instructions.Single(i=>i.OpCode.Code==Code.Stfld&&i.Operand is FieldReference f&&f.Name=="_endRotation");
        if(end.Previous.Operand is not MethodReference euler||euler.Name!="Euler"||end.Previous.Previous.Operand is not ParameterDefinition rotationParameter||rotationParameter.Name!="endRotation")
            throw new InvalidDataException("Jump end quaternion construction changed");
    }
}
