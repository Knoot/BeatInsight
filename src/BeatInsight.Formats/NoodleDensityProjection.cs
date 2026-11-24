using System;
using System.Linq;
using Newtonsoft.Json.Linq;

namespace BeatInsight.Formats;

// Nominal prepared-object times only. No swing, pose or history-index admission.
internal static class NoodleDensityProjection
{
    internal static bool Project(JObject root,int major,string characteristic)
    {
        bool preserveRows=false;
        if(major is not (2 or 3))return false;
        if(characteristic is not ("Standard" or "Lawless" or "OneSaber" or "NoArrows"))
            return false;
        if(!NoodleStaticProfile.IsEmptyTrackValue(root["_sliders"]))return false;
        if(!NoodleStaticProfile.IsEmptyTrackValue(root["burstSliders"]))
        {
            if(major!=3||root["burstSliders"] is not JArray chains)return false;
            foreach(var chain in chains)
                if(chain is not JObject||NoodleFakeData.IsFake(chain,3))return false;
            preserveRows=true;
        }
        if(!NoodleStaticProfile.IsEmptyTrackValue(root["sliders"]))
        {
            if(major!=3||root["sliders"] is not JArray arcs)return false;
            var zero=new JValue(0);
            JToken Default(JToken? value)=>value??zero;
            bool Number(JToken? value)=>value!=null&&(value.Type is JTokenType.Float or JTokenType.Integer)
                &&!double.IsNaN((double)value)&&!double.IsInfinity((double)value)&&Math.Abs((double)value)<=float.MaxValue;
            foreach(var arc in arcs)
            {
                if(arc is not JObject||!Number(Default(arc["b"]))||!Number(Default(arc["tb"]))||(double)Default(arc["tb"])<(double)Default(arc["b"])
                    ||Default(arc["c"]).Type!=JTokenType.Integer||((double)Default(arc["c"]) is not (0 or 1))
                    ||new[]{"x","y","tx","ty"}.Any(k=>Default(arc[k]).Type!=JTokenType.Integer||(double)Default(arc[k])<int.MinValue||(double)Default(arc[k])>int.MaxValue)
                    ||arc["customData"]!=null&&arc["customData"]!.Type!=JTokenType.Null&&arc["customData"] is not JObject
                    ||NoodleFakeData.IsFake(arc,3))return false;
            }
            if(root["bombNotes"]!=null&&root["bombNotes"]!.Type!=JTokenType.Null&&root["bombNotes"] is not JArray)return false;
            foreach(var bomb in (root["bombNotes"] as JArray)??new JArray())
            {
                if(bomb is not JObject)return false;
                if(!NoodleFakeData.IsFake(bomb,3))preserveRows=true;
            }
        }
        foreach(string key in new[]{"_customData","customData"})
        {
            if(root[key]!=null&&root[key]!.Type!=JTokenType.Null&&root[key] is not JObject)
                throw new NotSupportedException("Invalid Noodle root metadata for independent density");
            if(root[key] is JObject custom&&!NoodleStaticProfile.IsEmptyTrackValue(custom["fakeBurstSliders"]))
                return false;
        }
        if(preserveRows)foreach(string key in new[]{"sliders","bombNotes","burstSliders"})
            if(root[key]!=null&&root[key]!.Type!=JTokenType.Null&&root[key] is not JArray)
                throw new NotSupportedException("Invalid density row collection: "+key);
        string collection=major==2?"_notes":"colorNotes",dataKey=major==2?"_customData":"customData",fakeKey=major==2?"_fake":"NE_fake";
        if(root[collection]!=null&&root[collection]!.Type!=JTokenType.Null&&root[collection] is not JArray)
            throw new NotSupportedException("Invalid colored note array for independent density");
        var notes=new JArray();
        foreach(var item in (root[collection] as JArray)??new JArray())
        {
            if(item is not JObject)throw new NotSupportedException("Invalid colored note for independent density");
            var color=item[major==2?"_type":"c"]??(major==3?new JValue(0):null);
            if(color?.Type!=JTokenType.Integer)throw new NotSupportedException("Invalid note color for independent density");
            int colorValue;
            try{colorValue=(int)color;}catch(OverflowException e){throw new NotSupportedException("Invalid note color range for independent density",e);}
            if(colorValue is not (0 or 1))continue;
            var beat=item[major==2?"_time":"b"]??(major==3?new JValue(0):null);
            if(beat==null||beat.Type is not (JTokenType.Float or JTokenType.Integer)||double.IsNaN((double)beat)||double.IsInfinity((double)beat)||Math.Abs((double)beat)>float.MaxValue)
                throw new NotSupportedException("Invalid note time for independent density");
            if(item[dataKey]!=null&&item[dataKey]!.Type!=JTokenType.Null&&item[dataKey] is not JObject)
                throw new NotSupportedException("Invalid note metadata for independent density");
            bool fake=NoodleFakeData.IsFake(item,major);
            var note=major==2?new JObject{["_time"]=beat.DeepClone(),["_type"]=color.DeepClone(),["_lineIndex"]=0,["_lineLayer"]=0,["_cutDirection"]=1}:
                new JObject{["b"]=beat.DeepClone(),["c"]=color.DeepClone(),["x"]=0,["y"]=0,["d"]=1};
            if(preserveRows){note=RowObject(item,false);note["c"]=color.DeepClone();}
            if(fake&&!preserveRows)note[dataKey]=new JObject{[fakeKey]=true};
            notes.Add(note);
        }
        string versionKey=major==2?"_version":"version",eventsKey=major==2?"_events":"bpmEvents";
        var version=root[versionKey]?.DeepClone();var events=root[eventsKey]?.DeepClone();
        var rowArcs=preserveRows?new JArray(((root["sliders"] as JArray)??new JArray()).Select(a=>RowObject(a,true))):null;
        var rowBombs=preserveRows?new JArray(((root["bombNotes"] as JArray)??new JArray()).Select(b=>RowObject(b,false))):null;
        var rowChains=preserveRows?new JArray(((root["burstSliders"] as JArray)??new JArray()).Select(c=>RowObject(c,true,true))):null;
        // Discard geometry/event metadata only on the separately gated density path.
        root.RemoveAll();root[versionKey]=version;root[collection]=notes;
        if(events!=null)root[eventsKey]=events;
        if(preserveRows){root["sliders"]=rowArcs;root["bombNotes"]=rowBombs;root["burstSliders"]=rowChains;}
        return true;
    }

    // Row ownership precedes world/player animation. Keep only the inputs read
    // by the validated row classifier and chain projection, including fake ownership.
    static JObject RowObject(JToken source,bool arc,bool chain=false)
    {
        if(source is not JObject)throw new NotSupportedException("Invalid density row object");
        JToken Number(string key,bool integer)
        {
            var value=source[key]??new JValue(0);
            if(integer?value.Type!=JTokenType.Integer:value.Type is not (JTokenType.Integer or JTokenType.Float))
                throw new NotSupportedException("Invalid density row field: "+key);
            double number=(double)value;
            if(double.IsNaN(number)||double.IsInfinity(number)||Math.Abs(number)>float.MaxValue
                ||integer&&(number<int.MinValue||number>int.MaxValue))
                throw new NotSupportedException("Invalid density row field range: "+key);
            return value.DeepClone();
        }
        if(source["customData"]!=null&&source["customData"] is not JObject)
            throw new NotSupportedException("Invalid density row metadata");
        var result=new JObject{["b"]=Number("b",false),["x"]=Number("x",true),["y"]=Number("y",true),["d"]=Number("d",true)};
        if(arc)
        {
            result["c"]=Number("c",true);result["tb"]=Number("tb",false);result["tx"]=Number("tx",true);result["ty"]=Number("ty",true);
            result["tc"]=1;result["mu"]=1;result["tmu"]=1;result["m"]=0;
            if(chain)
            {
                if(source["sc"]?.Type!=JTokenType.Integer)throw new NotSupportedException("Density chain requires an integer slice count");
                result["sc"]=Number("sc",true);result["s"]=source["s"]==null?new JValue(1):Number("s",false);
            }
        }
        var metadata=new JObject();
        foreach(string key in arc?new[]{"coordinates","tailCoordinates"}:new[]{"coordinates"})
            if((source["customData"] as JObject)?[key] is JToken value)metadata[key]=value.DeepClone();
        if(NoodleFakeData.IsFake(source,3))metadata["NE_fake"]=true;
        if(metadata.Count>0)result["customData"]=metadata;
        NoodleGeometry.Read(result,3);if(arc)NoodleGeometry.Read(result,3,"tailCoordinates");
        return result;
    }
}
