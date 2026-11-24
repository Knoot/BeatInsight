using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json.Linq;

namespace BeatInsight.Formats;

internal static class NoodleMetadataScope
{
    internal static string Key(int major)=>major==2?"_customData":"customData";
    internal static void ValidateNested(JToken value)
    {
        if(value.Type==JTokenType.Null)return;
        if(value is not JObject obj)throw new NotSupportedException("Nested customData must be an object or null");
        foreach(var leaf in obj.Descendants())
            if(leaf.Type==JTokenType.Float&&(double.IsNaN((double)leaf)||double.IsInfinity((double)leaf)))
                throw new NotSupportedException("Non-finite nested customData metadata");
    }
    internal static IEnumerable<JProperty> OrientationProperties(JContainer metadata,int major,bool directOnly)
    {
        foreach(var property in metadata.Children<JProperty>())
        {
            yield return property;
            if(directOnly||(major==2||major==3)&&property.Name==Key(major))continue;
            if(property.Value is JContainer container)
                foreach(var descendant in container.Descendants().OfType<JProperty>())yield return descendant;
        }
    }
    // Only double-wrapped metadata of actual gameplay objects is opaque.
    // Root customData and fake collection objects remain active scopes.
    internal static IEnumerable<JProperty> ActiveProperties(JObject root,int major)
    {
        string key=Key(major);
        bool Opaque(JProperty property)
        {
            if(major!=2&&major!=3||property.Name!=key)return false;
            if(property.Parent?.Parent is not JProperty outer||outer.Name!=key)return false;
            if(outer.Parent?.Parent is not JArray array||array.Parent is not JProperty collection)return false;
            if(ReferenceEquals(collection.Parent,root))
                return major==2?collection.Name=="_notes"||collection.Name=="_obstacles"||collection.Name=="_sliders":
                    collection.Name=="colorNotes"||collection.Name=="bombNotes"||collection.Name=="obstacles"||collection.Name=="sliders"||collection.Name=="burstSliders";
            return major==3&&ReferenceEquals(collection.Parent,root[key])&&
                (collection.Name=="fakeColorNotes"||collection.Name=="fakeBombNotes"||collection.Name=="fakeObstacles"||collection.Name=="fakeBurstSliders");
        }
        IEnumerable<JProperty> Walk(JToken token)
        {
            if(token is JProperty property)
            {
                yield return property;
                if(Opaque(property))yield break;
            }
            foreach(var child in token.Children())foreach(var found in Walk(child))yield return found;
        }
        return Walk(root);
    }
}
