using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using BeatSaberMarkupLanguage;
using UnityEngine;

namespace BeatInsight.Mod;

// Reuse the user's installed icon assets without bundling a third-party DLL
// or depending on its obfuscated implementation. No assembly is loaded here.
internal static class MenuIcons
{
    static readonly Dictionary<string,(Sprite Sprite,Texture2D Texture)> sprites=new();
    internal static void Clear()
    {
        foreach(var entry in sprites.Values)Release(entry);
        sprites.Clear();
    }
    static void Release((Sprite Sprite,Texture2D Texture) entry)
    {
        if(entry.Sprite!=null)UnityEngine.Object.Destroy(entry.Sprite);
        if(entry.Texture!=null)UnityEngine.Object.Destroy(entry.Texture);
    }
    internal static Sprite? Get(string name)
    {
        if(sprites.TryGetValue(name,out var existing))
        {
            if(existing.Sprite!=null&&existing.Texture!=null)return existing.Sprite;
            Release(existing);sprites.Remove(name);
        }
        // Discover a compatible resource provider by its complete icon set,
        // without a dependency on an assembly or namespace name.
        var provider=AppDomain.CurrentDomain.GetAssemblies().Where(a=>!a.IsDynamic)
            .Select(a=>new {Assembly=a,Resources=a.GetManifestResourceNames()})
            .FirstOrDefault(a=>new[]{"saber1","saber2","squat"}.All(icon=>a.Resources.Any(r=>r.EndsWith(".Resources."+icon+".png",StringComparison.Ordinal))));
        string? resource=provider?.Resources.Single(r=>r.EndsWith(".Resources."+name+".png",StringComparison.Ordinal));
        using var stream=resource==null?null:provider!.Assembly.GetManifestResourceStream(resource);
        if(stream==null)return null;
        using var bytes=new MemoryStream();stream.CopyTo(bytes);
        // Menu construction runs on Unity's thread. The pinned BSML version's
        // synchronous loader avoids blocking its async dispatcher on this thread.
#pragma warning disable CS0618
        var sprite=Utilities.LoadSpriteRaw(bytes.ToArray(),100);
#pragma warning restore CS0618
        sprites[name]=(sprite,sprite.texture);return sprite;
    }
}
