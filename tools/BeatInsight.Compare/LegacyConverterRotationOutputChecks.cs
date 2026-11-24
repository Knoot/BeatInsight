using System.Collections;
using System.Reflection;
using Newtonsoft.Json.Linq;

namespace BeatInsight.Compare;

static class LegacyConverterRotationOutputChecks
{
    const BindingFlags flags=BindingFlags.Public|BindingFlags.NonPublic|BindingFlags.Instance|BindingFlags.Static;
    static object Get(object value,string name)=>value.GetType().GetProperty(name,flags)?.GetValue(value)??value.GetType().GetField(name,flags)?.GetValue(value)
        ??throw new InvalidDataException("Legacy converter member changed: "+value.GetType().FullName+"."+name);
    internal static object Run(string gameRoot)
    {
        var game=AppDomain.CurrentDomain.GetAssemblies().Single(a=>a.GetName().Name=="DataModels");
        var custom=AppDomain.CurrentDomain.GetAssemblies().Single(a=>a.GetName().Name=="CustomJSONData");
        var patches=custom.GetType("CustomJSONData.HarmonyPatches.ConvertersCustomify",true)!;
        var bpm=game.GetType("BpmTimeProcessor",true)!.GetConstructors(flags).Single(c=>c.GetParameters().Length==2&&c.GetParameters()[1].ParameterType.GenericTypeArguments[0].Name=="EventData");
        var rotation=game.GetType("RotationTimeProcessor",true)!.GetConstructors(flags).Single(c=>c.GetParameters().Length==1&&c.GetParameters()[0].ParameterType.GenericTypeArguments[0].Name=="EventData");
        float[] beats={0,.001f,1,2,2.001f,4,4.001f,8};
        var fixtures=new ((float Beat,int Value)[] Events,int[] Gold)[]{
            (new[]{(0f,6),(2f,3),(4f,5)},new[]{45,45,45,45,30,30,60,60}),
            (new[]{(0f,1),(2f,4),(4f,5)},new[]{0,-45,-45,-45,-30,-30,0,0}),
            (new[]{(2f,4),(2f,5),(4f,3)},new[]{0,0,0,0,45,45,30,30})};
        int[][] orders={Enumerable.Range(0,8).ToArray(),Enumerable.Range(0,8).Reverse().ToArray(),new[]{7,0,4,3,6,1,5,2}};
        int cases=0,failed=0,tailChecks=0,prefixCalls=0;var failures=new List<object>();var results=new List<object>();
        foreach(string kind in new[]{"note","bomb","wall","slider"})
        {
            string name=kind=="wall"?"Obstacle":kind=="slider"?"Slider":"ColorNote";
            var type=game.GetType("BeatmapDataLoaderVersion2_6_0AndEarlier.BeatmapDataLoader+"+name+"Converter",true)!;
            var original=type.GetMethod("Convert",flags)!;
            var patch=patches.GetMethod(name+"ConvertV2_6_0AndEarlier",flags)!;
            MethodInfo? compiled=kind=="wall"?null:InstalledTranspilerCompiler.Compile(original,patch).Method;
            int beforeCases=cases,beforeFailed=failed;
            foreach(var fixture in fixtures)foreach(int eventType in new[]{14,15})foreach(bool changeBpm in new[]{false,true})foreach(bool fake in new[]{false,true})foreach(var order in orders)
            {
                object? instance=null;
                foreach(int index in order)
                {
                    float beat=beats[index],tail=beat>=4?beat+.001f:4;
                    int tailIndex=index<5?5:index==5?6:index;
                    var metadata=new JObject{["probeIdentity"]="legacy-rotation-output",["_rotation"]=new JArray(0,90,0)};
                    if(fake)metadata["_fake"]=true;
                    var item=kind=="slider"?new JObject{["_time"]=beat,["_tailTime"]=tail,["_headLineIndex"]=0,["_noteLineLayer"]=1,["_noteCutDirection"]=1,["_tailLineIndex"]=2,["_tailLineLayer"]=2,["_tailCutDirection"]=1,["_colorType"]=0}:
                        new JObject{["_time"]=beat,["_lineIndex"]=0,["_lineLayer"]=1,["_type"]=kind=="bomb"?3:0,["_cutDirection"]=1,["_duration"]=2,["_width"]=2};
                    item["_customData"]=metadata;
                    string collection=kind=="slider"?"sliders":kind=="wall"?"obstacles":"notes";
                    var events=fixture.Events.Select(e=>new JObject{["_time"]=e.Beat,["_type"]=eventType,["_value"]=e.Value}).ToList();
                    if(changeBpm)events.Add(new JObject{["_time"]=2,["_type"]=100,["_value"]=180,["_floatValue"]=180});
                    var root=new JObject{["_version"]="2.6.0",["_notes"]=new JArray(),["_obstacles"]=new JArray(),["_"+collection]=new JArray(item),["_events"]=new JArray(events.OrderBy(e=>(float)e["_time"]!))};
                    var saved=NoodleNativeData.Deserialize(gameRoot,root,"2");
                    var source=((IEnumerable)Get(saved,collection)).Cast<object>().Single();
                    instance??=Activator.CreateInstance(type,flags,null,new[]{bpm.Invoke(new[]{(object)120f,Get(saved,"events")}),rotation.Invoke(new[]{Get(saved,"events")})},null)!;
                    var before=JToken.FromObject(Get(source,"customData"));var native=original.Invoke(instance,new[]{source})!;
                    object actual;
                    if(compiled!=null)actual=compiled.Invoke(null,new[]{instance,source})!;
                    else
                    {
                        object?[] args={instance,source,null};bool proceed=(bool)patch.Invoke(null,args)!;prefixCalls++;
                        if(proceed||args[2]==null)throw new InvalidDataException("Legacy obstacle prefix did not produce replacement");actual=args[2]!;
                    }
                    double Time(float b)=>!changeBpm||b<=2?b/2d:1+(b-2)/3d;
                    bool pass=Convert.ToInt32(Get(native,"rotation"))==fixture.Gold[index]&&Convert.ToInt32(Get(actual,"rotation"))==fixture.Gold[index]
                        &&Math.Abs(((BeatmapObjectData)actual).time-Time(beat))<.00001
                        &&ReferenceEquals(Get(actual,"customData"),Get(source,"customData"))&&JToken.DeepEquals(before,JToken.FromObject(Get(source,"customData")));
                    if(kind=="wall")pass&=Math.Abs(Convert.ToDouble(Get(actual,"duration"))-(Time(beat+2)-Time(beat)))<.00001;
                    if(kind=="slider")
                    {
                        tailChecks++;pass&=Convert.ToInt32(Get(actual,"tailRotation"))==fixture.Gold[tailIndex]&&Convert.ToInt32(Get(native,"tailRotation"))==fixture.Gold[tailIndex]
                            &&Math.Abs(Convert.ToDouble(Get(actual,"tailTime"))-Time(tail))<.00001;
                    }
                    cases++;if(!pass){failed++;failures.Add(new{kind,beat,eventType,changeBpm,fake,expected=fixture.Gold[index],actual=Get(actual,"rotation"),sourceTime=Get(source,"time"),time=((BeatmapObjectData)actual).time,
                        source=kind=="slider"?JToken.FromObject(source):null,tailRotation=kind=="slider"?Get(actual,"tailRotation"):null,tailTime=kind=="slider"?Get(actual,"tailTime"):null});}
                }
            }
            results.Add(new{kind,cases=cases-beforeCases,failed=failed-beforeFailed});
        }
        return new{cases,failed,tailChecks,prefixCalls,results,failures,
            scope="Actual v2 save-data deserialization, native BPM/rotation processors, note/bomb/slider custom converter transpilers and obstacle replacement prefix. Independent angles/times; shared processors across nonmonotonic queries, head/tail boundaries, fake metadata and source identity. Nonzero _rotation metadata preserved only; no Noodle quaternion composition, mirror, runtime ordering or admission."};
    }
}
