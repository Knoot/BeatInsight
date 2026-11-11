namespace BeatInsight.Compare;

// Replaces only the Unity/game lifecycle dependencies of the original async initializer.
public sealed class OracleKey { }
public sealed class OracleLevel
{
    public float songDuration { get; set; }
}
public sealed class OracleData
{
    public LinkedList<BeatmapDataItem> allBeatmapDataItems { get; set; } = new();
}
public static class PreviewBridge
{
    public static object? Records { get; set; }
    public static OracleData Data { get; set; } = new();
    public static bool KeysEqual(OracleKey? a, OracleKey? b) => ReferenceEquals(a,b);
    public static object? GetModifiers() => null;
    public static object? GetStore() => null;
    public static IList<T> GetRecords<T>(object? store, OracleKey key) => (IList<T>)Records!;
    public static Task<OracleData> GetData(OracleLevel level, OracleKey key, object? modifiers) => Task.FromResult(Data);
}
