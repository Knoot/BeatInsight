using System.Collections;
using BeatInsight.Core;
using BeatInsight.Formats;
using Newtonsoft.Json.Linq;

namespace BeatInsight.Compare;

static class IndependentArcDensityChecks
{
    internal static object Run(string gameRoot,Func<JObject,float,BeatmapData> load,string? inventory=null)
    {
        string folder=Path.Combine(".local","density-arc-native-fixtures",Guid.NewGuid().ToString("N"));Directory.CreateDirectory(folder);
        File.WriteAllText(Path.Combine(folder,"Info.dat"),"""{"version":"4.0.1","audio":{"bpm":120,"songDuration":12},"difficultyBeatmaps":[{"characteristic":"Standard","difficulty":"Expert","beatmapDataFilename":"map.dat","customData":{"requirements":["Noodle Extensions"]}}]}""");
        int cases=0,failed=0,chainProjections=0,loadedMaps=0,nativeRejected=0;var results=new List<object>();
        MapNote[] Prepared(JObject root,float bpm=120)
        {
            var map=load(root,bpm);
            loadedMaps++;
            if(root["burstSliders"] is JArray {Count:>0})
            {chainProjections++;return NoodleBurstProjection.Prepare(gameRoot,map,bpm,densityOnly:true).Notes;}
            return OracleBridge.GetItems(map).OfType<NoteData>()
                .Where(n=>((IDictionary)n.GetType().GetProperty("customData")!.GetValue(n)!)["NE_fake"] is not true&&(int)n.scoringType>0&&(int)n.scoringType!=4&&(int)n.scoringType!=7)
                .Select(n=>new MapNote{Time=n.time,Color=(int)n.colorType,ScoringType=(int)n.scoringType}).OrderBy(n=>n.Time).ToArray();
        }
        var mapCase=new MapCase{Folder=folder,Characteristic="Standard",Difficulty="Expert",File="map.dat"};
        foreach(bool change in new[]{false,true})foreach(bool fake in new[]{false,true})foreach(bool fakeBomb in new[]{false,true})
        foreach(bool orphan in new[]{false,true})foreach(bool reverse in new[]{false,true})foreach(bool player in new[]{false,true})
        foreach(string topology in new[]{"cross-color","same-color","repeated","coincident"})
        foreach(bool omitDefaults in new[]{false,true})
        {
            var root=JObject.Parse("""{"version":"3.3.0","colorNotes":[{"b":2,"x":0,"y":0,"c":0,"d":1},{"b":4,"x":1,"y":1,"c":1,"d":1}],"bombNotes":[],"sliders":[{"b":2,"x":0,"y":0,"c":0,"d":1,"mu":1,"tb":4,"tx":1,"ty":1,"tc":1,"tmu":1,"m":0}],"bpmEvents":[],"customData":{}}""");
            foreach(var note in root["colorNotes"]!)note["customData"]=new JObject{["worldRotation"]=new JArray(30,90,0)};
            root["colorNotes"]![0]!["customData"]!["NE_fake"]=fake;
            root["customData"]!["fakeColorNotes"]=new JArray(new JObject{["b"]=3,["x"]=0,["y"]=0,["c"]=0,["d"]=1});
            if(fakeBomb)root["customData"]!["fakeBombNotes"]=new JArray(new JObject{["b"]=4,["x"]=1,["y"]=1});
            if(change)((JArray)root["bpmEvents"]!).Add(new JObject{["b"]=2,["m"]=180});
            if(orphan)root["sliders"]![0]!["b"]=1;
            if(topology=="same-color")root["colorNotes"]![1]!["c"]=0;
            if(topology=="repeated")((JArray)root["sliders"]!).Add(root["sliders"]![0]!.DeepClone());
            if(topology=="coincident")
            {
                root["sliders"]![0]!["tb"]=2;root["sliders"]![0]!["tx"]=0;root["sliders"]![0]!["ty"]=0;
            }
            if(reverse)root["colorNotes"]=new JArray(((JArray)root["colorNotes"]!).Reverse().Select(n=>n.DeepClone()));
            if(player)root["customData"]!["customEvents"]=new JArray(new JObject{["b"]=0,["t"]="AssignPlayerToTrack",["d"]=new JObject{["track"]="player"}});
            if(omitDefaults)foreach(var item in ((JArray)root["colorNotes"]!).Concat((JArray)root["sliders"]!))
                foreach(string field in new[]{"b","c","x","y","tx","ty"})if(item[field]?.Type==JTokenType.Integer&&(int)item[field]! ==0)((JObject)item).Remove(field);
            var native=Prepared(root);string json=root.ToString();File.WriteAllText(Path.Combine(folder,"map.dat"),json);
            var own=LocalMap.Load(mapCase,AnalysisFeatures.Density);
            var analyzed=CompatibleAnalysis.Analyze(own.Input,reproduceKnownDefects:false,features:AnalysisFeatures.Density);
            var goldTimes=fake?new[]{change?5f/3:2f}:new[]{1f,change?5f/3:2f};
            bool pass=native.Length==(fake?1:2)&&native.Select(n=>n.Time).Zip(goldTimes,(a,b)=>Math.Abs(a-b)<.00001f).All(x=>x)
                &&native.All(n=>n.Color>=0)&&own.Json==json
                &&analyzed.Segments.SequenceEqual(CompatibleAnalysis.SegmentValues(native,12))
                &&analyzed.LeftBins.Length==0&&analyzed.RightBins.Length==0;
            cases++;if(!pass)failed++;results.Add(new{change,fake,fakeBomb,orphan,reverse,player,topology,omitDefaults,nativeNotes=native.Length,pass});
        }
        foreach(bool omitBeat in new[]{false,true})foreach(bool omitColor in new[]{false,true})
        {
            var root=JObject.Parse("""{"version":"3.3.0","colorNotes":[{"b":0,"c":0,"customData":{"worldRotation":[30,90,0]}}],"sliders":[{}]}""");
            if(omitBeat)((JObject)root["colorNotes"]![0]!).Remove("b");
            if(omitColor)((JObject)root["colorNotes"]![0]!).Remove("c");
            var native=Prepared(root);File.WriteAllText(Path.Combine(folder,"map.dat"),root.ToString());
            var own=LocalMap.Load(mapCase,AnalysisFeatures.Density);
            bool pass=native.Length==1&&native[0].Time==0&&native[0].Color==0&&own.Input.Notes.Length==1&&own.Input.Notes[0].Time==0;
            cases++;if(!pass)failed++;results.Add(new{name="zero-field-defaults",omitBeat,omitColor,pass});
        }
        foreach(bool change in new[]{false,true})foreach(string location in new[]{"head","tail","after-tail","before-tail","far","other-column"})
        foreach(string coordinates in new[]{"none","both","bomb-only","partial"})foreach(bool fake in new[]{false,true})
        foreach(bool reverse in new[]{false,true})foreach(bool zeroDuration in new[]{false,true})
        {
            var root=JObject.Parse("""{"version":"3.3.0","colorNotes":[{"b":0,"x":0,"y":0,"c":0,"d":1},{"b":4,"x":1,"y":0,"c":1,"d":1}],"bombNotes":[{"b":4,"x":1,"y":0},{"b":8,"x":3,"y":2}],"sliders":[{"b":0,"x":0,"y":0,"c":0,"d":1,"tb":4,"tx":1,"ty":0,"tc":1}],"bpmEvents":[],"customData":{"customEvents":[{"b":0,"t":"AssignPlayerToTrack","d":{"track":"player"}}]}}""");
            var target=root["bombNotes"]![0]!;var arc=root["sliders"]![0]!;
            target["b"]=location switch{"head"=>0f,"after-tail"=>4.0005f,"before-tail"=>3.9995f,"far"=>6f,_=>4f};
            target["x"]=location=="head"?0:location=="other-column"?3:1;
            target["customData"]=new JObject{["NE_fake"]=fake,["worldRotation"]=new JArray(30,90,0),["animation"]=new JObject{["offsetPosition"]=new JArray(10,0,0)}};
            if(coordinates!="none")
            {
                var point=coordinates=="partial"?new JArray(JValue.CreateNull(),.25f):new JArray(-.5f,.25f);
                target["customData"]!["coordinates"]=point.DeepClone();
                if(coordinates!="bomb-only")
                {
                    arc["customData"]=new JObject{["coordinates"]=point.DeepClone(),["tailCoordinates"]=point.DeepClone()};
                    foreach(var note in root["colorNotes"]!)note["customData"]=new JObject{["coordinates"]=point.DeepClone()};
                }
            }
            if(zeroDuration)arc["tb"]=0;
            if(change)((JArray)root["bpmEvents"]!).Add(new JObject{["b"]=2,["m"]=180});
            if(reverse)root["bombNotes"]=new JArray(((JArray)root["bombNotes"]!).Reverse().Select(n=>n.DeepClone()));
            var native=Prepared(root);File.WriteAllText(Path.Combine(folder,"map.dat"),root.ToString());
            var own=LocalMap.Load(mapCase,AnalysisFeatures.Density);
            var ownNotes=own.Input.Notes.Where(n=>!n.IsFake).OrderBy(n=>n.Time).ToArray();
            bool pass=ownNotes.Length==native.Length&&ownNotes.Zip(native,(a,b)=>Math.Abs(a.Time-b.Time)<.00001f&&a.Color==b.Color).All(x=>x)
                &&CompatibleAnalysis.Analyze(own.Input,reproduceKnownDefects:false,features:AnalysisFeatures.Density).Segments.SequenceEqual(CompatibleAnalysis.SegmentValues(native,12));
            cases++;if(!pass)failed++;results.Add(new{name="bomb-row-density",change,location,coordinates,fake,reverse,zeroDuration,nativeNotes=native.Length,ownNotes=ownNotes.Length,pass});
        }
        var bomb=JObject.Parse("""{"version":"3.3.0","colorNotes":[],"bombNotes":[{"b":4,"x":1,"y":0}],"sliders":[{"b":0,"x":3,"y":0,"c":0,"d":1,"mu":1,"tb":4,"tx":1,"ty":0,"tc":1,"tmu":1,"m":0}],"customData":{"customEvents":[{"b":0,"t":"AssignPlayerToTrack","d":{"track":"player"}}]}}""");
        var promoted=Prepared(bomb);File.WriteAllText(Path.Combine(folder,"map.dat"),bomb.ToString());
        var ownBomb=LocalMap.Load(mapCase,AnalysisFeatures.Density);
        bool bombPass=promoted.Any(n=>n.Color<0&&n.ScoringType>0)&&ownBomb.Input.Notes.Count(n=>!n.IsFake)==promoted.Length
            &&CompatibleAnalysis.Analyze(ownBomb.Input,reproduceKnownDefects:false,features:AnalysisFeatures.Density).Segments.SequenceEqual(CompatibleAnalysis.SegmentValues(promoted,12));
        cases++;if(!bombPass)failed++;results.Add(new{name="real-bomb-arc-tail-counted",promoted=promoted.Length,pass=bombPass});
        foreach(bool physicalHead in new[]{false,true})foreach(int slices in new[]{1,2,4,8})foreach(bool change in new[]{false,true})
        foreach(string coordinates in new[]{"none","both","partial"})foreach(float squish in new[]{.5f,1f,2f})
        foreach(int direction in new[]{0,1,8})foreach(bool sharedArc in new[]{false,true})
        foreach(bool zeroDuration in new[]{false,true})
        {
            if(!physicalHead&&(sharedArc||zeroDuration||slices==1))continue;
            var root=JObject.Parse("""{"version":"3.3.0","colorNotes":[],"bombNotes":[],"burstSliders":[{"b":2,"x":0,"y":0,"c":0,"d":1,"tb":4,"tx":1,"ty":1,"sc":4,"s":1}],"bpmEvents":[],"customData":{"customEvents":[{"b":0,"t":"AssignPlayerToTrack","d":{"track":"player"}}]}}""");
            var chain=root["burstSliders"]![0]!;chain["sc"]=slices;chain["s"]=squish;chain["d"]=direction;
            if(zeroDuration)chain["tb"]=2;
            chain["customData"]=new JObject{["worldRotation"]=new JArray(30,90,0)};
            if(physicalHead)((JArray)root["colorNotes"]!).Add(new JObject{["b"]=2,["x"]=0,["y"]=0,["c"]=0,["d"]=direction,["customData"]=new JObject{["worldRotation"]=new JArray(30,90,0)}});
            if(coordinates!="none")
            {
                var point=coordinates=="partial"?new JArray(JValue.CreateNull(),.25f):new JArray(-1f,.25f);
                chain["customData"]!["coordinates"]=point.DeepClone();chain["customData"]!["tailCoordinates"]=new JArray(0f,1.25f);
                foreach(var note in root["colorNotes"]!)note["customData"]!["coordinates"]=point.DeepClone();
            }
            if(sharedArc)
            {
                var arc=(JObject)chain.DeepClone();arc.Remove("sc");arc.Remove("s");arc["tb"]=6;arc["tx"]=3;arc["ty"]=2;arc["tc"]=1;
                ((JObject)arc["customData"]!).Remove("tailCoordinates");root["sliders"]=new JArray(arc);
            }
            root["customData"]!["fakeColorNotes"]=new JArray(new JObject{["b"]=8,["x"]=3,["y"]=2,["c"]=1,["d"]=1});
            if(change)((JArray)root["bpmEvents"]!).Add(new JObject{["b"]=3,["m"]=180});
            var native=Prepared(root);File.WriteAllText(Path.Combine(folder,"map.dat"),root.ToString());
            var own=LocalMap.Load(mapCase,AnalysisFeatures.Density);var ownNotes=own.Input.Notes.Where(n=>!n.IsFake).OrderBy(n=>n.Time).ToArray();
            float tail=zeroDuration?1:change?11f/6:2f,goldTime=physicalHead?1:1+(tail-1)/(slices-1);
            bool pass=native.Length==1&&Math.Abs(native[0].Time-goldTime)<.00001f&&native[0].SliceCount==(physicalHead?slices:slices-1)
                &&ownNotes.Length==native.Length&&ownNotes.Zip(native,(a,b)=>Math.Abs(a.Time-b.Time)<.00001f&&a.Color==b.Color&&a.SliceCount==b.SliceCount).All(x=>x)
                &&CompatibleAnalysis.Analyze(own.Input,reproduceKnownDefects:false,features:AnalysisFeatures.Density).Segments.SequenceEqual(CompatibleAnalysis.SegmentValues(native,12));
            cases++;if(!pass)failed++;results.Add(new{name="chain-density",physicalHead,slices,change,coordinates,squish,direction,sharedArc,zeroDuration,nativeNotes=native.Length,ownNotes=ownNotes.Length,pass});
        }
        foreach(bool chain in new[]{false,true})foreach(string target in new[]{"note","bomb","slider"})
        {
            var root=JObject.Parse("""{"version":"3.3.0","colorNotes":[{"b":2,"x":0,"y":0,"c":0,"d":1}],"bombNotes":[{"b":8,"x":1,"y":1}],"customData":{"customEvents":[{"b":0,"t":"AssignPlayerToTrack","d":{"track":"player"}}]}}""");
            var slider=JObject.Parse("""{"b":2,"x":0,"y":0,"c":0,"d":1,"tb":4,"tx":1,"ty":1,"tc":1}""");
            if(chain){slider["sc"]=4;slider["s"]=1;}else root["bombNotes"]![0]!["b"]=4;
            if(target=="note")root["colorNotes"]![0]!["customData"]=JValue.CreateNull();
            if(target=="bomb")root["bombNotes"]![0]!["customData"]=JValue.CreateNull();
            if(target=="slider")slider["customData"]=JValue.CreateNull();
            root[chain?"burstSliders":"sliders"]=new JArray(slider);
            bool rejected=false,ownRejected=false;
            try{Prepared(root);}catch(System.Reflection.TargetInvocationException e)when(e.GetBaseException() is NullReferenceException inner&&inner.StackTrace?.Contains("CustomJSONData.JsonExtensions.",StringComparison.Ordinal)==true){rejected=true;nativeRejected++;}
            File.WriteAllText(Path.Combine(folder,"map.dat"),root.ToString());
            try{LocalMap.Load(mapCase,AnalysisFeatures.Density);}catch(NotSupportedException){ownRejected=true;}
            bool pass=rejected&&ownRejected;
            cases++;if(!pass)failed++;results.Add(new{name="null-row-metadata-rejected",chain,target,pass});
        }
        if(inventory!=null)
        {
            var maps=System.Text.Json.JsonSerializer.Deserialize<MapCase[]>(File.ReadAllText(inventory))??throw new InvalidDataException("Missing density map inventory");
            foreach(var map in maps)
            {
                var own=LocalMap.Load(map,AnalysisFeatures.Density);var native=Prepared(JObject.Parse(own.Json),own.Bpm);
                var analyzed=CompatibleAnalysis.Analyze(own.Input,reproduceKnownDefects:false,features:AnalysisFeatures.Density);
                var ownNotes=own.Input.Notes.Where(n=>!n.IsFake).OrderBy(n=>n.Time).ToArray();
                bool pass=ownNotes.Length==native.Length&&ownNotes.Zip(native,(a,b)=>Math.Abs(a.Time-b.Time)<.0001f).All(x=>x)
                    &&analyzed.Segments.SequenceEqual(CompatibleAnalysis.SegmentValues(native,own.Input.Duration));
                cases++;if(!pass)failed++;results.Add(new{name="local-map-density",level=Path.GetFileName(map.Folder),map.Characteristic,map.Difficulty,map.File,nativeNotes=native.Length,ownNotes=ownNotes.Length,pass});
            }
        }
        bool processorPass=NoodleRowBridge.InstalledProcessors==loadedMaps&&loadedMaps==cases-nativeRejected&&NoodleRowBridge.AllCalls>0&&NoodleRowBridge.ColorCalls>0;
        cases++;if(!processorPass)failed++;results.Add(new{name="installed-row-pipeline",pass=processorPass});
        return new{schemaVersion=1,profileVersion=LocalMap.AnalysisProfileVersion,cases,failed,results,chainProjections,nativeRejected,processors=NoodleRowBridge.InstalledProcessors,allRows=NoodleRowBridge.AllCalls,colorRows=NoodleRowBridge.ColorCalls,scope="Native custom v3 loader with installed Noodle row processor; nominal density only, ordinary arcs including row-classified bombs and validated chains. Native chain spawner callbacks use controlled vectors/half jump and nominal coordinate/fake metadata; no Noodle spawn patches or Unity/player animation. World/player geometry remains unsupported. Source JSON preserved."};
    }
}
