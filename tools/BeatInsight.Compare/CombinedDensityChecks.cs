using System.Collections;
using Newtonsoft.Json.Linq;
using BeatInsight.Core;
using BeatInsight.Formats;

namespace BeatInsight.Compare;

// Nominal note stream only. This does not validate combined physical geometry.
static class CombinedDensityChecks
{
    internal static object Run(Func<JObject,float,BeatmapData> load,string? inventory=null)
    {
        int arcHeadTailSamples=0;int cases=1,failed=NoodleRowBridge.CombinedReverseOrderError==null?1:0;var results=new List<object>();
        results.Add(new{name="reverse-transpiler-order-control",pass=NoodleRowBridge.CombinedReverseOrderError!=null});
        bool GraphMatches(LoadedMap own,float[] times)=>CompatibleAnalysis.Analyze(own.Input,reproduceKnownDefects:false,features:AnalysisFeatures.Density).Segments
            .SequenceEqual(CompatibleAnalysis.SegmentValues(times.Select(t=>new MapNote{Time=t,Color=0,ScoringType=1}).ToArray(),own.Input.Duration));
        string folder=BeatInsight.Tooling.LocalArtifacts.PathFor("combined-density-fixtures",Guid.NewGuid().ToString("N"));Directory.CreateDirectory(folder);
        File.WriteAllText(Path.Combine(folder,"Info.dat"),"""{"version":"4.0.1","audio":{"bpm":120,"songDuration":12},"difficultyBeatmaps":[{"characteristic":"Standard","difficulty":"Expert","beatmapDataFilename":"map.dat","customData":{"requirements":["Mapping Extensions","Noodle Extensions"]}}]}""");
        var mapCase=new MapCase{Folder=folder,Characteristic="Standard",Difficulty="Expert",File="map.dat"};
        float[] Prepared(BeatmapData map)=>OracleBridge.GetItems(map).OfType<NoteData>()
            .Where(n=>((IDictionary)n.GetType().GetProperty("customData")!.GetValue(n)!)["NE_fake"] is not true&&(int)n.scoringType>0&&(int)n.scoringType!=4&&(int)n.scoringType!=7)
            .Select(n=>n.time).OrderBy(t=>t).ToArray();
        object? lastNoteDifferences=null;
        bool NotesMatch(BeatmapData native,LoadedMap own)
        {
            var notes=OracleBridge.GetItems(native).OfType<NoteData>()
                .Where(n=>((IDictionary)n.GetType().GetProperty("customData")!.GetValue(n)!)["NE_fake"] is not true&&(int)n.scoringType>0&&(int)n.scoringType!=4&&(int)n.scoringType!=7).ToArray();
            arcHeadTailSamples+=notes.Count(n=>(int)n.scoringType==6);
            var prepared=own.Input.Notes.Where(n=>!n.IsFake).ToArray();
            bool nominal=own.Features.Contains("noodle-nominal-notes");
            bool input=notes.Length==prepared.Length&&notes.Zip(prepared,(a,b)=>Math.Abs(a.time-b.Time)<.0001f&&(int)a.colorType==b.Color&&(int)a.cutDirection==b.Direction
                &&(nominal?!b.CutDirectionAngleOffset.HasValue:a.lineIndex==b.X&&(int)a.noteLineLayer==b.Y&&(int)a.scoringType==b.ScoringType
                &&b.CutDirectionAngleOffset.HasValue&&Math.Abs(a.cutDirectionAngleOffset-b.CutDirectionAngleOffset.Value)<.0001f)).All(x=>x);
            lastNoteDifferences=input?null:new{native=notes.Select(n=>new{n.time,n.lineIndex,layer=(int)n.noteLineLayer,color=(int)n.colorType,direction=(int)n.cutDirection,scoring=(int)n.scoringType,offset=n.cutDirectionAngleOffset}).ToArray(),
                own=prepared.Select(n=>new{n.Time,n.X,n.Y,n.Color,n.Direction,n.ScoringType,n.CutDirectionAngleOffset,n.NoodleX,n.NoodleY}).ToArray()};
            var reference=new MapInput{Duration=own.Input.Duration,Notes=notes.Select(n=>new MapNote{Time=n.time,X=n.lineIndex,Y=(int)n.noteLineLayer,Color=(int)n.colorType,Direction=(int)n.cutDirection,ScoringType=(int)n.scoringType}).ToArray()};
            var expected=CompatibleAnalysis.Analyze(reference,reproduceKnownDefects:false);var actual=CompatibleAnalysis.Analyze(own.Input,reproduceKnownDefects:false);
            return input&&expected.LeftBins.SequenceEqual(actual.LeftBins)&&expected.RightBins.SequenceEqual(actual.RightBins)
                &&expected.Segments.SequenceEqual(actual.Segments);
        }
        bool Matches(JObject root,float[] times,MapCase selected)
        {
            File.WriteAllText(Path.Combine(folder,"map.dat"),root.ToString());
            var own=LocalMap.Load(selected,AnalysisFeatures.Density);
            var ownTimes=own.Input.Notes.Where(n=>!n.IsFake).Select(n=>n.Time).OrderBy(t=>t).ToArray();
            return ownTimes.Length==times.Length&&ownTimes.Zip(times,(a,b)=>Math.Abs(a-b)<.0001f).All(x=>x)
                &&GraphMatches(own,times)&&own.Json==root.ToString();
        }
        try
        {
            NoodleRowBridge.CombinedMapping=true;
            foreach(bool mappingFirst in new[]{true})foreach(bool postfixFirst in new[]{false,true})
            foreach(int column in new[]{-2,0,3,5,1500,2500})foreach(bool coordinates in new[]{false,true})
            foreach(bool fake in new[]{false,true})foreach(bool change in new[]{false,true})
            foreach(int direction in new[]{1,8,1050,2250})foreach(bool decorations in new[]{false,true})
            foreach(string arc in new[]{"none","ordinary","orphan","duplicate","coincident"})
            {
                NoodleRowBridge.MappingFirst=mappingFirst;NoodleRowBridge.MappingPostfixFirst=postfixFirst;
                var root=JObject.Parse("""{"version":"3.3.0","colorNotes":[{"b":2,"x":0,"y":0,"c":0,"d":1},{"b":4,"x":1,"y":1,"c":1,"d":1}],"sliders":[],"bombNotes":[],"obstacles":[],"bpmEvents":[],"customData":{}}""");
                root["colorNotes"]![0]!["x"]=column;root["colorNotes"]![0]!["d"]=direction;
                root["colorNotes"]![0]!["y"]=column;
                if(decorations)
                {
                    root["customData"]!["fakeColorNotes"]=new JArray(new JObject{["b"]=2,["x"]=column,["y"]=0,["c"]=0,["d"]=direction});
                    root["customData"]!["fakeBombNotes"]=new JArray(new JObject{["b"]=4,["x"]=column,["y"]=0});
                }
                root["colorNotes"]![0]!["customData"]=new JObject{["NE_fake"]=fake};
                if(coordinates)root["colorNotes"]![0]!["customData"]!["coordinates"]=new JArray(-.5f,.25f);
                if(change)((JArray)root["bpmEvents"]!).Add(new JObject{["b"]=2,["m"]=180});
                if(arc!="none")
                {
                    var slider=JObject.Parse("""{"b":2,"x":0,"y":0,"c":0,"d":1,"mu":1,"tb":4,"tx":1,"ty":1,"tc":1,"tmu":1,"m":0}""");
                    slider["x"]=column;
                    if(arc=="orphan")slider["b"]=1;
                    if(arc=="coincident"){slider["tb"]=2;slider["tx"]=column;slider["ty"]=0;}
                    ((JArray)root["sliders"]!).Add(slider);
                    if(arc=="duplicate")((JArray)root["sliders"]!).Add(slider.DeepClone());
                }
                string? error=null;float[] times=Array.Empty<float>();bool pass=false;
                try
                {
                    var map=load(root,120);
                    times=Prepared(map);
                    var expected=fake?new[]{change?5f/3:2f}:new[]{1f,change?5f/3:2f};
                    pass=Matches(root,times,mapCase)&&times.Length==expected.Length&&times.Zip(expected,(a,b)=>Math.Abs(a-b)<.00001f).All(x=>x);
                }
                catch(Exception ex){error=ex.GetBaseException().ToString();}
                cases++;if(!pass)failed++;
                results.Add(new{mappingFirst,postfixFirst,column,coordinates,fake,change,direction,decorations,arc,times,pass,error});
            }
            foreach(bool first in new[]{false,true})foreach(bool coordinates in new[]{false,true})
            foreach(bool fake in new[]{false,true})foreach(bool reverse in new[]{false,true})
            foreach(string topology in new[]{"plain","same-color","orphan","duplicate","coincident"})
            {
                NoodleRowBridge.MappingPostfixFirst=first;
                var root=JObject.Parse("""{"version":"3.3.0","colorNotes":[{"b":2,"x":0,"y":0,"c":0,"d":1},{"b":2,"x":1,"y":1,"c":0,"d":1},{"b":4,"x":1,"y":1,"c":1,"d":8}],"sliders":[{"b":2,"x":0,"y":0,"c":0,"d":1,"tb":4,"tx":1,"ty":1,"tc":8}],"bombNotes":[],"obstacles":[]}""");
                root["colorNotes"]![0]!["customData"]=new JObject{["NE_fake"]=fake};
                if(coordinates)root["colorNotes"]![0]!["customData"]!["coordinates"]=new JArray(-.5f,.25f);
                if(topology=="same-color")root["colorNotes"]![2]!["c"]=0;
                if(topology=="orphan")root["sliders"]![0]!["b"]=1;
                if(topology=="duplicate")((JArray)root["sliders"]!).Add(root["sliders"]![0]!.DeepClone());
                if(topology=="coincident"){root["sliders"]![0]!["tb"]=2;root["sliders"]![0]!["tx"]=0;root["sliders"]![0]!["ty"]=0;}
                if(reverse)root["colorNotes"]=new JArray(((JArray)root["colorNotes"]!).Reverse().Select(n=>n.DeepClone()));
                File.WriteAllText(Path.Combine(folder,"map.dat"),root.ToString());
                lastNoteDifferences=null;
                var native=load(root,120);
                var own=LocalMap.Load(mapCase,AnalysisFeatures.All&~(AnalysisFeatures.Squats|AnalysisFeatures.Reference),includeSwingWindowWalls:false);
                bool pass=NotesMatch(native,own)&&own.HasNoodleGeometry&&own.Json==root.ToString();
                bool? preservedRawHeadRole=null;
                if(coordinates&&!fake&&topology is "plain" or "same-color" or "duplicate")
                {
                    preservedRawHeadRole=(int)OracleBridge.GetItems(native).OfType<NoteData>().Single(n=>n.lineIndex==0&&(int)n.noteLineLayer==0).scoringType==2;
                    pass&=preservedRawHeadRole.Value;
                }
                cases++;if(!pass)failed++;results.Add(new{name="canonical-note-input",first,coordinates,fake,reverse,topology,preservedRawHeadRole,pass,differences=lastNoteDifferences});
            }
            foreach(bool first in new[]{false,true})foreach(bool zero in new[]{false,true})foreach(bool moved in new[]{false,true})
            foreach(bool fake in new[]{false,true})foreach(string head in new[]{"raw","other","tail","away"})foreach(string tail in new[]{"raw","other","head","away"})
            {
                NoodleRowBridge.MappingPostfixFirst=first;
                var root=JObject.Parse("""{"version":"3.3.0","colorNotes":[{"b":2,"x":0,"y":0,"c":0,"d":1},{"b":2,"x":1,"y":1,"c":1,"d":1},{"b":4,"x":2,"y":0,"c":0,"d":0},{"b":4,"x":3,"y":1,"c":1,"d":0}],"sliders":[{"b":2,"x":0,"y":0,"c":0,"d":1,"tb":4,"tx":2,"ty":0,"tc":0}],"obstacles":[]}""");
                var arc=root["sliders"]![0]!;
                if(zero){arc["tb"]=2;root["colorNotes"]![2]!["b"]=2;root["colorNotes"]![3]!["b"]=2;}
                var data=new JObject();arc["customData"]=data;
                if(head!="raw")data["coordinates"]=head=="other"?new JArray(-1,1):head=="tail"?new JArray(0,0):new JArray(.5f,2.5f);
                if(tail!="raw")data["tailCoordinates"]=tail=="other"?new JArray(1,1):tail=="head"?new JArray(-2,0):new JArray(1.5f,2.5f);
                ((JArray)root["sliders"]!).Add(new JObject{["b"]=zero?2:4,["x"]=2,["y"]=0,["c"]=1,["d"]=0,["tb"]=6,["tx"]=0,["ty"]=2,["tc"]=1,
                    ["customData"]=new JObject{["coordinates"]=new JArray(1,1)}});
                root["colorNotes"]![0]!["customData"]=new JObject{["NE_fake"]=fake};
                root["colorNotes"]![2]!["customData"]=new JObject{["NE_fake"]=fake};
                if(moved){root["colorNotes"]![0]!["customData"]!["coordinates"]=new JArray(-1,2);root["colorNotes"]![2]!["customData"]!["coordinates"]=new JArray(-2,2);}
                File.WriteAllText(Path.Combine(folder,"map.dat"),root.ToString());
                var own=LocalMap.Load(mapCase,AnalysisFeatures.All&~(AnalysisFeatures.Squats|AnalysisFeatures.Reference),includeSwingWindowWalls:false);
                bool pass=NotesMatch(load(root,120),own)&&own.Json==root.ToString();
                cases++;if(!pass)failed++;results.Add(new{name="raw-custom-arc-roles",first,zero,moved,fake,head,tail,pass,differences=lastNoteDifferences});
            }
            foreach(bool first in new[]{false,true})foreach(int x in new[]{-1500,4,1500})foreach(int y in new[]{-2,1500})
            foreach(int direction in new[]{0,1,8})foreach(bool coordinates in new[]{false,true})foreach(bool fake in new[]{false,true})foreach(bool change in new[]{false,true})
            {
                NoodleRowBridge.MappingPostfixFirst=first;
                var root=JObject.Parse("""{"version":"3.3.0","colorNotes":[{"b":2,"x":0,"y":0,"c":0,"d":1},{"b":2.05,"x":1,"y":1,"c":0,"d":5},{"b":4,"x":1,"y":1,"c":1,"d":8}],"sliders":[{"b":2,"x":0,"y":0,"c":0,"d":1,"tb":4,"tx":1,"ty":1,"tc":8}],"bombNotes":[],"obstacles":[],"bpmEvents":[]}""");
                root["colorNotes"]![0]!["x"]=x;root["colorNotes"]![0]!["y"]=y;root["colorNotes"]![0]!["d"]=direction;
                root["sliders"]![0]!["x"]=x;root["sliders"]![0]!["y"]=y;
                root["colorNotes"]![0]!["customData"]=new JObject{["NE_fake"]=fake,["_isAnchor"]=true};
                if(coordinates)root["colorNotes"]![0]!["customData"]!["coordinates"]=new JArray(-.5f,.25f);
                if(change)((JArray)root["bpmEvents"]!).Add(new JObject{["b"]=2,["m"]=180});
                File.WriteAllText(Path.Combine(folder,"map.dat"),root.ToString());
                var own=LocalMap.Load(mapCase,AnalysisFeatures.All&~(AnalysisFeatures.Squats|AnalysisFeatures.Reference),includeSwingWindowWalls:false);
                bool pass=own.Features.Contains("noodle-nominal-notes")&&own.HasNoodleGeometry&&NotesMatch(load(root,120),own)&&own.Json==root.ToString();
                cases++;if(!pass)failed++;results.Add(new{name="nominal-note-input",first,x,y,direction,coordinates,fake,change,pass,differences=lastNoteDifferences});
            }
            // Distinguish a shared row boundary from a combined-only failure.
            {
                string infoFile=Path.Combine(folder,"Info.dat"),savedInfo=File.ReadAllText(infoFile);
                var info=JObject.Parse(savedInfo);info["difficultyBeatmaps"]![0]!["customData"]!["requirements"]=new JArray("Noodle Extensions");
                var root=JObject.Parse("""{"version":"3.3.0","colorNotes":[{"b":2,"x":0,"y":0,"c":0,"d":1,"customData":{"coordinates":[-0.5,0.25]}},{"b":4,"x":1,"y":1,"c":1,"d":1}],"sliders":[{"b":2,"x":0,"y":0,"c":0,"d":1,"tb":4,"tx":1,"ty":1,"tc":1}],"obstacles":[]}""");
                try
                {
                    File.WriteAllText(infoFile,info.ToString());File.WriteAllText(Path.Combine(folder,"map.dat"),root.ToString());
                    NoodleRowBridge.CombinedMapping=false;
                    var native=OracleBridge.GetItems(load(root,120)).OfType<NoteData>().First(n=>n.lineIndex==0);
                    var own=LocalMap.Load(mapCase,AnalysisFeatures.FirstNotes,includeSwingWindowWalls:false).Input.Notes.First(n=>n.X==0);
                    bool pass=(int)native.scoringType==1&&own.ScoringType==1;
                    cases++;if(!pass)failed++;
                    results.Add(new{name="no-mapping-row-parity-control",nativeRole=(int)native.scoringType,ownRole=own.ScoringType,
                        knownMismatch=false,admissionChanged=false,pass});
                }
                finally{NoodleRowBridge.CombinedMapping=true;File.WriteAllText(infoFile,savedInfo);}
            }
            if(inventory!=null)foreach(var item in JArray.Parse(File.ReadAllText(inventory)))
            {
                var selected=item.ToObject<MapCase>()!;
                string infoPath=Directory.EnumerateFiles(selected.Folder).Single(f=>Path.GetFileName(f).Equals("Info.dat",StringComparison.OrdinalIgnoreCase));
                var info=JObject.Parse(File.ReadAllText(infoPath));
                float bpm=(float)(info["_beatsPerMinute"]??info["audio"]!["bpm"]!);
                string mapPath=Path.Combine(selected.Folder,selected.File);
                string before=Comparison.HashFile(mapPath),infoBefore=Comparison.HashFile(infoPath);
                var root=JObject.Parse(File.ReadAllText(mapPath));
                foreach(bool first in new[]{false,true})
                {
                    NoodleRowBridge.MappingPostfixFirst=first;
                    var native=load(root,bpm);var times=Prepared(native);
                    var own=LocalMap.Load(selected,AnalysisFeatures.Density);
                    var ownTimes=own.Input.Notes.Where(n=>!n.IsFake).Select(n=>n.Time).OrderBy(t=>t).ToArray();
                    bool pass=times.Length==ownTimes.Length&&times.Zip(ownTimes,(a,b)=>Math.Abs(a-b)<.0001f).All(x=>x)
                        &&GraphMatches(own,times)&&before==Comparison.HashFile(mapPath)&&infoBefore==Comparison.HashFile(infoPath);
                    bool notesAdmitted=false;
                    try
                    {
                        var noteMap=LocalMap.Load(selected,AnalysisFeatures.All&~(AnalysisFeatures.Squats|AnalysisFeatures.Reference),includeSwingWindowWalls:false);
                        notesAdmitted=true;pass&=NotesMatch(native,noteMap)&&noteMap.HasNoodleGeometry;
                    }
                    catch(NotSupportedException){pass=false;}
                    pass&=notesAdmitted;
                    cases++;if(!pass)failed++;
                    results.Add(new{name=Path.GetFileName(selected.Folder),selected.Characteristic,selected.Difficulty,postfixFirst=first,nativeNotes=times.Length,ownNotes=ownTimes.Length,mapSha256=before,infoSha256=infoBefore,notesAdmitted,pass});
                }
            }
        }
        finally{NoodleRowBridge.CombinedMapping=false;}
        return new{cases,failed,arcHeadTailSamples,reverseOrderRejected=NoodleRowBridge.CombinedReverseOrderError!=null,reverseOrderError=NoodleRowBridge.CombinedReverseOrderError,scope="Mapping-first transpilers, both postfix orders; reverse transpiler order is a rejection control; nominal density and canonical note input only; no combined wall geometry",results};
    }
}
