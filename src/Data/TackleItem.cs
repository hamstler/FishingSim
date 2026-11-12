using Godot;

namespace FishingSim.Data;

/// <summary>Base for everything that can be bought in the shop. Items are data-only resources (.tres).</summary>
public abstract partial class TackleItem : Resource
{
    [Export] public string DisplayName { get; set; } = "";

    /// <summary>Price in gr (for bait: per pack).</summary>
    [Export] public int Price { get; set; }

    [Export] public int RequiredLevel { get; set; } = 1;

    /// <summary>A sentence or two for the shop card.</summary>
    [Export(PropertyHint.MultilineText)] public string Description { get; set; } = "";

    /// <summary>Stable id: the resource file name without extension (e.g. "teleskop").</summary>
    public string ItemId => System.IO.Path.GetFileNameWithoutExtension(ResourcePath);
}
