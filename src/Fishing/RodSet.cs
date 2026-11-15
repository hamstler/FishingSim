using System;
using Godot;

namespace FishingSim.Fishing;

/// <summary>
/// The angler's rods: up to <see cref="MaxRods"/> set up at once (hotbar slots 1-3), each a whole
/// <see cref="FishingRod"/> with its own rig, float or feeder and tackle (rod slot 0 is the profile's Equipped, 1 and 2
/// its RodSlots). One is <see cref="Active"/>: it takes the input and the HUD follows it. The others are either in the
/// backpack (hidden, idle) or lying on their rests with the line in the water, where they keep fishing: a bite rings
/// the bell or nods the quiver tip and the angler takes that rod with its key.
/// </summary>
public sealed class RodSet
{
    public const int MaxRods = 3;

    private readonly FishingRod?[] _rods = new FishingRod?[MaxRods];
    private readonly Func<int, FishingRod> _make;
    private readonly Player _player;

    /// <summary>The rod slot in use (0..2).</summary>
    public int ActiveSlot { get; private set; }

    public FishingRod Active => _rods[ActiveSlot]!;

    /// <summary>Raised when another rod becomes the active one.</summary>
    public event Action<FishingRod>? Changed;

    /// <summary>Raised when a rod of the set is made (it needs its sound, tournament and rise hooks).</summary>
    public event Action<FishingRod>? Made;

    public RodSet(FishingRod first, Player player, Func<int, FishingRod> make)
    {
        _rods[0] = first;
        first.Slot = 0;
        first.Active = true;
        _player = player;
        _make = make;
        FishingRod.ActiveRod = first;
    }

    /// <summary>The rod in a slot, or null while it was never taken out.</summary>
    public FishingRod? At(int slot) => slot >= 0 && slot < MaxRods ? _rods[slot] : null;

    /// <summary>The rod in a slot, made when first needed.</summary>
    public FishingRod Get(int slot)
    {
        if (_rods[slot] is FishingRod rod)
            return rod;
        rod = _make(slot);
        rod.Slot = slot;
        rod.Active = false;
        _rods[slot] = rod;
        Made?.Invoke(rod);
        return rod;
    }

    /// <summary>The line is in the water (float, feeder or lure).</summary>
    public static bool InWater(FishingRod rod) => rod.State is RodState.Waiting or RodState.Nibble or RodState.Bite;

    /// <summary>The rod is busy and cannot be put down: casting, a fish on, a catch being decided.</summary>
    public static bool Busy(FishingRod rod) =>
        rod.State is RodState.Charging or RodState.Flying or RodState.Fight or RodState.Close or RodState.Landed or RodState.Broken;

    /// <summary>How many rods have their line in the water.</summary>
    public int InWaterCount()
    {
        int n = 0;
        foreach (var r in _rods)
            if (r != null && InWater(r))
                n++;
        return n;
    }

    /// <summary>
    /// The rod in the hands is put down so the hands are free for another rod or a tool: an idle rod goes into the
    /// backpack, a float or bottom rig in the water onto a rest. Returns why not (a busy rod, a lure being led, no room
    /// for a rest), or null.
    /// </summary>
    public string? PutDownActive()
    {
        var rod = Active;
        if (rod.OnRest)
            return null;
        if (Busy(rod))
            return "Najpierw skończ z tą wędką (rzut, hol albo ryba w ręku).";
        if (InWater(rod))
        {
            if (rod.CastMethod == Core.Methods.FishingMethod.Spinning)
                return "Spinningiem łowi się z ręki: najpierw zwiń przynętę.";
            return rod.PutOnRest() ? null : "Nie ma gdzie postawić podpórki.";
        }
        rod.InHands = false;    // into the backpack
        return null;
    }

    /// <summary>
    /// Takes the rod of a slot in hand: from the backpack, or from its rest (only from close by). The rod in hand is
    /// put down first. Returns why not, or null.
    /// </summary>
    public string? Take(int slot)
    {
        if (slot < 0 || slot >= MaxRods)
            return "Nie ma takiej wędki.";
        var target = Get(slot);
        if (target.OnRest && !target.NearRest)
            return $"Podejdź do wędki {slot + 1}, żeby ją podnieść.";
        if (slot != ActiveSlot)
        {
            string? why = PutDownActive();
            if (why != null)
                return why;
        }
        if (target.OnRest)
            target.PickUpRod();
        Activate(slot);
        target.InHands = true;
        return null;
    }

    /// <summary>Makes a slot's rod the one in use (input, HUD, the hands' rig) without moving anything.</summary>
    public void Activate(int slot)
    {
        var target = Get(slot);
        for (int i = 0; i < MaxRods; i++)
            if (_rods[i] is FishingRod r)
                r.Active = i == slot;
        ActiveSlot = slot;
        FishingRod.ActiveRod = target;
        _player.Rig = target.Rig;
        _player.Body.SetRig(target.Rig);
        Changed?.Invoke(target);
    }

    /// <summary>Every rod back to idle and into the backpack (a new map), the first one in the hands.</summary>
    public void ResetAll()
    {
        for (int i = 0; i < MaxRods; i++)
            if (_rods[i] is FishingRod r)
            {
                r.ResetToIdle();
                r.InHands = i == 0;
            }
        Activate(0);
    }
}
