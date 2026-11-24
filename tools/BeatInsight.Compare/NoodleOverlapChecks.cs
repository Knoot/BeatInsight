using System.Reflection;
using System.Linq.Expressions;
using System.Reflection.Emit;
using System.Runtime.Loader;
using System.Runtime.CompilerServices;
using BeatInsight.Core;
using BeatInsight.Formats;
using Newtonsoft.Json.Linq;
namespace BeatInsight.Compare;

static class NoodleOverlapChecks
{
    public static object Run(string gameRoot,string reportPath)
    {
        const BindingFlags flags=BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic;
        var raw=GameLoader.Load(gameRoot,"""{"version":"3.3.0","colorNotes":[],"obstacles":[]}""",120,noodleExtensions:true);
        var custom=AssemblyLoadContext.Default.Assemblies.Single(a=>a.GetName().Name=="CustomJSONData");
        var dataType=custom.GetType("CustomJSONData.CustomBeatmap.CustomData",true)!;
        object Metadata()=>Activator.CreateInstance(dataType)!;
        var noteFactory=custom.GetType("CustomJSONData.CustomBeatmap.CustomNoteData",true)!.GetMethod("CreateCustomBasicNoteData")!;
        NoteData Note(int x,int y,int direction,int color=0,float time=1,int major=3)
        {
            var p=noteFactory.GetParameters();return (NoteData)noteFactory.Invoke(null,new object[]{time,time*2,0,x,Enum.ToObject(p[4].ParameterType,y),Enum.ToObject(p[5].ParameterType,color),Enum.ToObject(p[6].ParameterType,direction),Metadata(),new Version(major,major==2?6:3,0)})!;
        }
        var sliderType=custom.GetType("CustomJSONData.CustomBeatmap.CustomSliderData",true)!;
        SliderData Slider(bool burst,bool tailAtHead=false,int direction=1,string? extendedBase=null,int major=3,bool zeroDuration=false,int color=0,int slices=4,int headX=0,int headY=0,int tailX=1,int tailY=2)
        {
            var factory=sliderType.GetMethod(burst?"CreateCustomBurstSliderData":"CreateCustomSliderData")!;
            var p=factory.GetParameters();var a=new object?[p.Length];
            for(int i=0;i<a.Length;i++)
            {
                a[i]=p[i].ParameterType.IsValueType?Activator.CreateInstance(p[i].ParameterType):null;
                if(p[i].Name=="customData")
                {
                    a[i]=Metadata();
                    if(extendedBase!=null)
                    {
                        var data=(System.Collections.IDictionary)a[i]!;
                        data[major==2?"_position":"coordinates"]=new List<object>{-2f,0f};
                        data["tailCoordinates"]=new List<object>{-1f,2f};
                    }
                }
                else if(p[i].Name=="version")a[i]=new Version(major,major==2?6:3,0);
                else if(p[i].Name=="headTime")a[i]=tailAtHead?0f:1f;
                else if(p[i].Name=="headBeat")a[i]=tailAtHead?0f:2f;
                else if(p[i].Name=="tailTime")a[i]=zeroDuration?1f:tailAtHead?1f:2f;
                else if(p[i].Name=="colorType")a[i]=Enum.ToObject(p[i].ParameterType,color);
                else if(p[i].Name=="headLineIndex")a[i]=headX;
                else if(p[i].Name=="headLineLayer")a[i]=Enum.ToObject(p[i].ParameterType,headY);
                else if(p[i].Name=="tailLineIndex")a[i]=tailAtHead?0:tailX;
                else if(p[i].Name=="tailLineLayer")a[i]=Enum.ToObject(p[i].ParameterType,tailAtHead?0:tailY);
                else if(p[i].Name=="headCutDirection"||p[i].Name=="tailCutDirection")a[i]=Enum.ToObject(p[i].ParameterType,direction);
                else if(p[i].Name=="sliceCount")a[i]=slices;
                else if(p[i].Name=="squishAmount")a[i]=1f;
                if(extendedBase=="head"||extendedBase=="both")
                {
                    if(p[i].Name=="headLineIndex")a[i]=6;
                    if(p[i].Name=="headLineLayer")a[i]=Enum.ToObject(p[i].ParameterType,-2);
                }
                if(extendedBase=="tail"||extendedBase=="both")
                {
                    if(p[i].Name=="tailLineIndex")a[i]=-4;
                    if(p[i].Name=="tailLineLayer")a[i]=Enum.ToObject(p[i].ParameterType,7);
                }
            }
            return (SliderData)factory.Invoke(null,a)!;
        }
        var core=typeof(NoteData).Assembly;var baseProcessor=core.GetType("BeatmapObjectsInTimeRowProcessor",true)!;
        var genericSlice=baseProcessor.GetNestedType("TimeSliceContainer`1",flags)!;
        object Slice(Type itemType,IEnumerable<object> items)
        {
            var type=genericSlice.MakeGenericType(itemType);var value=Activator.CreateInstance(type,flags,null,new object[]{8},null)!;
            foreach(var item in items)type.GetMethod("AddWithoutNotifications",flags)!.Invoke(value,new[]{item});
            return value;
        }
        var noodle=AssemblyLoadContext.Default.Assemblies.Single(a=>a.GetName().Name=="NoodleExtensions");
        var processorType=noodle.GetType("NoodleExtensions.HarmonyPatches.ObjectProcessing.NoodleBeatmapObjectsInTimeRowProcessor",true)!;
        var patch=noodle.GetType("NoodleExtensions.HarmonyPatches.ObjectProcessing.ProcessNotesNoodleDataInTimeRow",true)!.GetMethod("ProcessAllNotesInTimeRowPatch",BindingFlags.Static|BindingFlags.NonPublic)!;
        var pair=baseProcessor.GetMethod("HandlePerColorTypeTimeSliceContainerDidFinishTimeSlice",flags)!;
        var results=new List<object>();int failed=0,cases=0;
        int zeroTailDispatchCases=0;
        foreach(int color in new[]{0,1})foreach(int direction in new[]{1,8})foreach(bool selfTail in new[]{false,true})
        foreach(bool secondChain in new[]{false,true})foreach(bool reverse in new[]{false,true})foreach(bool nextRow in new[]{false,true})
        {
            cases++;zeroTailDispatchCases++;
            var head=Note(0,0,direction,color);var neighbor=Note(1,2,1,1-color);
            var burst=Slider(true,direction:direction,zeroDuration:true,color:color,slices:1,tailX:selfTail?0:1,tailY:selfTail?0:2);
            var other=Slider(true,direction:1,zeroDuration:true,color:1-color,slices:1,headX:1,headY:2);
            var processor=Activator.CreateInstance(processorType,flags,null,new[]{(object)4,raw},null)!;
            var container=baseProcessor.GetField("_currentTimeSliceAllNotesAndSliders",flags)!.GetValue(processor)!;
            var add=container.GetType().GetMethod("add_didFinishTimeSliceEvent",flags)!;
            var callbackType=add.GetParameters()[0].ParameterType;var parameters=callbackType.GetMethod("Invoke")!.GetParameters().Select(p=>Expression.Parameter(p.ParameterType)).ToArray();
            var callback=Expression.Lambda(callbackType,Expression.Call(patch,Expression.Constant(processor,patch.GetParameters()[0].ParameterType),Expression.Constant(4),parameters[0]),parameters).Compile();
            add.Invoke(container,new object[]{callback});
            var processNote=baseProcessor.GetMethod("ProcessNote",flags)!;var processSlider=baseProcessor.GetMethod("ProcessSlider",flags)!;
            foreach(var note in reverse?new[]{neighbor,head}:new[]{head,neighbor})processNote.Invoke(processor,new object[]{note});
            var sliders=secondChain?(reverse?new[]{other,burst}:new[]{burst,other}):new[]{burst};
            foreach(var slider in sliders)processSlider.Invoke(processor,new object[]{slider});
            if(nextRow)processNote.Invoke(processor,new object[]{Note(3,0,1,time:2)});
            baseProcessor.GetMethod("ProcessAllRemainingData",flags)!.Invoke(processor,null);
            bool pass=(int)head.scoringType==4&&(int)head.gameplayType==2&&(int)neighbor.scoringType==(secondChain?4:1)
                &&(int)neighbor.gameplayType==(secondChain?2:0)&&head.cutDirectionAngleOffset==0;
            if(!pass)failed++;results.Add(new{kind="zero-single-tail-native-dispatch",color,direction,selfTail,secondChain,reverse,nextRow,headRole=(int)head.scoringType,neighborRole=(int)neighbor.scoringType,status=pass?"pass":"mismatch"});
        }
        foreach(int color in new[]{0,1})foreach(int direction in Enumerable.Range(0,9))foreach(int slices in new[]{1,4})
        {
            cases++;var head=Note(0,0,direction,color);var burst=Slider(true,direction:direction,zeroDuration:true,color:color,slices:slices);
            var processor=Activator.CreateInstance(processorType,flags,null,new[]{(object)4,raw},null)!;
            var colorSlice=Slice(typeof(NoteData),new object[]{head});var all=Slice(typeof(BeatmapDataItem),new object[]{head,burst});
            pair.Invoke(processor,new[]{colorSlice,(object)100f});patch.Invoke(null,new[]{processor,(object)4,all});
            bool pass=burst.time==burst.tailTime&&(int)head.scoringType==4&&(int)head.gameplayType==2&&head.time==1&&head.cutDirectionAngleOffset==0;
            if(!pass)failed++;results.Add(new {kind="zero-duration-real-head",color,direction,slices,status=pass?"pass":"mismatch"});
        }
        foreach(string order in new[]{"arc-first","burst-first"})foreach(bool pairFirst in new[]{false,true})
        {
            cases++;var head=Note(0,0,1);var other=Note(1,2,1);var burst=Slider(true);var arc=Slider(false);
            var processor=Activator.CreateInstance(processorType,flags,null,new[]{(object)4,raw},null)!;
            var colorSlice=Slice(typeof(NoteData),new object[]{head,other});
            var items=order=="arc-first"?new object[]{head,other,arc,burst}:new object[]{head,other,burst,arc};
            var all=Slice(typeof(BeatmapDataItem),items);
            if(pairFirst)pair.Invoke(processor,new[]{colorSlice,(object)100f});
            patch.Invoke(null,new[]{processor,(object)4,all});
            if(!pairFirst)pair.Invoke(processor,new[]{colorSlice,(object)100f});
            int expected=order=="arc-first"?4:2;
            bool pass=(int)head.scoringType==expected&&(int)head.gameplayType==2&&Math.Abs(head.cutDirectionAngleOffset+26.565051f)<1e-4;
            if(!pass)failed++;results.Add(new {order,pairFirst,score=(int)head.scoringType,gameplay=(int)head.gameplayType,head.cutDirectionAngleOffset,status=pass?"pass":"mismatch"});
        }

        foreach(bool pairFirst in new[]{false,true})
        {
            cases++;var head=Note(0,0,1);var other=Note(1,2,1);var burst=Slider(true);var arc=Slider(false,tailAtHead:true);
            var processor=Activator.CreateInstance(processorType,flags,null,new[]{(object)4,raw},null)!;
            var tailType=baseProcessor.GetNestedType("SliderTailData",flags)!;
            var tail=Activator.CreateInstance(tailType,flags,null,new object[]{arc},null)!;
            var color=Slice(typeof(NoteData),new object[]{head,other});var all=Slice(typeof(BeatmapDataItem),new object[]{head,other,burst,tail});
            if(pairFirst)pair.Invoke(processor,new[]{color,(object)100f});
            patch.Invoke(null,new[]{processor,(object)4,all});
            if(!pairFirst)pair.Invoke(processor,new[]{color,(object)100f});
            bool pass=(int)head.scoringType==7&&(int)head.gameplayType==2&&Math.Abs(head.cutDirectionAngleOffset+26.565051f)<1e-4;
            if(!pass)failed++;results.Add(new {kind="arc-tail-on-burst-head",pairFirst,score=(int)head.scoringType,gameplay=(int)head.gameplayType,head.cutDirectionAngleOffset,status=pass?"pass":"mismatch"});
        }
        foreach(bool pairFirst in new[]{false,true})
        {
            cases++;var head=Note(0,0,8);var other=Note(1,2,1);var burst=Slider(true,direction:8);
            var processor=Activator.CreateInstance(processorType,flags,null,new[]{(object)4,raw},null)!;
            var color=Slice(typeof(NoteData),new object[]{head,other});var all=Slice(typeof(BeatmapDataItem),new object[]{head,other,burst});
            if(pairFirst)pair.Invoke(processor,new[]{color,(object)100f});
            patch.Invoke(null,new[]{processor,(object)4,all});
            if(!pairFirst)pair.Invoke(processor,new[]{color,(object)100f});
            float expected=pairFirst?0:-26.565051f;bool pass=Math.Abs(head.cutDirectionAngleOffset-expected)<1e-4;
            if(!pass)failed++;results.Add(new {kind="deferred-dot-row",pairFirst,expected,head.cutDirectionAngleOffset,status=pass?"pass":"mismatch"});
        }
        string folder=Path.Combine(Path.GetDirectoryName(Path.GetFullPath(reportPath))!,"dot-row-fixtures");
        Directory.CreateDirectory(folder);
        File.WriteAllText(Path.Combine(folder,"Info.dat"),new JObject {
            ["_version"]="2.1.0",["_beatsPerMinute"]=120,["_songDuration"]=10,
            ["_difficultyBeatmapSets"]=new JArray(new JObject {
                ["_beatmapCharacteristicName"]="Standard",["_difficultyBeatmaps"]=new JArray(new JObject {
                    ["_difficulty"]="Expert",["_beatmapFilename"]="map.dat",
                    ["_customData"]=new JObject {["_requirements"]=new JArray("Noodle Extensions")}})})}.ToString());
        for(int direction=0;direction<=8;direction++)for(int x=0;x<4;x++)for(int y=0;y<3;y++)
        {
            if(x==0&&y==0)continue;
            cases++;var headAngles=new List<float>();var partnerAngles=new List<float>();
            foreach(bool pairFirst in new[]{false,true})
            {
                var head=Note(0,0,8);head.SetCutDirectionAngleOffset(17);
                var other=Note(x,y,direction);var burst=Slider(true,direction:8);
                var processor=Activator.CreateInstance(processorType,flags,null,new[]{(object)4,raw},null)!;
                var color=Slice(typeof(NoteData),new object[]{head,other});
                var all=Slice(typeof(BeatmapDataItem),new object[]{head,other,burst});
                if(pairFirst)pair.Invoke(processor,new[]{color,(object)100f});
                patch.Invoke(null,new[]{processor,(object)4,all});
                if(!pairFirst)pair.Invoke(processor,new[]{color,(object)100f});
                headAngles.Add(head.cutDirectionAngleOffset);partnerAngles.Add(other.cutDirectionAngleOffset);
            }
            var fixture=new JObject {["version"]="3.3.0",["colorNotes"]=new JArray(
                new JObject {["b"]=2,["x"]=0,["y"]=0,["c"]=0,["d"]=8,["a"]=17},
                new JObject {["b"]=2,["x"]=x,["y"]=y,["c"]=0,["d"]=direction}),
                ["burstSliders"]=new JArray(new JObject {["b"]=2,["x"]=0,["y"]=0,["c"]=0,["d"]=8,["tb"]=4,["tx"]=1,["ty"]=2,["sc"]=4,["s"]=1})};
            File.WriteAllText(Path.Combine(folder,"map.dat"),fixture.ToString());
            var own=LocalMap.Load(new MapCase {Folder=folder,File="map.dat",Characteristic="Standard",Difficulty="Expert"},AnalysisFeatures.FirstNotes|AnalysisFeatures.Average);
            var ownHead=own.Input.Notes.First().CutDirectionAngleOffset;
            var ownPartner=own.Input.Notes.Single(n=>n.SliceCount==1).CutDirectionAngleOffset;
            bool agrees=Math.Abs(headAngles[0]-headAngles[1])<1e-4;
            bool pass=(agrees?ownHead.HasValue&&Math.Abs(ownHead.Value-headAngles[0])<1e-4:!ownHead.HasValue)
                &&ownPartner.HasValue&&partnerAngles.All(angle=>Math.Abs(ownPartner.Value-angle)<1e-4);
            if(!pass)failed++;
            results.Add(new {kind="dot-row-order-contract",direction,x,y,headAngles,partnerAngles,ownHead,ownPartner,status=pass?"pass":"mismatch"});
        }
        for(int color=0;color<2;color++)for(int direction=0;direction<=8;direction++)
        {
            cases++;var anchor=Note(0,0,direction,color);anchor.SetCutDirectionAngleOffset(17);
            var burst=Slider(true,tailAtHead:true);
            var tailType=baseProcessor.GetNestedType("SliderTailData",flags)!;
            var tail=Activator.CreateInstance(tailType,flags,null,new object[]{burst},null)!;
            var processor=Activator.CreateInstance(processorType,flags,null,new[]{(object)4,raw},null)!;
            var all=Slice(typeof(BeatmapDataItem),new object[]{anchor,tail});
            patch.Invoke(null,new[]{processor,(object)4,all});
            bool pass=(int)anchor.scoringType==3&&(int)anchor.gameplayType==0&&anchor.cutDirectionAngleOffset==17;
            var fixture=new JObject {["version"]="3.3.0",["colorNotes"]=new JArray(
                new JObject {["b"]=2,["x"]=0,["y"]=0,["c"]=0,["d"]=1},
                new JObject {["b"]=4,["x"]=1,["y"]=2,["c"]=color,["d"]=direction,["a"]=17}),
                ["burstSliders"]=new JArray(new JObject {["b"]=2,["x"]=0,["y"]=0,["c"]=0,["d"]=1,["tb"]=4,["tx"]=1,["ty"]=2,["sc"]=4,["s"]=1})};
            File.WriteAllText(Path.Combine(folder,"map.dat"),fixture.ToString());
            var own=LocalMap.Load(new MapCase {Folder=folder,File="map.dat",Characteristic="Standard",Difficulty="Expert"},AnalysisFeatures.FirstNotes|AnalysisFeatures.Average);
            var ownTail=own.Input.Notes.Single(n=>n.Time==2);
            pass=pass&&ownTail.ScoringType==(int)anchor.scoringType&&ownTail.CutDirectionAngleOffset==anchor.cutDirectionAngleOffset&&ownTail.Direction==direction&&ownTail.Color==color&&own.Input.Notes.Length==2;
            if(!pass)failed++;
            results.Add(new {kind="burst-tail-anchor",color,direction,score=(int)anchor.scoringType,gameplay=(int)anchor.gameplayType,anchor.cutDirectionAngleOffset,ownScore=ownTail.ScoringType,ownAngle=ownTail.CutDirectionAngleOffset,status=pass?"pass":"mismatch"});
        }
        foreach(string kind in new[]{"arc-head","arc-tail","chain-head"})
        for(int color=0;color<2;color++)for(int direction=0;direction<=8;direction++)
        {
            cases++;var anchor=Note(0,0,direction,color);anchor.SetCutDirectionAngleOffset(17);
            var incoming=Slider(true,tailAtHead:true);var outgoing=Slider(kind=="chain-head",tailAtHead:kind=="arc-tail",direction:direction);
            var tailType=baseProcessor.GetNestedType("SliderTailData",flags)!;
            object Tail(SliderData slider)=>Activator.CreateInstance(tailType,flags,null,new object[]{slider},null)!;
            var items=new object[]{anchor,kind=="arc-tail"?Tail(outgoing):outgoing,Tail(incoming)};
            var processor=Activator.CreateInstance(processorType,flags,null,new[]{(object)4,raw},null)!;
            patch.Invoke(null,new[]{processor,(object)4,Slice(typeof(BeatmapDataItem),items)});
            int expectedScore=kind=="arc-head"?6:kind=="chain-head"?7:3;
            float expectedAngle=kind=="chain-head"&&direction==8?0:17;
            bool pass=(int)anchor.scoringType==expectedScore&&(int)anchor.gameplayType==(kind=="chain-head"?2:0)&&anchor.cutDirectionAngleOffset==expectedAngle;
            var fixture=new JObject {["version"]="3.3.0",["colorNotes"]=new JArray(
                new JObject {["b"]=2,["x"]=0,["y"]=0,["c"]=0,["d"]=1},
                new JObject {["b"]=4,["x"]=1,["y"]=2,["c"]=color,["d"]=direction,["a"]=17}),
                ["burstSliders"]=new JArray(new JObject {["b"]=2,["x"]=0,["y"]=0,["c"]=0,["d"]=1,["tb"]=4,["tx"]=1,["ty"]=2,["sc"]=4,["s"]=1})};
            var next=new JObject {["b"]=4,["x"]=1,["y"]=2,["c"]=color,["d"]=direction,["tb"]=6,["tx"]=3,["ty"]=0,["tc"]=1,["sc"]=4,["s"]=1};
            if(kind=="chain-head")((JArray)fixture["burstSliders"]!).Add(next);
            else
            {
                if(kind=="arc-tail"){next["b"]=0;next["x"]=2;next["tb"]=4;next["tx"]=1;next["ty"]=2;}
                fixture["sliders"]=new JArray(next);
            }
            File.WriteAllText(Path.Combine(folder,"map.dat"),fixture.ToString());
            var own=LocalMap.Load(new MapCase {Folder=folder,File="map.dat",Characteristic="Standard",Difficulty="Expert"},AnalysisFeatures.FirstNotes|AnalysisFeatures.Average);
            var ownAnchor=own.Input.Notes.Single(n=>n.Time==2);
            // A physical burst head is represented by one logical swing head,
            // regardless of its combined native arc-tail scoring role.
            pass=pass&&own.Input.Notes.Length==2&&ownAnchor.CutDirectionAngleOffset==expectedAngle&&ownAnchor.Color==color&&ownAnchor.Direction==direction
                &&ownAnchor.ScoringType==(kind=="chain-head"?1:expectedScore)&&ownAnchor.SliceCount==(kind=="chain-head"?4:1);
            if(!pass)failed++;
            results.Add(new {kind="burst-tail-"+kind,color,direction,score=(int)anchor.scoringType,gameplay=(int)anchor.gameplayType,anchor.cutDirectionAngleOffset,ownScore=ownAnchor.ScoringType,ownAngle=ownAnchor.CutDirectionAngleOffset,status=pass?"pass":"mismatch"});
        }
        foreach(int count in new[]{3,4})foreach(bool headLast in new[]{false,true})
        for(int color=0;color<2;color++)for(int direction=0;direction<=8;direction++)foreach(bool pairFirst in new[]{false,true})
        {
            cases++;var head=Note(0,0,direction,color);head.SetCutDirectionAngleOffset(17);
            var notes=new List<NoteData>{head};
            for(int i=1;i<count;i++){var n=Note(i,0,(direction+i)%9,color);n.SetCutDirectionAngleOffset(17+i);notes.Add(n);}
            if(headLast){notes.RemoveAt(0);notes.Add(head);}
            var burst=Slider(true,direction:direction);
            var processor=Activator.CreateInstance(processorType,flags,null,new[]{(object)4,raw},null)!;
            var colorSlice=Slice(typeof(NoteData),notes.Cast<object>());
            var all=Slice(typeof(BeatmapDataItem),notes.Cast<object>().Append(burst));
            if(pairFirst)pair.Invoke(processor,new[]{colorSlice,(object)100f});
            patch.Invoke(null,new[]{processor,(object)4,all});
            if(!pairFirst)pair.Invoke(processor,new[]{colorSlice,(object)100f});
            var fixtureNotes=new JArray(notes.Select(n=>new JObject {["b"]=2,["x"]=n.lineIndex,["y"]=0,["c"]=color,["d"]=(int)n.cutDirection,["a"]=17+n.lineIndex}));
            var fixture=new JObject {["version"]="3.3.0",["colorNotes"]=fixtureNotes,
                ["burstSliders"]=new JArray(new JObject {["b"]=2,["x"]=0,["y"]=0,["c"]=color,["d"]=direction,["tb"]=4,["tx"]=1,["ty"]=2,["sc"]=4,["s"]=1})};
            File.WriteAllText(Path.Combine(folder,"map.dat"),fixture.ToString());
            var own=LocalMap.Load(new MapCase {Folder=folder,File="map.dat",Characteristic="Standard",Difficulty="Expert"},AnalysisFeatures.FirstNotes|AnalysisFeatures.Average);
            bool pass=(int)head.scoringType==4&&(int)head.gameplayType==2&&head.cutDirectionAngleOffset==(direction==8?0:17)&&own.Input.Notes.Length==count;
            for(int i=0;i<count;i++)pass=pass&&own.Input.Notes[i].X==notes[i].lineIndex&&own.Input.Notes[i].CutDirectionAngleOffset==notes[i].cutDirectionAngleOffset;
            if(!pass)failed++;
            results.Add(new {kind="multi-note-burst-row",count,headLast,color,direction,pairFirst,nativeAngles=notes.Select(n=>n.cutDirectionAngleOffset).ToArray(),ownAngles=own.Input.Notes.Select(n=>n.CutDirectionAngleOffset).ToArray(),status=pass?"pass":"mismatch"});
        }
        var fakeCtor=noodle.GetType("NoodleExtensions.NoodleNoteData",true)!.GetConstructors(flags).Single();
        var fakeParams=fakeCtor.GetParameters();
        foreach(int headDirection in new[]{1,8})for(int direction=0;direction<=8;direction++)foreach(bool headLast in new[]{false,true})
        {
            cases++;var headAngles=new List<float>();var fakeAngles=new List<float>();bool fakeIgnored=true;
            foreach(bool pairFirst in new[]{false,true})foreach(bool fakeFirst in new[]{false,true})
            {
                var head=Note(0,0,headDirection);head.SetCutDirectionAngleOffset(17);
                var fake=Note(1,2,direction);var metadata=fake.GetType().GetProperty("customData")!.GetValue(fake)!;
                ((System.Collections.IDictionary)metadata)["NE_fake"]=true;
                void Ignore()=>fakeCtor.Invoke(new object[]{fake,metadata,120f,Activator.CreateInstance(fakeParams[3].ParameterType)!,Activator.CreateInstance(fakeParams[4].ParameterType)!,false,false});
                if(fakeFirst)Ignore();
                var burst=Slider(true,direction:headDirection);
                var notes=headLast?new object[]{fake,head}:new object[]{head,fake};
                var processor=Activator.CreateInstance(processorType,flags,null,new[]{(object)4,raw},null)!;
                var colorSlice=Slice(typeof(NoteData),notes);var all=Slice(typeof(BeatmapDataItem),notes.Append(burst));
                if(pairFirst)pair.Invoke(processor,new[]{colorSlice,(object)100f});
                patch.Invoke(null,new[]{processor,(object)4,all});
                if(!pairFirst)pair.Invoke(processor,new[]{colorSlice,(object)100f});
                if(!fakeFirst)Ignore();
                fakeIgnored=fakeIgnored&&(int)fake.scoringType==-1;
                headAngles.Add(head.cutDirectionAngleOffset);fakeAngles.Add(fake.cutDirectionAngleOffset);
            }
            bool pass=fakeIgnored&&fakeAngles.All(angle=>Math.Abs(angle-fakeAngles[0])<1e-4);
            var realJson=new JObject {["b"]=2,["x"]=0,["y"]=0,["c"]=0,["d"]=headDirection,["a"]=17};
            var fakeJson=new JObject {["b"]=2,["x"]=1,["y"]=2,["c"]=0,["d"]=direction,["customData"]=new JObject {["NE_fake"]=true}};
            var fixture=new JObject {["version"]="3.3.0",["colorNotes"]=headLast?new JArray(fakeJson,realJson):new JArray(realJson,fakeJson),
                ["burstSliders"]=new JArray(new JObject {["b"]=2,["x"]=0,["y"]=0,["c"]=0,["d"]=headDirection,["tb"]=4,["tx"]=1,["ty"]=2,["sc"]=4,["s"]=1})};
            File.WriteAllText(Path.Combine(folder,"map.dat"),fixture.ToString());
            var own=LocalMap.Load(new MapCase {Folder=folder,File="map.dat",Characteristic="Standard",Difficulty="Expert"},AnalysisFeatures.FirstNotes|AnalysisFeatures.Average);
            var ownHead=own.Input.Notes.Single(n=>!n.IsFake);var ownFake=own.Input.Notes.Single(n=>n.IsFake);
            bool agrees=headAngles.All(angle=>Math.Abs(angle-headAngles[0])<1e-4);
            pass=pass&&own.Input.Notes.Length==2&&(agrees?ownHead.CutDirectionAngleOffset.HasValue&&Math.Abs(ownHead.CutDirectionAngleOffset.Value-headAngles[0])<1e-4:!ownHead.CutDirectionAngleOffset.HasValue)
                &&ownFake.CutDirectionAngleOffset.HasValue&&Math.Abs(ownFake.CutDirectionAngleOffset.Value-fakeAngles[0])<1e-4
                &&CompatibleAnalysis.Analyze(own.Input,reproduceKnownDefects:false).LeftBins.Sum()==1;
            if(!pass)failed++;
            results.Add(new {kind="fake-burst-partner",headDirection,direction,headLast,headAngles,fakeAngles,fakeIgnored,ownAngle=ownHead.CutDirectionAngleOffset,status=pass?"pass":"mismatch"});
        }
        var versionHolderType=custom.GetType("CustomJSONData.CustomBeatmap.CustomBeatmapData",true)!;
        var setVersionMethod=new DynamicMethod("SetFixtureVersion",typeof(void),new[]{typeof(object),typeof(Version)},typeof(NoodleOverlapChecks).Module,true);
        var setter=setVersionMethod.GetILGenerator();setter.Emit(OpCodes.Ldarg_0);setter.Emit(OpCodes.Castclass,versionHolderType);setter.Emit(OpCodes.Ldarg_1);
        setter.Emit(OpCodes.Stfld,versionHolderType.GetField("<version>k__BackingField",flags)!);setter.Emit(OpCodes.Ret);
        var setVersion=(Action<object,Version>)setVersionMethod.CreateDelegate(typeof(Action<object,Version>));
        foreach(int major in new[]{2,3})foreach(bool burst in new[]{false,true})foreach(string replacement in new[]{"head","tail","both"})
        {
            if(major==2&&burst)continue;
            cases++;var head=Note(0,0,1,major:major);var tailNote=Note(1,2,1,time:2,major:major);var slider=Slider(burst,extendedBase:replacement,major:major);
            // This row handler only reads the version. Its normal beatmap ctor
            // needs runtime IPA field accessors that are absent in this stand.
            var beatmap=RuntimeHelpers.GetUninitializedObject(versionHolderType);
            setVersion(beatmap,new Version(major,major==2?6:3,0));
            var processor=Activator.CreateInstance(processorType,flags,null,new[]{(object)4,beatmap},null)!;
            bool version=(bool)processorType.GetMethod("GetV2",BindingFlags.Static|BindingFlags.NonPublic)!.Invoke(null,new[]{processor})!;
            patch.Invoke(null,new[]{processor,(object)4,Slice(typeof(BeatmapDataItem),new object[]{head,slider})});
            var tailType=baseProcessor.GetNestedType("SliderTailData",flags)!;
            var tail=Activator.CreateInstance(tailType,flags,null,new object[]{slider},null)!;
            patch.Invoke(null,new[]{processor,(object)4,Slice(typeof(BeatmapDataItem),new object[]{tailNote,tail})});
            bool pass=version==(major==2)&&slider.hasHeadNote&&(int)head.scoringType==(burst?4:2)&&(int)tailNote.scoringType==3&&head.cutDirectionAngleOffset==0;
            if(!pass)failed++;
            results.Add(new {kind="replaced-slider-base",major,burst,replacement,version,slider.hasHeadNote,headRole=(int)head.scoringType,tailRole=(int)tailNote.scoringType,status=pass?"pass":"mismatch"});
        }
        return new {schemaVersion=1,profileVersion=BeatInsight.Formats.LocalMap.AnalysisProfileVersion,cases,zeroTailDispatchCases,failed,referenceUsed=false,gameStarted=false,
            scope="direct installed Noodle all-object row postfix and native same-color handler on real custom notes/sliders; explicit row orders, not full event lifecycle",results};
    }
}
