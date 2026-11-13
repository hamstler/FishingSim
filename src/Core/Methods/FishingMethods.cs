using System;
using System.Collections.Generic;
using FishingSim.Data;

namespace FishingSim.Core.Methods;

/// <summary>How the bait is fished: under a float, as a lure drawn through the water, or lying on the bottom.</summary>
public enum FishingMethod { Float, Spinning, Bottom }

/// <summary>The kinds of lure (the bait's AffinityAs family): each runs, sinks and wants to be led its own way.</summary>
public enum LureFamily { Spinner, Spoon, Wobbler, Softbait, Fly }

/// <summary>How the retrieve suits the lure (for the HUD).</summary>
public enum RetrieveJudgement { Still, Falling, TooSlow, Good, TooFast, Snagged }

/// <summary>What happened when a snag was jerked or pulled.</summary>
public enum SnagResult { Stuck, Freed, Broke }

/// <summary>Picks the method from the tackle and names things for the HUD.</summary>
public static class Method
{
    /// <summary>A lure on any rod is spinning; bait on a bottom or carp rod lies on the bottom; otherwise the float.</summary>
    public static FishingMethod For(RodKind rod, BaitKind bait) =>
        bait == BaitKind.Lure ? FishingMethod.Spinning
        : rod is RodKind.Bottom or RodKind.Carp ? FishingMethod.Bottom
        : FishingMethod.Float;

    public static string Title(this FishingMethod m) => m switch
    {
        FishingMethod.Spinning => "Spinning",
        FishingMethod.Bottom => "Grunt",
        _ => "Spławik",
    };

    public static LureFamily FamilyOf(string affinityAs) => affinityAs switch
    {
        "spoon" => LureFamily.Spoon,
        "wobbler" => LureFamily.Wobbler,
        "softbait" => LureFamily.Softbait,
        "fly" => LureFamily.Fly,
        _ => LureFamily.Spinner,
    };

    public static string Title(this LureFamily f) => f switch
    {
        LureFamily.Spoon => "wahadłówka",
        LureFamily.Wobbler => "wobler",
        LureFamily.Softbait => "guma na główce",
        LureFamily.Fly => "mucha",
        _ => "obrotówka",
    };

    public static string Title(this RetrieveJudgement j) => j switch
    {
        RetrieveJudgement.Falling => "opada",
        RetrieveJudgement.TooSlow => "za wolno",
        RetrieveJudgement.Good => "dobre prowadzenie",
        RetrieveJudgement.TooFast => "za szybko",
        RetrieveJudgement.Snagged => "ZACZEP",
        _ => "stoi",
    };

    /// <summary>A spinning rod leads a lure best; a float or bottom rod does it clumsily.</summary>
    public static float LureRodMatch(RodKind rod) => rod == RodKind.Spinning ? 1.25f : 0.85f;

    /// <summary>
    /// The chance that a fish hitting a lure that is being reeled hooks itself on the moving lure: high for the
    /// aggressive predators (pike, asp), small for the shy ones.
    /// </summary>
    public static float SelfHookChance(float aggression, bool reeling) =>
        reeling ? Math.Clamp((aggression - 0.4f) * 0.9f, 0f, 0.5f) : 0f;

    /// <summary>Seconds the angler has to strike once the fish takes: a float dives, a lure is hit and spat out fast,
    /// a fish on the bottom pulls the rig away more slowly.</summary>
    public static float HookWindow(FishingMethod m) => m switch
    {
        FishingMethod.Spinning => 0.9f,
        FishingMethod.Bottom => 1.8f,
        _ => 1.2f,
    };

    /// <summary>A bigger lure acts like a bigger hook (in hook sizes): it draws bigger fish and puts small mouths off.</summary>
    public static float SizeHookShift(float sizeCm) => sizeCm <= 0f ? 0f : Math.Clamp((sizeCm - 6f) / 2.5f, -1.5f, 3.5f);

    /// <summary>Small lures are taken more often (up to 1.2x), big ones less (down to 0.75x).</summary>
    public static float SizePace(float sizeCm) => sizeCm <= 0f ? 1f : Math.Clamp(1.2f - (sizeCm - 4f) * 0.04f, 0.75f, 1.2f);

    /// <summary>
    /// How far a cast goes against the rod's full range: a heavier lure flies further (a fly or a light spinner
    /// barely 0.8, a 25 g spoon 1.2), a thicker line drags more (0.12 mm 1.02, 0.25 mm 0.92, 0.40 mm 0.8).
    /// </summary>
    public static float CastFactor(float weightG, float diameterMm, bool lure) =>
        (lure && weightG > 0f ? Math.Clamp(0.72f + 0.1f * MathF.Pow(weightG, 0.55f), 0.7f, 1.2f) : 1f)
        * Math.Clamp(1.12f - diameterMm * 0.8f, 0.75f, 1.08f);

    /// <summary>Daylight reaching the water (0 night .. 1 bright day): dawn and dusk in between, clouds and rain dim it.</summary>
    public static float LightLevel(float hour, Weather weather)
    {
        float h = (hour % 24f + 24f) % 24f;
        float sun = h < 5f || h > 19.5f ? 0.08f
            : h < 6f ? 0.08f + (h - 5f) * 0.27f
            : h < 9f ? 0.35f + (h - 6f) / 3f * 0.65f
            : h <= 16f ? 1f
            : h < 18f ? 1f - (h - 16f) / 2f * 0.65f
            : 0.35f - (h - 18f) / 1.5f * 0.27f;
        float dim = weather switch { Weather.Cloudy => 0.75f, Weather.Rain => 0.55f, _ => 1f };
        return Math.Clamp(sun > 0.1f ? sun * dim : sun, 0f, 1f);
    }

    /// <summary>
    /// How a lure's colours suit the light and the water (multiplies the bite clock, about 0.8-1.3): fluorescent,
    /// white and pearl lures show in murky water and poor light; natural colours work best in clear water by day;
    /// polished metal flashes in the sun; black stands out against the sky at dusk and at night. The main colour counts
    /// three quarters, the accent a quarter.
    /// </summary>
    public static float ColourAppeal(Godot.Color main, Godot.Color accent, bool metallic, float light, float clarity)
    {
        float murk = Math.Clamp((3.5f - clarity) / 2.5f, 0f, 1f);
        float dim = MathF.Max(1f - light, murk);
        float One(Godot.Color c, bool metal)
        {
            float v = MathF.Max(c.R, MathF.Max(c.G, c.B)), lo = MathF.Min(c.R, MathF.Min(c.G, c.B));
            float sat = v > 0f ? (v - lo) / v : 0f;
            if (metal)
                return 1f + 0.22f * light * (1f - murk) - 0.08f * (1f - light);
            if (v < 0.25f)
                return 0.9f + 0.35f * (1f - light);
            if ((sat > 0.6f && v > 0.85f) || (v > 0.88f && sat < 0.15f))
                return 0.88f + 0.4f * dim;
            return 1.15f - 0.35f * dim;
        }
        return 0.75f * One(main, metallic) + 0.25f * One(accent, false);
    }

    /// <summary>A word for the HUD on how the colour suits the conditions.</summary>
    public static string ColourWord(float appeal) => appeal >= 1.08f ? "kolor pasuje" : appeal <= 0.94f ? "kolor słabo widać" : "";
}

/// <summary>How one lure differs from its family's usual run (from its data): sink faster or slower, run deeper or
/// shallower; a lure that runs at depth 0 and does not sink stays on the surface (a dry fly, a popper).</summary>
public readonly record struct LureTuning(float SinkScale, float SinkOffset, float DepthScale, float DepthOffset)
{
    public static readonly LureTuning None = new(1f, 0f, 1f, 0f);

    public static LureTuning From(BaitData bait) => new(bait.SinkScale, bait.SinkOffset, bait.DepthScale, bait.DepthOffset);
}

/// <summary>
/// How a lure moves: <paramref name="SinkRate"/> m/s down while it is not reeled (negative: it floats up),
/// the depth it runs at on a slow (<paramref name="RunDepth"/>) and a fast (<paramref name="FastRunDepth"/>) retrieve
/// and how fast it gets there (<paramref name="DepthChase"/>), the retrieve speeds (m/s) it plays best between,
/// how often it catches on the bottom per second dragged along it, and how much it still fishes lying still.
/// </summary>
public readonly record struct LureSpec(
    float SinkRate, float RunDepth, float FastRunDepth, float DepthChase,
    float IdealSlow, float IdealFast, float SnagRate, float StillPace, float TwitchDart);

/// <summary>
/// One lure in the water (pure): its depth under the surface as it sinks, runs and rises, whether it scrapes the
/// bottom or is caught on it, and how well it is being led, which sets how fast the fish come to it
/// (<see cref="BitePace"/>). A spinner sinks slowly and rises on a fast retrieve; a spoon sinks fast and runs deep;
/// a wobbler floats when stopped and dives the deeper the faster it is reeled; a jig (soft bait on a lead head) drops
/// straight to the bottom and is taken as it falls or just after it is lifted (stop and go); a fly rides the surface.
/// A twitch (the rod jerked) makes it dart and flash for a moment.
/// </summary>
public sealed class LureMotion
{
    public static LureSpec SpecOf(LureFamily f) => f switch
    {
        LureFamily.Spoon => new(0.8f, 1.6f, 0.8f, 0.7f, 0.4f, 0.95f, 0.28f, 0.05f, 0.2f),
        LureFamily.Wobbler => new(-0.25f, 0.8f, 1.8f, 0.8f, 0.55f, 1.3f, 0.4f, 0.1f, 0.35f),
        LureFamily.Softbait => new(1.1f, 0.6f, 0.3f, 0.35f, 0.3f, 0.8f, 0.06f, 0.2f, 0.25f),
        LureFamily.Fly => new(0f, 0f, 0f, 2f, 0.2f, 0.6f, 0f, 0.15f, 0.15f),
        _ => new(0.45f, 0.9f, 0.35f, 0.6f, 0.45f, 1.0f, 0.35f, 0.05f, 0.1f),
    };

    public LureFamily Family { get; }
    public LureSpec Spec { get; }

    /// <summary>Metres under the surface.</summary>
    public float Depth { get; private set; }

    /// <summary>Horizontal speed it is drawn at (m/s), 0 when not reeled.</summary>
    public float Speed { get; private set; }

    public bool OnBottom { get; private set; }
    public bool Falling { get; private set; }
    public bool Snagged { get; private set; }

    /// <summary>0..0.9: the flash after a twitch, fading in under two seconds.</summary>
    public float Flash { get; private set; }

    private float _fell;          // metres fallen since the retrieve stopped
    private float _liftBonus;     // seconds of the "just lifted" jig bonus left
    private float _pulled;        // seconds a snag has been pulled on

    /// <summary>Rides the surface film (a dry fly, a popper): never sinks or snags.</summary>
    public bool Surface { get; }

    public LureMotion(LureFamily family, LureTuning? tuning = null)
    {
        Family = family;
        var t = tuning ?? LureTuning.None;
        var b = SpecOf(family);
        Spec = b with
        {
            SinkRate = b.SinkRate * t.SinkScale + t.SinkOffset,
            RunDepth = b.RunDepth * t.DepthScale + t.DepthOffset,
            FastRunDepth = b.FastRunDepth * t.DepthScale + t.DepthOffset,
        };
        Surface = Spec.SinkRate <= 0f && Spec.RunDepth <= 0.01f && Spec.FastRunDepth <= 0.01f;
        Depth = Surface ? 0f : 0.05f;
    }

    /// <summary>The depth it runs at when drawn at this speed.</summary>
    public float RunDepthAt(float speed)
    {
        float t = Math.Clamp((speed - Spec.IdealSlow) / MathF.Max(Spec.IdealFast * 1.6f - Spec.IdealSlow, 0.1f), 0f, 1f);
        return Spec.RunDepth + (Spec.FastRunDepth - Spec.RunDepth) * t;
    }

    /// <summary>One step: <paramref name="retrieve"/> = horizontal speed (m/s, 0 when the reel stands),
    /// <paramref name="bottom"/> = the water's depth where the lure is.</summary>
    public void Step(float dt, float retrieve, float bottom, IRng rng)
    {
        Flash = MathF.Max(0f, Flash - dt * 0.5f);
        _liftBonus = MathF.Max(0f, _liftBonus - dt);
        if (Snagged)
        {
            Speed = 0f;
            Falling = false;
            return;
        }
        bool reeled = retrieve > 0.01f;
        if (reeled && !(Speed > 0.01f) && _fell >= 0.3f)
            _liftBonus = 1.5f;       // lifted off the bottom after a fall: the moment a jig is taken
        Speed = reeled ? retrieve : 0f;
        float before = Depth;
        if (reeled)
        {
            float target = RunDepthAt(retrieve);
            Depth = Depth < target ? MathF.Min(Depth + Spec.DepthChase * dt, target) : MathF.Max(Depth - Spec.DepthChase * dt, target);
            _fell = 0f;
        }
        else
            Depth += Spec.SinkRate * dt;
        float floor = MathF.Max(bottom - 0.04f, 0f);
        Depth = Surface ? 0f : Math.Clamp(Depth, 0.02f, MathF.Max(floor, 0.02f));
        OnBottom = !Surface && bottom > 0.08f && Depth >= floor - 0.01f;
        Falling = !reeled && !OnBottom && Depth - before > 0.15f * dt;
        if (!reeled && Depth > before)
            _fell += Depth - before;
        // Dragged along the bottom it may catch on a root, a stone or a sunken branch.
        if (OnBottom && reeled && rng.NextFloat() < Spec.SnagRate * dt)
        {
            Snagged = true;
            _pulled = 0f;
            Speed = 0f;
        }
    }

    /// <summary>
    /// The rod is jerked: the lure darts towards the angler (metres returned) and flashes; a jig hops off the bottom.
    /// </summary>
    public float Twitch()
    {
        if (Snagged)
            return 0f;
        Flash = MathF.Min(Flash + (Family is LureFamily.Wobbler or LureFamily.Softbait or LureFamily.Spoon ? 0.5f : 0.25f), 0.9f);
        if (Family == LureFamily.Softbait)
        {
            Depth = MathF.Max(0.02f, Depth - 0.3f);
            _fell = 0f;
        }
        OnBottom = false;
        return Spec.TwitchDart;
    }

    /// <summary>How the retrieve suits this lure now.</summary>
    public RetrieveJudgement Judgement =>
        Snagged ? RetrieveJudgement.Snagged
        : Speed <= 0.01f ? (Falling ? RetrieveJudgement.Falling : RetrieveJudgement.Still)
        : Speed < Spec.IdealSlow ? RetrieveJudgement.TooSlow
        : Speed > Spec.IdealFast ? RetrieveJudgement.TooFast
        : RetrieveJudgement.Good;

    /// <summary>How well it plays at its speed: 1.3 within its range, less the further off (0.3 at worst).</summary>
    public float Quality
    {
        get
        {
            if (Speed <= 0.01f)
                return 0f;
            float off = Speed < Spec.IdealSlow ? Spec.IdealSlow - Speed : Speed > Spec.IdealFast ? Speed - Spec.IdealFast : 0f;
            return Math.Clamp(1.3f - off * 2f, 0.3f, 1.3f);
        }
    }

    /// <summary>How fast bites come (multiplies the bite clock): 0 snagged, the play's quality while reeled, a little
    /// while it lies still, more for a falling jig; twitches and the lift after a fall add to it.</summary>
    public float BitePace
    {
        get
        {
            if (Snagged)
                return 0f;
            float p = Speed > 0.01f ? Quality
                : Falling ? (Family == LureFamily.Softbait ? 1.1f : 0.45f)
                : Spec.StillPace;
            if (_liftBonus > 0f)
                p *= 1.5f;
            return p * (1f + Flash);
        }
    }

    /// <summary>The rod jerked against a snag: it comes free a third of the time, and now and then the line gives.</summary>
    public SnagResult Jerk(IRng rng)
    {
        if (!Snagged)
            return SnagResult.Freed;
        float roll = rng.NextFloat();
        if (roll < 0.35f)
        {
            Free();
            return SnagResult.Freed;
        }
        return roll > 0.92f ? SnagResult.Broke : SnagResult.Stuck;
    }

    /// <summary>The line held tight against a snag (reeling): it may pull free, but after four seconds the line gives.</summary>
    public SnagResult Pull(IRng rng, float dt)
    {
        if (!Snagged)
            return SnagResult.Freed;
        _pulled += dt;
        if (rng.NextFloat() < 0.12f * dt)
        {
            Free();
            return SnagResult.Freed;
        }
        return _pulled >= 4f ? SnagResult.Broke : SnagResult.Stuck;
    }

    private void Free()
    {
        Snagged = false;
        OnBottom = false;
        Depth = MathF.Max(0.02f, Depth - 0.2f);
        _pulled = 0f;
    }
}

/// <summary>
/// Groundbait on the bottom (pure): every feeder emptied on the bottom adds to a baited spot (spots closer than
/// <see cref="Radius"/> merge), the fish gather there and bites come faster on a bait lying in it; the spots fade
/// over a couple of game hours as the fish eat them up.
/// </summary>
public sealed class GroundbaitSpots
{
    public const float Radius = 5f;
    public const float MaxStrength = 3f;
    public const int MaxSpots = 6;

    /// <summary>Game hours for a spot to fade to about a third.</summary>
    public const float FadeHours = 1.5f;

    public readonly record struct Spot(float X, float Z, float Strength);

    private readonly List<Spot> _spots = new();

    public IReadOnlyList<Spot> Spots => _spots;

    /// <summary>A feeder of groundbait emptied at (x, z).</summary>
    public void Feed(float x, float z, float amount = 1f)
    {
        int nearest = -1;
        float best = Radius;
        for (int i = 0; i < _spots.Count; i++)
        {
            float d = MathF.Sqrt((_spots[i].X - x) * (_spots[i].X - x) + (_spots[i].Z - z) * (_spots[i].Z - z));
            if (d < best)
            {
                best = d;
                nearest = i;
            }
        }
        if (nearest >= 0)
        {
            var s = _spots[nearest];
            float total = s.Strength + amount;
            // The spot drifts towards where the new groundbait fell, by its share.
            _spots[nearest] = new Spot(s.X + (x - s.X) * amount / total, s.Z + (z - s.Z) * amount / total, MathF.Min(total, MaxStrength));
            return;
        }
        if (_spots.Count >= MaxSpots)
        {
            int weakest = 0;
            for (int i = 1; i < _spots.Count; i++)
                if (_spots[i].Strength < _spots[weakest].Strength)
                    weakest = i;
            _spots.RemoveAt(weakest);
        }
        _spots.Add(new Spot(x, z, MathF.Min(amount, MaxStrength)));
    }

    /// <summary>Game time passes: the groundbait is eaten and washed away.</summary>
    public void Decay(float hours)
    {
        if (hours <= 0f)
            return;
        float k = MathF.Exp(-hours / FadeHours);
        for (int i = _spots.Count - 1; i >= 0; i--)
        {
            var s = _spots[i];
            if (s.Strength * k < 0.05f)
                _spots.RemoveAt(i);
            else
                _spots[i] = s with { Strength = s.Strength * k };
        }
    }

    /// <summary>How much groundbait works at a point: full at a spot's middle, nothing beyond its radius.</summary>
    public float HeatAt(float x, float z)
    {
        float heat = 0f;
        foreach (var s in _spots)
        {
            float d = MathF.Sqrt((s.X - x) * (s.X - x) + (s.Z - z) * (s.Z - z));
            float f = MathF.Max(0f, 1f - d / Radius);
            heat += s.Strength * f * f;
        }
        return heat;
    }

    /// <summary>The bite clock's speed-up on a baited spot (1 = none, up to 2.75x).</summary>
    public static float PaceFor(float heat) => 1f + 0.7f * Math.Clamp(heat, 0f, 2.5f);

    public void Clear() => _spots.Clear();
}
