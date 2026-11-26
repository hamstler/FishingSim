using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using UnityEngine;

namespace FishingSim.Game
{
    /// <summary>
    /// The game's own texture cache (Graphics/TextureStore and BlockCompress in the Godot game): pictures the game
    /// decodes, resizes or paints itself are block compressed on worker threads (Unity's Texture2D.Compress runs on the
    /// main thread: a 4K photograph took one to two seconds, fourteen ground layers froze the loading for tens of seconds)
    /// and kept in the user's folder (texcache/*.fstx: the GPU's own data with its mip chain), so the next start only
    /// reads them back. Keys carry the source file's size, so a changed picture is made again.
    /// </summary>
    public static class TexCache
    {
        /// <summary>Bump when the encoder or a picture's preparation changes: old files are then ignored.</summary>
        public const int Version = 1;

        private const uint Magic = 0x58545346;   // "FSTX"

        public static string Dir => Path.Combine(Application.persistentDataPath, "texcache");

        /// <summary>A texture's data as the GPU takes it (all mip levels, Unity's layout: rows bottom-up).</summary>
        public sealed class Entry
        {
            public TextureFormat Format;
            public int Width, Height, Mips;
            public bool Linear;
            public float[] Extra = Array.Empty<float>();
            public byte[] Data = Array.Empty<byte>();
        }

        private static string FileOf(string key)
        {
            var chars = key.ToCharArray();
            for (int i = 0; i < chars.Length; i++)
                if (!(char.IsLetterOrDigit(chars[i]) || chars[i] is '_' or '-' or '.'))
                    chars[i] = '_';
            return Path.Combine(Dir, new string(chars) + ".fstx");
        }

        /// <summary>A file's size as a cache stamp (0 when missing).</summary>
        public static long Stamp(string path)
        {
            try
            {
                return File.Exists(path) ? new FileInfo(path).Length : 0;
            }
            catch (Exception)
            {
                return 0;
            }
        }

        /// <summary>Reads a cached texture (any thread); null when missing, old or damaged.</summary>
        public static Entry? Read(string key)
        {
            string path = FileOf(key);
            try
            {
                if (!File.Exists(path))
                    return null;
                using var stream = File.OpenRead(path);
                using var r = new BinaryReader(stream);
                if (r.ReadUInt32() != Magic || r.ReadInt32() != Version)
                    return null;
                var e = new Entry
                {
                    Format = (TextureFormat)r.ReadInt32(),
                    Width = r.ReadInt32(),
                    Height = r.ReadInt32(),
                    Mips = r.ReadInt32(),
                    Linear = r.ReadInt32() != 0,
                };
                int extra = r.ReadInt32();
                if (extra < 0 || extra > 64)
                    return null;
                e.Extra = new float[extra];
                for (int i = 0; i < extra; i++)
                    e.Extra[i] = r.ReadSingle();
                int length = r.ReadInt32();
                if (length <= 0 || length != DataSize(e.Format, e.Width, e.Height, e.Mips))
                    return null;
                e.Data = r.ReadBytes(length);
                return e.Data.Length == length ? e : null;
            }
            catch (Exception)
            {
                return null;
            }
        }

        /// <summary>Writes a texture to the cache (any thread; a failure only costs the next start the work again).</summary>
        public static void Write(string key, Entry e)
        {
            try
            {
                Directory.CreateDirectory(Dir);
                string path = FileOf(key), part = path + ".part";
                using (var stream = File.Create(part))
                using (var w = new BinaryWriter(stream))
                {
                    w.Write(Magic);
                    w.Write(Version);
                    w.Write((int)e.Format);
                    w.Write(e.Width);
                    w.Write(e.Height);
                    w.Write(e.Mips);
                    w.Write(e.Linear ? 1 : 0);
                    w.Write(e.Extra.Length);
                    foreach (var f in e.Extra)
                        w.Write(f);
                    w.Write(e.Data.Length);
                    w.Write(e.Data);
                }
                if (File.Exists(path))
                    File.Delete(path);
                File.Move(part, path);
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"TexCache: {key} not saved ({ex.Message})");
            }
        }

        public static void WriteLater(string key, Entry e) => Task.Run(() => Write(key, e));

        /// <summary>A texture from cached data (main thread), uploaded and no longer readable.</summary>
        public static Texture2D Make(Entry e, string name, TextureWrapMode wrap = TextureWrapMode.Repeat, int aniso = 8)
        {
            var t = new Texture2D(e.Width, e.Height, e.Format, e.Mips > 1, e.Linear)
            {
                name = name,
                wrapMode = wrap,
                filterMode = e.Mips > 1 ? FilterMode.Trilinear : FilterMode.Bilinear,
                anisoLevel = aniso,
            };
            int expected = t.GetRawTextureData<byte>().Length;
            if (expected != e.Data.Length)
            {
                // never expected: a layout the encoder does not match; grey instead of an exception mid-loading
                Debug.LogWarning($"TexCache: {name} {e.Format} {e.Width}x{e.Height} x{e.Mips}: {e.Data.Length} bytes, the GPU wants {expected}");
                UnityEngine.Object.Destroy(t);
                var grey = new Texture2D(4, 4, TextureFormat.RGBA32, false, e.Linear) { name = name };
                var px = new Color32[16];
                for (int i = 0; i < px.Length; i++)
                    px[i] = new Color32(128, 128, 128, 255);
                grey.SetPixels32(px);
                grey.Apply(false, true);
                return grey;
            }
            t.LoadRawTextureData(e.Data);
            t.Apply(false, true);
            return t;
        }

        /// <summary>A readable texture's data as it is (painted or compressed by the caller), for <see cref="Write"/>.</summary>
        public static Entry FromTexture(Texture2D t, float[]? extra = null) => new()
        {
            Format = t.format,
            Width = t.width,
            Height = t.height,
            Mips = t.mipmapCount,
            Linear = !t.isDataSRGB,
            Extra = extra ?? Array.Empty<float>(),
            Data = t.GetRawTextureData(),
        };

        // ------------------------------------------------------------------ the encoder (worker threads)

        public static int Levels(int w, int h)
        {
            int n = 1;
            while (w > 1 || h > 1)
            {
                w = Math.Max(w / 2, 1);
                h = Math.Max(h / 2, 1);
                n++;
            }
            return n;
        }

        private static bool Block(TextureFormat f) => f is TextureFormat.DXT1 or TextureFormat.DXT5;

        /// <summary>The bytes of one mip level.</summary>
        public static int LevelSize(TextureFormat f, int w, int h, int level)
        {
            int lw = Math.Max(w >> level, 1), lh = Math.Max(h >> level, 1);
            return f switch
            {
                TextureFormat.DXT1 => Math.Max((lw + 3) / 4, 1) * Math.Max((lh + 3) / 4, 1) * 8,
                TextureFormat.DXT5 => Math.Max((lw + 3) / 4, 1) * Math.Max((lh + 3) / 4, 1) * 16,
                _ => lw * lh * 4,
            };
        }

        public static int DataSize(TextureFormat f, int w, int h, int mips)
        {
            long size = 0;
            for (int m = 0; m < mips; m++)
                size += LevelSize(f, w, h, m);
            return size > int.MaxValue ? -1 : (int)size;
        }

        /// <summary>
        /// Mip chain and block compression of an RGBA picture (Unity's row order), on the calling thread with every core:
        /// BC3 (DXT5) with <paramref name="alpha"/>, else BC1 (DXT1); plain RGBA when the size is not a multiple of 4.
        /// </summary>
        public static Entry Encode(Color32[] top, int w, int h, bool linear, bool alpha, float[]? extra = null)
        {
            int levels = Levels(w, h);
            var format = w % 4 == 0 && h % 4 == 0 ? (alpha ? TextureFormat.DXT5 : TextureFormat.DXT1) : TextureFormat.RGBA32;
            var data = new byte[DataSize(format, w, h, levels)];
            int offset = 0;
            var level = top;
            int lw = w, lh = h;
            for (int m = 0; m < levels; m++)
            {
                if (m > 0)
                {
                    level = Half(level, lw, lh, out int nw, out int nh);
                    lw = nw;
                    lh = nh;
                }
                if (Block(format))
                    Compress(level, lw, lh, format == TextureFormat.DXT5, data, offset);
                else
                    for (int i = 0; i < level.Length; i++)
                    {
                        data[offset + i * 4] = level[i].r;
                        data[offset + i * 4 + 1] = level[i].g;
                        data[offset + i * 4 + 2] = level[i].b;
                        data[offset + i * 4 + 3] = level[i].a;
                    }
                offset += LevelSize(format, w, h, m);
            }
            return new Entry { Format = format, Width = w, Height = h, Mips = levels, Linear = linear, Extra = extra ?? Array.Empty<float>(), Data = data };
        }

        /// <summary>The next mip level: each texel the mean of its 2x2 parents.</summary>
        private static Color32[] Half(Color32[] src, int w, int h, out int nw, out int nh)
        {
            int ow = Math.Max(w / 2, 1), oh = Math.Max(h / 2, 1);
            nw = ow;
            nh = oh;
            var dst = new Color32[ow * oh];
            Parallel.For(0, oh, y =>
            {
                int y0 = Math.Min(y * 2, h - 1), y1 = Math.Min(y * 2 + 1, h - 1);
                for (int x = 0; x < ow; x++)
                {
                    int x0 = Math.Min(x * 2, w - 1), x1 = Math.Min(x * 2 + 1, w - 1);
                    var a = src[y0 * w + x0];
                    var b = src[y0 * w + x1];
                    var c = src[y1 * w + x0];
                    var d = src[y1 * w + x1];
                    dst[y * ow + x] = new Color32((byte)((a.r + b.r + c.r + d.r + 2) >> 2), (byte)((a.g + b.g + c.g + d.g + 2) >> 2),
                        (byte)((a.b + b.b + c.b + d.b + 2) >> 2), (byte)((a.a + b.a + c.a + d.a + 2) >> 2));
                }
            });
            return dst;
        }

        private static void Compress(Color32[] px, int w, int h, bool alpha, byte[] dst, int offset)
        {
            int bw = Math.Max((w + 3) / 4, 1), bh = Math.Max((h + 3) / 4, 1);
            int blockBytes = alpha ? 16 : 8;
            Parallel.For(0, bh, by =>
            {
                var block = new Color32[16];
                for (int bx = 0; bx < bw; bx++)
                {
                    for (int j = 0; j < 4; j++)
                        for (int i = 0; i < 4; i++)
                        {
                            int x = Math.Min(bx * 4 + i, w - 1), y = Math.Min(by * 4 + j, h - 1);
                            block[j * 4 + i] = px[y * w + x];
                        }
                    int o = offset + (by * bw + bx) * blockBytes;
                    if (alpha)
                    {
                        AlphaBlock(block, dst, o);
                        ColourBlock(block, dst, o + 8);
                    }
                    else
                        ColourBlock(block, dst, o);
                }
            });
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

        /// <summary>A BC1 colour block (always the four-colour mode, as BC3 reads it too): the bounding box's diagonal
        /// turned to follow the colours' correlation, inset a sixteenth, each texel the nearest of the four.</summary>
        private static void ColourBlock(Color32[] px, byte[] dst, int o)
        {
            int minR = 255, minG = 255, minB = 255, maxR = 0, maxG = 0, maxB = 0;
            int sumR = 0, sumG = 0, sumB = 0;
            for (int i = 0; i < 16; i++)
            {
                int r = px[i].r, g = px[i].g, b = px[i].b;
                if (r < minR) minR = r;
                if (r > maxR) maxR = r;
                if (g < minG) minG = g;
                if (g > maxG) maxG = g;
                if (b < minB) minB = b;
                if (b > maxB) maxB = b;
                sumR += r; sumG += g; sumB += b;
            }
            float mr = sumR / 16f, mg = sumG / 16f, mb = sumB / 16f;
            float crg = 0f, cbg = 0f, crb = 0f;
            for (int i = 0; i < 16; i++)
            {
                float r = px[i].r - mr, g = px[i].g - mg, b = px[i].b - mb;
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
            static int Inset(int lo, int hi, bool end) => Math.Clamp(end ? hi - (hi - lo) / 16 : lo + (hi - lo) / 16, 0, 255);
            int c0 = Pack565(Inset(minR, maxR, true), Inset(minG, maxG, true), Inset(minB, maxB, true));
            int c1 = Pack565(Inset(minR, maxR, false), Inset(minG, maxG, false), Inset(minB, maxB, false));
            if (c0 < c1)
                (c0, c1) = (c1, c0);
            uint indices = 0;
            if (c0 != c1)
            {
                Unpack565(c0, out int r0, out int g0, out int b0);
                Unpack565(c1, out int r1, out int g1, out int b1);
                int pr0 = r0, pr1 = r1, pr2 = (2 * r0 + r1) / 3, pr3 = (r0 + 2 * r1) / 3;
                int pg0 = g0, pg1 = g1, pg2 = (2 * g0 + g1) / 3, pg3 = (g0 + 2 * g1) / 3;
                int pb0 = b0, pb1 = b1, pb2 = (2 * b0 + b1) / 3, pb3 = (b0 + 2 * b1) / 3;
                for (int i = 0; i < 16; i++)
                {
                    int r = px[i].r, g = px[i].g, b = px[i].b;
                    int d0 = D(r - pr0, g - pg0, b - pb0), d1 = D(r - pr1, g - pg1, b - pb1);
                    int d2 = D(r - pr2, g - pg2, b - pb2), d3 = D(r - pr3, g - pg3, b - pb3);
                    int best = 0, bestD = d0;
                    if (d1 < bestD) { bestD = d1; best = 1; }
                    if (d2 < bestD) { bestD = d2; best = 2; }
                    if (d3 < bestD) best = 3;
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

        private static int D(int dr, int dg, int db) => dr * dr * 3 + dg * dg * 4 + db * db * 2;

        /// <summary>BC3's alpha block: 8-bit ends, six steps between.</summary>
        private static void AlphaBlock(Color32[] px, byte[] dst, int o)
        {
            int lo = 255, hi = 0;
            for (int i = 0; i < 16; i++)
            {
                int v = px[i].a;
                if (v < lo) lo = v;
                if (v > hi) hi = v;
            }
            ulong bits = 0;
            if (hi > lo)
            {
                Span<int> pal = stackalloc int[8];
                pal[0] = hi;
                pal[1] = lo;
                for (int k = 1; k <= 6; k++)
                    pal[k + 1] = ((7 - k) * hi + k * lo + 3) / 7;
                for (int i = 0; i < 16; i++)
                {
                    int v = px[i].a;
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

        // ------------------------------------------------------------------ pictures from files

        /// <summary>
        /// Decodes a picture file (main thread: Unity's decoders run only there) into RGBA texels of at most
        /// <paramref name="limit"/> on the longer side (scaled on the GPU), or null when it is missing.
        /// </summary>
        public static Color32[]? Decode(string path, int limit, bool linear, out int width, out int height)
        {
            width = height = 0;
            if (!File.Exists(path))
                return null;
            var raw = new Texture2D(2, 2, TextureFormat.RGBA32, false, linear);
            try
            {
                if (!raw.LoadImage(File.ReadAllBytes(path), false))
                    return null;
                int w = raw.width, h = raw.height;
                if (Mathf.Max(w, h) <= limit)
                {
                    width = w;
                    height = h;
                    return raw.GetPixels32();
                }
                float k = limit / (float)Mathf.Max(w, h);
                w = Mathf.Max(4, Mathf.RoundToInt(w * k));
                h = Mathf.Max(4, Mathf.RoundToInt(h * k));
                var rt = RenderTexture.GetTemporary(w, h, 0, RenderTextureFormat.ARGB32, linear ? RenderTextureReadWrite.Linear : RenderTextureReadWrite.sRGB);
                Graphics.Blit(raw, rt);
                var prev = RenderTexture.active;
                RenderTexture.active = rt;
                var copy = new Texture2D(w, h, TextureFormat.RGBA32, false, linear);
                copy.ReadPixels(new Rect(0, 0, w, h), 0, 0);
                copy.Apply(false, false);
                RenderTexture.active = prev;
                RenderTexture.ReleaseTemporary(rt);
                var px = copy.GetPixels32();
                UnityEngine.Object.Destroy(copy);
                width = w;
                height = h;
                return px;
            }
            finally
            {
                UnityEngine.Object.Destroy(raw);
            }
        }

        /// <summary>The first of the picture's files that exists (".jpg" then ".png" by default), or null.</summary>
        public static string? Find(string pathWithoutExtension, params string[] extensions)
        {
            foreach (var ext in extensions.Length > 0 ? extensions : new[] { ".jpg", ".png" })
                if (File.Exists(pathWithoutExtension + ext))
                    return pathWithoutExtension + ext;
            return null;
        }

        /// <summary>
        /// A photograph as a compressed texture (synchronous): from the cache, else decoded, resized to the limit,
        /// compressed with every core and saved for the next start.
        /// </summary>
        public static Texture2D? Picture(string file, int limit, bool linear, bool alpha, string name, TextureWrapMode wrap = TextureWrapMode.Repeat, int aniso = 8)
        {
            string key = $"pic_{Path.GetFileName(Path.GetDirectoryName(file))}_{Path.GetFileNameWithoutExtension(file)}_{limit}_{(linear ? "l" : "s")}_{(alpha ? "a" : "o")}_{Stamp(file)}";
            var e = Read(key);
            if (e == null)
            {
                var px = Decode(file, limit, linear, out int w, out int h);
                if (px == null)
                    return null;
                e = Encode(px, w, h, linear, alpha);
                WriteLater(key, e);
            }
            return Make(e, name, wrap, aniso);
        }

        /// <summary>The same as <see cref="Picture"/> spread over frames (a coroutine; the result in <paramref name="done"/>):
        /// the cache is read and the compression done on worker threads, only the decoding on the main thread.</summary>
        public static System.Collections.IEnumerator PictureAsync(string file, int limit, bool linear, bool alpha, string name, Action<Texture2D?> done,
            TextureWrapMode wrap = TextureWrapMode.Repeat, int aniso = 8)
        {
            string key = $"pic_{Path.GetFileName(Path.GetDirectoryName(file))}_{Path.GetFileNameWithoutExtension(file)}_{limit}_{(linear ? "l" : "s")}_{(alpha ? "a" : "o")}_{Stamp(file)}";
            var read = Task.Run(() => Read(key));
            while (!read.IsCompleted)
                yield return null;
            var e = read.Result;
            if (e == null)
            {
                var px = Decode(file, limit, linear, out int w, out int h);
                if (px == null)
                {
                    done(null);
                    yield break;
                }
                var work = Task.Run(() =>
                {
                    var made = Encode(px, w, h, linear, alpha);
                    Write(key, made);
                    return made;
                });
                while (!work.IsCompleted)
                    yield return null;
                if (work.Exception != null)
                {
                    Debug.LogException(work.Exception);
                    done(null);
                    yield break;
                }
                e = work.Result;
            }
            done(Make(e, name, wrap, aniso));
        }

        /// <summary>Copies a cached texture's levels into one layer of an array of the same format and size.</summary>
        public static void ToArray(Texture2DArray array, int layer, Entry e)
        {
            int offset = 0;
            for (int m = 0; m < e.Mips; m++)
            {
                int size = LevelSize(e.Format, e.Width, e.Height, m);
                if (m < array.mipmapCount && offset + size <= e.Data.Length)
                {
                    try
                    {
                        array.SetPixelData(e.Data, m, layer, offset);
                    }
                    catch (Exception ex)
                    {
                        Debug.LogWarning($"TexCache: layer {layer} mip {m} not set ({ex.Message})");
                        return;
                    }
                }
                offset += size;
            }
        }

        /// <summary>Removes cache files of older versions (not the current one) in the background.</summary>
        public static void Tidy()
        {
            Task.Run(() =>
            {
                try
                {
                    if (!Directory.Exists(Dir))
                        return;
                    foreach (var f in Directory.GetFiles(Dir, "*.part"))
                        File.Delete(f);
                }
                catch (Exception)
                {
                    // nothing to do
                }
            });
        }
    }

    /// <summary>Timings of the loading steps in the log ("LOAD step ms"), to see what a start costs.</summary>
    public static class LoadLog
    {
        private static readonly System.Diagnostics.Stopwatch Clock = System.Diagnostics.Stopwatch.StartNew();
        private static readonly Dictionary<string, double> Started = new();

        public static double Now => Clock.Elapsed.TotalMilliseconds;

        public static void Begin(string step) => Started[step] = Now;

        public static void End(string step)
        {
            if (Started.TryGetValue(step, out double t))
            {
                Debug.Log($"LOAD {step}: {Now - t:0} ms (at {Now / 1000.0:0.0} s)");
                Started.Remove(step);
            }
        }

        public static void Mark(string what) => Debug.Log($"LOAD {what} at {Now / 1000.0:0.0} s");
    }
}
