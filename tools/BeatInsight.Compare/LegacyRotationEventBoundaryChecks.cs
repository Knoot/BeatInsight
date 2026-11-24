using System.Reflection;
using Newtonsoft.Json.Linq;

namespace BeatInsight.Compare;

static class LegacyRotationEventBoundaryChecks
{
    internal static object Run(string gameRoot)
    {
        const BindingFlags flags=BindingFlags.Public|BindingFlags.NonPublic|BindingFlags.Instance|BindingFlags.Static;
        var game=AppDomain.CurrentDomain.GetAssemblies().Single(a=>a.GetName().Name=="DataModels");
        var type=game.GetType("RotationTimeProcessor",true)!;
        var ctor=type.GetConstructors(flags).Single(c=>c.GetParameters().Length==1&&c.GetParameters()[0].ParameterType.GenericTypeArguments.Single().Name=="EventData");
        var query=type.GetMethod("ConvertBeatToRotation",flags)!;var reset=type.GetMethod("Reset",flags)!;
        float[] beats={0,.001f,1,2,2.001f,4,4.001f,8};int[] angles={-60,-45,-30,-15,15,30,45,60};
        int[][] orders={Enumerable.Range(0,8).ToArray(),Enumerable.Range(0,8).Reverse().ToArray(),new[]{7,0,4,3,6,1,5,2}};
        int cases=0,failed=0,resets=0,fixtures=0;var failures=new List<object>();
        void Check(string name,(float Beat,int Type,int Value)[] events,int[] expected)
        {
            fixtures++;
            var root=new JObject{["_version"]="2.6.0",["_notes"]=new JArray(),["_obstacles"]=new JArray(),
                ["_events"]=new JArray(events.Select(e=>new JObject{["_time"]=e.Beat,["_type"]=e.Type,["_value"]=e.Value}))};
            var saved=NoodleNativeData.Deserialize(gameRoot,root,"2");
            var data=saved.GetType().GetProperty("events",flags)?.GetValue(saved)??saved.GetType().GetField("events",flags)!.GetValue(saved)!;
            var before=JToken.FromObject(data);var processor=ctor.Invoke(new[]{data});
            foreach(var order in orders)
            {
                reset.Invoke(processor,null);resets++;
                foreach(int index in order)
                {
                    int actual=(int)query.Invoke(processor,new object[]{beats[index]})!;
                    cases++;if(actual!=expected[index]){failed++;failures.Add(new{name,beat=beats[index],actual,expected=expected[index]});}
                }
            }
            if(!JToken.DeepEquals(before,JToken.FromObject(data)))throw new InvalidDataException("Legacy rotation source mutated");
        }
        foreach(int eventType in new[]{14,15})
        {
            foreach(int value in Enumerable.Range(-1,10))
            {
                int angle=value>=0&&value<8?angles[value]:0;
                Check("later-"+eventType+"-"+value,new[]{(2f,eventType,value)},new[]{0,0,0,0,angle,angle,angle,angle});
            }
            foreach(int value in Enumerable.Range(0,8))
            {
                int angle=angles[value];
                Check("initial-"+eventType+"-"+value,new[]{(0f,eventType,value)},new[]{Math.Max(0,angle),angle,angle,angle,angle,angle,angle,angle});
            }
            Check("same-beat-"+eventType,new[]{(2f,eventType,4),(2f,eventType,5),(4f,eventType,2)},new[]{0,0,0,0,45,45,15,15});
        }
        Check("mixed-and-unrelated",new[]{(1f,0,7),(2f,14,4),(3f,13,7),(4f,15,1),(5f,16,7)},new[]{0,0,0,0,15,15,-30,-30});
        return new{cases,failed,fixtures,resets,failures,
            scope="Actual legacy JSON deserialization and native RotationTimeProcessor constructor/query/Reset. Independent eight-angle table, both rotation types 14/15, invalid values, unrelated types, beat-zero sign asymmetry and duplicate beats; three time orders. No runtime early/late dispatch or Unity geometry/admission."};
    }
}
