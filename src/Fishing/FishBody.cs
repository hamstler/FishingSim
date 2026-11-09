using System;
using System.Collections.Generic;
using FishingSim.Data;

namespace FishingSim.Fishing;

/// <summary>Kind of dorsal fin.</summary>
public enum DorsalKind { Soft, Spiny, Sail, Long, Tiny, Continuous, Spines }

/// <summary>
/// The build of a species, used to loft its body and place its fins: how wide and humped the body is, where it is
/// deepest, how pointed the snout and how broad the head, how thin the tail stalk, the shape of the tail, which fins
/// it has and where, barbels and the eye. Lengths are fractions of the body length (nose to tail base).
/// </summary>
public sealed record FishBody
{
    /// <summary>Width of the cross-section relative to its height (0.3 = flat bream, 0.95 = round eel).</summary>
    public float Width { get; init; } = 0.48f;
    /// <summary>Where along the body it is deepest (0 = nose, 1 = tail base).</summary>
    public float PeakAt { get; init; } = 0.38f;
    /// <summary>A high, arched back (carp, bream, perch): 0 = symmetric.</summary>
    public float Hump { get; init; } = 0.12f;
    /// <summary>Flattened belly of bottom dwellers.</summary>
    public float BellyFlat { get; init; }
    /// <summary>0 = blunt, round head; 1 = long pointed snout (pike); &gt;1 = sturgeon rostrum.</summary>
    public float Snout { get; init; } = 0.3f;
    /// <summary>Head flattened top to bottom (pike, catfish): the head is wider than it is high.</summary>
    public float FlatHead { get; init; }
    /// <summary>Extra width of a broad head (catfish, sculpin, burbot).</summary>
    public float HeadBroad { get; init; }
    /// <summary>Thickness of the tail stalk relative to the deepest point.</summary>
    public float Peduncle { get; init; } = 0.24f;
    /// <summary>0 = rounded tail, 0.5 = slightly notched, 1 = deeply forked.</summary>
    public float TailFork { get; init; } = 0.7f;
    /// <summary>Height of the tail fin relative to the body depth.</summary>
    public float TailSize { get; init; } = 0.95f;
    /// <summary>Upper tail lobe longer than the lower (sturgeons).</summary>
    public float TailUpper { get; init; } = 1f;
    public DorsalKind Dorsal { get; init; } = DorsalKind.Soft;
    /// <summary>Middle of the dorsal fin (fraction of body length); &lt;0 = use the species data.</summary>
    public float DorsalAt { get; init; } = -1f;
    public float DorsalLength { get; init; } = 0.14f;
    public float DorsalHeight { get; init; } = 0.34f;
    /// <summary>Small fleshy fin before the tail (salmonids, North-American catfishes).</summary>
    public bool Adipose { get; init; }
    public float AnalAt { get; init; } = 0.66f;
    public float AnalLength { get; init; } = 0.1f;
    public float PectoralSize { get; init; } = 1f;
    /// <summary>Barbel pairs (0 none, 1 = two, 2 = four, 3 = six, 4 = eight) and their length (fraction of body).</summary>
    public int BarbelPairs { get; init; }
    public float BarbelLength { get; init; } = 0.05f;
    /// <summary>Eye diameter as a fraction of the body length.</summary>
    public float Eye { get; init; } = 0.045f;
    /// <summary>Where the eye sits along the body and how high on the head (0 = mid, 1 = top).</summary>
    public float EyeAt { get; init; } = 0.11f;
    public float EyeHigh { get; init; } = 0.3f;

    private static readonly FishBody Cyprinid = new();
    private static readonly FishBody DeepCyprinid = new()
    {
        Width = 0.3f, PeakAt = 0.46f, Hump = 0.38f, Snout = 0.3f, Peduncle = 0.15f, TailFork = 0.85f, TailSize = 0.75f,
        DorsalLength = 0.12f, DorsalHeight = 0.38f, AnalAt = 0.66f, AnalLength = 0.26f, Eye = 0.05f,
    };
    private static readonly FishBody CarpLike = new()
    {
        Width = 0.55f, PeakAt = 0.42f, Hump = 0.32f, Snout = 0.38f, Peduncle = 0.28f, TailFork = 0.55f, TailSize = 0.85f,
        Dorsal = DorsalKind.Long, DorsalAt = 0.55f, DorsalLength = 0.36f, DorsalHeight = 0.22f, BarbelPairs = 2,
        BarbelLength = 0.03f, Eye = 0.032f,
    };
    private static readonly FishBody Salmonid = new()
    {
        Width = 0.5f, PeakAt = 0.45f, Hump = 0.06f, Snout = 0.42f, Peduncle = 0.24f, TailFork = 0.3f, TailSize = 0.95f,
        DorsalAt = 0.44f, DorsalLength = 0.13f, DorsalHeight = 0.3f, Adipose = true, AnalAt = 0.72f, Eye = 0.034f,
    };
    private static readonly FishBody Percid = new()
    {
        Width = 0.48f, PeakAt = 0.36f, Hump = 0.32f, Snout = 0.42f, Peduncle = 0.2f, TailFork = 0.45f, TailSize = 0.9f,
        Dorsal = DorsalKind.Spiny, DorsalAt = 0.5f, DorsalLength = 0.4f, DorsalHeight = 0.38f, AnalAt = 0.7f, Eye = 0.042f,
    };
    private static readonly FishBody Loach = new()
    {
        Width = 0.7f, PeakAt = 0.45f, Hump = 0f, BellyFlat = 0.35f, Snout = 0.25f, Peduncle = 0.5f, TailFork = 0.08f,
        TailSize = 1.1f, DorsalAt = 0.5f, DorsalLength = 0.1f, DorsalHeight = 0.3f, AnalAt = 0.72f, BarbelPairs = 3,
        BarbelLength = 0.04f, Eye = 0.03f, EyeHigh = 0.45f,
    };
    private static readonly FishBody Sculpin = new()
    {
        Width = 0.9f, PeakAt = 0.2f, Hump = 0f, BellyFlat = 0.65f, Snout = 0.05f, FlatHead = 0.5f, HeadBroad = 0.7f,
        Peduncle = 0.32f, TailFork = 0f, TailSize = 1.1f, Dorsal = DorsalKind.Spiny, DorsalAt = 0.55f, DorsalLength = 0.6f,
        DorsalHeight = 0.3f, AnalAt = 0.7f, AnalLength = 0.25f, PectoralSize = 2f, Eye = 0.06f, EyeAt = 0.1f, EyeHigh = 0.8f,
    };
    private static readonly FishBody Sturgeon = new()
    {
        Width = 0.85f, PeakAt = 0.32f, Hump = 0.1f, BellyFlat = 0.5f, Snout = 1.6f, FlatHead = 0.4f, Peduncle = 0.2f,
        TailFork = 0.75f, TailSize = 1.1f, TailUpper = 1.7f, DorsalAt = 0.84f, DorsalLength = 0.09f, DorsalHeight = 0.32f,
        AnalAt = 0.84f, BarbelPairs = 2, BarbelLength = 0.035f, Eye = 0.014f, EyeAt = 0.17f, EyeHigh = 0.5f,
    };

    private static readonly Dictionary<string, FishBody> Bodies = new()
    {
        ["Płoć"] = Cyprinid with { Hump = 0.18f, Eye = 0.055f },
        ["Wzdręga"] = Cyprinid with { Hump = 0.25f, Width = 0.4f, DorsalAt = 0.55f, Eye = 0.05f },
        ["Jaź"] = Cyprinid with { Width = 0.52f, Hump = 0.2f, Snout = 0.3f },
        ["Orfa"] = Cyprinid with { Width = 0.52f, Hump = 0.2f, Snout = 0.3f },
        ["Kleń"] = Cyprinid with { Width = 0.62f, Hump = 0.06f, HeadBroad = 0.15f, Snout = 0.25f, Peduncle = 0.3f, TailFork = 0.5f, Eye = 0.04f },
        ["Jelec"] = Cyprinid with { Width = 0.5f, Hump = 0.04f, Snout = 0.4f, Eye = 0.05f },
        ["Boleń"] = Cyprinid with { Width = 0.45f, Hump = 0.1f, Snout = 0.55f, PeakAt = 0.42f, TailFork = 0.85f, Eye = 0.035f },
        ["Świnka"] = Cyprinid with { BellyFlat = 0.25f, Snout = 0.45f, Hump = 0.08f, Eye = 0.04f },
        ["Certa"] = Cyprinid with { Snout = 0.55f, Hump = 0.18f, AnalLength = 0.16f },
        ["Ukleja"] = Cyprinid with { Width = 0.34f, Hump = -0.05f, Snout = 0.45f, PeakAt = 0.4f, TailFork = 0.85f, AnalLength = 0.16f, Eye = 0.065f },
        ["Słonecznica"] = Cyprinid with { Width = 0.34f, Hump = 0f, Snout = 0.4f, TailFork = 0.8f, Eye = 0.07f },
        ["Ciosa"] = Cyprinid with { Width = 0.3f, Hump = -0.35f, Snout = 0.45f, PeakAt = 0.32f, DorsalAt = 0.75f, AnalLength = 0.3f, TailFork = 0.9f, Eye = 0.05f },
        ["Leszcz"] = DeepCyprinid,
        ["Krąp"] = DeepCyprinid with { Eye = 0.06f },
        ["Rozpiór"] = DeepCyprinid with { AnalLength = 0.34f, Hump = 0.25f },
        ["Sapa"] = DeepCyprinid with { AnalLength = 0.3f, Hump = 0.3f, Eye = 0.06f },
        ["Karaś złocisty"] = CarpLike with { Width = 0.45f, Hump = 0.4f, Peduncle = 0.3f, TailFork = 0.25f, BarbelPairs = 0, Eye = 0.04f },
        ["Karaś srebrzysty"] = CarpLike with { Width = 0.45f, Hump = 0.35f, Peduncle = 0.28f, TailFork = 0.6f, BarbelPairs = 0, Eye = 0.045f },
        ["Karp"] = CarpLike,
        ["Sazan"] = CarpLike with { Hump = 0.18f, Width = 0.6f },
        ["Karp lustrzeń"] = CarpLike with { Hump = 0.42f, Width = 0.58f },
        ["Amur biały"] = Cyprinid with { Width = 0.72f, Hump = 0.05f, PeakAt = 0.42f, HeadBroad = 0.1f, Snout = 0.2f, Peduncle = 0.32f, TailFork = 0.55f, Eye = 0.03f },
        ["Tołpyga biała"] = Cyprinid with { Width = 0.5f, Hump = 0.12f, Snout = 0.1f, PeakAt = 0.36f, TailFork = 0.8f, Eye = 0.028f, EyeAt = 0.09f, EyeHigh = -0.35f },
        ["Tołpyga pstra"] = Cyprinid with { Width = 0.52f, Hump = 0.15f, Snout = 0.05f, PeakAt = 0.34f, HeadBroad = 0.12f, TailFork = 0.75f, Eye = 0.025f, EyeAt = 0.09f, EyeHigh = -0.35f },
        ["Lin"] = Cyprinid with { Width = 0.6f, Hump = 0.15f, Snout = 0.25f, Peduncle = 0.42f, TailFork = 0.1f, TailSize = 1f, DorsalHeight = 0.3f, BarbelPairs = 1, BarbelLength = 0.02f, Eye = 0.03f },
        ["Brzana"] = Cyprinid with { Width = 0.62f, Hump = 0.16f, BellyFlat = 0.4f, Snout = 0.6f, Peduncle = 0.24f, TailFork = 0.7f, BarbelPairs = 2, BarbelLength = 0.05f, Eye = 0.03f, EyeHigh = 0.45f },
        ["Brzanka"] = Cyprinid with { Width = 0.6f, Hump = 0.12f, BellyFlat = 0.4f, Snout = 0.55f, BarbelPairs = 2, BarbelLength = 0.045f, Eye = 0.035f, EyeHigh = 0.45f },
        ["Kiełb"] = Cyprinid with { Width = 0.62f, Hump = 0.05f, BellyFlat = 0.4f, Snout = 0.35f, Peduncle = 0.3f, BarbelPairs = 1, BarbelLength = 0.035f, Eye = 0.05f, EyeHigh = 0.5f },
        ["Kiełb białopłetwy"] = Cyprinid with { Width = 0.6f, Hump = 0.05f, BellyFlat = 0.4f, Snout = 0.35f, Peduncle = 0.3f, BarbelPairs = 1, BarbelLength = 0.035f, Eye = 0.055f, EyeHigh = 0.55f },
        ["Strzebla potokowa"] = Cyprinid with { Width = 0.55f, Hump = 0.02f, Snout = 0.2f, Peduncle = 0.32f, TailFork = 0.4f, DorsalAt = 0.55f, Eye = 0.06f },
        ["Strzebla błotna"] = Cyprinid with { Width = 0.55f, Hump = 0.06f, Snout = 0.2f, Peduncle = 0.32f, TailFork = 0.3f, DorsalAt = 0.55f, Eye = 0.055f },
        ["Różanka"] = DeepCyprinid with { Width = 0.35f, Hump = 0.3f, Peduncle = 0.2f, TailFork = 0.6f, TailSize = 0.9f, AnalLength = 0.16f, Eye = 0.07f },
        ["Szczupak"] = new FishBody
        {
            Width = 0.62f, PeakAt = 0.55f, Hump = 0f, Snout = 1f, FlatHead = 0.55f, Peduncle = 0.36f, TailFork = 0.45f, TailSize = 1f,
            DorsalAt = 0.8f, DorsalLength = 0.12f, DorsalHeight = 0.3f, AnalAt = 0.81f, AnalLength = 0.1f, Eye = 0.03f, EyeAt = 0.16f, EyeHigh = 0.6f,
        },
        ["Okoń"] = Percid,
        ["Jazgarz"] = Percid with { Hump = 0.25f, Snout = 0.35f, Eye = 0.055f },
        ["Sandacz"] = Percid with { Width = 0.5f, Hump = 0.06f, PeakAt = 0.42f, Snout = 0.6f, Peduncle = 0.24f, Eye = 0.045f, DorsalHeight = 0.32f },
        ["Sandacz wołżański"] = Percid with { Width = 0.5f, Hump = 0.1f, PeakAt = 0.4f, Snout = 0.5f, Eye = 0.045f },
        ["Bass wielkogębowy"] = Percid with { Width = 0.5f, Hump = 0.14f, PeakAt = 0.42f, Snout = 0.45f, FlatHead = 0.15f, Eye = 0.035f, DorsalHeight = 0.32f },
        ["Czop"] = Percid with { Width = 0.65f, Hump = 0f, PeakAt = 0.3f, Snout = 0.55f, Peduncle = 0.12f, BellyFlat = 0.35f, DorsalAt = 0.48f, DorsalLength = 0.5f, DorsalHeight = 0.26f, Eye = 0.04f, EyeHigh = 0.6f },
        ["Pstrąg potokowy"] = Salmonid,
        ["Pstrąg tęczowy"] = Salmonid with { Hump = 0.1f, Width = 0.52f },
        ["Pstrąg źródlany"] = Salmonid with { Width = 0.5f, TailFork = 0.2f },
        ["Troć jeziorowa"] = Salmonid with { Snout = 0.48f, TailFork = 0.25f },
        ["Troć wędrowna"] = Salmonid with { Snout = 0.48f, TailFork = 0.25f, Width = 0.46f },
        ["Łosoś"] = Salmonid with { Snout = 0.55f, TailFork = 0.4f, Width = 0.45f, Peduncle = 0.2f },
        ["Palia"] = Salmonid with { TailFork = 0.45f },
        ["Głowacica"] = Salmonid with { Width = 0.72f, PeakAt = 0.42f, Snout = 0.5f, FlatHead = 0.25f, Peduncle = 0.28f, TailFork = 0.25f, Eye = 0.025f },
        ["Lipień"] = Salmonid with { Snout = 0.35f, Dorsal = DorsalKind.Sail, DorsalAt = 0.42f, DorsalLength = 0.3f, DorsalHeight = 0.55f, TailFork = 0.65f, Eye = 0.045f },
        ["Sielawa"] = Salmonid with { Width = 0.42f, Snout = 0.35f, TailFork = 0.85f, Eye = 0.06f },
        ["Sieja"] = Salmonid with { Width = 0.44f, Hump = 0.1f, Snout = 0.25f, TailFork = 0.85f, Eye = 0.045f },
        ["Stynka"] = Salmonid with { Width = 0.4f, Snout = 0.5f, PeakAt = 0.42f, TailFork = 0.85f, Eye = 0.06f },
        ["Sum"] = new FishBody
        {
            Width = 0.9f, PeakAt = 0.24f, Hump = 0f, BellyFlat = 0.3f, Snout = 0.1f, FlatHead = 0.65f, HeadBroad = 0.45f, Peduncle = 0.3f,
            TailFork = 0f, TailSize = 0.9f, Dorsal = DorsalKind.Tiny, DorsalAt = 0.3f, DorsalLength = 0.05f, DorsalHeight = 0.18f,
            AnalAt = 0.66f, AnalLength = 0.6f, BarbelPairs = 3, BarbelLength = 0.08f, Eye = 0.012f, EyeAt = 0.08f, EyeHigh = 0.45f,
        },
        ["Sumik karłowaty"] = new FishBody
        {
            Width = 0.75f, PeakAt = 0.3f, Hump = 0.1f, BellyFlat = 0.3f, Snout = 0.1f, FlatHead = 0.45f, HeadBroad = 0.3f, Peduncle = 0.35f,
            TailFork = 0.12f, TailSize = 1f, DorsalAt = 0.36f, DorsalLength = 0.09f, DorsalHeight = 0.32f, Adipose = true, AnalAt = 0.72f,
            AnalLength = 0.2f, BarbelPairs = 4, BarbelLength = 0.06f, Eye = 0.025f, EyeHigh = 0.45f,
        },
        ["Sumik kanałowy"] = new FishBody
        {
            Width = 0.72f, PeakAt = 0.32f, Hump = 0.08f, BellyFlat = 0.25f, Snout = 0.2f, FlatHead = 0.35f, HeadBroad = 0.2f, Peduncle = 0.22f,
            TailFork = 0.75f, TailSize = 1f, DorsalAt = 0.36f, DorsalLength = 0.09f, DorsalHeight = 0.34f, Adipose = true, AnalAt = 0.74f,
            AnalLength = 0.2f, BarbelPairs = 4, BarbelLength = 0.07f, Eye = 0.025f, EyeHigh = 0.45f,
        },
        ["Węgorz"] = new FishBody
        {
            Width = 0.95f, PeakAt = 0.4f, Hump = 0f, Snout = 0.35f, FlatHead = 0.2f, Peduncle = 0.7f, TailFork = 0f, TailSize = 0.6f,
            Dorsal = DorsalKind.Continuous, DorsalAt = 0.68f, DorsalLength = 0.62f, DorsalHeight = 0.35f, AnalAt = 0.75f,
            AnalLength = 0.5f, PectoralSize = 0.6f, Eye = 0.012f, EyeAt = 0.06f,
        },
        ["Miętus"] = new FishBody
        {
            Width = 0.8f, PeakAt = 0.3f, Hump = 0f, BellyFlat = 0.2f, Snout = 0.15f, FlatHead = 0.3f, HeadBroad = 0.25f, Peduncle = 0.4f,
            TailFork = 0f, TailSize = 0.9f, Dorsal = DorsalKind.Continuous, DorsalAt = 0.66f, DorsalLength = 0.55f, DorsalHeight = 0.3f,
            AnalAt = 0.72f, AnalLength = 0.45f, BarbelPairs = 1, BarbelLength = 0.04f, Eye = 0.025f, EyeHigh = 0.5f,
        },
        ["Piskorz"] = Loach with { Width = 0.75f, Peduncle = 0.55f, BarbelPairs = 4, BarbelLength = 0.035f },
        ["Śliz"] = Loach,
        ["Koza"] = Loach with { Width = 0.45f, BarbelPairs = 2 },
        ["Głowacz białopłetwy"] = Sculpin,
        ["Głowacz pręgopłetwy"] = Sculpin,
        ["Babka bycza"] = Sculpin with { Width = 0.75f, HeadBroad = 0.45f, PectoralSize = 1.6f, Eye = 0.065f, EyeHigh = 0.75f, DorsalLength = 0.55f },
        ["Ciernik"] = Cyprinid with { Width = 0.4f, Hump = 0.1f, Snout = 0.35f, Peduncle = 0.1f, TailFork = 0.15f, Dorsal = DorsalKind.Spines, DorsalAt = 0.5f, DorsalLength = 0.45f, DorsalHeight = 0.3f, Eye = 0.08f },
        ["Cierniczek"] = Cyprinid with { Width = 0.4f, Hump = 0.05f, Snout = 0.35f, Peduncle = 0.1f, TailFork = 0.15f, Dorsal = DorsalKind.Spines, DorsalAt = 0.55f, DorsalLength = 0.55f, DorsalHeight = 0.2f, Eye = 0.075f },
        ["Jesiotr ostronosy"] = Sturgeon,
        ["Sterlet"] = Sturgeon with { Snout = 2.1f, Eye = 0.012f },
    };

    public static FishBody Of(FishSpecies sp) => Bodies.TryGetValue(sp.SpeciesName, out var b) ? b : Cyprinid;

    /// <summary>Species with a body shape of their own (for tests).</summary>
    public static bool HasOwnShape(FishSpecies sp) => Bodies.ContainsKey(sp.SpeciesName);
}
