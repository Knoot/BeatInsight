using Newtonsoft.Json.Linq;
using UnityEngine;

namespace BeatInsight.Compare;

sealed class RotationMathCapture
{
    internal readonly record struct Sample(Vector3 Degrees,Quaternion Value);
    internal Sample[] Samples {get;}
    internal string Unity {get;}
    RotationMathCapture(Sample[] samples,string unity){Samples=samples;Unity=unity;}
    internal static RotationMathCapture Load(string gameRoot)
    {
        using var stream=typeof(RotationMathCapture).Assembly.GetManifestResourceStream("BeatInsight.Compare.UnityRotationMath.json")??throw new InvalidDataException("Missing rotation capture");
        using var reader=new StreamReader(stream);var root=JObject.Parse(reader.ReadToEnd());
        if((int?)root["schema"]!=1||(int?)root["count"]!=24||root["rows"] is not JArray {Count:24} rows
            ||(string?)root["assembly"]!="UnityEngine.CoreModule"||(int?)root["captureSessions"]!=2
            ||Comparison.HashFile(Path.Combine(gameRoot,"Beat Saber_Data/Managed/UnityEngine.CoreModule.dll"))!=(string?)root["assemblySha256"])
            throw new InvalidDataException("Recapture changed Unity rotation math source");
        float[] Values(JToken row,string key,int size)
        {
            if(row[key] is not JArray array||array.Count!=size)throw new InvalidDataException("Malformed rotation capture: "+key);
            var values=array.Select(v=>v.Value<float>()).ToArray();
            if(values.Any(v=>!float.IsFinite(v)))throw new InvalidDataException("Nonfinite rotation capture");return values;
        }
        var samples=rows.Select(row=>
        {
            var d=Values(row,"degrees",3);var q=Values(row,"vector",4);
            if(Math.Abs(q.Sum(v=>v*v)-1)>2e-5)throw new InvalidDataException("Captured quaternion is not normalized");
            return new Sample(new Vector3(d[0],d[1],d[2]),new Quaternion(q[0],q[1],q[2],q[3]));
        }).ToArray();
        return new RotationMathCapture(samples,(string?)root["unity"]??throw new InvalidDataException("Missing Unity version"));
    }
    internal Vector3 Angles(Quaternion value,bool scalarWorld=false)
    {
        foreach(var sample in Samples)
            if((!scalarWorld||sample.Degrees.x==0&&sample.Degrees.z==0)
                &&sample.Value.x==value.x&&sample.Value.y==value.y&&sample.Value.z==value.z&&sample.Value.w==value.w)return sample.Degrees;
        throw new InvalidDataException("Quaternion is outside captured Euler inputs");
    }
    internal Quaternion?[] Worlds(bool scalar)=>new Quaternion?[]{null}.Concat(Samples.Where(s=>!scalar||s.Degrees.x==0&&s.Degrees.z==0).Select(s=>(Quaternion?)s.Value)).ToArray();
    internal Quaternion?[] Locals()=>new Quaternion?[]{null}.Concat(new[]{0,5,7,20,21}.Select(i=>(Quaternion?)Samples[i].Value)).ToArray();
}
