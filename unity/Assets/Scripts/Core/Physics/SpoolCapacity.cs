using System;

namespace FishingSim.Core.Physics;

/// <summary>
/// How much line a fixed-spool reel holds, from its size and the line's diameter (the volume of the wound line):
/// L = pi (R^2 - r^2) W k / (pi (d/2)^2), R = the full spool's radius (under its lip), r = the core's, W = the
/// winding's width, k = 0.85 (how closely the turns pack). A bigger reel holds more of a given line, a thicker line
/// fills the same spool with fewer metres. The wound line's radius with l metres left follows the same volume:
/// rho(l) = sqrt(r^2 + (l / L)(R^2 - r^2)).
/// </summary>
public static class SpoolCapacity
{
    public const float Packing = 0.85f;

    /// <summary>Typical spools by reel size (mm): the full winding's radius, the core's radius, the winding's width.</summary>
    private static readonly (float Size, float R, float Core, float W)[] Table =
    {
        (1000f, 19f, 13f, 12f),
        (2500f, 22f, 15f, 14f),
        (4000f, 25f, 17f, 16f),
        (5000f, 27f, 18f, 18f),
        (8000f, 32f, 21f, 22f),
        (10000f, 35f, 23f, 25f),
    };

    /// <summary>The spool of a reel of this size (mm), interpolated on the size's logarithm, clamped to the table.</summary>
    public static (float R, float Core, float W) Spool(float size)
    {
        size = Math.Clamp(size, Table[0].Size, Table[^1].Size);
        for (int i = 0; i < Table.Length - 1; i++)
        {
            var a = Table[i];
            var b = Table[i + 1];
            if (size > b.Size)
                continue;
            float t = (MathF.Log(size) - MathF.Log(a.Size)) / (MathF.Log(b.Size) - MathF.Log(a.Size));
            return (a.R + (b.R - a.R) * t, a.Core + (b.Core - a.Core) * t, a.W + (b.W - a.W) * t);
        }
        var last = Table[^1];
        return (last.R, last.Core, last.W);
    }

    /// <summary>Metres of line of this diameter (mm) a reel of this size holds when filled to the lip.</summary>
    public static float Metres(float size, float diameterMm)
    {
        var (r1, r0, w) = Spool(size);
        float d = MathF.Max(diameterMm, 0.04f);
        float volume = MathF.PI * (r1 * r1 - r0 * r0) * w * Packing;         // mm^3 of line
        return volume / (MathF.PI * d * d * 0.25f) / 1000f;                 // mm -> m
    }

    /// <summary>The core's radius as a share of the full winding's (where the wound line ends when the spool is empty).</summary>
    public static float CoreRatio(float size)
    {
        var (r1, r0, _) = Spool(size);
        return r0 / r1;
    }

    /// <summary>The wound line's radius as a share of the full winding's, with <paramref name="fill"/> = line left / capacity.</summary>
    public static float WoundRatio(float size, float fill)
    {
        float c = CoreRatio(size);
        fill = Math.Clamp(fill, 0f, 1f);
        return MathF.Sqrt(c * c + fill * (1f - c * c));
    }

    /// <summary>
    /// The reel's size from its name ("Fjordline Fjell 2500 HS" -> 2500; "Hokuto Sora 1000" -> 1000): the first number
    /// from 500 to 30000 in it. Without one, the size whose spool holds <paramref name="ratedMetres"/> of 0.25 mm line.
    /// </summary>
    public static float SizeFromName(string? name, float ratedMetres)
    {
        if (!string.IsNullOrEmpty(name))
        {
            int i = 0;
            while (i < name!.Length)
            {
                if (!char.IsDigit(name[i]))
                {
                    i++;
                    continue;
                }
                int start = i, value = 0;
                while (i < name.Length && char.IsDigit(name[i]) && i - start < 6)
                    value = value * 10 + (name[i++] - '0');
                bool partOfDecimal = start > 0 && (name[start - 1] == ',' || name[start - 1] == '.');
                if (!partOfDecimal && value >= 500 && value <= 30000)
                    return value;
            }
        }
        // no size in the name: the size that holds the rated metres of 0.25 mm (bisection on the log size)
        float lo = Table[0].Size, hi = Table[^1].Size;
        for (int k = 0; k < 30; k++)
        {
            float mid = MathF.Sqrt(lo * hi);
            if (Metres(mid, 0.25f) < ratedMetres) lo = mid; else hi = mid;
        }
        return MathF.Sqrt(lo * hi);
    }
}
