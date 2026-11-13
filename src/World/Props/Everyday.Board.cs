using System;
using Godot;

namespace FishingSim.World.Props;

public static partial class Everyday
{
    private static StandardMaterial3D? _paper;

    /// <summary>Parchment with lines of handwriting, a heading and a signature (one picture for every note).</summary>
    private static StandardMaterial3D Paper()
    {
        if (_paper != null)
            return _paper;
        const int w = 128, h = 160;
        var img = Image.CreateEmpty(w, h, true, Image.Format.Rgba8);
        var rng = new RandomNumberGenerator { Seed = 314 };
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
            {
                float n = rng.Randf() * 0.05f;
                float edge = MathF.Min(MathF.Min(x, w - 1 - x), MathF.Min(y, h - 1 - y)) < 4 ? 0.08f : 0f;
                img.SetPixel(x, y, new Color(0.93f - n - edge, 0.88f - n - edge, 0.74f - n - edge * 1.4f));
            }
        var ink = new Color(0.2f, 0.16f, 0.12f);
        void Stroke(int y, int x0, int x1, int thick)
        {
            int x = x0;
            while (x < x1)
            {
                int word = rng.RandiRange(6, 18);
                for (int i = 0; i < word && x + i < x1; i++)
                    for (int t = 0; t < thick; t++)
                        img.SetPixel(x + i, y + t + (int)(MathF.Sin((x + i) * 0.9f) * 1.2f), ink);
                x += word + rng.RandiRange(4, 7);
            }
        }
        Stroke(18, 30, 98, 3);
        for (int y = 40; y < 124; y += 11)
            Stroke(y, 14, y > 110 ? 70 : 114, 1);
        Stroke(140, 70, 112, 2);
        img.GenerateMipmaps();
        return _paper = BuildingFactory.Pixel(new StandardMaterial3D
        {
            AlbedoTexture = ImageTexture.CreateFromImage(img), Roughness = 0.95f,
            CullMode = BaseMaterial3D.CullModeEnum.Disabled,
        });
    }

    /// <summary>
    /// The village notice board: a board of planks on two posts under a little gable roof, the villagers' notes pinned
    /// on it with red pins (front +Z).
    /// </summary>
    public static Node3D NoticeBoard()
    {
        var root = new Node3D { Name = "NoticeBoard" };
        foreach (float x in new[] { -0.9f, 0.9f })
            Part(root, Shapes.RoundedBox(new Vector3(0.12f, 2.35f, 0.12f), 0.02f), CampProps.DarkWood, new Vector3(x, 1.175f, 0f));
        // The board: planks with dark gaps between them, framed.
        for (int i = 0; i < 5; i++)
            Part(root, Shapes.RoundedBox(new Vector3(1.66f, 0.2f, 0.04f), 0.01f), CampProps.Wood, new Vector3(0f, 0.95f + i * 0.205f, 0.02f));
        Part(root, Shapes.RoundedBox(new Vector3(1.8f, 0.07f, 0.08f), 0.015f), CampProps.DarkWood, new Vector3(0f, 0.82f, 0.03f));
        Part(root, Shapes.RoundedBox(new Vector3(1.8f, 0.07f, 0.08f), 0.015f), CampProps.DarkWood, new Vector3(0f, 1.98f, 0.03f));
        // The roof: two thick boards meeting over the posts.
        foreach (float sz in new[] { -1f, 1f })
            Part(root, Shapes.RoundedBox(new Vector3(2.15f, 0.05f, 0.42f), 0.012f), CampProps.DarkWood,
                new Vector3(0f, 2.42f, sz * 0.17f), new Vector3(sz * 28f, 0f, 0f));
        // The notes, a little askew, each with a pin.
        var paper = Paper();
        var pin = new StandardMaterial3D { AlbedoColor = new Color(0.75f, 0.12f, 0.1f), Roughness = 0.4f };
        var notes = new (float X, float Y, float W, float H, float Tilt)[]
        {
            (-0.52f, 1.5f, 0.34f, 0.44f, -4f), (0f, 1.52f, 0.36f, 0.46f, 2f), (0.5f, 1.46f, 0.32f, 0.42f, 5f),
            (-0.3f, 1.06f, 0.28f, 0.22f, 7f), (0.38f, 1.08f, 0.3f, 0.24f, -6f),
        };
        foreach (var (x, y, w, h, tilt) in notes)
        {
            Part(root, new QuadMesh { Size = new Vector2(w, h) }, paper, new Vector3(x, y, 0.045f), new Vector3(0f, 0f, tilt));
            Part(root, new SphereMesh { Radius = 0.012f, Height = 0.024f, RadialSegments = 8, Rings = 4 }, pin,
                new Vector3(x, y + h * 0.42f, 0.05f));
        }
        return root;
    }
}
