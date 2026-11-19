using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace FishingSim.Core;

/// <summary>JSON helpers (Unity port: Newtonsoft, which Unity ships; the Godot game used System.Text.Json.Nodes).</summary>
public static class JsonText
{
    /// <summary>Indented JSON text; non-ASCII characters (Polish letters) stay readable.</summary>
    public static string Pretty(JToken node) => node.ToString(Formatting.Indented);
}
