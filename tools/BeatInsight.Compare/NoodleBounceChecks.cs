using System.Reflection;
using System.Runtime.Loader;
using BeatInsight.Formats;
using Newtonsoft.Json.Linq;

namespace BeatInsight.Compare;

static class NoodleBounceChecks
{
    public static object Run(string gameRoot)
    {
        string path=Path.Combine(gameRoot,"Plugins/Heck.dll");
        if(Comparison.HashFile(path)!="9E5163F09E122713543FB69F89799D1121FB712820E6B4350F7A30C5D790793C")
            throw new InvalidDataException("Reinspect changed bounce easing provider");
        var heck=AssemblyLoadContext.Default.LoadFromAssemblyPath(path);
        var easing=heck.GetType("Heck.Animation.Easings",true)!;
        var times=Enumerable.Range(0,10001).Select(i=>i/10000f).ToList();
        foreach(float boundary in new[]{0f,4f/11,8f/11,.9f,1f})
            foreach(float transformed in new[]{boundary,1-boundary,boundary*.5f,(1+boundary)*.5f,(1-boundary)*.5f})
                foreach(float time in new[]{MathF.BitDecrement(transformed),transformed,MathF.BitIncrement(transformed)})
                    if(time>=0&&time<=1)times.Add(time);
        int cases=0,failed=0;float maxError=0;
        var failures=new List<object>();
        foreach(string mode in new[]{"In","Out","InOut"})
        {
            var method=easing.GetMethod("Ease"+mode+"Bounce",BindingFlags.Public|BindingFlags.NonPublic|BindingFlags.Static)!;
            NoodleVectorCurve.TryRead(new JArray(new JArray(0,0,0,0),new JArray(1,0,0,1,"ease"+mode+"Bounce")),out var curve);
            foreach(float time in times)
            {
                float actual=(float)method.Invoke(null,new object[]{time})!,own=curve!.At(time).X;
                float error=Math.Abs(actual-own);maxError=Math.Max(maxError,error);cases++;
                if(!float.IsFinite(error)||error>2e-6f){failed++;if(failures.Count<20)failures.Add(new{mode,time,actual,own,error});}
            }
        }
        return new{schemaVersion=1,profileVersion=BeatInsight.Formats.LocalMap.AnalysisProfileVersion,cases,failed,maxError,failures,gameStarted=false,referenceUsed=false,
            scope="actual pinned scalar Bounce methods against own curve evaluation, dense grid and adjacent floats at all transformed branch boundaries; vector/scalar consumers verified separately"};
    }
}
