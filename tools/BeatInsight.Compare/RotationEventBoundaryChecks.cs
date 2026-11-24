using System.Reflection;
using Newtonsoft.Json.Linq;

namespace BeatInsight.Compare;

static class RotationEventBoundaryChecks
{
    const BindingFlags flags=BindingFlags.Public|BindingFlags.NonPublic|BindingFlags.Instance|BindingFlags.Static;
    internal static object Run(string gameRoot)
    {
        var game=AppDomain.CurrentDomain.GetAssemblies().Single(a=>a.GetName().Name=="DataModels");
        var type=game.GetType("RotationTimeProcessor",true)!;
        var ctor=type.GetConstructors(flags).Single(c=>c.GetParameters().Length==1&&c.GetParameters()[0].ParameterType.GenericTypeArguments.Single().Name=="RotationEventData");
        var query=type.GetMethod("ConvertBeatToRotation",flags)!;var reset=type.GetMethod("Reset",flags)!;
        float[] beats={0,.001f,1,2,2.001f,4,4.001f,8};
        var fixtures=new (string Name,(float Beat,float Rotation)[] Events,int[] Expected)[]{
            ("empty",Array.Empty<(float,float)>(),new[]{0,0,0,0,0,0,0,0}),
            ("later",new[]{(2f,15f),(4f,-30f)},new[]{0,0,0,0,15,15,-15,-15}),
            ("initial-positive",new[]{(0f,45f),(2f,-15f)},new[]{45,45,45,45,30,30,30,30}),
            ("initial-negative",new[]{(0f,-45f),(2f,15f)},new[]{0,-45,-45,-45,-30,-30,-30,-30}),
            ("same-beat",new[]{(2f,15f),(2f,30f),(4f,-15f)},new[]{0,0,0,0,45,45,30,30}),
            ("fractional",new[]{(2f,15.9f),(4f,-30.9f)},new[]{0,0,0,0,15,15,-15,-15})};
        int cases=0,failed=0,resets=0;var failures=new List<object>();
        int[][] orders={Enumerable.Range(0,8).ToArray(),Enumerable.Range(0,8).Reverse().ToArray(),new[]{7,0,4,3,6,1,5,2}};
        foreach(var fixture in fixtures)foreach(int executionTime in new[]{0,1})
        {
            var root=new JObject{["version"]="3.3.0",["colorNotes"]=new JArray(),
                ["rotationEvents"]=new JArray(fixture.Events.Select(e=>new JObject{["b"]=e.Beat,["e"]=executionTime,["r"]=e.Rotation}))};
            var saved=NoodleNativeData.Deserialize(gameRoot,root,"3");
            var events=saved.GetType().GetField("rotationEvents",flags)?.GetValue(saved)
                ??saved.GetType().GetProperty("rotationEvents",flags)!.GetValue(saved)!;
            var before=JToken.FromObject(events);var processor=ctor.Invoke(new[]{events});
            foreach(var order in orders)
            {
                reset.Invoke(processor,null);resets++;
                foreach(int index in order)
                {
                    int actual=(int)query.Invoke(processor,new object[]{beats[index]})!;
                    cases++;if(actual!=fixture.Expected[index]){failed++;failures.Add(new{fixture=fixture.Name,executionTime,beat=beats[index],actual,expected=fixture.Expected[index],order=Array.IndexOf(orders,order)});}
                }
            }
            if(!JToken.DeepEquals(before,JToken.FromObject(events)))throw new InvalidDataException("Rotation processor mutated source events");
        }
        var legacy=JObject.FromObject(LegacyRotationEventBoundaryChecks.Run(gameRoot));
        failed+=(int)legacy["failed"]!;
        return new{cases,failed,resets,fixtures=fixtures.Length,executionTimes=2,failures,legacy,
            dataModelsSha256=Comparison.HashFile(Path.Combine(gameRoot,"Beat Saber_Data/Managed/DataModels.dll")),
            scope="Actual v3 JSON deserialization, native RotationTimeProcessor constructor/query/Reset. Explicit independent expected tables cover strict beat boundary, same-beat accumulation, fractional truncation and initial positive/negative asymmetry; forward, reverse and zigzag queries. ExecutionTime does not affect this processor. No Unity world pose, runtime event dispatch, mirror geometry or admission proof."};
    }
}
