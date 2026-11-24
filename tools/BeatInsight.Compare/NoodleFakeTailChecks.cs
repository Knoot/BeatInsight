using System.Collections;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.Loader;
using Newtonsoft.Json.Linq;

namespace BeatInsight.Compare;

static class NoodleFakeTailChecks
{
    public static object Run(string gameRoot)
    {
        foreach(var pin in new[]{("NoodleExtensions","FE50124E33631EED7ECB78EA1A19BB7EA64CC12974B0298DAD991672C2ABDA95"),
            ("CustomJSONData","4F9F8EFD8012159D8995B6D936E3C21B6AA6A35E319686FA3E9B04C2B6331F7C"),
            ("Heck","9E5163F09E122713543FB69F89799D1121FB712820E6B4350F7A30C5D790793C")})
            if(Comparison.HashFile(Path.Combine(gameRoot,"Plugins",pin.Item1+".dll"))!=pin.Item2)throw new InvalidDataException("Reinspect changed decorative fake consumer");
        const BindingFlags flags=BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic;
        var raw=GameLoader.Load(gameRoot,"""{"version":"3.3.0","colorNotes":[]}""",120,noodleExtensions:true);
        var custom=AssemblyLoadContext.Default.Assemblies.Single(a=>a.GetName().Name=="CustomJSONData");
        var metadataType=custom.GetType("CustomJSONData.CustomBeatmap.CustomData",true)!;
        var factory=custom.GetType("CustomJSONData.CustomBeatmap.CustomNoteData",true)!.GetMethod("CreateCustomBasicNoteData")!;
        var fp=factory.GetParameters();
        var noodle=AssemblyLoadContext.Default.Assemblies.Single(a=>a.GetName().Name=="NoodleExtensions");
        var noteCtor=noodle.GetType("NoodleExtensions.NoodleNoteData",true)!.GetConstructors(flags).Single();var cp=noteCtor.GetParameters();
        var objectType=noodle.GetType("NoodleExtensions.NoodleObjectData",true)!;
        var baseNoteType=noodle.GetType("NoodleExtensions.NoodleBaseNoteData",true)!;
        var helperType=noodle.GetType("NoodleExtensions.Animation.AnimationHelper",true)!;
        var offset=helperType.GetMethod("GetObjectOffset",flags)!;
        var definite=helperType.GetMethod("GetDefinitePositionOffset",flags)!;
        var processorType=noodle.GetType("NoodleExtensions.HarmonyPatches.ObjectProcessing.NoodleBeatmapObjectsInTimeRowProcessor",true)!;
        var baseProcessor=typeof(NoteData).Assembly.GetType("BeatmapObjectsInTimeRowProcessor",true)!;
        var all=noodle.GetType("NoodleExtensions.HarmonyPatches.ObjectProcessing.ProcessNotesNoodleDataInTimeRow",true)!.GetMethod("ProcessAllNotesInTimeRowPatch",BindingFlags.Static|BindingFlags.NonPublic)!;
        var pair=baseProcessor.GetMethod("HandlePerColorTypeTimeSliceContainerDidFinishTimeSlice",flags)!;
        var mirrorPostfix=noodle.GetType("NoodleExtensions.HarmonyPatches.ObjectProcessing.LeftHandedMirrorNoteData",true)!.GetMethod("FlipLineIndex",BindingFlags.Static|BindingFlags.Public|BindingFlags.NonPublic)!;
        var heck=AssemblyLoadContext.Default.Assemblies.Single(a=>a.GetName().Name=="Heck");
        var trackType=heck.GetType("Heck.Animation.Track",true)!;
        var deserializedCtor=heck.GetType("Heck.Deserialize.DeserializedData",true)!.GetConstructors(flags).Single();
        var managerType=noodle.GetType("NoodleExtensions.Managers.FakePatchesManager",true)!;
        var cuttable=managerType.GetMethod("GetCuttable",flags)!;
        object Slice(Type itemType,NoteData[] notes)
        {
            var type=baseProcessor.GetNestedType("TimeSliceContainer`1",flags)!.MakeGenericType(itemType);
            var slice=Activator.CreateInstance(type,flags,null,new object[]{4},null)!;
            foreach(var note in notes)type.GetMethod("AddWithoutNotifications",flags)!.Invoke(slice,new object[]{note});
            return slice;
        }
        int cases=0,failed=0,coincidentChanges=0,helperSamples=0;var failures=new List<object>();
        foreach(int color in new[]{0,1})foreach(int direction in new[]{0,1,8})foreach(bool mirror in new[]{false,true})
        foreach(bool interaction in new[]{false,true})foreach(bool sameRow in new[]{false,true})foreach(bool frames in new[]{false,true})
        {
            (JArray Real,bool CanCut,int Score,bool IsFake,bool RealUnbound) Build(bool includeFake)
            {
                var items=new List<NoteData>();var metadata=new List<IDictionary>();
                for(int i=0;i<(includeFake?3:2);i++)
                {
                    bool fake=i==2;float beat=fake&&!sameRow?8:2;
                    var data=(IDictionary)Activator.CreateInstance(metadataType)!;
                    if(fake)
                    {
                        data["NE_fake"]=true;data["uninteractable"]=interaction;data["disableNoteLook"]=true;data["disableNoteGravity"]=true;
                        data["track"]="decorative";data["noteJumpStartBeatOffset"]=444f;
                        var animation=(IDictionary)Activator.CreateInstance(metadataType)!;
                        animation["definitePosition"]=new List<object>{new List<object>{0f,10f,500f,0f}};
                        animation["scale"]=frames?new List<object>{new List<object>{20f,20f,20f,0f},new List<object>{20f,20f,20f,.49f}}:new List<object>{20f,20f,20f};
                        data["animation"]=animation;
                    }
                    var note=(NoteData)factory.Invoke(null,new object[]{beat*.5f,beat,0,i,Enum.ToObject(fp[4].ParameterType,i==1?2:0),Enum.ToObject(fp[5].ParameterType,color),Enum.ToObject(fp[6].ParameterType,direction),data,new Version(3,3,0)})!;
                    items.Add(note);metadata.Add(data);
                }
                var processor=Activator.CreateInstance(processorType,flags,null,new[]{(object)4,raw},null)!;
                foreach(var row in items.GroupBy(n=>n.time))
                {
                    var notes=row.ToArray();all.Invoke(null,new[]{processor,(object)4,Slice(typeof(BeatmapDataItem),notes)});
                    pair.Invoke(processor,new[]{Slice(typeof(NoteData),notes),(object)100f});
                }
                var dictionaries=deserializedCtor.GetParameters().Select(p=>Activator.CreateInstance(p.ParameterType)!).ToArray();
                var real=new JArray();object? fakeParsed=null;bool unbound=true;
                var tracks=(IDictionary)Activator.CreateInstance(cp[4].ParameterType)!;tracks["decorative"]=Activator.CreateInstance(trackType,true)!;
                for(int i=0;i<items.Count;i++)
                {
                    var note=items[i];if(mirror){note.Mirror(4);mirrorPostfix.Invoke(null,new object[]{note});}
                    var parsed=noteCtor.Invoke(new object[]{note,metadata[i],120f,Activator.CreateInstance(cp[3].ParameterType)!,tracks,false,mirror});
                    ((IDictionary)dictionaries[2])[note]=parsed;
                    if(i==2){fakeParsed=parsed;continue;}
                    unbound&=objectType.GetProperty("Track",flags)!.GetValue(parsed)==null;
                    var snapshot=new JObject {["direction"]=(int)note.cutDirection,["angle"]=note.cutDirectionAngleOffset,["scoring"]=(int)note.scoringType,
                        ["color"]=(int)note.colorType,["lane"] =note.lineIndex,["layer"]=(int)note.noteLineLayer,["beforeJumpLayer"]=(int)note.beforeJumpNoteLineLayer,
                        ["flipLane"]=note.flipLineIndex,["flipY"]=note.flipYSide};
                    foreach(string key in new[]{"InternalStartNoteLineLayer","InternalFlipLineIndex","InternalFlipYSide"})
                        snapshot[key]=JToken.FromObject(baseNoteType.GetProperty(key,flags)!.GetValue(parsed)??JValue.CreateNull());
                    real.Add(snapshot);
                }
                bool canCut=true;int score=0;bool fakeFlag=false;
                if(fakeParsed!=null)
                {
                    var manager=RuntimeHelpers.GetUninitializedObject(managerType);
                    managerType.GetField("_deserializedData",flags)!.SetValue(manager,deserializedCtor.Invoke(dictionaries));
                    canCut=(bool)cuttable.Invoke(manager,new object[]{items[2]})!;score=(int)items[2].scoringType;
                    fakeFlag=(bool?)objectType.GetProperty("Fake",flags)!.GetValue(fakeParsed)==true;
                    var helper=Activator.CreateInstance(helperType,flags,null,new object[]{mirror},null)!;
                    foreach(float local in new[]{0f,.49f,1f})
                    {
                        var animation=objectType.GetProperty("AnimationObject",flags)!.GetValue(fakeParsed);
                        var boundTracks=objectType.GetProperty("Track",flags)!.GetValue(fakeParsed);
                        object?[] args=new object?[offset.GetParameters().Length];args[0]=animation;args[1]=boundTracks;args[2]=local;
                        offset.Invoke(helper,args);helperSamples++;
                        object?[] position={animation,boundTracks,local,null};definite.Invoke(helper,position);
                        if(args[5]==null||position[3]==null)throw new InvalidDataException("Decorative animation control was not active");
                    }
                }
                return(real,canCut,score,fakeFlag,unbound);
            }
            var baseline=Build(false);var actual=Build(true);bool equal=JToken.DeepEquals(baseline.Real,actual.Real);
            if(sameRow&&!equal)coincidentChanges++;
            bool pass=(sameRow||equal)&&actual.CanCut==!interaction&&actual.Score==-1&&actual.IsFake&&actual.RealUnbound&&baseline.RealUnbound;
            cases++;if(!pass){failed++;if(failures.Count<12)failures.Add(new{color,direction,mirror,interaction,sameRow,frames,equal,actual.CanCut,actual.Score});}
        }
        if(coincidentChanges==0)throw new InvalidDataException("Missing same-row negative control");
        return new{cases,failed,coincidentChanges,helperSamples,definitePositionSamples=helperSamples,failures,
            scope="Installed custom note factory, row handlers, selected mirror/postfix, NoodleNoteData and Heck lookup/GetCuttable; active decorative animations, tail isolation and same-row negative controls; no fake-array loader, track event/environment execution, Unity lifecycle or support admission"};
    }
}
