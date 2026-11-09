using System.Collections.Generic;

namespace FishingSim.Progress;

/// <summary>Gear outside the rod chain: keepnets and landing nets (one equipped each) and accessories (owned = active).</summary>
public enum GearSlot { Keepnet, LandingNet, Accessory }

public static class GearSlotExtensions
{
    public static readonly GearSlot[] All = { GearSlot.Keepnet, GearSlot.LandingNet, GearSlot.Accessory };

    /// <summary>Key used in save files and shop tabs.</summary>
    public static string Key(this GearSlot slot) => slot switch
    {
        GearSlot.Keepnet => "keepnet",
        GearSlot.LandingNet => "landingnet",
        _ => "accessory",
    };

    public static string Title(this GearSlot slot) => slot switch
    {
        GearSlot.Keepnet => "Siatki",
        GearSlot.LandingNet => "Kasarki",
        _ => "Akcesoria",
    };

    /// <summary>Accessories are not worn in a slot: owning one is enough.</summary>
    public static bool IsEquippable(this GearSlot slot) => slot != GearSlot.Accessory;
}

/// <summary>Owned and equipped ids of keepnets, landing nets and accessories. Data-free so it is trivial to save.</summary>
public sealed class GearInventory
{
    public const string StarterKeepnet = "siatka_mala";

    public Dictionary<string, List<string>> Owned { get; set; } = new();
    public Dictionary<string, string> Equipped { get; set; } = new();

    public GearInventory() => Reset();

    public void Reset()
    {
        Owned = new()
        {
            [GearSlot.Keepnet.Key()] = new() { StarterKeepnet },
            [GearSlot.LandingNet.Key()] = new(),          // no landing net until you buy one
            [GearSlot.Accessory.Key()] = new(),
        };
        Equipped = new()
        {
            [GearSlot.Keepnet.Key()] = StarterKeepnet,
        };
    }

    public bool Owns(GearSlot slot, string id) => Owned.TryGetValue(slot.Key(), out var ids) && ids.Contains(id);

    public void Add(GearSlot slot, string id)
    {
        if (!Owned.TryGetValue(slot.Key(), out var ids))
            Owned[slot.Key()] = ids = new();
        if (!ids.Contains(id))
            ids.Add(id);
    }

    public string? EquippedId(GearSlot slot) => Equipped.TryGetValue(slot.Key(), out var id) ? id : null;

    /// <summary>Equips an owned keepnet or landing net. Returns false when not owned or not equippable.</summary>
    public bool Equip(GearSlot slot, string id)
    {
        if (!slot.IsEquippable() || !Owns(slot, id))
            return false;
        Equipped[slot.Key()] = id;
        return true;
    }
}
