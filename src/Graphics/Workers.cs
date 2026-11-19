using System;
using System.Threading.Tasks;

namespace FishingSim.Graphics;

/// <summary>
/// How wide the background painters (textures, skins, block compression, map renders) may spread: all cores but two,
/// so the main thread and the audio keep theirs while the menu and the first map paint (a 4-core laptop stuttered
/// with every core busy painting).
/// </summary>
public static class Workers
{
    public static readonly ParallelOptions Options = new() { MaxDegreeOfParallelism = Math.Max(1, Environment.ProcessorCount - 2) };
}
