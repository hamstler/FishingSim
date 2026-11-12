using Godot;

namespace FishingSim.Data;

/// <summary>What an accessory does. New kinds need handling where their effect applies (see docs/ROZWOJ.md).</summary>
public enum AccessoryKind
{
    /// <summary>Okulary polaryzacyjne: cut the glare, fish are visible deeper and further away.</summary>
    PolarizedGlasses,
    /// <summary>Latarka czołowa: light at night.</summary>
    Headlamp,
    /// <summary>Podpórka z dzwoneczkiem: a bell rings on every bite (planned: putting the rod down, several rods).</summary>
    RodPod,
    /// <summary>Krzesełko: resting speeds up the clock (planned).</summary>
    Chair,
    /// <summary>Wiadro na przynęty: keeps live bait alive longer (planned).</summary>
    BaitBucket,
    /// <summary>Wodery: wade deeper (Power = how deep, in metres).</summary>
    Waders,
    /// <summary>Osełka: sharper hooks set better (Power = extra share of strikes that hold).</summary>
    HookHone,
    /// <summary>Smalec do kołowrotka: the reel winds faster (Power = extra share of speed).</summary>
    ReelGrease,
    /// <summary>Łopata: a hand tool (Q) to dig for worms, night crawlers and grubs (Core/Camp/Digging).</summary>
    Shovel,
    /// <summary>Siatka na owady: a hand tool (Q) to catch mayflies, grasshoppers and chafers (Core/Camp/Sweeping).</summary>
    InsectNet,
    /// <summary>Garnek: cook fish soup over a campfire (Core/Camp/Cooking).</summary>
    CookingPot,
}

/// <summary>Any other accessory. Owning one is enough for it to work (they are not equipped in a slot).</summary>
[GlobalClass]
public partial class AccessoryData : TackleItem
{
    [Export] public AccessoryKind Kind { get; set; }

    /// <summary>Strength of the effect; meaning depends on <see cref="Kind"/> (e.g. extra visibility in metres).</summary>
    [Export] public float Power { get; set; } = 1f;

}
