using System;
using System.Collections.Generic;
using System.IO;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace FishingSim.Compat;

/// <summary>
/// The game's data records (fish, places, tackle, tournaments), converted from Godot's data/**/*.tres to JSON by
/// tools/unity/tres_to_json.py. A Godot path res://data/fish/karp.tres is read from &lt;Root&gt;/fish/karp.json; each record
/// is made once (the type named by its "$type", or the type asked for) and cached like Godot's resource cache.
/// </summary>
public static class DataStore
{
    /// <summary>Where the JSON lives: Application.streamingAssetsPath + "/data" in the game, a folder in tests.</summary>
    public static string Root { get; set; } = "StreamingAssets/data";

    private static readonly Dictionary<string, object?> Cache = new();

    private static string FileOf(string resPath)
    {
        string rel = resPath.StartsWith("res://data/") ? resPath.Substring("res://data/".Length) : resPath;
        if (rel.EndsWith(".tres"))
            rel = rel.Substring(0, rel.Length - 5) + ".json";
        return Path.Combine(Root, rel);
    }

    public static bool Exists(string resPath) => File.Exists(FileOf(resPath));

    private static readonly Dictionary<string, Type?> Types = new();

    /// <summary>The game's class with this simple name (Godot's script_class), in any FishingSim namespace.</summary>
    private static Type? TypeNamed(string? name)
    {
        if (string.IsNullOrEmpty(name))
            return null;
        if (Types.TryGetValue(name!, out var known))
            return known;
        Type? found = null;
        foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
        {
            Type[] all;
            try { all = asm.GetTypes(); }
            catch (System.Reflection.ReflectionTypeLoadException e) { all = Array.FindAll(e.Types, t => t != null)!; }
            foreach (var t in all)
                if (t.Name == name && t.Namespace != null && t.Namespace.StartsWith("FishingSim") && !t.IsAbstract)
                    found = t;
        }
        Types[name!] = found;
        return found;
    }

    public static T? Load<T>(string resPath) where T : class
    {
        lock (Cache)
        {
            if (Cache.TryGetValue(resPath, out var done))
                return done as T;
            string file = FileOf(resPath);
            T? result = null;
            if (File.Exists(file))
            {
                var json = JObject.Parse(File.ReadAllText(file));
                var type = TypeNamed((string?)json["$type"]) ?? typeof(T);
                json.Remove("$type");
                result = json.ToObject(type) as T;
                if (result is Resource r)
                    r.ResourcePath = resPath;
            }
            Cache[resPath] = result;
            return result;
        }
    }
}
