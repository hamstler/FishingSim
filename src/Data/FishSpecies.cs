using Godot;
using Godot.Collections;

namespace FishingSim.Data;

/// <summary>Data-only description of a fish species. Add a species by adding a .tres in data/fish/.</summary>
[GlobalClass]
public partial class FishSpecies : Resource
{
    // --- identity & size ---
    [Export] public string SpeciesName { get; set; } = "";

    /// <summary>Scientific name, shown in the atlas.</summary>
    [Export] public string LatinName { get; set; } = "";

    /// <summary>A few sentences about the fish for the atlas (Polish).</summary>
    [Export(PropertyHint.MultilineText)] public string Description { get; set; } = "";
    [Export] public float MinWeight { get; set; } = 0.1f;
    [Export] public float MaxWeight { get; set; } = 0.9f;

    /// <summary>Relative bite chance.</summary>
    [Export] public float Rarity { get; set; } = 1f;

    // --- fight ---
    /// <summary>Peak pull (kg-force) = PullFactor * (weight^0.7 + 0.25).</summary>
    [Export] public float PullFactor { get; set; } = 1f;
    [Export(PropertyHint.Range, "0,1")] public float Aggression { get; set; } = 0.5f;
    [Export] public float StaminaDrain { get; set; } = 0.12f;
    [Export] public float RunLengthMult { get; set; } = 1f;
    [Export] public float BurstMult { get; set; } = 1.5f;
    [Export] public float BurstRate { get; set; } = 1f;

    /// <summary>Teeth: the chance per second of the fight that it bites through a leader that is not wire (0 = none).</summary>
    [Export] public float BiteOff { get; set; }

    // --- habitat & feeding ---
    /// <summary>Hook size (1..10) this species takes best.</summary>
    [Export] public float MouthSize { get; set; } = 5f;

    /// <summary>Bait id -> multiplier (a missing id means the species never takes it).</summary>
    [Export] public Dictionary BaitAffinity { get; set; } = new();
    [Export] public float MinDepth { get; set; } = 0.5f;
    [Export] public float MaxDepth { get; set; } = 6f;
    [Export] public float DayActivity { get; set; } = 1f;
    [Export] public float NightActivity { get; set; } = 0.5f;
    [Export] public float TwilightBonus { get; set; } = 1.3f;

    /// <summary>Weather key ("clear", "cloudy", "rain") -> bite multiplier.</summary>
    [Export] public Dictionary WeatherPref { get; set; } = new();

    // --- look ---
    /// <summary>Body length (m) of a 1 kg fish; scales with weight^0.33.</summary>
    [Export] public float LengthRef { get; set; } = 0.35f;
    [Export] public float BodyDepth { get; set; } = 0.26f;
    [Export] public float DorsalPos { get; set; } = 0.5f;
    [Export] public Color BackColor { get; set; } = new(0.35f, 0.4f, 0.3f);
    [Export] public Color BellyColor { get; set; } = new(0.85f, 0.85f, 0.8f);
    [Export] public Color FinColor { get; set; } = new(0.6f, 0.4f, 0.3f);

    // --- economy ---
    [Export] public float PricePerKg { get; set; } = 5f;

    /// <summary>Weight (kg) from which the fish market pays the trophy bonus; 0 = 80% of <see cref="MaxWeight"/>.</summary>
    [Export] public float TrophyWeight { get; set; }

    public float TrophyThreshold => TrophyWeight > 0f ? TrophyWeight : MaxWeight * 0.8f;

    /// <summary>The lord's minimum size (as a weight): smaller fish must go back into the water.</summary>
    public float LegalWeight => MinWeight + (MaxWeight - MinWeight) * 0.12f;

    /// <summary>
    /// Affinity for a bait id (0 when the species ignores it): its own entry, or else the entry for the bait's family
    /// times the bait's scale (a hemp seed is judged as corn, a brass spoon as "spoon").
    /// </summary>
    public float AffinityFor(string baitId)
    {
        if (BaitAffinity.TryGetValue(baitId, out var v))
            return v.AsSingle();
        var (family, scale) = BaitFamilies.Of(baitId);
        return family != "" && BaitAffinity.TryGetValue(family, out var f) ? f.AsSingle() * scale : 0f;
    }

    /// <summary>Weather multiplier for a weather key (1 when unspecified).</summary>
    public float WeatherMultiplier(string weatherKey) =>
        WeatherPref.TryGetValue(weatherKey, out var v) ? v.AsSingle() : 1f;
}
