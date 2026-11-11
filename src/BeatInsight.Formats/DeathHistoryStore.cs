using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using BeatInsight.Core;

namespace BeatInsight.Formats;

public sealed class DeathHistoryReadResult
{
    public string Status { get; set; } = "";
    public List<DeathRecord> Records { get; set; } = new List<DeathRecord>();
}

public static class DeathHistoryStore
{
    public const string FileName = "BeatInsight.Deaths.json";
    static readonly object WriteLock = new object();
    public static DeathHistoryReadResult Read(string path)
    {
        if (!System.IO.File.Exists(path)) return new DeathHistoryReadResult {Status="source-missing"};
        try {
            var root=JObject.Parse(System.IO.File.ReadAllText(path));
            if((int?)root["schemaVersion"]!=1) return new DeathHistoryReadResult {Status="unsupported-schema"};
            if(root["deaths"] is not JArray entries) return new DeathHistoryReadResult {Status="invalid-data"};
            var records=new List<DeathRecord>();
            foreach(var entry in entries) {
                if(entry is not JObject item || item.GetValue("MapKey",StringComparison.OrdinalIgnoreCase)?.Type!=JTokenType.String ||
                    item.GetValue("SongTimeSeconds",StringComparison.OrdinalIgnoreCase)?.Type is not (JTokenType.Float or JTokenType.Integer))
                    return new DeathHistoryReadResult {Status="invalid-data"};
                var record=item.ToObject<DeathRecord>()!;
                // Older schemas used provider-prefixed mask field names. Keep
                // their unique integer *Param value without embedding a vendor name.
                if(record.Modifiers!=null&&record.Modifiers.LegacyParam==null&&
                    item.GetValue("Modifiers",StringComparison.OrdinalIgnoreCase) is JObject modifiers)
                {
                    var masks=modifiers.Properties().Where(p=>p.Name.EndsWith("Param",StringComparison.OrdinalIgnoreCase)&&p.Value.Type==JTokenType.Integer).ToArray();
                    if(masks.Length==1)record.Modifiers.LegacyParam=masks[0].Value.Value<int>();
                }
                records.Add(record);
            }
            return new DeathHistoryReadResult {Status="available",Records=records};
        } catch(Exception e) when(e is JsonException || e is FormatException || e is InvalidCastException || e is OverflowException) {
            return new DeathHistoryReadResult {Status="invalid-data"};
        } catch(Exception e) when(e is IOException || e is UnauthorizedAccessException) {
            return new DeathHistoryReadResult {Status="source-unreadable"};
        }
    }

    // Explicit own-history write API. Never called by import or by the long map comparison.
    public static bool Append(string path, DeathRecord? record)
    {
        lock(WriteLock) return AppendCore(path,record);
    }
    static bool AppendCore(string path, DeathRecord? record)
    {
        if(record==null||record.IsPractice)return false;
        if(!string.Equals(Path.GetFileName(path),FileName,StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("Own history may only be written to "+FileName,nameof(path));
        if(string.IsNullOrWhiteSpace(record.MapKey)||double.IsNaN(record.SongTimeSeconds)||double.IsInfinity(record.SongTimeSeconds)||record.SongTimeSeconds<0)
            throw new ArgumentException("Invalid death record",nameof(record));
        JObject root;
        if(System.IO.File.Exists(path)) {
            if(Read(path).Status!="available")throw new InvalidDataException("Refusing to overwrite unreadable or unsupported history");
            root=JObject.Parse(System.IO.File.ReadAllText(path));
        } else root=new JObject { ["schemaVersion"]=1,["deaths"]=new JArray() };
        ((JArray)root["deaths"]!).Add(JObject.FromObject(record));
        string fullPath=Path.GetFullPath(path);
        Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
        string temporary=fullPath+"."+Guid.NewGuid().ToString("N")+".tmp";
        try {
            System.IO.File.WriteAllText(temporary,root.ToString(Formatting.Indented));
            if(System.IO.File.Exists(fullPath))System.IO.File.Replace(temporary,fullPath,null);
            else System.IO.File.Move(temporary,fullPath);
        } finally {if(System.IO.File.Exists(temporary))System.IO.File.Delete(temporary);}
        return true;
    }
}
