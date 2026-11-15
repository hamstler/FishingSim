using System;

namespace FishingSim.Core.Methods;

/// <summary>
/// The local anglers on the bank (pure rules): each takes about a fish an hour from the water in front of it (which
/// then counts as fished: SwimPressure), and a float cast right next to someone else's is bad manners: it spooks
/// their swim and earns a word from them.
/// </summary>
public static class OtherAnglers
{
    /// <summary>Fish an angler takes per game hour.</summary>
    public const float CatchesPerHour = 1.1f;

    /// <summary>Closer than this (m) to another angler's float the player is crowding them.</summary>
    public const float Crowding = 8f;

    /// <summary>The player's float lies in someone else's swim.</summary>
    public static bool Crowds(float distanceToTheirFloat) => distanceToTheirFloat < Crowding;
}
