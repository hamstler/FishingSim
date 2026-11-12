using System.Collections.Generic;
using Godot;

namespace FishingSim.Data;

/// <summary>Which family a bait id belongs to (read once from data/tackle/baits/&lt;id&gt;.tres).</summary>
public static class BaitFamilies
{
    private static readonly Dictionary<string, (string Family, float Scale)> Cache = new();

    public static (string Family, float Scale) Of(string baitId)
    {
        lock (Cache)
        {
            if (Cache.TryGetValue(baitId, out var known))
                return known;
            var result = ("", 1f);
            string path = $"res://data/tackle/baits/{baitId}.tres";
            if (baitId != "" && ResourceLoader.Exists(path) && GD.Load<BaitData>(path) is { AffinityAs: not "" } bait)
                result = (bait.AffinityAs, bait.AffinityScale);
            Cache[baitId] = result;
            return result;
        }
    }
}
