using System;
using System.Security.Cryptography;
using System.Text;

namespace BeatInsight.Formats;

public static class BeatmapFingerprint
{
    // Hash decoded JSON identically in the menu and in the loaded game data.
    public static string Compute(string json)
    {
        if(string.IsNullOrWhiteSpace(json))throw new ArgumentException("Beatmap JSON is empty",nameof(json));
        using var sha=SHA256.Create();
        return BitConverter.ToString(sha.ComputeHash(Encoding.UTF8.GetBytes(json))).Replace("-","");
    }
}

// Scoped to one level/attempt series, not a global cache by path or levelId.
public sealed class AttemptFingerprint
{
    public string MapKey {get;}
    public string? Hash {get;private set;}
    object? source;
    public AttemptFingerprint(string mapKey)=>MapKey=mapKey;
    public void Invalidate(){source=null;Hash=null;}
    public bool Prepare(string mapKey,object loadedSource,Func<string> readJson)
    {
        if(mapKey!=MapKey)return false;
        if(loadedSource==null)throw new ArgumentNullException(nameof(loadedSource));
        if(ReferenceEquals(source,loadedSource)&&Hash!=null)return true;
        // A changed source or a failed read must never reuse the old hash.
        Invalidate();
        string hash=BeatmapFingerprint.Compute(readJson());
        source=loadedSource;Hash=hash;return true;
    }
}
