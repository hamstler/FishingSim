using System;
using System.Collections.Generic;
using Godot;
using FishingSim.Core.Camp;
using FishingSim.Graphics;

namespace FishingSim.World.Props;

/// <summary>
/// A campfire to cook at: a ring of stones with split wood, a tripod with an iron pot over it and two forked sticks
/// with a spit for roasting fish. Lit (E, the cooking panel) it burns for a few game hours: crossed quads of
/// shaders/flame.gdshader, sparks and smoke (CPU particles), a warm flickering light, glowing embers; cold, it is ash
/// and charred wood. While a dish cooks the fish turn on the spit over the flames or the soup bubbles and steams in the
/// pot. Every campfire is in <see cref="All"/> (the sound's crackle follows the nearest burning one).
/// </summary>
public partial class Campfire : Node3D
{
    public const float BurnHours = 3f;

    public static readonly List<Campfire> All = new();

    public string Title { get; set; } = "Ognisko";

    /// <summary>The angler's own camp (the pot shows only once one is bought) or a village hearth (always has one).</summary>
    public bool PlayerCamp { get; set; }

    public float BurnHoursLeft { get; set; }

    public bool Lit => BurnHoursLeft > 0f;

    /// <summary>The dish on the fire now, or null.</summary>
    public Recipe? Cooking { get; private set; }

    /// <summary>0..1 how done the dish is.</summary>
    public float CookProgress { get; private set; }

    /// <summary>Raised once when the dish is ready.</summary>
    public event Action<Campfire, Recipe>? Cooked;

    private Node3D _flames = null!, _pot = null!, _spit = null!, _roast = null!;
    private MeshInstance3D _soup = null!, _embers = null!;
    private CpuParticles3D? _sparks, _smoke, _steam;
    private OmniLight3D? _light;
    private float _strength, _time;
    private readonly float _seed = GD.Randf() * 10f;

    public override void _EnterTree() => All.Add(this);

    public override void _ExitTree() => All.Remove(this);

    public override void _Ready()
    {
        // Stones: the photographed fire pit when present, else a ring of rounded stones.
        if (PropAsset.Create("stone_fire_pit", height: 0.32f) is { } pit)
            AddChild(pit);
        else
            for (int i = 0; i < 9; i++)
            {
                float a = i * Mathf.Tau / 9f;
                AddChild(new MeshInstance3D
                {
                    Mesh = new SphereMesh { Radius = 0.13f, Height = 0.18f, RadialSegments = 10, Rings = 5 },
                    Position = new Vector3(MathF.Cos(a) * 0.48f, 0.06f, MathF.Sin(a) * 0.48f),
                    MaterialOverride = TreeFactory.RockMaterial(),
                });
            }
        // Split wood laid crosswise, and the bed of embers under it.
        var st = new SurfaceTool();
        st.Begin(Mesh.PrimitiveType.Triangles);
        for (int i = 0; i < 4; i++)
        {
            float a = i * Mathf.Pi / 4f + 0.3f;
            Shapes.Log(st, new Vector3(0f, 0.09f + (i % 2) * 0.05f, 0f), new Vector3(MathF.Cos(a), 0.12f, MathF.Sin(a)), 0.55f, 0.045f, i + 3);
        }
        st.GenerateNormals();
        AddChild(new MeshInstance3D { Mesh = st.Commit(), MaterialOverride = Charred });
        _embers = new MeshInstance3D
        {
            Mesh = new SphereMesh { Radius = 0.26f, Height = 0.08f, RadialSegments = 16, Rings = 4 },
            Position = new Vector3(0f, 0.03f, 0f), MaterialOverride = EmberMaterial(),
        };
        AddChild(_embers);

        // The tripod over the fire with the pot on its hook; the spit for roasting on two forked sticks.
        var tripod = CampProps.Tripod(1.3f);
        tripod.Position = new Vector3(0f, 1.3f, 0f);
        AddChild(tripod);
        _pot = CampProps.Pot(out _soup);
        _pot.Position = new Vector3(0f, 1.32f, 0f);
        _pot.Visible = !PlayerCamp;
        AddChild(_pot);
        _spit = new Node3D { Name = "Spit" };
        foreach (float x in new[] { -0.55f, 0.55f })
        {
            _spit.AddChild(new MeshInstance3D
            {
                Mesh = new CylinderMesh { TopRadius = 0.015f, BottomRadius = 0.02f, Height = 0.62f, RadialSegments = 6 },
                Position = new Vector3(x, 0.31f, 0.5f), MaterialOverride = CampProps.DarkWood,
            });
            _spit.AddChild(new MeshInstance3D
            {
                Mesh = new CylinderMesh { TopRadius = 0.012f, BottomRadius = 0.014f, Height = 0.14f, RadialSegments = 6 },
                Position = new Vector3(x + 0.03f, 0.64f, 0.5f), RotationDegrees = new Vector3(0f, 0f, -30f), MaterialOverride = CampProps.DarkWood,
            });
        }
        _roast = new Node3D { Position = new Vector3(0f, 0.6f, 0.5f), Visible = false };
        _roast.AddChild(new MeshInstance3D
        {
            Mesh = new CylinderMesh { TopRadius = 0.008f, BottomRadius = 0.008f, Height = 1.3f, RadialSegments = 6 },
            RotationDegrees = new Vector3(0f, 0f, 90f), MaterialOverride = CampProps.Wood,
        });
        _spit.AddChild(_roast);
        AddChild(_spit);

        if (Renderer.IsHeadless)
            return;
        _flames = new Node3D { Position = new Vector3(0f, 0.08f, 0f) };
        var flame = new ShaderMaterial { Shader = GD.Load<Shader>("res://shaders/flame.gdshader") };
        flame.SetShaderParameter("noise_tex", ProceduralTextures.Noise(23, 0.05f, 256));
        flame.SetShaderParameter("seed", _seed);
        for (int i = 0; i < 3; i++)
            _flames.AddChild(new MeshInstance3D
            {
                Mesh = new QuadMesh { Size = new Vector2(0.62f, 0.95f), CenterOffset = new Vector3(0f, 0.47f, 0f) },
                RotationDegrees = new Vector3(0f, i * 60f, 0f), MaterialOverride = flame,
                CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
            });
        AddChild(_flames);
        _sparks = new CpuParticles3D
        {
            Amount = 24, Lifetime = 1.6f, Position = new Vector3(0f, 0.3f, 0f), Direction = Vector3.Up, Spread = 18f,
            InitialVelocityMin = 0.8f, InitialVelocityMax = 2.2f, Gravity = new Vector3(0f, 0.4f, 0f),
            ScaleAmountMin = 0.6f, ScaleAmountMax = 1.2f, EmissionShape = CpuParticles3D.EmissionShapeEnum.Sphere, EmissionSphereRadius = 0.18f,
            Mesh = new QuadMesh { Size = new Vector2(0.018f, 0.018f), Material = Glow(new Color(1f, 0.62f, 0.2f)) },
        };
        AddChild(_sparks);
        _smoke = new CpuParticles3D
        {
            Amount = 18, Lifetime = 5f, Position = new Vector3(0f, 0.9f, 0f), Direction = Vector3.Up, Spread = 10f,
            InitialVelocityMin = 0.35f, InitialVelocityMax = 0.6f, Gravity = new Vector3(0.12f, 0.05f, 0f),
            ScaleAmountMin = 0.4f, ScaleAmountMax = 0.7f, ScaleAmountCurve = Grow(), ColorRamp = Fade(new Color(0.3f, 0.3f, 0.31f, 0.16f)),
            Mesh = new QuadMesh { Size = new Vector2(0.5f, 0.5f), Material = SmokeMaterial() },
        };
        AddChild(_smoke);
        _steam = new CpuParticles3D
        {
            Amount = 12, Lifetime = 2.2f, Position = new Vector3(0f, 1.05f, 0f), Direction = Vector3.Up, Spread = 15f,
            InitialVelocityMin = 0.25f, InitialVelocityMax = 0.45f, Gravity = Vector3.Zero, Emitting = false,
            ScaleAmountMin = 0.3f, ScaleAmountMax = 0.5f, ScaleAmountCurve = Grow(), ColorRamp = Fade(new Color(0.95f, 0.95f, 0.95f, 0.35f)),
            Mesh = new QuadMesh { Size = new Vector2(0.35f, 0.35f), Material = SmokeMaterial() },
        };
        AddChild(_steam);
        _light = new OmniLight3D
        {
            Position = new Vector3(0f, 0.6f, 0f), LightColor = new Color(1f, 0.6f, 0.28f), OmniRange = 9f, OmniAttenuation = 1.6f,
            ShadowEnabled = true, LightEnergy = 0f,
        };
        AddChild(_light);
        _strength = Lit ? 1f : 0f;
        Apply();
    }

    private static Material Charred => _charred ??= BuildingFactory.Pixel(new StandardMaterial3D { AlbedoColor = new Color(0.16f, 0.12f, 0.1f), Roughness = 0.95f });

    private static Material? _charred;

    private StandardMaterial3D EmberMaterial() => BuildingFactory.Pixel(new StandardMaterial3D
    {
        AlbedoColor = new Color(0.12f, 0.1f, 0.09f), Roughness = 1f, EmissionEnabled = true, Emission = new Color(1f, 0.32f, 0.08f),
        EmissionEnergyMultiplier = 0f,
    });

    private static StandardMaterial3D Glow(Color c) => new()
    {
        ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded, AlbedoColor = c, BlendMode = BaseMaterial3D.BlendModeEnum.Add,
        BillboardMode = BaseMaterial3D.BillboardModeEnum.Particles, VertexColorUseAsAlbedo = true,
    };

    private static StandardMaterial3D? _smokeMaterial;

    /// <summary>A soft round puff (radial gradient), billboarded, tinted by the particle colour ramp.</summary>
    private static StandardMaterial3D SmokeMaterial()
    {
        if (_smokeMaterial != null)
            return _smokeMaterial;
        var g = new Gradient();
        g.SetColor(0, new Color(1f, 1f, 1f, 1f));
        g.SetColor(1, new Color(1f, 1f, 1f, 0f));
        var tex = new GradientTexture2D { Gradient = g, Fill = GradientTexture2D.FillEnum.Radial, FillFrom = new Vector2(0.5f, 0.5f), FillTo = new Vector2(0.5f, 0f), Width = 64, Height = 64 };
        return _smokeMaterial = new StandardMaterial3D
        {
            AlbedoTexture = tex, Transparency = BaseMaterial3D.TransparencyEnum.Alpha, ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
            BillboardMode = BaseMaterial3D.BillboardModeEnum.Particles, VertexColorUseAsAlbedo = true, CullMode = BaseMaterial3D.CullModeEnum.Disabled,
        };
    }

    private static Curve Grow()
    {
        var c = new Curve();
        c.AddPoint(new Vector2(0f, 0.4f));
        c.AddPoint(new Vector2(1f, 1.6f));
        return c;
    }

    private static Gradient Fade(Color c)
    {
        var g = new Gradient();
        g.SetColor(0, c with { A = 0f });
        g.SetColor(1, c with { A = 0f });
        g.AddPoint(0.15f, c);
        return g;
    }

    /// <summary>Lights the fire (it burns for <see cref="BurnHours"/> game hours).</summary>
    public void Light()
    {
        BurnHoursLeft = BurnHours;
    }

    /// <summary>The time passes for the fire: it burns down.</summary>
    public void PassTime(float hours)
    {
        if (Cooking == null)
            BurnHoursLeft = MathF.Max(0f, BurnHoursLeft - hours);
    }

    /// <summary>Puts a dish on the fire: the fish on the spit (models of the species used) or the soup in the pot.</summary>
    public bool StartCooking(Recipe recipe, IReadOnlyList<Node3D> fishModels)
    {
        if (!Lit || Cooking != null)
            return false;
        Cooking = recipe;
        CookProgress = 0f;
        foreach (var c in _roast.GetChildren())
            if (c is Fishing.FishModel)
                c.QueueFree();
        if (!recipe.NeedsPot)
        {
            float x = -0.25f;
            foreach (var fish in fishModels)
            {
                fish.Position = new Vector3(x, 0f, 0f);
                fish.RotationDegrees = new Vector3(0f, 0f, 0f);
                _roast.AddChild(fish);
                x += 0.3f;
            }
            _roast.Visible = true;
        }
        else
        {
            _pot.Visible = true;
            _soup.Visible = true;
            foreach (var fish in fishModels)
                fish.QueueFree();
        }
        return true;
    }

    public void ShowPot(bool owned) => _pot.Visible = owned || !PlayerCamp;

    public override void _Process(double delta)
    {
        float dt = (float)delta;
        _time += dt;
        if (Cooking is { } dish)
        {
            CookProgress = MathF.Min(1f, CookProgress + dt / dish.Seconds);
            // The spit turns slowly; the fish brown as they cook.
            _roast.Rotation = new Vector3(_time * 0.8f, 0f, 0f);
            if (CookProgress >= 1f)
            {
                Cooking = null;
                _roast.Visible = false;
                _soup.Visible = false;
                if (_steam != null)
                    _steam.Emitting = false;
                Cooked?.Invoke(this, dish);
            }
            else if (_steam != null)
                _steam.Emitting = dish.NeedsPot && CookProgress > 0.2f;
        }
        _strength = Mathf.MoveToward(_strength, Lit ? 1f : 0f, dt * (Lit ? 0.6f : 0.15f));
        Apply();
    }

    private void Apply()
    {
        if (_embers.MaterialOverride is StandardMaterial3D em)
            em.EmissionEnergyMultiplier = 0.4f + _strength * 2.4f * (0.85f + 0.15f * MathF.Sin(_time * 3.1f + _seed));
        if (_flames == null)
            return;
        float flicker = 0.85f + 0.1f * MathF.Sin(_time * 13f + _seed) + 0.05f * MathF.Sin(_time * 31f);
        _flames.Visible = _strength > 0.02f;
        _flames.Scale = new Vector3(1f, 0.3f + 0.7f * _strength, 1f);
        foreach (var n in _flames.GetChildren())
            if (n is MeshInstance3D { MaterialOverride: ShaderMaterial m })
                m.SetShaderParameter("strength", _strength * flicker);
        if (_light != null)
        {
            _light.LightEnergy = _strength * 2.2f * flicker;
            _light.Visible = _strength > 0.02f;
        }
        if (_sparks != null)
            _sparks.Emitting = _strength > 0.5f;
        if (_smoke != null)
            _smoke.Emitting = _strength > 0.05f;
    }
}
