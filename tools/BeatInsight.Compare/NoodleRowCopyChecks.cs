using System.Collections;
using System.Reflection;
using System.Runtime.Loader;
using Newtonsoft.Json.Linq;

namespace BeatInsight.Compare;

static class NoodleRowCopyChecks
{
    public static object Run(string gameRoot,BeatmapData source,float bpm)
    {
        const BindingFlags flags=BindingFlags.Public|BindingFlags.NonPublic|BindingFlags.Instance|BindingFlags.Static;
        string[] cacheKeys={"NE_flipYSide","NE_flipLineIndex","NE_startNoteLineLayer","NE_tailStartNoteLineLayer"};
        var generatedFields=new HashSet<string>(new[]{"subtypeIdentifier","isArcHead","isArcTail","beforeJumpNoteLineLayer","headBeforeJumpLineLayer","tailBeforeJumpLineLayer",
            "timeToNextColorNote","timeToPrevColorNote","flipLineIndex","flipYSide","cutDirectionAngleOffset"},StringComparer.Ordinal);
        IDictionary Data(BeatmapObjectData item)=>(IDictionary)item.GetType().GetProperty("customData")!.GetValue(item)!;
        string Id(BeatmapObjectData item)=>Data(item)["probeObjectIdentity"] as string??throw new InvalidDataException("Copy source identity missing");
        JObject Fields(BeatmapObjectData item)
        {
            Type type=item is NoteData?typeof(NoteData):item is SliderData?typeof(SliderData):item is ObstacleData?typeof(ObstacleData):typeof(BeatmapObjectData);
            return new JObject(type.GetProperties(BindingFlags.Public|BindingFlags.Instance).Where(p=>p.GetIndexParameters().Length==0)
                .Select(p=>new JProperty(p.Name,p.GetValue(item)==null?JValue.CreateNull():JToken.FromObject(p.GetValue(item)!))));
        }
        var original=OracleBridge.GetItems(source).OfType<BeatmapObjectData>().ToDictionary(Id);
        bool hasChains=original.Values.OfType<SliderData>().Any(s=>(int)s.sliderType==1);
        string? burstBoundary=null;
        if(hasChains)
        {
            try{_ = NoodleBurstProjection.Prepare(gameRoot,source,bpm);}
            catch(InvalidDataException e)when(e.Message=="Native chain head binding is ambiguous")
            {burstBoundary="Source fixture already has an ambiguous/nonmatching chain head; only copied raw bindings are compared";}
        }
        var sourceFields=original.ToDictionary(p=>p.Key,p=>Fields(p.Value));
        var sourceMetadata=original.ToDictionary(p=>p.Key,p=>JToken.FromObject(Data(p.Value)));
        var transform=AssemblyLoadContext.Default.Assemblies.Select(a=>a.GetType("BeatmapDataMirrorTransform")).First(t=>t!=null)!.GetMethod("CreateTransformedData",flags)!;
        var noodle=AssemblyLoadContext.Default.Assemblies.Single(a=>a.GetName().Name=="NoodleExtensions");
        var noteCtor=noodle.GetType("NoodleExtensions.NoodleNoteData",true)!.GetConstructors(flags).Single();var cp=noteCtor.GetParameters();
        var tracks=(IDictionary)Activator.CreateInstance(cp[4].ParameterType)!;var definitions=Activator.CreateInstance(cp[3].ParameterType)!;
        var trackType=AssemblyLoadContext.Default.LoadFromAssemblyName(new AssemblyName("Heck")).GetType("Heck.Animation.Track",true)!;
        foreach(var item in original.Values)
        {
            object? value=Data(item)["track"];
            var names=value is string one?new[]{one}:value is IEnumerable many?many.Cast<object>().OfType<string>():Array.Empty<string>();
            foreach(string name in names)if(!tracks.Contains(name))tracks[name]=Activator.CreateInstance(trackType,true)!;
        }
        int failed=0;var results=new List<object>();
        bool previousEnabled=NoodleRowBridge.Enabled;NoodleRowBridge.Enabled=true;
        var previousObserver=NoodleCopyMirrorBridge.AfterMirror;
        try
        {
        foreach(string mode in new[]{"copy","mirror","double-mirror","filtered-fake-notes"})
        {
            NoodleCopyMirrorBridge.Reset();
            int rowsBefore=NoodleRowBridge.AllCalls,colorBefore=NoodleRowBridge.ColorCalls;
            var errors=new List<string>();var generatedChanges=new List<object>();int immediateInvalidations=0,preInsertCaches=0;
            var mirrorCounts=new Dictionary<string,int>();
            NoodleCopyMirrorBridge.AfterMirror=value=>
            {
                var obj=(BeatmapObjectData)value;var metadata=Data(obj);string id=Id(obj);
                int count=mirrorCounts.GetValueOrDefault(id)+1;mirrorCounts[id]=count;
                foreach(string key in cacheKeys)if(metadata.Contains(key))errors.Add("Mirror postfix retained stale cache");
                foreach(string key in new[]{"coordinates","tailCoordinates"})
                    if(sourceMetadata[id][key] is JArray point&&point.Count>0&&point[0].Type is JTokenType.Integer or JTokenType.Float)
                    {
                        bool active=key!="tailCoordinates"||obj is SliderData;
                        float originalX=(float)point[0],expectedX=active&&count%2==1?-originalX-1:originalX;
                        if(metadata[key] is not IList actual||actual.Count==0||Math.Abs(Convert.ToSingle(actual[0])-expectedX)>1e-5)
                            errors.Add(id+":"+obj.GetType().Name+":"+key+": custom coordinate reflection differs");
                    }
                immediateInvalidations++;
            };
            BeatmapDataItem BeforeInsertion(BeatmapDataItem item)
            {
                if(item is BeatmapObjectData obj)
                {
                    if(!JToken.DeepEquals(JToken.FromObject(Data(obj)),sourceMetadata[Id(obj)]))errors.Add("GetCopy did not preserve source metadata before insertion");
                    preInsertCaches++;
                }
                return item;
            }
            var copy=mode=="copy"?source.GetFilteredCopy(BeforeInsertion):mode=="filtered-fake-notes"?
                source.GetFilteredCopy(item=>{BeforeInsertion(item);return item is NoteData n&&Data(n)["NE_fake"] is true?null!:item;}):
                (BeatmapData)transform.Invoke(null,new object[]{source})!;
            if(mode=="double-mirror")copy=(BeatmapData)transform.Invoke(null,new object[]{copy})!;
            var items=OracleBridge.GetItems(copy).OfType<BeatmapObjectData>().ToDictionary(Id);
            string? processor=typeof(BeatmapData).GetField("_beatmapObjectsInTimeRowProcessor",flags)!.GetValue(copy)?.GetType().FullName;
            if(processor!="NoodleExtensions.HarmonyPatches.ObjectProcessing.NoodleBeatmapObjectsInTimeRowProcessor")errors.Add("Copy receiver has no native Noodle row processor");
            var expected=original.Where(p=>mode!="filtered-fake-notes"||p.Value is not NoteData||Data(p.Value)["NE_fake"] is not true).ToDictionary(p=>p.Key,p=>p.Value);
            int cacheRemoved=0,cachePreserved=0,cacheRebuilt=0,cacheChanged=0,parsed=0;
            if(ReferenceEquals(copy,source)||copy.GetType()!=source.GetType()||!items.Keys.Order().SequenceEqual(expected.Keys.Order())
                ||NoodleCopyMirrorBridge.PrefixCalls!=(mode=="double-mirror"?2:1)||NoodleCopyMirrorBridge.Fallbacks!=0)errors.Add("Custom copy dispatch or identity set changed");
            foreach(var entry in items)
            {
                var item=entry.Value;var before=expected[entry.Key];var metadata=Data(item);
                if(ReferenceEquals(item,before)||ReferenceEquals(metadata,Data(before)))errors.Add("Copy aliases source object or top-level metadata");
                if(mode!="mirror")
                {
                    var fields=Fields(item);
                    foreach(var property in sourceFields[entry.Key].Properties())
                        if(!JToken.DeepEquals(property.Value,fields[property.Name]))
                        {
                            if(generatedFields.Contains(property.Name))generatedChanges.Add(new{source=entry.Key,field=property.Name,before=property.Value,after=fields[property.Name]});
                            else errors.Add(entry.Key+":"+property.Name+" changed");
                        }
                }
                else if(item is NoteData note&&before is NoteData old)
                {
                    int color=(int)old.colorType;
                    if(note.lineIndex!=3-old.lineIndex||(int)note.colorType!=(color<0?color:1-color)||note.scoringType!=old.scoringType||note.gameplayType!=old.gameplayType)
                        errors.Add(entry.Key+":mirrored note identity or role changed");
                }
                else if(item is SliderData slider&&before is SliderData oldSlider)
                {
                    if(slider.headLineIndex!=3-oldSlider.headLineIndex||slider.tailLineIndex!=3-oldSlider.tailLineIndex
                        ||slider.hasHeadNote!=oldSlider.hasHeadNote||slider.hasTailNote!=oldSlider.hasTailNote||slider.sliceCount!=oldSlider.sliceCount)
                        errors.Add(entry.Key+":mirrored slider binding changed");
                }
                bool mirrored=mode is "mirror" or "double-mirror";
                foreach(string key in cacheKeys)
                {
                    if(mirrored&&item is NoteData or SliderData)
                    {if(metadata.Contains(key))cacheRebuilt++;else if(Data(before).Contains(key))cacheRemoved++;}
                    else if(Data(before).Contains(key))
                    {if(!metadata.Contains(key))cacheRemoved++;else if(!JToken.DeepEquals(JToken.FromObject(metadata[key]!),JToken.FromObject(Data(before)[key]!)))cacheChanged++;else cachePreserved++;}
                }
                if(!mirrored)
                {
                    var actualMetadata=JObject.FromObject(metadata);var expectedMetadata=(JObject)sourceMetadata[entry.Key].DeepClone();
                    foreach(string key in cacheKeys){actualMetadata.Remove(key);expectedMetadata.Remove(key);}
                    if(!JToken.DeepEquals(actualMetadata,expectedMetadata))errors.Add(entry.Key+":opaque metadata changed");
                }
                string? name=item is NoteData?"NoodleNoteData":item is SliderData?"NoodleSliderData":null;
                if(name!=null)
                {
                    var ctor=noodle.GetType("NoodleExtensions."+name,true)!.GetConstructors(flags).Single();
                    var data=ctor.Invoke(new[]{(object)item,metadata,bpm,definitions,tracks,false,mode=="mirror"});parsed++;
                    if(metadata.Contains("NE_startNoteLineLayer"))
                    {
                        float actual=Convert.ToSingle(data.GetType().GetProperty("InternalStartNoteLineLayer",flags)!.GetValue(data));
                        if(actual!=Convert.ToSingle(metadata["NE_startNoteLineLayer"]))errors.Add(entry.Key+":copied cache not consumed");
                    }
                }
            }
            object? burst=null;
            if(hasChains&&burstBoundary==null)burst=NoodleBurstProjection.Prepare(gameRoot,copy,bpm).Evidence;
            foreach(var entry in original)
                if(!JToken.DeepEquals(Fields(entry.Value),sourceFields[entry.Key])||!JToken.DeepEquals(JToken.FromObject(Data(entry.Value)),sourceMetadata[entry.Key]))errors.Add("Source map mutated");
            if(errors.Count>0)failed++;
            if(mode is "mirror" or "double-mirror"&&immediateInvalidations!=original.Values.Count(item=>item is NoteData or SliderData)*(mode=="double-mirror"?2:1))errors.Add("Mirror observation count changed");
            results.Add(new{mode,objects=items.Count,processor,rows=NoodleRowBridge.AllCalls-rowsBefore,colorRows=NoodleRowBridge.ColorCalls-colorBefore,
                cacheRemoved,cachePreserved,cacheRebuilt,cacheChanged,immediateInvalidations,preInsertCaches,generatedChanges,parsed,burst,errors=errors.Take(16).ToArray()});
        }
        }
        finally{NoodleRowBridge.Enabled=previousEnabled;NoodleCopyMirrorBridge.AfterMirror=previousObserver;}
        return new{cases=4,failed,burstBoundary,results,scope="Actual custom copy/mirror with Noodle processor installed at controlled constructor boundary; source metadata checked before insertion, mirror cache invalidation observed immediately, later native row cache rebuild and real note/slider consumers executed. Generated field changes reported, not treated as physical pose equivalence; source immutability and burst binding checked where baseline projection is defined. No full Heck deserializer, all-mod ordering or Unity lifecycle."};
    }
}
