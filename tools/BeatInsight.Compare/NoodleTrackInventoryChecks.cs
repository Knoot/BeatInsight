using Newtonsoft.Json.Linq;

namespace BeatInsight.Compare;

static class NoodleTrackInventoryChecks
{
    public static object Run()
    {
        int cases=0;var errors=new List<string>();
        void Check(string name,JObject map,Func<NoodleTrackInventory.Graph,bool> expected)
        {
            cases++;
            try {if(!expected(NoodleTrackInventory.Inspect(map)))errors.Add(name);}
            catch(Exception e){errors.Add(name+": "+e.Message);}
        }
        foreach(bool old in new[]{false,true})
        {
            string custom=old?"_customData":"customData",track=old?"_track":"track";
            JObject Map(JToken? tracks=null,params JObject[] events)=>new()
            {
                [old?"_version":"version"]=old?"2.6.0":"3.3.0",
                [old?"_notes":"colorNotes"]=new JArray(new JObject{[custom]=new JObject{[track]=tracks??JValue.CreateNull()}}),
                [custom]=new JObject{[old?"_customEvents":"customEvents"]=new JArray(events)}
            };
            JObject Event(string type,JObject data)=>new(){[old?"_type":"t"]=type,[old?"_data":"d"]=data};
            JObject Animate(JToken target)=>Event("AnimateTrack",new(){[track]=target});
            JObject Parent(string parent,params string[] children)=>Event("AssignTrackParent",new()
            {[old?"_parentTrack":"parentTrack"]=parent,[old?"_childrenTracks":"childrenTracks"]=new JArray(children)});
            Check("empty "+old,Map(),g=>g.GameplayTracks.Length==0&&g.Issues.Length==0);
            Check("static name "+old,Map("notes"),g=>g.UnreferencedGameplayTracks.SequenceEqual(new[]{"notes"})&&g.Events.Length==0);
            Check("duplicates "+old,Map(new JArray("notes","notes","other")),g=>g.GameplayTracks.SequenceEqual(new[]{"notes","other"}));
            Check("unrelated event "+old,Map("notes",Animate("visual")),g=>g.EventReachableGameplayTracks.Length==0&&g.ReferencedTracks.SequenceEqual(new[]{"visual"}));
            Check("direct event "+old,Map("notes",Animate("notes")),g=>g.EventReachableGameplayTracks.SequenceEqual(new[]{"notes"}));
            Check("target array "+old,Map("notes",Animate(new JArray("other","notes"))),g=>g.EventReachableGameplayTracks.SequenceEqual(new[]{"notes"}));
            Check("parent relation "+old,Map("notes",Parent("parent","notes"),Animate("parent")),g=>g.ParentEdges.Length==1&&g.EventReachableGameplayTracks.SequenceEqual(new[]{"notes"}));
            Check("transitive "+old,Map("notes",Parent("root","middle"),Parent("middle","notes"),Animate("root")),g=>g.EventReachableGameplayTracks.SequenceEqual(new[]{"notes"})&&g.UnresolvedParentTracks.Length==0);
            Check("child does not change parent "+old,Map("parent",Parent("parent","child"),Animate("child")),g=>g.EventReachableGameplayTracks.Length==0&&g.UnreferencedGameplayTracks.Length==0);
            Check("cycle "+old,Map("leaf",Parent("a","b"),Parent("b","a","leaf")),g=>g.UnresolvedParentTracks.SequenceEqual(new[]{"a","b","leaf"})&&g.Issues.Length>0);
            Check("player "+old,Map("notes",Event("AssignPlayerToTrack",new(){[track]="player"})),g=>g.PlayerMotion);
            Check("unknown "+old,Map("notes",Event("FutureEvent",new(){[track]="other"})),g=>g.UnknownEvents.SequenceEqual(new[]{"FutureEvent"}));
            Check("invalid assignment "+old,Map(new JArray("notes",123)),g=>g.Issues.Length>0);
            Check("empty target "+old,Map("notes",Animate(new JArray())),g=>g.Issues.Length>0);
            Check("missing parent "+old,Map("notes",Event("AssignTrackParent",new())),g=>g.Issues.Length>0);
            Check("case sensitive "+old,Map("Notes",Animate("notes")),g=>g.EventReachableGameplayTracks.Length==0);
            Check("self parent "+old,Map("notes",Parent("notes","notes")),g=>g.UnresolvedParentTracks.SequenceEqual(new[]{"notes"}));
            Check("array parent "+old,Map("notes",Event("AssignTrackParent",new(){[old?"_parentTrack":"parentTrack"]=new JArray("parent"),[old?"_childrenTracks":"childrenTracks"]="notes"})),g=>g.Issues.Length>0);
            var badEvent=Event("AnimateTrack",new());badEvent[old?"_data":"d"]=123;
            Check("invalid event data "+old,Map("notes",badEvent),g=>g.Issues.Length>0&&g.UnknownEvents.Length==1);
            var environment=Map("notes");environment[custom]![old?"_environment":"environment"]=new JArray(new JObject());
            Check("environment "+old,environment,g=>g.Environment);
            var alias=Map("notes");alias[custom]![old?"customEvents":"_customEvents"]=new JArray(Animate("notes"));
            Check("event alias "+old,alias,g=>g.Issues.Any(i=>i.Contains("noncanonical custom events")));
            var fake=Map();fake[custom]!["fakeColorNotes"]=new JArray(new JObject{[custom]=new JObject{[track]="fake"}});
            Check("fake binding "+old,fake,g=>g.GameplayTracks.SequenceEqual(new[]{"fake"}));
        }
        Check("unvalidated version",new JObject{["version"]="4.0.0"},g=>g.Issues.Contains("unvalidated format version"));
        Check("malformed customData",new JObject{["version"]="3.3.0",["customData"]=12},g=>g.Issues.Length>0);
        return new {scope="structural dependency inventory fixtures; no runtime or event semantics validation",cases,failed=errors.Count,errors};
    }
}
