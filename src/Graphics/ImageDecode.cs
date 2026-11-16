using System;
using System.Text;
using System.Text.Json.Nodes;
using Godot;

namespace FishingSim.Graphics;

/// <summary>Picture decoding shared by the model loaders (fish, props).</summary>
public static class ImageDecode
{
    public static Error Webp(Image image, byte[] bytes) => image.LoadWebpFromBuffer(bytes);

    /// <summary>
    /// The .glb with no pictures, textures or materials, for GLTFDocument: it decodes every picture in full even when
    /// told to discard them (4K WebP on the main thread, failing with "Couldn't load image ... image/webp" besides),
    /// while the game decodes and compresses the pictures itself.
    /// </summary>
    public static byte[] WithoutPictures(byte[] glb, bool keepMaterialNames = false)
    {
        int jsonLength = BitConverter.ToInt32(glb, 12);
        var json = JsonNode.Parse(Encoding.UTF8.GetString(glb, 20, jsonLength))!.AsObject();
        json.Remove("images");
        json.Remove("textures");
        json.Remove("samplers");
        if (keepMaterialNames && json["materials"] is JsonArray mats)
        {
            var names = new JsonArray();
            foreach (var m in mats)
                names.Add(new JsonObject { ["name"] = (string?)m?["name"] ?? "" });
            json["materials"] = names;
        }
        else
            json.Remove("materials");
        json.Remove("extensionsUsed");
        json.Remove("extensionsRequired");
        foreach (var mesh in json["meshes"]?.AsArray() ?? new JsonArray())
            foreach (var prim in mesh?["primitives"]?.AsArray() ?? new JsonArray())
                if (!keepMaterialNames)
                    prim?.AsObject().Remove("material");
        var text = Encoding.UTF8.GetBytes(json.ToJsonString());
        int padded = (text.Length + 3) & ~3;
        int binStart = 20 + jsonLength;                  // the binary chunk's header, as it was
        int binChunk = glb.Length - binStart;
        var output = new byte[12 + 8 + padded + binChunk];
        Encoding.ASCII.GetBytes("glTF").CopyTo(output, 0);
        BitConverter.GetBytes(2).CopyTo(output, 4);
        BitConverter.GetBytes(output.Length).CopyTo(output, 8);
        BitConverter.GetBytes(padded).CopyTo(output, 12);
        Encoding.ASCII.GetBytes("JSON").CopyTo(output, 16);
        text.CopyTo(output, 20);
        for (int i = 20 + text.Length; i < 20 + padded; i++)
            output[i] = (byte)' ';
        Array.Copy(glb, binStart, output, 20 + padded, binChunk);
        return output;
    }

}
