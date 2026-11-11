using BeatInsight.Core;
using Newtonsoft.Json.Linq;
using System.Security.Cryptography;

namespace BeatInsight.Compare;
static class Comparison
{
    public static string HashFile(string path)=>Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path)));
    public static string Hash(string value) => Convert.ToHexString(SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(value)));
    public static object Metrics(AnalysisResult r) => new {leftSwings=r.LeftBins.Sum(),rightSwings=r.RightBins.Sum(),r.LeftAverage,r.RightAverage,r.TotalAverage,r.LeftPeak,r.RightPeak,r.TotalPeak,r.FirstLeft,r.FirstRight,windows=r.LeftBins.Length,segments=r.Segments.Length};
    public static int InputDiff(MapNote[] original, MapNote[] own)
    {
        // Interleaving of the two hands at exactly equal times is immaterial:
        // calculations are per hand and history positions use time only. Preserve
        // order WITHIN each hand, including ties, and compare all output arrays too.
        original=original.OrderBy(n=>n.Color).ToArray();own=own.OrderBy(n=>n.Color).ToArray();
        int errors=Math.Abs(original.Length-own.Length);
        for(int i=0;i<Math.Min(original.Length,own.Length);i++)
        {
            var a=original[i];var b=own[i];
            if(Math.Abs(a.Time-b.Time)>1e-5 || a.X!=b.X || a.Y!=b.Y || a.Color!=b.Color || a.Direction!=b.Direction)errors++;
        }
        return errors;
    }
    public static bool SameSimultaneousNotes(MapNote[] original, MapNote[] own)
    {
        // Only exact same-time permutations are equivalent. This check alone
        // never establishes a pass: every calculated output must also agree.
        MapNote[] Canonical(MapNote[] notes)=>notes.OrderBy(n=>n.Color).ThenBy(n=>n.Time)
            .ThenBy(n=>n.X).ThenBy(n=>n.Y).ThenBy(n=>n.Direction).ToArray();
        var a=Canonical(original);var b=Canonical(own);
        return a.Length==b.Length&&a.Zip(b,(x,y)=>x.Time==y.Time&&x.Color==y.Color&&x.X==y.X&&x.Y==y.Y&&x.Direction==y.Direction).All(x=>x);
    }
    public static Dictionary<string,object> Diff(AnalysisResult a, AnalysisResult b)
    {
        var left=JObject.FromObject(a);var right=JObject.FromObject(b);var result=new Dictionary<string,object>();
        foreach(var p in left.Properties())
        {
            var aa=p.Value is JContainer ac ? ac.DescendantsAndSelf().OfType<JValue>().ToArray() : new[] {(JValue)p.Value};
            var bb=right[p.Name] is JContainer bc ? bc.DescendantsAndSelf().OfType<JValue>().ToArray() : new[] {(JValue)right[p.Name]!};
            int count=Math.Abs(aa.Length-bb.Length),first=-1;double max=0;
            for(int i=0;i<Math.Min(aa.Length,bb.Length);i++)
            {
                double x=aa[i].Value<double>(),y=bb[i].Value<double>(),delta=Math.Abs(x-y);
                double tolerance=p.Name.Contains("Bins")||p.Name.Contains("Peak")?0:1e-6;
                if(!double.IsFinite(x)||!double.IsFinite(y)||delta>tolerance){count++;if(first<0)first=i;max=Math.Max(max,delta);}
            }
            if(count>0)result[p.Name]=new {mismatches=count,firstIndex=first,maxAbsoluteError=max,originalLength=aa.Length,ownLength=bb.Length};
        }
        return result;
    }
}
