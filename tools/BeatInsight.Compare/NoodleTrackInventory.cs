using System.Security.Cryptography;
using System.Text.RegularExpressions;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace BeatInsight.Compare;

// Structural diagnostics only. This graph never authorizes gameplay analysis:
// event execution, transforms, copying patches and property consumers need separate checks.
static class NoodleTrackInventory
{
    internal sealed record Binding(string Path, string[] Tracks);
    internal sealed record Event(string Path, string Type, string[] Tracks, string[] Properties);
    internal sealed record Edge(string Parent, string Child);
    internal sealed record Graph(int Major, Binding[] Bindings, Event[] Events, Edge[] ParentEdges,
        string[] GameplayTracks, string[] ReferencedTracks, string[] EventReachableGameplayTracks,
        string[] UnreferencedGameplayTracks, string[] UnresolvedParentTracks, bool PlayerMotion,
        bool Environment, string[] UnknownEvents, string[] Issues);

    internal static Graph Inspect(JObject root)
    {
        string version=(string?)(root["version"]??root["_version"])??"";
        int major=Version.TryParse(version,out var parsed)?parsed.Major:0;
        bool old=major==2;
        var issues=new List<string>();
        if(major!=2&&major!=3)issues.Add("unvalidated format version");
        var bindings=new List<Binding>();var events=new List<Event>();var edges=new List<Edge>();
        var gameplay=new HashSet<string>(StringComparer.Ordinal);
        var referenced=new HashSet<string>(StringComparer.Ordinal);
        var affected=new HashSet<string>(StringComparer.Ordinal);
        var unknown=new HashSet<string>(StringComparer.Ordinal);
        bool player=false,environment=false;
        string[] Names(JToken? token, bool required=false, string? missingPath=null)
        {
            if(token==null||token.Type==JTokenType.Null)
            {
                if(required)issues.Add((token?.Path??missingPath??"missing track")+": missing track assignment");
                return [];
            }
            var result=new HashSet<string>(StringComparer.Ordinal);
            foreach(var name in token is JArray array?array:new JArray(token.DeepClone()))
            {
                if(name.Type==JTokenType.String&&!string.IsNullOrEmpty((string?)name))result.Add((string)name!);
                else issues.Add(token.Path+": invalid track name");
            }
            if(required&&result.Count==0)issues.Add(token.Path+": empty track assignment");
            return result.Order(StringComparer.Ordinal).ToArray();
        }
        string[] Sorted(IEnumerable<string> names)=>names.Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray();
        void Objects(JToken? collection)
        {
            if(collection==null||collection.Type==JTokenType.Null)return;
            if(collection is not JArray array){issues.Add(collection.Path+": invalid gameplay collection");return;}
            foreach(var item in array)
            {
                if(item is not JObject obj){issues.Add(item.Path+": invalid gameplay object");continue;}
                foreach(string key in new[]{"_customData","customData"})
                {
                    if(obj[key]==null||obj[key]!.Type==JTokenType.Null)continue;
                    if(obj[key] is not JObject custom){issues.Add(obj[key]!.Path+": invalid customData");continue;}
                    foreach(string track in new[]{"_track","track"})if(custom[track]!=null)
                    {
                        if(key!=(old?"_customData":"customData")||track!=(old?"_track":"track"))
                            issues.Add(custom[track]!.Path+": noncanonical gameplay track alias");
                        var names=Names(custom[track]);
                        bindings.Add(new(custom[track]!.Path,names));gameplay.UnionWith(names);
                    }
                }
            }
        }
        foreach(string key in old?new[]{"_notes","_obstacles","_sliders"}:new[]{"colorNotes","bombNotes","obstacles","sliders","burstSliders"})Objects(root[key]);
        foreach(string key in new[]{"environment","_environment"})
            if(root[key]!=null&&root[key]!.Type!=JTokenType.Null&&(root[key] is not JArray a||a.Count>0))environment=true;
        foreach(string customKey in new[]{"_customData","customData"})
        {
            if(root[customKey]==null||root[customKey]!.Type==JTokenType.Null)continue;
            if(root[customKey] is not JObject custom){issues.Add(root[customKey]!.Path+": invalid root customData");continue;}
            foreach(string fake in new[]{"fakeColorNotes","fakeBombNotes","fakeObstacles","fakeBurstSliders"})Objects(custom[fake]);
            foreach(string key in new[]{"environment","_environment"})
                if(custom[key]!=null&&custom[key]!.Type!=JTokenType.Null&&(custom[key] is not JArray a||a.Count>0))environment=true;
            foreach(string eventsKey in new[]{"_customEvents","customEvents"})
            {
                if(custom[eventsKey]==null||custom[eventsKey]!.Type==JTokenType.Null)continue;
                if(custom[eventsKey] is not JArray array){issues.Add(custom[eventsKey]!.Path+": invalid custom events");continue;}
                if(array.Count>0&&(customKey!=(old?"_customData":"customData")||eventsKey!=(old?"_customEvents":"customEvents")))
                    issues.Add(custom[eventsKey]!.Path+": noncanonical custom events alias");
                foreach(var ev in array)
                {
                    if(ev is not JObject obj){issues.Add(ev.Path+": invalid custom event");continue;}
                    string typeKey=old?"_type":"t",dataKey=old?"_data":"d";
                    string type=obj[typeKey]?.Type==JTokenType.String?(string)obj[typeKey]!:"";
                    if(type.Length==0)issues.Add(ev.Path+": missing event type");
                    if(obj[dataKey] is not JObject data){issues.Add(ev.Path+": invalid event data");unknown.Add(type);continue;}
                    string trackKey=old?"_track":"track";
                    var targets=new HashSet<string>(StringComparer.Ordinal);
                    switch(type)
                    {
                        case "AnimateTrack":
                        case "AssignPathAnimation":
                            targets.UnionWith(Names(data[trackKey],true,data.Path+"."+trackKey));break;
                        case "AssignTrackParent":
                            string parentKey=old?"_parentTrack":"parentTrack",childrenKey=old?"_childrenTracks":"childrenTracks";
                            string[] parents=Names(data[parentKey],true,data.Path+"."+parentKey);
                            string[] children=Names(data[childrenKey],true,data.Path+"."+childrenKey);
                            if(parents.Length!=1||data[parentKey]?.Type!=JTokenType.String)issues.Add(data.Path+": parent must be a single track name");
                            referenced.UnionWith(parents);referenced.UnionWith(children);
                            foreach(string parent in parents)foreach(string child in children)edges.Add(new(parent,child));
                            targets.UnionWith(children);break;
                        case "AssignPlayerToTrack":
                            player=true;targets.UnionWith(Names(data[trackKey],true,data.Path+"."+trackKey));break;
                        default:
                            unknown.Add(type);targets.UnionWith(Names(data[trackKey]));break;
                    }
                    referenced.UnionWith(targets);affected.UnionWith(targets);
                    events.Add(new(ev.Path,type,Sorted(targets),data.Properties().Select(p=>p.Name).Order(StringComparer.Ordinal).ToArray()));
                }
            }
        }
        // Aggregate all time-dependent parent relations conservatively. No event-time inference.
        var childrenByParent=edges.GroupBy(e=>e.Parent,StringComparer.Ordinal)
            .ToDictionary(g=>g.Key,g=>Sorted(g.Select(e=>e.Child)),StringComparer.Ordinal);
        var pending=new Queue<string>(affected);
        while(pending.Count>0)
            if(childrenByParent.TryGetValue(pending.Dequeue(),out var children))
                foreach(string child in children)if(affected.Add(child))pending.Enqueue(child);
        // Iterative topological pruning: cycles and their descendants remain unresolved.
        var vertices=new HashSet<string>(edges.SelectMany(e=>new[]{e.Parent,e.Child}),StringComparer.Ordinal);
        var indegree=vertices.ToDictionary(v=>v,v=>0,StringComparer.Ordinal);
        foreach(var entry in childrenByParent)foreach(string child in entry.Value)indegree[child]++;
        var roots=new Queue<string>(indegree.Where(p=>p.Value==0).Select(p=>p.Key));
        while(roots.Count>0)
        {
            string node=roots.Dequeue();vertices.Remove(node);
            if(childrenByParent.TryGetValue(node,out var children))foreach(string child in children)if(--indegree[child]==0)roots.Enqueue(child);
        }
        if(vertices.Count>0)issues.Add("cyclic or cycle-dependent parent relations");
        return new(major,bindings.ToArray(),events.ToArray(),edges.Distinct().ToArray(),Sorted(gameplay),Sorted(referenced),
            Sorted(gameplay.Intersect(affected)),Sorted(gameplay.Except(referenced)),Sorted(vertices),player,environment,Sorted(unknown),Sorted(issues));
    }

    public static object Run(string resultDirectory)
    {
        var results=new List<object>();var reasons=new Dictionary<string,List<string>>(StringComparer.Ordinal);
        int analyzed=0,unsupported=0,errors=0;
        var files=Directory.GetFiles(resultDirectory,"*.result.json").Order(StringComparer.Ordinal).ToArray();
        if(files.Length==0)throw new InvalidDataException("No case results found for track inventory");
        foreach(string file in files)
        {
            var result=JObject.Parse(File.ReadAllText(file));string id=Path.GetFileName(file);
            string status=(string?)result["status"]??"unknown";
            if(status=="analyzed")analyzed++;
            if(status=="unsupported")unsupported++;
            string reason=Regex.Replace((string?)result["error"]??"",@"\s+\([^()]*\)$","");
            if(status=="unsupported")
            {
                if(!reasons.TryGetValue(reason,out var members))reasons[reason]=members=[];
                members.Add(id);
            }
            try
            {
                var request=(JObject?)result["request"]??throw new InvalidDataException("Missing request");
                string folder=(string?)request["Folder"]??throw new InvalidDataException("Missing Folder");
                string selected=(string?)request["File"]??throw new InvalidDataException("Missing exact File selection");
                string path=Path.GetFullPath(Path.Combine(folder,selected));
                if(!path.StartsWith(Path.GetFullPath(folder).TrimEnd(Path.DirectorySeparatorChar)+Path.DirectorySeparatorChar,StringComparison.OrdinalIgnoreCase))
                    throw new InvalidDataException("File selection escapes map folder");
                byte[] bytes=File.ReadAllBytes(path);string hash=Convert.ToHexString(SHA256.HashData(bytes));
                string? previous=(string?)result["mapSha256"];
                var graph=Inspect(JObject.Parse(System.Text.Encoding.UTF8.GetString(bytes).TrimStart('\uFEFF')));
                results.Add(new {id,status,reason,mapSha256=hash,previousHashMatches=previous==null?(bool?)null:string.Equals(previous,hash,StringComparison.OrdinalIgnoreCase),graph});
            }
            catch(Exception e)when(e is IOException or UnauthorizedAccessException or JsonException or ArgumentException or InvalidDataException)
            {errors++;results.Add(new{id,status,reason,inventoryError=e.Message});}
        }
        return new {schemaVersion=1,scope="structural track dependencies; no event evaluation or gameplay validation",gameStarted=false,
            referenceUsed=false,changesSupportPolicy=false,cases=results.Count,analyzed,unsupported,errors,
            rejectionGroups=reasons.OrderByDescending(p=>p.Value.Count).ThenBy(p=>p.Key,StringComparer.Ordinal).Select(p=>new{reason=p.Key,count=p.Value.Count,representativeCase=p.Value[0],cases=p.Value}).ToArray(),results};
    }
}
