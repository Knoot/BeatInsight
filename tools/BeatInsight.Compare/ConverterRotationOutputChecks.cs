using System.Collections;
using System.Reflection;
using Newtonsoft.Json.Linq;

namespace BeatInsight.Compare;

static class ConverterRotationOutputChecks
{
    const BindingFlags flags=BindingFlags.Public|BindingFlags.NonPublic|BindingFlags.Instance|BindingFlags.Static;
    static object Get(object value,string name)=>value.GetType().GetProperty(name,flags)?.GetValue(value)??value.GetType().GetField(name,flags)?.GetValue(value)
        ??throw new InvalidDataException("Rotation output member changed: "+value.GetType().FullName+"."+name);
    internal static object Run(string gameRoot,string collection,Type converter,MethodInfo plain,MethodInfo patched)
    {
        var game=converter.Assembly;
        var bpmCtor=game.GetType("BpmTimeProcessor",true)!.GetConstructors(flags).Single(c=>c.GetParameters().Length==2&&c.GetParameters()[1].ParameterType.GenericTypeArguments[0].Name=="BpmChangeEventData");
        var rotationCtor=game.GetType("RotationTimeProcessor",true)!.GetConstructors(flags).Single(c=>c.GetParameters().Length==1&&c.GetParameters()[0].ParameterType.GenericTypeArguments[0].Name=="RotationEventData");
        float[] beats={0,.001f,1,2,2.001f,4,4.001f,8};
        var fixtures=new ((float Beat,float Angle)[] Events,int[] Gold)[]{
            (new[]{(0f,45f),(2f,-15.9f),(4f,30.9f)},new[]{45,45,45,45,30,30,60,60}),
            (new[]{(0f,-45f),(2f,15.9f),(4f,30.9f)},new[]{0,-45,-45,-45,-30,-30,0,0}),
            (new[]{(2f,15f),(2f,30f),(4f,-15f)},new[]{0,0,0,0,45,45,30,30})};
        int[][] orders={Enumerable.Range(0,8).ToArray(),Enumerable.Range(0,8).Reverse().ToArray(),new[]{7,0,4,3,6,1,5,2}};
        int cases=0,failed=0,tailChecks=0;var failures=new List<object>();
        foreach(var fixture in fixtures)foreach(int execution in new[]{0,1})foreach(bool changeBpm in new[]{false,true})foreach(bool fake in new[]{false,true})foreach(var order in orders)
        {
            object? instance=null;
            foreach(int index in order)
            {
                float beat=beats[index],tailBeat=beat>=4?beat+.001f:4f;
                int tailIndex=index<5?5:index==5?6:index;
                var metadata=new JObject{["probeIdentity"]="rotation-output",["worldRotation"]=new JArray(0,90,0)};
                if(fake)metadata["NE_fake"]=true;
                var item=new JObject{["b"]=beat,["x"]=0,["y"]=1,["c"]=0,["d"]=1,["tb"]=tailBeat,["tx"]=2,["ty"]=2,["tc"]=1,["sc"]=3,["s"]=1,["customData"]=metadata};
                if(collection=="obstacles"){item["d"]=2;item["w"]=2;item["h"]=3;}
                var root=new JObject{["version"]="3.3.0",["colorNotes"]=new JArray(),[collection]=new JArray(item),
                    ["rotationEvents"]=new JArray(fixture.Events.Select(e=>new JObject{["b"]=e.Beat,["e"]=execution,["r"]=e.Angle})),
                    ["bpmEvents"]=changeBpm?new JArray(new JObject{["b"]=2,["m"]=180}):new JArray()};
                var saved=NoodleNativeData.Deserialize(gameRoot,root,"3");
                var source=((IEnumerable)Get(saved,collection)).Cast<object>().Single();
                instance??=Activator.CreateInstance(converter,flags,null,new[]{bpmCtor.Invoke(new[]{(object)120f,Get(saved,"bpmEvents")}),rotationCtor.Invoke(new[]{Get(saved,"rotationEvents")})},null)!;
                var before=JToken.FromObject(Get(source,"customData"));
                var native=plain.Invoke(instance,new[]{source})!;var actual=patched.Invoke(null,new[]{instance,source})!;
                bool slider=collection is "sliders" or "burstSliders";
                const string head="rotation";
                double Time(float b)=>!changeBpm||b<=2?b/2d:1+(b-2)/3d;
                bool pass=Convert.ToInt32(Get(actual,head))==fixture.Gold[index]&&Convert.ToInt32(Get(native,head))==fixture.Gold[index]
                    &&Math.Abs(((BeatmapObjectData)actual).time-Time(beat))<.00001
                    &&ReferenceEquals(Get(actual,"customData"),Get(source,"customData"))&&JToken.DeepEquals(before,JToken.FromObject(Get(source,"customData")));
                if(slider)
                {
                    tailChecks++;
                    pass&=Convert.ToInt32(Get(actual,"tailRotation"))==fixture.Gold[tailIndex]&&Convert.ToInt32(Get(native,"tailRotation"))==fixture.Gold[tailIndex]
                        &&Math.Abs(Convert.ToDouble(Get(actual,"tailTime"))-Time(tailBeat))<.00001;
                }
                if(collection=="obstacles")pass&=Math.Abs(Convert.ToDouble(Get(actual,"duration"))-(Time(beat+2)-Time(beat)))<.00001;
                cases++;if(!pass){failed++;failures.Add(new{collection,beat,tailBeat,execution,changeBpm,fake,expected=fixture.Gold[index],actual=Get(actual,head)});}
            }
        }
        return new{cases,failed,tailChecks,failures,
            scope="Native v3 converters and actual custom converter transpilers share native BPM/rotation processors across nonmonotonic queries. Independent angle/time gold covers head/tail event boundaries and obstacle duration. Nonzero worldRotation metadata is preserved only, not composed through Noodle constructor/Unity pose. No loader ordering, handedness, runtime dispatch or admission proof."};
    }
}
