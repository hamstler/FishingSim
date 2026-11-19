using System;

namespace FishingSim.Core.Methods;

/// <summary>
/// The wind-blown shore (pure): a steady wind drives the surface water, the drifting insects and the stirred-up food
/// towards the shore it blows onto, and the fish follow. Bait fished near a shore downwind of it is taken sooner the
/// harder the wind blows; on a calm day, or with the wind at one's back, nothing changes.
/// </summary>
public static class WindShore
{
    /// <summary>How far downwind (m) a shore still gathers the drift.</summary>
    public const float Reach = 40f;

    private const float Step = 4f;

    /// <summary>
    /// How exposed a point is to the drift (0 .. 1): 1 when the shore the wind blows onto is right there, falling to 0
    /// at <see cref="Reach"/> metres or when no shore lies downwind. <paramref name="depthAt"/> gives the water's depth
    /// at (x, z) (land = 0 or less); (wx, wz) is the wind's direction (need not be normalised).
    /// </summary>
    public static float Exposure(Func<float, float, float> depthAt, float x, float z, float wx, float wz)
    {
        float len = MathF.Sqrt(wx * wx + wz * wz);
        if (len < 1e-4f || depthAt(x, z) <= 0f)
            return 0f;
        wx /= len;
        wz /= len;
        for (float d = Step; d <= Reach; d += Step)
            if (depthAt(x + wx * d, z + wz * d) <= 0f)
                return 1f - (d - Step) / Reach;
        return 0f;
    }

    /// <summary>The bite clock's speed for this exposure and wind strength (0 calm .. 1 gale): up to 1.35.</summary>
    public static float Pace(float exposure, float wind) =>
        1f + 0.35f * Math.Clamp(exposure, 0f, 1f) * Math.Clamp(wind, 0f, 1f);
}
