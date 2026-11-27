using System.Collections;
using System.Threading.Tasks;
using UnityEngine;

namespace FishingSim.Game
{
    public sealed partial class GameRoot
    {
        internal static bool ShowMapForShot;
        private Texture2D? _lakeChart;
        private const int ChartWidth = (int)(Lake.XMax - Lake.XMin), ChartHeight = (int)(Lake.ZMax - Lake.ZMin);
        private static readonly Color32 DeckColour = new(204, 170, 116, 255), HouseColour = new(215, 193, 157, 255);

        private IEnumerator BakeLakeChart(Lake lake)
        {
            var pixels = new Color32[ChartWidth * ChartHeight];
            var paint = Task.Run(() => Parallel.For(0, ChartHeight, row =>
            {
                float z = Lake.ZMin + row + .5f;
                for (int col = 0; col < ChartWidth; col++)
                {
                    float x = Lake.XMin + col + .5f;
                    float height = lake.HeightAt(x, z);
                    Color tint;
                    if (Lake.OnPier(x, z) || lake.TryBridgeDeck(x, z, out _)) tint = DeckColour;
                    else if (height < 0f)
                    {
                        float depth = -height;
                        tint = Color.Lerp(new Color(.31f, .62f, .60f), new Color(.055f, .23f, .32f),
                            depth / lake.Location.MaxDepth);
                        if (depth > 1f && depth % 2f < .13f) tint *= .85f;
                    }
                    else if (lake.PathDistance(x, z) < 1.35f) tint = new Color(.65f, .57f, .42f);
                    else if (height < .7f) tint = new Color(.72f, .70f, .49f);
                    else tint = Color.Lerp(new Color(.43f, .50f, .34f), new Color(.19f, .31f, .23f),
                        .42f + lake.ForestDensity(x, z) * .85f);
                    tint.a = 1f;
                    pixels[row * ChartWidth + col] = tint;
                }
            }));
            while (!paint.IsCompleted) yield return null;
            paint.GetAwaiter().GetResult();
            if (_lakeChart != null) Destroy(_lakeChart);
            _lakeChart = new Texture2D(ChartWidth, ChartHeight, TextureFormat.RGBA32, false)
                { name = "Mapa łowiska", wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear };
            _lakeChart.SetPixels32(pixels);
            _lakeChart.Apply(false, true);
        }

        private void DrawLakeChart(float w, float h)
        {
            InventoryArt();
            GUI.DrawTexture(new Rect(0, 0, w, h), _dim!);
            float pad = S(20), scale = Mathf.Min((w - 4 * pad) / ChartWidth, (h - S(200)) / ChartHeight);
            var chart = new Rect((w - ChartWidth * scale) / 2, (h - ChartHeight * scale) / 2,
                ChartWidth * scale, ChartHeight * scale);
            var panel = new Rect(chart.x - pad, chart.y - S(64), chart.width + 2 * pad, chart.height + S(132));
            GUI.Box(panel, GUIContent.none, _inventorySurface);
            GUI.Label(new Rect(chart.x, panel.y + S(12), chart.width - S(106), S(46)), _world.Lake.Location.DisplayName, _title);
            if (GUI.Button(new Rect(chart.xMax - S(105), panel.y + S(14), S(105), S(36)), "Zamknij", _button))
                ClosePanel();
            if (_lakeChart == null) return;
            GUI.DrawTexture(chart, _lakeChart);
            Vector2 Project(float x, float z) => new(chart.x + (x - Lake.XMin) * scale, chart.yMax - (z - Lake.ZMin) * scale);
            void Mark(Rect r, Color c)
            {
                var old = GUI.color;
                GUI.color = c;
                GUI.DrawTexture(r, White);
                GUI.color = old;
            }
            foreach (var building in _world.Lake.Specs)
            {
                if (!building.IsDwelling && !building.IsFunctional) continue;
                var p = Project(building.Position.X, building.Position.Y);
                Mark(new Rect(p.x - scale * 2, p.y - scale * 2, scale * 4, scale * 4), HouseColour);
            }
            var here = _player.transform.position;
            var pin = Project(here.x, here.z);
            var matrix = GUI.matrix;
            GUIUtility.RotateAroundPivot(_player.transform.eulerAngles.y, pin);
            Mark(new Rect(pin.x - S(2), pin.y - S(15), S(4), S(16)), Color.white);
            GUIUtility.RotateAroundPivot(45f, pin);
            Mark(new Rect(pin.x - S(6), pin.y - S(6), S(12), S(12)), Color.black);
            Mark(new Rect(pin.x - S(4), pin.y - S(4), S(8), S(8)), Accent);
            GUI.matrix = matrix;
            GUI.Label(new Rect(chart.x + pad, chart.y + S(8), S(110), S(32)), "↑  N", _text);
            Mark(new Rect(chart.x + pad, chart.yMax - S(13), scale * 50, S(2)), Color.white);
            GUI.Label(new Rect(chart.x + pad, chart.yMax - S(44), S(76), S(28)), "50 m", _text);
            GUI.Label(new Rect(chart.x, chart.yMax + S(8), chart.width, S(26)),
                $"Twoja pozycja   X {here.x:0} m   Z {here.z:0} m", _text);
            GUI.Label(new Rect(chart.x, chart.yMax + S(34), chart.width, S(28)),
                "Złoty znacznik · gracz     Beż · zabudowa i pomosty     Ciemna woda · głębia", _small);
        }
    }
}
