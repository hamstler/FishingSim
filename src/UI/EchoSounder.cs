using Godot;
using FishingSim.Data;
using FishingSim.Progress;
using FishingSim.World;

namespace FishingSim.UI;

/// <summary>
/// The echo sounder (accessory echosonda) while in the boat: a small screen at the left with the depth under the boat,
/// the bottom's profile scrolling past as the boat moves (newest on the right) and arcs where fish swim below
/// (within 3 m of the transducer), sampled 8 times a second.
/// </summary>
public partial class EchoSounder : Control
{
    private const int Samples = 96;
    private const float SampleSeconds = 0.125f, Beam = 3f;

    private readonly float[] _depth = new float[Samples];
    private readonly float[] _fish = new float[Samples * 3];
    private int _head;
    private float _clock;
    private bool _started;

    public Rowboat? Boat { get; set; }
    public LakeBed? Lake { get; set; }
    public FishPopulation? Fish { get; set; }
    public GearCatalog? Gear { get; set; }

    /// <summary>The screen is up (tests).</summary>
    public bool Showing => Visible;

    /// <summary>The depth under the boat at the last sample (tests).</summary>
    public float Depth => _depth[(_head + Samples - 1) % Samples];

    public override void _Ready()
    {
        MouseFilter = MouseFilterEnum.Ignore;
        CustomMinimumSize = new Vector2(300, 190);
        Visible = false;
        for (int i = 0; i < _fish.Length; i++)
            _fish[i] = -1f;
    }

    public override void _Process(double delta)
    {
        bool on = Rowboat.Aboard && Boat != null && Lake != null && Gear?.Has(AccessoryKind.EchoSounder) == true;
        if (on && !Visible)
            _started = false;
        Visible = on;
        if (!on)
            return;
        _clock += (float)delta;
        if (_clock < SampleSeconds)
            return;
        _clock = 0f;
        var p = Boat!.GlobalPosition;
        _depth[_head] = Mathf.Max(Lake!.DepthAt(p.X, p.Z), 0f);
        if (!_started)
        {
            // Switched on: the screen starts with a flat bottom at the depth under the boat.
            for (int i = 0; i < Samples; i++)
                _depth[i] = _depth[_head];
            _started = true;
        }
        // Up to three fish in the beam: their depths.
        int n = 0;
        for (int k = 0; k < 3; k++)
            _fish[_head * 3 + k] = -1f;
        if (Fish != null)
            foreach (var a in Fish.Agents)
            {
                if (n >= 3)
                    break;
                float dx = a.Position.X - p.X, dz = a.Position.Y - p.Z;
                if (dx * dx + dz * dz < Beam * Beam)
                    _fish[_head * 3 + n++] = a.Depth;
            }
        _head = (_head + 1) % Samples;
        QueueRedraw();
    }

    public override void _Draw()
    {
        var size = Size;
        DrawRect(new Rect2(Vector2.Zero, size), new Color(0.03f, 0.07f, 0.09f, 0.82f));
        DrawRect(new Rect2(Vector2.Zero, size), new Color(0.6f, 0.75f, 0.8f, 0.5f), false, 1.5f);
        var font = GetThemeDefaultFont();
        float top = 34f, h = size.Y - top - 8f, w = size.X - 16f;
        float max = 2f;
        for (int i = 0; i < Samples; i++)
            max = Mathf.Max(max, _depth[i]);
        max = Mathf.Ceil(max * 1.15f);
        // Water column, bottom profile (filled), fish arcs.
        var bottom = new Vector2[Samples + 2];
        for (int i = 0; i < Samples; i++)
        {
            int k = (_head + i) % Samples;
            float x = 8f + w * i / (Samples - 1);
            bottom[i] = new Vector2(x, top + h * _depth[k] / max);
            for (int f = 0; f < 3; f++)
            {
                float d = _fish[k * 3 + f];
                if (d >= 0f)
                    DrawArc(new Vector2(x, top + h * d / max + 3f), 4f, Mathf.Pi * 1.1f, Mathf.Pi * 1.9f, 6, new Color(1f, 0.85f, 0.25f), 2f, true);
            }
        }
        bottom[Samples] = new Vector2(8f + w, top + h);
        bottom[Samples + 1] = new Vector2(8f, top + h);
        DrawColoredPolygon(bottom, new Color(0.62f, 0.38f, 0.16f, 0.9f));
        for (int i = 0; i + 1 < Samples; i++)
            DrawLine(bottom[i], bottom[i + 1], new Color(1f, 0.6f, 0.25f), 2f, true);
        for (int m = 1; m <= 3; m++)
        {
            float y = top + h * m / 4f;
            DrawLine(new Vector2(8f, y), new Vector2(8f + w, y), new Color(1f, 1f, 1f, 0.08f));
            DrawString(font, new Vector2(10f, y - 2f), $"{max * m / 4f:0.#} m", HorizontalAlignment.Left, -1, 11, new Color(1f, 1f, 1f, 0.45f));
        }
        DrawString(font, new Vector2(10f, 24f), $"Echosonda   {Depth:0.0} m", HorizontalAlignment.Left, -1, 17, new Color(0.85f, 0.95f, 1f));
    }
}
