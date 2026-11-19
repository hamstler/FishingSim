using System;

namespace FishingSim.Compat;

// The engine-free game logic (src/Core) was written for Godot and uses a few of its maths types. These minimal
// stand-ins keep that code unchanged in the Unity port (the Unity side converts to UnityEngine types at the edges).

public struct Vector2
{
    public float X, Y;
    public Vector2(float x, float y) { X = x; Y = y; }
    public static readonly Vector2 Zero = new(0f, 0f);
    public readonly float Length() => MathF.Sqrt(X * X + Y * Y);
    public readonly float LengthSquared() => X * X + Y * Y;
    public readonly Vector2 Normalized() { float l = Length(); return l > 0f ? new Vector2(X / l, Y / l) : Zero; }
    public readonly float Dot(Vector2 o) => X * o.X + Y * o.Y;
    public readonly float DistanceTo(Vector2 o) => (this - o).Length();
    public readonly Vector2 Lerp(Vector2 to, float t) => this + (to - this) * t;
    public static Vector2 operator +(Vector2 a, Vector2 b) => new(a.X + b.X, a.Y + b.Y);
    public static Vector2 operator -(Vector2 a, Vector2 b) => new(a.X - b.X, a.Y - b.Y);
    public static Vector2 operator -(Vector2 a) => new(-a.X, -a.Y);
    public static Vector2 operator *(Vector2 a, float k) => new(a.X * k, a.Y * k);
    public static Vector2 operator *(float k, Vector2 a) => new(a.X * k, a.Y * k);
    public static Vector2 operator /(Vector2 a, float k) => new(a.X / k, a.Y / k);
}

public struct Vector3
{
    public float X, Y, Z;
    public Vector3(float x, float y, float z) { X = x; Y = y; Z = z; }
    public static readonly Vector3 Zero = new(0f, 0f, 0f);
    public static readonly Vector3 Up = new(0f, 1f, 0f);
    public readonly float Length() => MathF.Sqrt(X * X + Y * Y + Z * Z);
    public readonly float LengthSquared() => X * X + Y * Y + Z * Z;
    public readonly Vector3 Normalized() { float l = Length(); return l > 0f ? new Vector3(X / l, Y / l, Z / l) : Zero; }
    public readonly float Dot(Vector3 o) => X * o.X + Y * o.Y + Z * o.Z;
    public readonly Vector3 Cross(Vector3 o) => new(Y * o.Z - Z * o.Y, Z * o.X - X * o.Z, X * o.Y - Y * o.X);
    public readonly float DistanceTo(Vector3 o) => (this - o).Length();
    public readonly float DistanceSquaredTo(Vector3 o) => (this - o).LengthSquared();
    public readonly Vector3 Lerp(Vector3 to, float t) => this + (to - this) * t;
    public static Vector3 operator +(Vector3 a, Vector3 b) => new(a.X + b.X, a.Y + b.Y, a.Z + b.Z);
    public static Vector3 operator -(Vector3 a, Vector3 b) => new(a.X - b.X, a.Y - b.Y, a.Z - b.Z);
    public static Vector3 operator -(Vector3 a) => new(-a.X, -a.Y, -a.Z);
    public static Vector3 operator *(Vector3 a, float k) => new(a.X * k, a.Y * k, a.Z * k);
    public static Vector3 operator *(float k, Vector3 a) => new(a.X * k, a.Y * k, a.Z * k);
    public static Vector3 operator /(Vector3 a, float k) => new(a.X / k, a.Y / k, a.Z / k);
}

public static class Mathf
{
    public const float Pi = MathF.PI, Tau = MathF.PI * 2f;
    public static float Lerp(float a, float b, float t) => a + (b - a) * t;
    public static float InverseLerp(float a, float b, float v) => a == b ? 0f : (v - a) / (b - a);
    public static float MoveToward(float from, float to, float delta) =>
        MathF.Abs(to - from) <= delta ? to : from + MathF.Sign(to - from) * delta;
    public static float Wrap(float value, float min, float max)
    {
        float range = max - min;
        return range == 0f ? min : min + ((value - min) % range + range) % range;
    }
    public static float Clamp(float v, float lo, float hi) => MathF.Min(MathF.Max(v, lo), hi);
    public static float DegToRad(float deg) => deg * MathF.PI / 180f;
    public static float RadToDeg(float rad) => rad * 180f / MathF.PI;
}

public struct Color
{
    public float R, G, B, A;
    public Color(float r, float g, float b, float a = 1f) { R = r; G = g; B = b; A = a; }
    public readonly Color Lerp(Color to, float t) =>
        new(R + (to.R - R) * t, G + (to.G - G) * t, B + (to.B - B) * t, A + (to.A - A) * t);
    public static Color operator *(Color c, float k) => new(c.R * k, c.G * k, c.B * k, c.A * k);
    public static Color operator *(Color a, Color b) => new(a.R * b.R, a.G * b.G, a.B * b.B, a.A * b.A);
    public static Color operator +(Color a, Color b) => new(a.R + b.R, a.G + b.G, a.B + b.B, a.A + b.A);
    public readonly float Luminance => 0.2126f * R + 0.7152f * G + 0.0722f * B;
}

/// <summary>Godot's untyped resource dictionaries (bait affinity, abundance, tree mix) hold numbers keyed by name.</summary>
public class Dictionary : System.Collections.Generic.Dictionary<string, float> { }

/// <summary>The base of the game's data records (Godot Resource), read from StreamingAssets/data as JSON.</summary>
public abstract class Resource
{
    public string ResourcePath { get; set; } = "";
}

public enum PropertyHint { None, Range, MultilineText, Enum, File }

[AttributeUsage(AttributeTargets.Property | AttributeTargets.Field)]
public sealed class ExportAttribute : Attribute
{
    public ExportAttribute() { }
    public ExportAttribute(PropertyHint hint, string hintString = "") { }
}

[AttributeUsage(AttributeTargets.Class)]
public sealed class GlobalClassAttribute : Attribute { }

[AttributeUsage(AttributeTargets.Class)]
public sealed class ToolAttribute : Attribute { }

public static class GD
{
    public static T Load<T>(string path) where T : class => DataStore.Load<T>(path)!;
}

public static class ResourceLoader
{
    public static bool Exists(string path) => DataStore.Exists(path);
}

/// <summary>Godot's Variant accessors on values that are plain numbers and strings here.</summary>
public static class VariantExtensions
{
    public static float AsSingle(this float v) => v;
    public static string AsString(this string v) => v;
}
