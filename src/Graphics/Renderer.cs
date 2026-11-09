using Godot;

namespace FishingSim.Graphics;

/// <summary>Which rendering backend is running. The realistic shaders need Forward+ (screen and depth textures).</summary>
public static class Renderer
{
    public static bool IsHeadless => DisplayServer.GetName() == "headless";

    /// <summary>True on the OpenGL "Compatibility" backend: simpler water and no screen-space effects.</summary>
    public static bool IsCompatibility => !IsHeadless && RenderingServer.GetRenderingDevice() == null;
}
