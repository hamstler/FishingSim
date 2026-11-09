using Godot;

namespace FishingSim.Data;

/// <summary>What an accessory does. New kinds need handling where their effect applies (see docs/ROZWOJ.md).</summary>
public enum AccessoryKind
{
    /// <summary>Okulary polaryzacyjne: cut the glare, fish are visible deeper and further away.</summary>
    PolarizedGlasses,
    /// <summary>Latarka czołowa: light at night.</summary>
    Headlamp,
    /// <summary>Podpórka / rod pod: put the rod down while waiting (planned: several rods).</summary>
    RodPod,
    /// <summary>Krzesełko: resting speeds up the clock (planned).</summary>
    Chair,
    /// <summary>Wiadro na przynęty: keeps live bait alive longer (planned).</summary>
    BaitBucket,
}

/// <summary>Any other accessory. Owning one is enough for it to work (they are not equipped in a slot).</summary>
[GlobalClass]
public partial class AccessoryData : TackleItem
{
    [Export] public AccessoryKind Kind { get; set; }

    /// <summary>Strength of the effect; meaning depends on <see cref="Kind"/> (e.g. extra visibility in metres).</summary>
    [Export] public float Power { get; set; } = 1f;

    [Export(PropertyHint.MultilineText)] public string Description { get; set; } = "";
}
