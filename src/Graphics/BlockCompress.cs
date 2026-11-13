using System;
using System.Threading.Tasks;
using Godot;

namespace FishingSim.Graphics;

/// <summary>
/// Block compression for the GPU, done by the game itself (Godot's own compressors exist only in the editor, so an
/// exported game could not compress its painted textures): BC1 for opaque colour (8 bytes per 4x4 block), BC3 for
/// colour with alpha (16 bytes: the alpha keeps 8-bit end points) and BC5 for normal maps (16 bytes: two independent
/// channels, X and Y). Endpoints by bounding box with the box's diagonal turned to follow the colours' correlation,
/// inset a little; every texel then takes the nearest palette entry. Fast enough for 4096² textures in a second.
/// </summary>
public static class BlockCompress
{
    public enum Kind
    {
        /// <summary>RGB, no alpha (BC1).</summary>
        Colour,

        /// <summary>RGB with a smooth alpha (BC3): leaves' cut-outs, the ground's height.</summary>
        ColourAlpha,

        /// <summary>Two channels, R and G (BC5): a normal map's X and Y.</summary>
        TwoChannel,
    }

    public static Image.Format FormatOf(Kind kind) => kind switch
    {
        Kind.Colour => Image.Format.Dxt1,
        Kind.ColourAlpha => Image.Format.Dxt5,
        _ => Image.Format.RgtcRg,
    };

    /// <summary>Compresses an RGBA8 image and all its mipmaps (generate them first) into the GPU format of <paramref name="kind"/>.</summary>
    public static Image Compress(Image source, Kind kind)
    {
        if (source.GetFormat() != Image.Format.Rgba8)
            source.Convert(Image.Format.Rgba8);
        int w = source.GetWidth(), h = source.GetHeight();
        var format = FormatOf(kind);
        // Godot's own layout for this format (mip offsets, blocks of at least 4x4 for the smallest levels).
        var layout = Image.CreateEmpty(w, h, source.HasMipmaps(), format);
        int levels = layout.GetMipmapCount() + 1;
        int sourceLevels = source.GetMipmapCount() + 1;
        var dst = new byte[layout.GetData().Length];
        var src = source.GetData();
        int blockBytes = kind == Kind.Colour ? 8 : 16;
        for (int level = 0; level < levels; level++)
        {
            int sl = Math.Min(level, sourceLevels - 1);
            int lw = Math.Max(w >> sl, 1), lh = Math.Max(h >> sl, 1);
            int srcOffset = sl == 0 ? 0 : (int)source.GetMipmapOffset(sl);
            int dstOffset = level == 0 ? 0 : (int)layout.GetMipmapOffset(level);
            int bw = Math.Max((Math.Max(w >> level, 1) + 3) / 4, 1), bh = Math.Max((Math.Max(h >> level, 1) + 3) / 4, 1);
            Parallel.For(0, bh, by =>
            {
                Span<byte> block = stackalloc byte[64];
                for (int bx = 0; bx < bw; bx++)
                {
                    // Gather the 4x4 texels (clamped at the edges of the smallest levels).
                    for (int j = 0; j < 4; j++)
                        for (int i = 0; i < 4; i++)
                        {
                            int x = Math.Min(bx * 4 + i, lw - 1), y = Math.Min(by * 4 + j, lh - 1);
                            int s = srcOffset + (y * lw + x) * 4;
                            int t = (j * 4 + i) * 4;
                            block[t] = src[s];
                            block[t + 1] = src[s + 1];
                            block[t + 2] = src[s + 2];
                            block[t + 3] = src[s + 3];
                        }
                    int o = dstOffset + (by * bw + bx) * blockBytes;
                    switch (kind)
                    {
                        case Kind.Colour:
                            ColourBlock(block, dst, o);
                            break;
                        case Kind.ColourAlpha:
                            SingleChannelBlock(block, 3, dst, o);
                            ColourBlock(block, dst, o + 8);
                            break;
                        default:
                            SingleChannelBlock(block, 0, dst, o);
                            SingleChannelBlock(block, 1, dst, o + 8);
                            break;
                    }
                }
            });
        }
        return Image.CreateFromData(w, h, levels > 1, format, dst);
    }

    private static int Pack565(int r, int g, int b) => ((r * 31 + 127) / 255 << 11) | ((g * 63 + 127) / 255 << 5) | ((b * 31 + 127) / 255);

    private static void Unpack565(int c, out int r, out int g, out int b)
    {
        r = (c >> 11) & 31;
        g = (c >> 5) & 63;
        b = c & 31;
        r = (r << 3) | (r >> 2);
        g = (g << 2) | (g >> 4);
        b = (b << 3) | (b >> 2);
    }

    /// <summary>A BC1 colour block (always the four-colour mode, as BC3 reads it too).</summary>
    private static void ColourBlock(ReadOnlySpan<byte> px, byte[] dst, int o)
    {
        int minR = 255, minG = 255, minB = 255, maxR = 0, maxG = 0, maxB = 0;
        int sumR = 0, sumG = 0, sumB = 0;
        for (int i = 0; i < 16; i++)
        {
            int r = px[i * 4], g = px[i * 4 + 1], b = px[i * 4 + 2];
            minR = Math.Min(minR, r); maxR = Math.Max(maxR, r);
            minG = Math.Min(minG, g); maxG = Math.Max(maxG, g);
            minB = Math.Min(minB, b); maxB = Math.Max(maxB, b);
            sumR += r; sumG += g; sumB += b;
        }
        // Turn the box's diagonal to follow the colours: a channel that falls while the widest one rises swaps ends.
        float mr = sumR / 16f, mg = sumG / 16f, mb = sumB / 16f;
        float crg = 0f, cbg = 0f, crb = 0f;
        for (int i = 0; i < 16; i++)
        {
            float r = px[i * 4] - mr, g = px[i * 4 + 1] - mg, b = px[i * 4 + 2] - mb;
            crg += r * g;
            cbg += b * g;
            crb += r * b;
        }
        int rangeR = maxR - minR, rangeG = maxG - minG, rangeB = maxB - minB;
        if (rangeG >= rangeR && rangeG >= rangeB)
        {
            if (crg < 0f) (minR, maxR) = (maxR, minR);
            if (cbg < 0f) (minB, maxB) = (maxB, minB);
        }
        else if (rangeR >= rangeB)
        {
            if (crg < 0f) (minG, maxG) = (maxG, minG);
            if (crb < 0f) (minB, maxB) = (maxB, minB);
        }
        else
        {
            if (cbg < 0f) (minG, maxG) = (maxG, minG);
            if (crb < 0f) (minR, maxR) = (maxR, minR);
        }
        // Inset the ends by a sixteenth of the range (the palette's ends are rarely hit exactly).
        int Inset(int lo, int hi, bool end) => Math.Clamp(end ? hi - (hi - lo) / 16 : lo + (hi - lo) / 16, 0, 255);
        int c0 = Pack565(Inset(minR, maxR, true), Inset(minG, maxG, true), Inset(minB, maxB, true));
        int c1 = Pack565(Inset(minR, maxR, false), Inset(minG, maxG, false), Inset(minB, maxB, false));
        if (c0 < c1)
            (c0, c1) = (c1, c0);
        uint indices = 0;
        if (c0 != c1)
        {
            Unpack565(c0, out int r0, out int g0, out int b0);
            Unpack565(c1, out int r1, out int g1, out int b1);
            Span<int> pr = stackalloc int[4] { r0, r1, (2 * r0 + r1) / 3, (r0 + 2 * r1) / 3 };
            Span<int> pg = stackalloc int[4] { g0, g1, (2 * g0 + g1) / 3, (g0 + 2 * g1) / 3 };
            Span<int> pb = stackalloc int[4] { b0, b1, (2 * b0 + b1) / 3, (b0 + 2 * b1) / 3 };
            for (int i = 0; i < 16; i++)
            {
                int r = px[i * 4], g = px[i * 4 + 1], b = px[i * 4 + 2];
                int best = 0, bestD = int.MaxValue;
                for (int k = 0; k < 4; k++)
                {
                    int dr = r - pr[k], dg = g - pg[k], db = b - pb[k];
                    int d = dr * dr * 3 + dg * dg * 4 + db * db * 2;
                    if (d < bestD)
                    {
                        bestD = d;
                        best = k;
                    }
                }
                indices |= (uint)best << (i * 2);
            }
        }
        dst[o] = (byte)c0;
        dst[o + 1] = (byte)(c0 >> 8);
        dst[o + 2] = (byte)c1;
        dst[o + 3] = (byte)(c1 >> 8);
        dst[o + 4] = (byte)indices;
        dst[o + 5] = (byte)(indices >> 8);
        dst[o + 6] = (byte)(indices >> 16);
        dst[o + 7] = (byte)(indices >> 24);
    }

    /// <summary>A BC4 block (BC3's alpha, each half of BC5) of channel <paramref name="ch"/>: 8-bit ends, eight steps between.</summary>
    private static void SingleChannelBlock(ReadOnlySpan<byte> px, int ch, byte[] dst, int o)
    {
        int lo = 255, hi = 0;
        for (int i = 0; i < 16; i++)
        {
            int v = px[i * 4 + ch];
            lo = Math.Min(lo, v);
            hi = Math.Max(hi, v);
        }
        ulong bits = 0;
        if (hi > lo)
        {
            // a0 > a1: a0, a1 and six steps between them.
            Span<int> pal = stackalloc int[8];
            pal[0] = hi;
            pal[1] = lo;
            for (int k = 1; k <= 6; k++)
                pal[k + 1] = ((7 - k) * hi + k * lo + 3) / 7;
            for (int i = 0; i < 16; i++)
            {
                int v = px[i * 4 + ch];
                int best = 0, bestD = int.MaxValue;
                for (int k = 0; k < 8; k++)
                {
                    int d = Math.Abs(v - pal[k]);
                    if (d < bestD)
                    {
                        bestD = d;
                        best = k;
                    }
                }
                bits |= (ulong)best << (i * 3);
            }
        }
        dst[o] = (byte)hi;
        dst[o + 1] = (byte)lo;
        for (int k = 0; k < 6; k++)
            dst[o + 2 + k] = (byte)(bits >> (k * 8));
    }
}
