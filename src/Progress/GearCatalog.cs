using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using FishingSim.Core.Gear;
using FishingSim.Data;

namespace FishingSim.Progress;

/// <summary>
/// Shop catalogue and effects of keepnets, landing nets and accessories. Adding an item = a .tres in
/// data/tackle/{keepnets,landingnets,accessories}/ and its path here.
/// </summary>
public sealed class GearCatalog
{
    private static readonly string[] KeepnetPaths =
    {
        "res://data/tackle/keepnets/siatka_mala.tres", "res://data/tackle/keepnets/siatka_3m.tres",
        "res://data/tackle/keepnets/siatka_karpiowa.tres",
    };
    private static readonly string[] LandingNetPaths =
    {
        "res://data/tackle/landingnets/podbierak_maly.tres", "res://data/tackle/landingnets/podbierak_sredni.tres",
        "res://data/tackle/landingnets/podbierak_karpiowy.tres",
    };
    private static readonly string[] AccessoryPaths =
    {
        "res://data/tackle/accessories/okulary_polaryzacyjne.tres", "res://data/tackle/accessories/latarka_czolowa.tres",
        "res://data/tackle/accessories/podporka.tres", "res://data/tackle/accessories/krzeselko.tres",
        "res://data/tackle/accessories/wiadro.tres", "res://data/tackle/accessories/wodery.tres",
        "res://data/tackle/accessories/oselka.tres", "res://data/tackle/accessories/smalec.tres",
    };

    private readonly Dictionary<GearSlot, List<TackleItem>> _items;

    public Profile? Profile { get; private set; }

    /// <summary>Raised after equipping or buying, so the world can react (e.g. polarized glasses).</summary>
    public event Action? GearChanged;

    public GearCatalog()
    {
        _items = new()
        {
            [GearSlot.Keepnet] = Load(KeepnetPaths),
            [GearSlot.LandingNet] = Load(LandingNetPaths),
            [GearSlot.Accessory] = Load(AccessoryPaths),
        };
    }

    private static List<TackleItem> Load(IEnumerable<string> paths) =>
        paths.Select(p => GD.Load<TackleItem>(p)).Where(i => i != null).ToList();

    public IReadOnlyList<TackleItem> Items(GearSlot slot) => _items[slot];

    public TackleItem? Find(GearSlot slot, string? id) => id == null ? null : _items[slot].FirstOrDefault(i => i.ItemId == id);

    public KeepnetData? Keepnet => Profile == null ? null : Find(GearSlot.Keepnet, Profile.Gear.EquippedId(GearSlot.Keepnet)) as KeepnetData;

    public LandingNetData? LandingNet => Profile == null ? null : Find(GearSlot.LandingNet, Profile.Gear.EquippedId(GearSlot.LandingNet)) as LandingNetData;

    /// <summary>The equipped landing net as model input (null = landing by hand).</summary>
    public LandingNetSpec? LandingNetSpec => LandingNet?.Spec;

    /// <summary>The strongest owned accessory of a kind, or null.</summary>
    public AccessoryData? Accessory(AccessoryKind kind) =>
        Profile == null
            ? null
            : _items[GearSlot.Accessory].OfType<AccessoryData>()
                .Where(a => a.Kind == kind && Profile.Gear.Owns(GearSlot.Accessory, a.ItemId))
                .OrderByDescending(a => a.Power)
                .FirstOrDefault();

    public bool Has(AccessoryKind kind) => Accessory(kind) != null;

    public void Attach(Profile profile)
    {
        Profile = profile;
        Apply();
    }

    /// <summary>Pushes the equipped keepnet's limits into the profile.</summary>
    public void Apply()
    {
        if (Profile == null)
            return;
        Profile.KeepnetLimit = Keepnet?.Spec;
        GearChanged?.Invoke();
    }

    public string? Buy(GearSlot slot, TackleItem item)
    {
        if (Profile == null)
            return "Brak profilu";
        string? error = Profile.BuyGear(slot, item.ItemId, item.Price, item.RequiredLevel);
        if (error == null)
        {
            // The first keepnet or landing net you buy is put to use straight away.
            if (slot.IsEquippable() && Profile.Gear.EquippedId(slot) == null)
                Profile.Gear.Equip(slot, item.ItemId);
            Apply();
        }
        return error;
    }

    public bool Equip(GearSlot slot, TackleItem item)
    {
        if (Profile == null || !Profile.Gear.Equip(slot, item.ItemId))
            return false;
        Apply();
        Profile.NotifyChanged();
        return true;
    }

    public static string Stats(TackleItem item) => item switch
    {
        KeepnetData k => $"Do {k.MaxFish} ryb / {k.MaxKg:0} kg | utrata kondycji {k.ConditionLossPerHour * 100f:0.#}%/h",
        LandingNetData n => $"Ryby do {n.MaxWeight:0.#} kg | rączka {n.Reach:0.#} m",
        AccessoryData a => a.Description,
        _ => "",
    };
}
