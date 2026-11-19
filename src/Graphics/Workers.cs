using System;
using System.Threading.Tasks;

namespace FishingSim.Graphics;

/// <summary>
/// How wide the background painters (textures, skins, block compression, map renders) may spread: all cores but two,
/// so the main thread and the audio keep theirs while the menu and the first map paint (a 4-core laptop stuttered
/// with every core busy painting), and at most 8: a user's i9-14900K (32 threads) showed random shader-compiler and
/// decompression failures while every thread painted at once; fewer threads keep the load and the stutters lower.
/// </summary>
public static class Workers
{
    public static readonly ParallelOptions Options = new() { MaxDegreeOfParallelism = Math.Clamp(Environment.ProcessorCount - 2, 1, 8) };
}
