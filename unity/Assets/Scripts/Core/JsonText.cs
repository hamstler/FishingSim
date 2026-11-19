using System.IO;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace FishingSim.Core;

/// <summary>JSON helpers that avoid reflection-based serialization (not available in Godot's runtime config).</summary>
public static class JsonText
{
    /// <summary>Indented JSON text; non-ASCII characters (Polish letters) stay readable.</summary>
    public static string Pretty(JsonNode node)
    {
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream, new JsonWriterOptions
        {
            Indented = true,
            Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        }))
        {
            node.WriteTo(writer);
        }
        return Encoding.UTF8.GetString(stream.ToArray());
    }
}
