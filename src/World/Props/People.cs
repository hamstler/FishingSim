using System;
using System.Collections.Generic;
using Godot;
using FishingSim.Data;

namespace FishingSim.World.Props;

/// <summary>Who a figure is: decides the clothes, the hat and what the figure holds.</summary>
public enum Role
{
    TackleSeller, BaitSeller, Fishmonger, MarketTrader, Shopkeeper, GuildMaster, Coachman,
    Fisherman, Farmer, Woman, Elder, Hunter, Camper,
}

public enum HatKind { None, Cap, Straw, Souwester, Fur, Kerchief, TopHat, Feathered }

/// <summary>How a figure looks: clothes, hair, hat, height.</summary>
public sealed record Look(Color Coat, Color Trousers, Color Skin, Color Hair, HatKind Hat, Color HatColor, Color? Apron, bool Beard,
    bool Dress, float Height, Color? Scarf = null, Color? Boots = null);

/// <summary>
/// The village's people: shopkeepers standing at their shops (one per shop, so a shop has a face) and villagers about
/// the place. Built from rounded primitives in woven cloth (the canvas texture with its normal map, tinted), they
/// breathe, sway a little, now and then shift their weight, and turn their heads to watch the angler come near.
/// </summary>
public static class People
{
    public static Look LookFor(Role role, int variant)
    {
        var rng = new RandomNumberGenerator { Seed = (ulong)(variant * 7919 + (int)role * 104729 + 17) };
        Color[] skins = { new(0.93f, 0.76f, 0.64f), new(0.86f, 0.66f, 0.52f), new(0.96f, 0.82f, 0.72f), new(0.74f, 0.54f, 0.4f) };
        Color[] hairs = { new(0.25f, 0.17f, 0.11f), new(0.55f, 0.4f, 0.22f), new(0.82f, 0.68f, 0.42f), new(0.12f, 0.1f, 0.09f), new(0.6f, 0.58f, 0.55f) };
        var skin = skins[rng.RandiRange(0, skins.Length - 1)];
        var hair = hairs[rng.RandiRange(0, hairs.Length - 1)];
        float height = rng.RandfRange(1.66f, 1.88f);
        return role switch
        {
            Role.TackleSeller => new Look(new Color(0.3f, 0.36f, 0.24f), new Color(0.28f, 0.24f, 0.2f), skin, hair, HatKind.Cap, new Color(0.24f, 0.26f, 0.2f), new Color(0.24f, 0.4f, 0.22f), true, false, height),
            Role.BaitSeller => new Look(new Color(0.6f, 0.42f, 0.3f), new Color(0.35f, 0.3f, 0.26f), skin, hair, HatKind.Kerchief, new Color(0.7f, 0.2f, 0.18f), new Color(0.45f, 0.32f, 0.22f), false, true, height - 0.1f),
            Role.Fishmonger => new Look(new Color(0.5f, 0.56f, 0.62f), new Color(0.22f, 0.24f, 0.28f), skin, hair, HatKind.Cap, new Color(0.9f, 0.9f, 0.88f), new Color(0.92f, 0.92f, 0.9f), rng.Randf() < 0.5f, false, height, null, new Color(0.12f, 0.14f, 0.12f)),
            Role.MarketTrader => new Look(new Color(0.72f, 0.58f, 0.36f), new Color(0.3f, 0.26f, 0.22f), skin, hair, HatKind.Straw, new Color(0.86f, 0.74f, 0.45f), new Color(0.62f, 0.2f, 0.16f), rng.Randf() < 0.4f, rng.Randf() < 0.5f, height, new Color(0.7f, 0.18f, 0.16f)),
            Role.Shopkeeper => new Look(new Color(0.42f, 0.4f, 0.38f), new Color(0.24f, 0.22f, 0.2f), skin, hair, HatKind.None, default, new Color(0.7f, 0.66f, 0.56f), rng.Randf() < 0.6f, false, height),
            Role.GuildMaster => new Look(new Color(0.18f, 0.26f, 0.48f), new Color(0.16f, 0.16f, 0.18f), skin, new Color(0.6f, 0.58f, 0.55f), HatKind.Feathered, new Color(0.15f, 0.15f, 0.18f), null, true, false, height + 0.04f, new Color(0.75f, 0.62f, 0.25f)),
            Role.Coachman => new Look(new Color(0.22f, 0.2f, 0.18f), new Color(0.32f, 0.28f, 0.22f), skin, hair, HatKind.TopHat, new Color(0.1f, 0.09f, 0.08f), null, true, false, height, new Color(0.55f, 0.12f, 0.1f), new Color(0.1f, 0.08f, 0.06f)),
            Role.Fisherman => new Look(new Color(0.92f, 0.72f, 0.12f), new Color(0.2f, 0.24f, 0.3f), skin, hair, HatKind.Souwester, new Color(0.92f, 0.72f, 0.12f), null, rng.Randf() < 0.7f, false, height, null, new Color(0.12f, 0.14f, 0.12f)),
            Role.Farmer => new Look(new Color(0.52f, 0.46f, 0.36f), new Color(0.3f, 0.36f, 0.5f), skin, hair, HatKind.Straw, new Color(0.85f, 0.72f, 0.42f), null, rng.Randf() < 0.5f, false, height),
            Role.Woman => new Look(new Color(0.38f, 0.28f, 0.42f).Lerp(new Color(0.2f, 0.36f, 0.42f), rng.Randf()), new Color(0.3f, 0.26f, 0.22f), skin, hair, HatKind.Kerchief,
                new Color(0.86f, 0.82f, 0.7f).Lerp(new Color(0.6f, 0.2f, 0.2f), rng.Randf() * 0.6f), new Color(0.9f, 0.88f, 0.82f), false, true, height - 0.12f),
            Role.Elder => new Look(new Color(0.34f, 0.3f, 0.26f), new Color(0.28f, 0.26f, 0.24f), skin, new Color(0.86f, 0.86f, 0.84f), HatKind.Cap, new Color(0.3f, 0.27f, 0.24f), null, true, false, height - 0.08f),
            Role.Hunter => new Look(new Color(0.32f, 0.36f, 0.26f), new Color(0.3f, 0.26f, 0.2f), skin, hair, HatKind.Fur, new Color(0.4f, 0.3f, 0.2f), null, true, false, height, new Color(0.5f, 0.15f, 0.12f), new Color(0.2f, 0.14f, 0.1f)),
            _ => new Look(new Color(0.2f, 0.4f, 0.55f).Lerp(new Color(0.7f, 0.35f, 0.15f), rng.Randf()), new Color(0.24f, 0.26f, 0.3f), skin, hair, HatKind.Cap,
                new Color(0.6f, 0.2f, 0.15f), null, rng.Randf() < 0.4f, false, height),
        };
    }

    /// <summary>
    /// A figure of the given role, feet at the origin, facing +Z, with a body the angler cannot walk through: one of
    /// the Quaternius villagers (CharacterAsset) dressed and busy as the role says, or the primitive figure without a
    /// screen.
    /// </summary>
    public static Npc Create(Role role, int variant) => Create(role, variant, null);

    /// <summary>
    /// A villager busy with something: <paramref name="idle"/> replaces the role's own (chopping wood, sitting by the
    /// fire...), <paramref name="gestures"/> its occasional gestures, and <paramref name="inHand"/> is held in the right
    /// fist (an axe, a bucket: built along +Y from the grip, the business end towards +X).
    /// </summary>
    public static Npc Create(Role role, int variant, string? idle, string[]? gestures = null, Node3D? inHand = null)
    {
        var look = LookFor(role, variant);
        var npc = new Npc { Name = role.ToString() };
        var (appearance, defaultIdle, defaultGestures) = Dress(role, variant, look);
        idle ??= defaultIdle;
        gestures ??= defaultGestures;
        if (CharacterAsset.Create(appearance, idle) is { } model)
        {
            model.Scale = Vector3.One * (look.Height / CharacterAsset.HeightOf(appearance.Model));
            npc.AddChild(model);
            npc.Rig(model, Greets(role) && !idle.StartsWith("Sitting"), idle, gestures);
            if (inHand != null && CharacterAsset.SkeletonOf(model) is { } skeleton)
                Hold(skeleton, inHand);
        }
        else
        {
            inHand?.QueueFree();
            Build(npc, look, role);
        }
        var body = new StaticBody3D { CollisionLayer = 1, Position = new Vector3(0f, look.Height * 0.5f, 0f) };
        body.AddChild(new CollisionShape3D { Shape = new CapsuleShape3D { Radius = 0.3f, Height = look.Height } });
        npc.AddChild(body);
        return npc;
    }

    /// <summary>
    /// Puts a thing in the right fist: the handle across the palm (towards the index finger), the business end the way
    /// the fingers point, worked out from the hand's rest pose (the packs' T-pose, palm down).
    /// </summary>
    private static void Hold(Skeleton3D sk, Node3D item)
    {
        int hand = sk.FindBone("hand_r"), mid = sk.FindBone("middle_01_r"), index = sk.FindBone("index_01_r"), pinky = sk.FindBone("pinky_01_r");
        if (hand < 0 || mid < 0 || index < 0 || pinky < 0)
        {
            item.QueueFree();
            return;
        }
        var rest = sk.GetBoneGlobalRest(hand);
        var inv = rest.Basis.Inverse();
        var fingers = (inv * (sk.GetBoneGlobalRest(mid).Origin - rest.Origin)).Normalized();
        var across = (inv * (sk.GetBoneGlobalRest(index).Origin - sk.GetBoneGlobalRest(pinky).Origin)).Normalized();
        var palm = (inv * Vector3.Down).Normalized();
        across = (across - fingers * across.Dot(fingers)).Normalized();
        var z = fingers.Cross(across).Normalized();
        item.Transform = new Transform3D(new Basis(fingers, across, z), fingers * 0.075f + palm * 0.03f - across * 0.08f);
        var at = new BoneAttachment3D { BoneName = "hand_r" };
        sk.AddChild(at);
        at.AddChild(item);
    }

    /// <summary>Shopkeepers and traders nod when the angler walks up to them.</summary>
    private static bool Greets(Role role) => role is Role.TackleSeller or Role.BaitSeller or Role.Fishmonger or Role.MarketTrader
        or Role.Shopkeeper or Role.GuildMaster or Role.Coachman;

    private static readonly Color[] HairColours =
    {
        new(0.3f, 0.2f, 0.13f), new(0.48f, 0.33f, 0.19f), new(0.86f, 0.72f, 0.48f), new(0.74f, 0.6f, 0.38f),
        new(0.16f, 0.13f, 0.11f), new(0.58f, 0.3f, 0.15f), new(0.62f, 0.5f, 0.32f),
    };

    private static readonly Color[] SkinTints =
    {
        new(1f, 1f, 1f), new(1.04f, 1.0f, 0.97f), new(0.96f, 0.9f, 0.85f), new(0.9f, 0.82f, 0.74f), new(1.02f, 0.96f, 0.93f),
    };

    /// <summary>
    /// What a role looks like and does: the model (peasant or ranger, man or woman), the outfit's colouring, hair and
    /// skin, the animation it stands about in and the ones it now and then plays.
    /// </summary>
    public static (Appearance Look, string Idle, string[] Gestures) Dress(Role role, int variant, Look l)
    {
        var rng = new RandomNumberGenerator { Seed = (ulong)(variant * 6151 + (int)role * 92821 + 3) };
        T Pick<T>(params T[] items) => items[rng.RandiRange(0, items.Length - 1)];
        bool woman = role switch
        {
            Role.BaitSeller or Role.Woman => true,
            Role.MarketTrader or Role.Shopkeeper or Role.Camper => variant % 2 == 1,
            Role.Fishmonger => variant % 3 == 2,
            _ => false,
        };
        bool ranger = role is Role.Hunter or Role.Coachman || role == Role.Camper && variant % 3 == 0;
        string model = (woman ? "female_" : "male_") + (ranger ? "ranger" : "peasant");
        var hairs = woman ? new[] { "Hair_Long", "Hair_Buns", "Hair_Long", "Hair_Buns", "Hair_BuzzedFemale" }
            : new[] { "Hair_SimpleParted", "Hair_Buzzed", "Hair_SimpleParted", "Hair_Long" };
        var hairColour = role == Role.Elder ? new Color(0.78f, 0.77f, 0.74f) : Pick(HairColours);
        if (role == Role.GuildMaster)
            hairColour = hairColour.Lerp(new Color(0.7f, 0.69f, 0.66f), 0.5f);
        bool beard = !woman && (role is Role.Elder or Role.GuildMaster or Role.Hunter || l.Beard);
        bool hood = ranger && (role == Role.Hunter || rng.Randf() < 0.5f);
        float shade = rng.RandfRange(0.9f, 1.04f);
        var look = new Appearance(model, rng.RandiRange(0, 1), Pick(hairs), hairColour, Pick(SkinTints), beard, hood,
            new Color(shade, shade, shade * rng.RandfRange(0.96f, 1.03f)));
        var (idle, gestures) = role switch
        {
            Role.TackleSeller => (Pick("Idle_FoldArms_Loop", "Idle_Loop"), new[] { "Interact", "PickUp_Table", "Yes" }),
            Role.BaitSeller => ("Idle_Talking_Loop", new[] { "PickUp_Table", "Interact" }),
            Role.Fishmonger => ("Idle_Loop", new[] { "PickUp_Table", "Interact", "Consume" }),
            Role.MarketTrader => (Pick("Idle_Talking_Loop", "Idle_Loop"), new[] { "PickUp_Table", "Interact", "Yes" }),
            Role.Shopkeeper => ("Idle_FoldArms_Loop", new[] { "Interact", "Consume" }),
            Role.GuildMaster => ("Idle_FoldArms_Loop", new[] { "Yes", "Idle_No_Loop" }),
            Role.Coachman => ("Idle_Loop", new[] { "Consume", "Interact" }),
            Role.Fisherman => ("Idle_Loop", new[] { "Consume", "Interact" }),
            Role.Farmer => (Pick("Farm_Watering", "Farm_Harvest", "Farm_PlantSeed"), new[] { "Idle_Loop" }),
            Role.Woman => (Pick("Idle_Talking_Loop", "Idle_Loop"), new[] { "PickUp_Table", "Consume" }),
            Role.Elder => ("Idle_Loop", new[] { "Yes", "Idle_No_Loop" }),
            Role.Hunter => ("Idle_Loop", new[] { "Crouch_Idle_Loop", "Interact" }),
            _ => (Pick("Idle_Loop", "Idle_Talking_Loop"), new[] { "Consume", "Interact" }),
        };
        return (look, idle, gestures);
    }


    private static StandardMaterial3D Skin(Color c) => BuildingFactory.Pixel(new StandardMaterial3D { AlbedoColor = c, Roughness = 0.62f });

    private static StandardMaterial3D Plain(Color c, float roughness = 0.85f) => BuildingFactory.Pixel(new StandardMaterial3D { AlbedoColor = c, Roughness = roughness });

    private static MeshInstance3D Part(Node3D parent, Mesh mesh, Material mat, Vector3 pos, Vector3? scale = null, Vector3? rotDeg = null)
    {
        var mi = new MeshInstance3D { Mesh = mesh, MaterialOverride = mat, Position = pos };
        if (scale is Vector3 s)
            mi.Scale = s;
        if (rotDeg is Vector3 r)
            mi.RotationDegrees = r;
        parent.AddChild(mi);
        return mi;
    }

    private static CapsuleMesh Capsule(float r, float h) => new() { Radius = r, Height = h, RadialSegments = 12, Rings = 4 };

    private static SphereMesh Ball(float r) => new() { Radius = r, Height = r * 2f, RadialSegments = 14, Rings = 8 };

    private static CylinderMesh Tube(float top, float bottom, float h, int seg = 14) => new() { TopRadius = top, BottomRadius = bottom, Height = h, RadialSegments = seg, Rings = 1 };

    private static void Build(Npc npc, Look l, Role role)
    {
        float k = l.Height / 1.78f;
        // The canvas texture is light; clothes a little brighter than their nominal colour read right in the shade.
        static Color Lift(Color c) => new(Math.Min(c.R * 1.3f + 0.04f, 1f), Math.Min(c.G * 1.3f + 0.04f, 1f), Math.Min(c.B * 1.3f + 0.04f, 1f));
        var coat = BuildingFactory.Cloth(Lift(l.Coat), twoSided: false);
        var trousers = BuildingFactory.Cloth(Lift(l.Trousers), twoSided: false);
        var skin = Skin(l.Skin);
        var hair = Plain(l.Hair, 0.8f);
        var boots = Plain(l.Boots ?? new Color(0.2f, 0.15f, 0.11f), 0.6f);
        var dark = Plain(new Color(0.08f, 0.07f, 0.07f), 0.4f);

        // Everything above the feet breathes and sways together.
        var bodyRoot = new Node3D { Name = "Body" };
        npc.AddChild(bodyRoot);
        npc.BodyRoot = bodyRoot;

        float hip = 0.86f * k, shoulder = 1.42f * k, neck = 1.5f * k;
        // Legs and boots (or a long dress).
        foreach (float sx in new[] { -1f, 1f })
        {
            Part(npc, new BoxMesh { Size = new Vector3(0.11f, 0.09f, 0.27f) * k }, boots, new Vector3(sx * 0.1f * k, 0.045f * k, 0.04f * k));
            if (!l.Dress)
                Part(bodyRoot, Capsule(0.075f * k, hip + 0.06f * k), trousers, new Vector3(sx * 0.1f * k, hip * 0.5f + 0.03f * k, 0f));
            else
                Part(bodyRoot, Capsule(0.06f * k, 0.4f * k), boots, new Vector3(sx * 0.09f * k, 0.22f * k, 0f));
            if (l.Boots != null && !l.Dress)
                Part(bodyRoot, Tube(0.088f * k, 0.085f * k, 0.42f * k), boots, new Vector3(sx * 0.1f * k, 0.22f * k, 0f));     // tall boots
        }
        if (l.Dress)
            Part(bodyRoot, Tube(0.19f * k, 0.32f * k, 0.78f * k, 18), coat, new Vector3(0f, hip - 0.31f * k, 0f));
        // Torso: a rounded, slightly flattened body, a belt.
        Part(bodyRoot, Capsule(0.21f * k, 0.72f * k), coat, new Vector3(0f, (hip + shoulder) * 0.5f, 0f), new Vector3(1f, 1f, 0.74f));
        // Broad shoulders: a rounded bar across the top of the body.
        Part(bodyRoot, Capsule(0.09f * k, 0.56f * k), coat, new Vector3(0f, shoulder - 0.06f * k, 0f), new Vector3(1f, 1f, 1.1f), new Vector3(0f, 0f, 90f));
        Part(bodyRoot, Tube(0.2f * k, 0.2f * k, 0.06f * k), dark, new Vector3(0f, hip + 0.05f * k, 0f), new Vector3(1f, 1f, 0.74f));
        if (l.Apron is Color apron)
            Part(bodyRoot, new BoxMesh { Size = new Vector3(0.34f, 0.62f, 0.02f) * k }, BuildingFactory.Cloth(apron), new Vector3(0f, hip - 0.12f * k, 0.15f * k), null, new Vector3(-6f, 0f, 0f));
        if (l.Scarf is Color scarf)
            Part(bodyRoot, Tube(0.1f * k, 0.13f * k, 0.09f * k), BuildingFactory.Cloth(scarf, false), new Vector3(0f, shoulder + 0.03f * k, 0.01f * k));

        // Arms hang from the shoulders, hands at the hips; each in its own pivot for the idle gestures.
        foreach (float sx in new[] { -1f, 1f })
        {
            var arm = new Node3D { Position = new Vector3(sx * 0.25f * k, shoulder - 0.04f * k, 0f), RotationDegrees = new Vector3(0f, 0f, sx * 7f) };
            bodyRoot.AddChild(arm);
            Part(arm, Capsule(0.072f * k, 0.62f * k), coat, new Vector3(0f, -0.29f * k, 0f));
            Part(arm, Ball(0.05f * k), skin, new Vector3(0f, -0.62f * k, 0.01f * k), new Vector3(0.9f, 1.15f, 0.8f));
            if (sx < 0f)
                npc.LeftArm = arm;
            else
                npc.RightArm = arm;
        }

        // Neck and head (its own pivot: it turns to look).
        Part(bodyRoot, Tube(0.05f * k, 0.055f * k, 0.12f * k), skin, new Vector3(0f, neck, 0f));
        var head = new Node3D { Name = "Head", Position = new Vector3(0f, neck + 0.06f * k, 0f) };
        bodyRoot.AddChild(head);
        npc.Head = head;
        float hr = 0.11f * k;
        Part(head, Ball(hr), skin, new Vector3(0f, hr * 1.05f, 0f), new Vector3(0.92f, 1.12f, 1f));
        Part(head, new BoxMesh { Size = new Vector3(0.035f, 0.05f, 0.05f) * k }, Skin(l.Skin * 0.93f), new Vector3(0f, hr * 0.95f, hr * 0.95f), null, new Vector3(-12f, 0f, 0f));
        var white = Plain(new Color(0.95f, 0.94f, 0.9f), 0.3f);
        Part(head, new BoxMesh { Size = new Vector3(0.045f, 0.009f, 0.012f) * k }, Plain(new Color(0.5f, 0.24f, 0.22f), 0.5f), new Vector3(0f, hr * 0.62f, hr * 0.95f));  // mouth
        foreach (float sx in new[] { -1f, 1f })
        {
            Part(head, Ball(0.019f * k), white, new Vector3(sx * 0.038f * k, hr * 1.16f, hr * 0.94f), new Vector3(1.2f, 0.85f, 0.6f));
            Part(head, Ball(0.011f * k), dark, new Vector3(sx * 0.038f * k, hr * 1.16f, hr * 1.02f));
            Part(head, Ball(0.025f * k), skin, new Vector3(sx * hr * 0.92f, hr * 1.05f, 0f), new Vector3(0.5f, 1f, 0.8f));
            Part(head, new BoxMesh { Size = new Vector3(0.04f, 0.008f, 0.01f) * k }, hair, new Vector3(sx * 0.038f * k, hr * 1.36f, hr * 0.98f));   // brows
        }
        // Hair (a cap of it, longer at the back for the women), beard.
        Part(head, Ball(hr * 1.06f), hair, new Vector3(0f, hr * 1.22f, -hr * 0.12f), new Vector3(0.96f, l.Dress ? 1f : 0.82f, l.Dress ? 1.08f : 1f));
        if (l.Beard)
            Part(head, Ball(hr * 0.55f), hair, new Vector3(0f, hr * 0.3f, hr * 0.62f), new Vector3(1.1f, 0.85f, 0.62f));

        switch (l.Hat)
        {
            case HatKind.Cap:
                Part(head, Tube(hr * 1.02f, hr * 1.06f, hr * 0.45f), Plain(l.HatColor), new Vector3(0f, hr * 1.78f, -hr * 0.05f));
                Part(head, new BoxMesh { Size = new Vector3(hr * 1.6f, hr * 0.06f, hr * 0.8f) }, Plain(l.HatColor * 0.8f), new Vector3(0f, hr * 1.6f, hr * 0.95f), null, new Vector3(8f, 0f, 0f));
                break;
            case HatKind.Straw:
                var straw = Textured(Textures.Shingles(), l.HatColor);
                Part(head, Tube(hr * 2.3f, hr * 2.3f, hr * 0.06f, 20), straw, new Vector3(0f, hr * 1.72f, 0f));
                Part(head, Tube(hr * 0.85f, hr * 1.05f, hr * 0.7f, 18), straw, new Vector3(0f, hr * 2.05f, 0f));
                Part(head, Tube(hr * 1.06f, hr * 1.06f, hr * 0.12f, 18), Plain(new Color(0.5f, 0.15f, 0.12f)), new Vector3(0f, hr * 1.8f, 0f));
                break;
            case HatKind.Souwester:
                Part(head, Ball(hr * 1.12f), Plain(l.HatColor, 0.35f), new Vector3(0f, hr * 1.4f, -hr * 0.05f), new Vector3(1f, 0.85f, 1f));
                Part(head, Tube(hr * 1.75f, hr * 1.9f, hr * 0.08f, 20), Plain(l.HatColor, 0.35f), new Vector3(0f, hr * 1.3f, -hr * 0.3f), null, new Vector3(-14f, 0f, 0f));
                break;
            case HatKind.Fur:
                Part(head, Tube(hr * 1.1f, hr * 1.15f, hr * 0.9f, 18), Plain(l.HatColor, 1f), new Vector3(0f, hr * 1.95f, 0f));
                break;
            case HatKind.Kerchief:
                Part(head, Ball(hr * 1.1f), BuildingFactory.Cloth(l.HatColor), new Vector3(0f, hr * 1.35f, -hr * 0.12f), new Vector3(1f, 0.92f, 1.05f));
                break;
            case HatKind.TopHat:
                Part(head, Tube(hr * 0.95f, hr * 0.95f, hr * 1.5f, 18), Plain(l.HatColor, 0.5f), new Vector3(0f, hr * 2.55f, 0f));
                Part(head, Tube(hr * 1.6f, hr * 1.6f, hr * 0.06f, 20), Plain(l.HatColor, 0.5f), new Vector3(0f, hr * 1.82f, 0f));
                Part(head, Tube(hr * 0.97f, hr * 0.97f, hr * 0.2f, 18), Plain(new Color(0.55f, 0.12f, 0.1f)), new Vector3(0f, hr * 1.95f, 0f));
                break;
            case HatKind.Feathered:
                Part(head, Tube(hr * 1.0f, hr * 1.08f, hr * 0.6f, 18), Plain(l.HatColor, 0.6f), new Vector3(0f, hr * 1.9f, 0f));
                Part(head, Tube(hr * 1.7f, hr * 1.7f, hr * 0.06f, 20), Plain(l.HatColor, 0.6f), new Vector3(0f, hr * 1.65f, 0f));
                Part(head, new BoxMesh { Size = new Vector3(0.015f, 0.32f, 0.06f) * k }, Plain(new Color(0.85f, 0.7f, 0.25f), 0.5f),
                    new Vector3(hr * 0.9f, hr * 2.3f, -hr * 0.2f), null, new Vector3(-30f, 0f, -20f));
                break;
        }

        // What they hold.
        var wood = Plain(new Color(0.42f, 0.3f, 0.2f), 0.7f);
        switch (role)
        {
            case Role.Fisherman or Role.Camper or Role.TackleSeller when npc.RightArm != null:
                Part(npc.RightArm, Tube(0.006f, 0.014f, 2.6f, 6), Plain(new Color(0.12f, 0.12f, 0.14f), 0.4f), new Vector3(0f, -0.3f * k, 0.9f * k), null, new Vector3(62f, 0f, 0f));
                break;
            case Role.Elder when npc.RightArm != null:
                Part(npc.RightArm, Tube(0.018f, 0.018f, 0.95f, 6), wood, new Vector3(0f, -0.95f * k, 0.12f * k), null, new Vector3(8f, 0f, 0f));
                break;
            case Role.Farmer when npc.RightArm != null:
                Part(npc.RightArm, Tube(0.02f, 0.02f, 1.5f, 6), wood, new Vector3(0f, -0.62f * k, 0.12f * k), null, new Vector3(4f, 0f, 0f));
                Part(npc.RightArm, new BoxMesh { Size = new Vector3(0.3f, 0.02f, 0.1f) }, wood, new Vector3(0f, 0.1f * k, 0.15f * k));
                break;
            case Role.Fishmonger when npc.LeftArm != null:
                Part(npc.LeftArm, Ball(0.18f), Plain(new Color(0.62f, 0.68f, 0.7f), 0.3f), new Vector3(0f, -0.66f * k, 0.12f * k), new Vector3(0.25f, 0.4f, 1f));
                break;
        }
    }

    private static StandardMaterial3D Textured(Texture2D tex, Color tint)
    {
        var m = BuildingFactory.Pixel(new StandardMaterial3D
        {
            AlbedoTexture = tex, AlbedoColor = tint, Uv1Triplanar = true, Uv1Scale = Vector3.One * 2f, Roughness = 0.9f,
        });
        if (Textures.NormalFor(tex) is { } n)
        {
            m.NormalEnabled = true;
            m.NormalTexture = n;
        }
        return m;
    }

    /// <summary>The role of the person who keeps a shop of this kind.</summary>
    public static Role KeeperOf(BuildingKind kind) => kind switch
    {
        BuildingKind.Tackle => Role.TackleSeller,
        BuildingKind.Bait => Role.BaitSeller,
        BuildingKind.FishBuyer => Role.Fishmonger,
        BuildingKind.Market => Role.MarketTrader,
        BuildingKind.Office => Role.GuildMaster,
        BuildingKind.BusStop => Role.Coachman,
        BuildingKind.Smokehouse => Role.Fisherman,
        _ => Role.Shopkeeper,
    };
}

/// <summary>A standing figure: breathes, sways, shifts its weight now and then, and watches the angler when near.</summary>
public partial class Npc : Node3D
{
    public Node3D? BodyRoot { get; set; }
    public Node3D? Head { get; set; }
    public Node3D? LeftArm { get; set; }
    public Node3D? RightArm { get; set; }

    private float _t;
    private readonly float _phase = GD.Randf() * 50f;
    private float _headYaw;

    private AnimationPlayer? _animator;
    private HeadLook? _look;
    private bool _greets, _greeted;
    private float _check, _nextGesture = 8f + GD.Randf() * 20f, _distance = 999f;

    private string _idle = "Idle_Loop";
    private string[] _gestures = Array.Empty<string>();

    /// <summary>Takes a rigged villager (CharacterAsset): its animations play, its head follows the angler.</summary>
    public void Rig(Node3D model, bool greets, string idle, string[] gestures)
    {
        _animator = CharacterAsset.PlayerOf(model);
        _greets = greets;
        _idle = idle;
        _gestures = gestures;
        if (CharacterAsset.SkeletonOf(model) is { } skeleton)
        {
            // Busy hands keep their eyes on the work.
            _look = new HeadLook { Wander = idle.StartsWith("Farm_") ? 0f : 1f };
            skeleton.AddChild(_look);
        }
        if (_animator != null)
            _animator.AnimationFinished += name =>
            {
                // Work and gestures play once; then back to standing about (work starts over after a breather).
                if (name != _idle)
                    _animator.Play(_idle, 0.4);
                else if (!_idle.EndsWith("_Loop"))
                {
                    _animator.Play(_gestures.Length > 0 ? _gestures[0] : "Idle_Loop", 0.4);
                }
            };
    }

    private void Gesture(string name)
    {
        if (_animator == null || !_animator.HasAnimation(name))
            return;
        _animator.Play(name, 0.35);
        // A looping gesture (folding arms, shaking the head) is cut short after a few seconds.
        if (name.EndsWith("_Loop"))
            GetTree().CreateTimer(3.5f + GD.Randf() * 2f).Timeout += () =>
            {
                if (IsInstanceValid(this) && _animator.CurrentAnimation == name)
                    _animator.Play(_idle, 0.5);
            };
    }

    private void ProcessRigged(float dt)
    {
        _check -= dt;
        if (_check <= 0f)
        {
            // Far away villagers stand still (no animation, no head turning): nobody sees the difference.
            _check = 0.4f;
            var cam = GetViewport()?.GetCamera3D();
            _distance = cam == null ? 999f : GlobalPosition.DistanceTo(cam.GlobalPosition);
            // How far they move and how far they are drawn follow the view distance setting.
            float view = Graphics.GraphicsQuality.ViewFactor;
            bool near = _distance < 70f * view;
            if (_animator!.Active != near)
                _animator.Active = near;
            bool shown = _distance < 150f * view;
            if (Visible != shown)
                Visible = shown;
            if (_look != null)
                _look.Active = _distance < 25f;
            // Shopkeepers nod once as the angler walks up in front of them.
            if (cam != null && _greets && !_greeted && _distance < 6.5f && ToLocal(cam.GlobalPosition).Z > 0.5f)
            {
                _greeted = true;
                Gesture("Yes");
                _nextGesture = 12f + GD.Randf() * 15f;
            }
            else if (_distance > 12f)
                _greeted = false;
        }
        // Now and then they busy themselves with something.
        _nextGesture -= dt;
        if (_nextGesture <= 0f)
        {
            _nextGesture = 14f + GD.Randf() * 26f;
            if (_distance < 40f && _gestures.Length > 0 && _animator!.CurrentAnimation == _idle && _idle.EndsWith("_Loop"))
                Gesture(_gestures[(int)(GD.Randi() % (uint)_gestures.Length)]);
        }
    }

    public override void _Process(double delta)
    {
        using var perfScope = Graphics.Perf.Scope("villagers");
        float dt = (float)delta;
        if (_animator != null)
        {
            ProcessRigged(dt);
            return;
        }
        if (BodyRoot == null)
            return;
        _t += dt;
        float t = _t + _phase;
        // Breathing and a slow sway; every ~9 s the weight moves to the other foot.
        BodyRoot.Scale = new Vector3(1f, 1f + 0.008f * MathF.Sin(t * 1.6f), 1f + 0.012f * MathF.Sin(t * 1.6f));
        float shift = MathF.Sin(t * 0.7f) * 0.6f + MathF.Sin(t * 0.23f) * 0.4f;
        BodyRoot.RotationDegrees = new Vector3(0f, 0f, shift * 1.4f);
        if (LeftArm != null)
            LeftArm.RotationDegrees = new Vector3(MathF.Sin(t * 0.9f) * 4f, 0f, -7f - shift);
        if (RightArm != null)
            RightArm.RotationDegrees = new Vector3(MathF.Sin(t * 0.8f + 1f) * 4f, 0f, 7f - shift);
        if (Head == null)
            return;
        // Watch the angler within ten metres, otherwise look about.
        float want = MathF.Sin(t * 0.3f) * 25f;
        var cam = GetViewport()?.GetCamera3D();
        if (cam != null)
        {
            var local = ToLocal(cam.GlobalPosition);
            float dist = new Vector2(local.X, local.Z).Length();
            if (dist < 10f && local.Z > -2f)
                want = Math.Clamp(Mathf.RadToDeg(MathF.Atan2(local.X, local.Z)), -70f, 70f);
        }
        _headYaw += (want - _headYaw) * MathF.Min(dt * 3f, 1f);
        Head.RotationDegrees = new Vector3(0f, _headYaw, 0f);
    }
}
