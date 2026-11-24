using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.Loader;
using System.Security.Cryptography;
using Mono.Cecil;
using Mono.Cecil.Cil;
using BeatInsight.Core;

namespace BeatInsight.Compare;

// Local IL extraction, never written to disk or distributed. Original arithmetic and branches stay intact.
sealed class OriginalOracle
{
    const string Arrays = "adu1beEdUo3MFZy1DcjytJeR2x0ywBYBmLm69rjwwlo5";
    const string Average = "VWETw0JdWlsOrjdtF5ntIKo";
    const string First = "iLiqpI$ietw9ko0Hbgih46iEI7GMCB$2GfBdQSlsDst8";
    const string Prepare = "IP5oKUizR8cAt48Vr$9Z9UOgcPtceN97TM9uz4ALY6Bs";
    static readonly HashSet<string> LevelMethods = [Arrays, Average, First, Prepare,
        "w0nJ6m3J4OLFFyH7_2DwFawmBVHdzTuZKQp0QAHWwTuf",
        "KrvUlm9wn_YCdJV_xBdIK55hGs2JKpbC8YrHTTlnHS_G", "YOwZA$$0tzQsiGe3dm7XBL8",
        "r9i9bUsd$_6wV_T5QvBr5Yn25YQ5AbbEXSgDkbiXwha2"];
    readonly Type level;
    readonly Type preview;
    readonly Type controller;
    public string LevelHash { get; }
    public string PreviewHash { get; }
    public MapInput LastPrepared { get; private set; } = new();

    public OriginalOracle(string gameRoot)
    {
        AssemblyLoadContext.Default.Resolving += (_, name) =>
        {
            foreach (string dir in new[] { "Beat Saber_Data/Managed", "Plugins", "Libs" })
            {
                var path = Path.Combine(gameRoot, dir, name.Name + ".dll");
                if (File.Exists(path)) return AssemblyLoadContext.Default.LoadFromAssemblyPath(Path.GetFullPath(path));
            }
            return null;
        };
        string lp = ReferenceFiles.Get("Analysis",gameRoot);
        string pp = ReferenceFiles.Get("Preview",gameRoot);
        string analysisType="";
        LevelHash = Hash(lp); PreviewHash = Hash(pp);
        foreach(var dependency in new Dictionary<string,string> {
            ["BeatmapCore.dll"]="22AFA4502CBBF85A4265F20A91E983AAD45ADEC3B3A9D45C4AB4C406D5DA02D1",
            ["DataModels.dll"]="B95112A1A581F52070195B419600591C98ADFB5E225DCB49C819F69B61C703B8" })
            if(Hash(Path.Combine(gameRoot,"Beat Saber_Data/Managed",dependency.Key))!=dependency.Value)
                throw new InvalidDataException($"Game dependency changed: {dependency.Key}. Reinspect the loader.");
        if (LevelHash != "3FF93B02DB8D59A65E017D57D1D0AD1B64A5C4D8141B934ACD079DFE7186EF74" ||
            PreviewHash != "10253D74A32CAB5F04FDAA6F25C51B451BBFB0E57AB9CAC3596A0A64E26DF24C")
            throw new InvalidDataException("Oracle DLL hash changed. Reinspect before comparing.");
        level = Extract(lp, m =>
        {
            var t = m.Types.Single(t => t.Name=="BeatmapAnalysis"&&t.Methods.Any(method=>method.Name==Arrays));
            analysisType=t.FullName;
            KeepRoot(m, t);
            foreach (var method in t.Methods.ToArray()) if (!LevelMethods.Contains(method.Name)) t.Methods.Remove(method);
            var prepare = t.Methods.Single(x => x.Name == Prepare);
            prepare.Parameters[0].ParameterType = m.TypeSystem.Object;
            foreach (var i in prepare.Body.Instructions)
                if (i.Operand is MethodReference mr && mr.Name == "get_allBeatmapDataItems")
                {
                    i.OpCode = OpCodes.Call;
                    i.Operand = m.ImportReference(typeof(OracleBridge).GetMethod(nameof(OracleBridge.GetItems))!);
                }
            TrimFields(t);
        }).GetType(analysisType, true)!;
        var pa = Extract(pp, m =>
        {
            var t = m.Types.Single(t => t.NestedTypes.Any(n => n.Name == "LjjHNaW5TQAIVlHg6EGlhtw"));
            var store = m.Types.Single(t => t.NestedTypes.Any(n=>n.Name=="Record"));
            KeepRoot(m, t);
            m.Types.Add(store);store.BaseType=m.TypeSystem.Object;store.Methods.Clear();store.Fields.Clear();store.CustomAttributes.Clear();
            foreach(var n in store.NestedTypes.ToArray())if(n.Name!="Record")store.NestedTypes.Remove(n);
            t.BaseType = m.TypeSystem.Object;
            t.Methods.Clear();
            foreach (var n in t.NestedTypes.ToArray()) if (n.Name != "LjjHNaW5TQAIVlHg6EGlhtw") t.NestedTypes.Remove(n);
            var p = t.NestedTypes.Single();
            var names = new HashSet<string> { "PofELkpUPlp5x0hyvWaGsHY", "zVVlwBTQ6zrP93okOiJO7sVJ8vZu2TK23KkhqICiQuHG", "OqUWw1bbWnsfEHKXMitSlg3pGH5u4S$i0vr$CWrMIqPp", "9A$kZrJ7DSK4_g8prJBbLTXg_hbfagPew7VjIsqTSQFt", "jMEwzP4OQ_lS2POQ2dDtVFWcii56dEn4DpydzOrAO1Cy", "hUhH1HVq4PvxECWlIIVezp4", "rciDrTpI9VmGc8IQNrDxdqU", ".cctor" };
            foreach (var method in p.Methods.ToArray()) if (!names.Contains(method.Name)) p.Methods.Remove(method);
            var predicate = p.NestedTypes.Single(n=>n.Name=="siQHysmardExqKIBVGiNKUM");
            foreach (var i in predicate.Methods.Where(x=>x.HasBody).SelectMany(x=>x.Body.Instructions))
                if (i.Operand is MethodReference mr && mr.Name == "IsFakeNote") i.Operand = m.ImportReference(typeof(OracleBridge).GetMethod(nameof(OracleBridge.IsFakeNote))!);
            PreviewExtraction.Adapt(m,t);
            TrimFields(t);
        });
        controller = pa.GetTypes().Single(t => t.GetNestedTypes(BindingFlags.NonPublic | BindingFlags.Public).Any(n => n.Name == "LjjHNaW5TQAIVlHg6EGlhtw"));
        preview = controller.GetNestedType("LjjHNaW5TQAIVlHg6EGlhtw", BindingFlags.NonPublic | BindingFlags.Public)!;
        Set(controller, null, "Y3IJ16bZ7aBZQgB_QfJoCo8", 90);
    }

    static string Hash(string p) => Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(p)));
    static void KeepRoot(ModuleDefinition m, TypeDefinition root)
    {
        foreach (var t in m.Types.ToArray()) if (t != root && t.Name != "<Module>") m.Types.Remove(t);
        var module = m.Types.Single(t => t.Name == "<Module>");
        module.Methods.Clear(); module.Fields.Clear();
    }
    static IEnumerable<TypeDefinition> Types(TypeDefinition t) => new[] { t }.Concat(t.NestedTypes.SelectMany(Types));
    static void TrimFields(TypeDefinition root)
    {
        var used = Types(root).SelectMany(t => t.Methods).Where(m => m.HasBody)
            .SelectMany(m => m.Body.Instructions).Select(i => i.Operand).OfType<FieldReference>().Select(f => f.FullName).ToHashSet();
        foreach (var t in Types(root))
        {
            t.CustomAttributes.Clear(); t.Properties.Clear(); t.Events.Clear();
            foreach (var f in t.Fields.ToArray()) if (!used.Contains(f.FullName)) t.Fields.Remove(f);
            foreach (var f in t.Fields) f.IsInitOnly = false; // Allow harness input injection; no arithmetic is rewritten.
            foreach (var method in t.Methods) method.CustomAttributes.Clear();
        }
    }
    static Assembly Extract(string path, Action<ModuleDefinition> prune)
    {
        using var m = ModuleDefinition.ReadModule(path);
        prune(m);
        m.Assembly.Name.Name += ".BeatInsight.LocalOracle";
        m.Assembly.CustomAttributes.Clear(); m.CustomAttributes.Clear(); m.Resources.Clear();
        using var stream = new MemoryStream();
        m.Write(stream); stream.Position = 0;
        return AssemblyLoadContext.Default.LoadFromStream(stream);
    }
    static readonly BindingFlags Flags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static;
    static object? Call(Type t, object? self, string name, params object?[] args) => t.GetMethod(name, Flags)!.Invoke(self, args);
    static void Set(Type t, object? self, string name, object value) => t.GetField(name, Flags)!.SetValue(self, value);

    [MethodImpl(MethodImplOptions.NoInlining)]
    public AnalysisResult Analyze(MapInput input, object? gameData = null)
    {
        // These are real game NoteData objects; only getters and managed math are called.
        var notes = input.Notes.Select(n => NoteData.CreateBasicNoteData(n.Time, n.Beat, 0, n.X, (NoteLineLayer)n.Y, (ColorType)n.Color, (NoteCutDirection)n.Direction)).ToList();
        for(int i=0;i<notes.Count;i++)if(input.Notes[i].IsFake)NativeFakeRegistry.Mark(notes[i]);
        var walls = input.Walls.Select(w => new ObstacleData(w.Time, w.Beat, w.EndBeat, 0, w.X, (NoteLineLayer)w.Y, w.Duration, w.Width, w.Height)).ToList();
        var self = RuntimeHelpers.GetUninitializedObject(level);
        Set(level, self, "$s92sJU0KuriMsfarVs0bLw", notes);
        Set(level, self, "PTUb1fWVKU98nXz8511$9IU", walls);
        Set(level, self, "ZJCIwFRUePQwYLc5lnRUIlY8OcX4NnLh8Fq1X2zT$cnn", .06f);
        Set(level, self, "XKZwsIOE9J0A4i1dM7LatfkvOE7brQGE5J9nngz4qMyJ", .07f);
        if (gameData != null)
        {
            Set(level, null, "NqTMMpQ92835_9K$Ki$lSWjHZ2qS33fvWjfIwh7cffLN", (Func<float,float,int,int,NoteLineLayer,ColorType,NoteCutDirection,NoteData>)NoteData.CreateBasicNoteData);
            Call(level, self, Prepare, gameData);
            notes = (List<NoteData>)level.GetField("$s92sJU0KuriMsfarVs0bLw", Flags)!.GetValue(self)!;
        }
        LastPrepared = new MapInput { Duration=input.Duration, Notes=notes.Select(n => new MapNote { IsFake=NativeFakeRegistry.IsFake(n),Time=n.time, Beat=n.beat, X=n.lineIndex, Y=(int)n.noteLineLayer, Color=(int)n.colorType, Direction=(int)n.cutDirection }).ToArray() };
        var bins = ((int[], int[]))Call(level, self, Arrays)!;
        var first = ((float, float))Call(level, self, First)!;
        float Avg(int[] a) => (float)Call(level, self, Average, a)!;
        int[] total = bins.Item1.Zip(bins.Item2, (a, b) => a + b).ToArray();
        Set(preview, null, "FHul0EsQfcnmw8Za_mGUXaGhZ6QNAoMl$Ps7qlWaFrAF", input.Duration);
        PreviewBridge.Data = new OracleData { allBeatmapDataItems=gameData==null?new LinkedList<BeatmapDataItem>(notes):OracleBridge.GetItems(gameData) };
        var recordType=preview.Assembly.GetTypes().Single(t=>t.Name=="Record");
        var records=(System.Collections.IList)Activator.CreateInstance(typeof(List<>).MakeGenericType(recordType))!;
        foreach(var source in HistoryFixture(input))
        {
            var record=RuntimeHelpers.GetUninitializedObject(recordType);
            recordType.GetField("Date")!.SetValue(record,source.Date);recordType.GetField("LastNote")!.SetValue(record,source.LastNote);
            records.Add(record);
        }
        PreviewBridge.Records=records;
        ((Task)Call(preview,null,"hUhH1HVq4PvxECWlIIVezp4",new OracleKey(),new OracleLevel { songDuration=input.Duration })!).GetAwaiter().GetResult();
        var selected=((System.Collections.IEnumerable)Call(preview,null,"rciDrTpI9VmGc8IQNrDxdqU")!).Cast<object>().ToArray();
        var indices=selected.Select(r=>(int)recordType.GetField("LastNote")!.GetValue(r)!).ToArray();
        var values = (List<float>)Call(preview, null, "PofELkpUPlp5x0hyvWaGsHY", 90)!;
        var colors = (List<UnityEngine.Color>)Call(preview, null, "OqUWw1bbWnsfEHKXMitSlg3pGH5u4S$i0vr$CWrMIqPp", 90)!;
        return new AnalysisResult
        {
            LeftBins = bins.Item1, RightBins = bins.Item2,
            LeftAverage = Avg(bins.Item1), RightAverage = Avg(bins.Item2), TotalAverage = Avg(total),
            LeftPeak = bins.Item1.DefaultIfEmpty().Max(), RightPeak = bins.Item2.DefaultIfEmpty().Max(), TotalPeak = total.DefaultIfEmpty().Max(),
            FirstLeft = first.Item1, FirstRight = first.Item2,
            Segments = values.ToArray(), Colors = colors.Select(c => new[] { c.r, c.g, c.b, c.a }).ToArray(),
            HistoryIndices=indices,
            HistorySeconds=indices.Select(i=>(float)Call(preview,null,"9A$kZrJ7DSK4_g8prJBbLTXg_hbfagPew7VjIsqTSQFt",i)!).ToArray(),
            HistoryNormalized=indices.Select(i=>(float)Call(preview,null,"jMEwzP4OQ_lS2POQ2dDtVFWcii56dEn4DpydzOrAO1Cy",i)!).ToArray()
        };
    }
    public static PlayRecord[] HistoryFixture(MapInput input) => Enumerable.Range(0,125).Select(i=>new PlayRecord {Date=i,LastNote=i%19==0?-1:i%17==0?0:i%13==0?int.MaxValue:(i*17)%Math.Max(1,input.Notes.Length)}).ToArray();
}

public static class OracleBridge
{
    public static LinkedList<BeatmapDataItem> GetItems(object data) => (LinkedList<BeatmapDataItem>)data.GetType().GetProperty("allBeatmapDataItems")!.GetValue(data)!;
    public static bool IsFakeNote(BeatmapDataItem item) => NativeFakeRegistry.IsFake(item);
}
