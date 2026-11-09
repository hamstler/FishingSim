using System;
using System.Globalization;
using Godot;

namespace FishingSim.Data;

/// <summary>What a building or prop is. Functional kinds can be entered (E); the rest is scenery.</summary>
public enum BuildingKind
{
    // functional
    Tackle, Bait, FishBuyer, Market, General, Office, BusStop,
    // scenery
    House, Barn, Windmill, Boathouse, Haystack, Lamp, Bench, Boat, Crates, Signpost, Fence,
}

/// <summary>A building or prop placed on a map, parsed from the "kind,x,z[,yaw]" strings in <see cref="LocationData.Buildings"/>.</summary>
public sealed record BuildingSpec(BuildingKind Kind, Vector2 Position, float? YawDegrees)
{
    public static BuildingSpec? Parse(string text)
    {
        var parts = text.Split(',', StringSplitOptions.TrimEntries);
        if (parts.Length < 3 || !Enum.TryParse<BuildingKind>(parts[0], ignoreCase: true, out var kind))
            return null;
        if (!float.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out float x)
            || !float.TryParse(parts[2], NumberStyles.Float, CultureInfo.InvariantCulture, out float z))
            return null;
        float? yaw = parts.Length > 3 && float.TryParse(parts[3], NumberStyles.Float, CultureInfo.InvariantCulture, out float y) ? y : null;
        return new BuildingSpec(kind, new Vector2(x, z), yaw);
    }

    public bool IsFunctional => Kind <= BuildingKind.BusStop;

    /// <summary>Radius of the flat pad (m) the terrain is levelled to; 0 for small props.</summary>
    public float PadRadius => Kind switch
    {
        BuildingKind.Tackle or BuildingKind.General or BuildingKind.Office => 9f,
        BuildingKind.Bait or BuildingKind.FishBuyer => 7f,
        BuildingKind.Market => 8f,
        BuildingKind.House or BuildingKind.Boathouse => 8f,
        BuildingKind.Barn => 10f,
        BuildingKind.Windmill => 6f,
        BuildingKind.BusStop => 4f,
        _ => 0f,
    };

    /// <summary>Yaw (radians) that makes the building's front (+Z) face <paramref name="plaza"/>, unless a yaw was given.</summary>
    public float FacingYaw(Vector2 plaza)
    {
        if (YawDegrees is float deg)
            return Mathf.DegToRad(deg);
        var d = plaza - Position;
        return d.LengthSquared() < 1f ? 0f : MathF.Atan2(d.X, d.Y);
    }

    /// <summary>Where the door is on the ground (a few metres in front of the building).</summary>
    public Vector2 DoorPoint(Vector2 plaza)
    {
        float yaw = FacingYaw(plaza);
        float reach = Kind switch { BuildingKind.BusStop => 2.4f, BuildingKind.Windmill => 4f, _ => 5.2f };
        return Position + new Vector2(MathF.Sin(yaw), MathF.Cos(yaw)) * reach;
    }
}
