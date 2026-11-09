using System;

namespace FishingSim.Core.Animation;

/// <summary>What the angler's body is doing. Locomotion comes from speed; the rest is an action layered on top.</summary>
public enum BodyAction { None, Cast, Fight, Reel, Land, Interact }

/// <summary>Pose offsets for one frame of first-person animation (metres / radians, camera space).</summary>
public readonly record struct BodyPose(
    float HeadBobY,
    float HeadBobX,
    float HeadRoll,
    float ArmSwing,
    float ArmLift,
    float Breath,
    float Lean);

/// <summary>
/// Procedural first-person "human" animation: head bob and sway in step with the gait, footstep events, breathing at
/// rest, arm swing while walking, and the arm lift/lean of casting, fighting and landing a fish. Pure maths so it can
/// be tested and tuned without the engine; <c>Fishing/FirstPersonBody</c> applies the pose to the camera and arms.
/// </summary>
public sealed class BodyMotion
{
    public const float WalkSpeed = 3.2f;

    /// <summary>Strides per second at walking speed (a stride is two steps, so ~1.9 steps/s).</summary>
    public const float StepRate = 0.95f;

    private float _phase;          // 0..1 per stride (two steps)
    private float _gait;           // 0 = standing, 1 = walking, ~1.8 = running (smoothed)
    private float _action;         // 0..1 blend of the current action
    private float _time;
    private int _lastStepHalf;

    public BodyAction Action { get; private set; }

    /// <summary>Raised on every footfall (left and right).</summary>
    public event Action? Footstep;

    public int Footsteps { get; private set; }

    public BodyPose Pose { get; private set; }

    /// <summary>Advances the animation. <paramref name="speed"/> is horizontal speed in m/s.</summary>
    public BodyPose Update(float dt, float speed, bool grounded, BodyAction action)
    {
        _time += dt;
        float targetGait = grounded ? Math.Clamp(speed / WalkSpeed, 0f, 2f) : 0f;
        _gait += (targetGait - _gait) * Math.Clamp(dt * 8f, 0f, 1f);

        if (action != Action)
        {
            Action = action;
            _action = 0f;
        }
        _action = MathF.Min(1f, _action + dt * 4f);

        if (_gait > 0.05f)
        {
            _phase += dt * StepRate * MathF.Sqrt(MathF.Max(_gait, 0.1f));
            int half = (int)MathF.Floor(_phase * 2f);
            if (half != _lastStepHalf)
            {
                _lastStepHalf = half;
                Footsteps++;
                Footstep?.Invoke();
            }
        }

        float a = _phase * MathF.Tau;
        float amp = MathF.Min(_gait, 2f);
        float bobY = -MathF.Abs(MathF.Sin(a)) * 0.045f * amp;          // dips on every footfall
        float bobX = MathF.Sin(a) * 0.025f * amp;                        // sways once per stride
        float roll = MathF.Sin(a) * 0.012f * amp;
        float swing = MathF.Sin(a) * 0.35f * MathF.Min(amp, 1.2f);
        float breath = MathF.Sin(_time * 1.6f) * 0.006f * (1f - MathF.Min(_gait, 1f)) + 0.002f * amp;

        float lift = 0f, lean = 0f;
        switch (Action)
        {
            case BodyAction.Cast:
                lift = 0.9f * _action; lean = 0.08f * _action; break;
            case BodyAction.Fight:
                lift = 0.6f; lean = -0.12f + MathF.Sin(_time * 3.1f) * 0.03f; break;
            case BodyAction.Reel:
                lift = 0.4f; lean = -0.05f; break;
            case BodyAction.Land:
                lift = 0.2f; lean = 0.25f * _action; break;
            case BodyAction.Interact:
                lift = 0.3f * _action; break;
        }
        // Arms do not swing while holding a bent rod.
        if (Action is BodyAction.Fight or BodyAction.Reel or BodyAction.Cast)
            swing *= 0.2f;

        Pose = new BodyPose(bobY, bobX, roll, swing, lift, breath, lean);
        return Pose;
    }
}
