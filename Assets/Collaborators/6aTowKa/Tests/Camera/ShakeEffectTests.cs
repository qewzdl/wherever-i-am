using NUnit.Framework;
using UnityEngine;

public sealed class ShakeEffectTests
{
    // A fixed noise origin, so a test reads the same stripe of the noise field every run.
    private const float FixedNoiseOrigin = 12.3f;

    private static ShakeEffect CreateEffect(float falloffExponent = 1f)
    {
        return new ShakeEffect(
            pitchDegrees: 4f,
            yawDegrees: 4f,
            rollDegrees: 2f,
            positionAmplitude: 0.05f,
            fovAmplitude: 3f,
            frequency: 18f,
            falloffExponent: falloffExponent,
            noiseOrigin: FixedNoiseOrigin);
    }

    private static CameraEffectContext Context(float deltaTime = 0.05f, float weight = 1f)
    {
        return new CameraEffectContext(
            deltaTime: deltaTime,
            localVelocity: Vector3.zero,
            horizontalSpeed: 0f,
            moveInput: Vector2.zero,
            yawRate: 0f,
            isGrounded: true,
            isCrouching: false,
            isHiding: false,
            weight: weight);
    }

    private static float TotalMovement(in CameraEffectOutput output)
    {
        return Mathf.Abs(output.RotationOffset.x)
            + Mathf.Abs(output.RotationOffset.y)
            + Mathf.Abs(output.RotationOffset.z)
            + Mathf.Abs(output.PositionOffset.x)
            + Mathf.Abs(output.PositionOffset.y)
            + Mathf.Abs(output.FovOffset);
    }

    [Test]
    public void Evaluate_NoImpulse_ContributesNothing()
    {
        ShakeEffect effect = CreateEffect();
        CameraEffectOutput output = default;
        output.Clear();

        effect.Evaluate(Context(), ref output);

        Assert.That(TotalMovement(output), Is.EqualTo(0f));
        Assert.That(effect.ActiveImpulses, Is.EqualTo(0));
    }

    [Test]
    public void AddImpulse_ThenEvaluate_MovesTheView()
    {
        ShakeEffect effect = CreateEffect();
        CameraEffectOutput output = default;
        output.Clear();

        effect.AddImpulse(1f, 0.4f);
        effect.Evaluate(Context(), ref output);

        Assert.That(effect.ActiveImpulses, Is.EqualTo(1));
        Assert.That(TotalMovement(output), Is.GreaterThan(0f));
    }

    // Strength is a fraction of the authored amplitudes, so the same noise sample has to come
    // out proportionally smaller for a weaker hit.
    [Test]
    public void AddImpulse_WeakerStrength_MovesTheViewLess()
    {
        ShakeEffect strong = CreateEffect();
        ShakeEffect weak = CreateEffect();
        CameraEffectOutput strongOutput = default;
        strongOutput.Clear();
        CameraEffectOutput weakOutput = default;
        weakOutput.Clear();

        strong.AddImpulse(1f, 0.4f);
        weak.AddImpulse(0.25f, 0.4f);
        strong.Evaluate(Context(), ref strongOutput);
        weak.Evaluate(Context(), ref weakOutput);

        Assert.That(TotalMovement(weakOutput), Is.LessThan(TotalMovement(strongOutput)));
        Assert.That(weak.Envelope, Is.EqualTo(0.25f).Within(0.0001f));
    }

    [Test]
    public void AddImpulse_ZeroOrNegative_IsIgnored()
    {
        ShakeEffect effect = CreateEffect();

        effect.AddImpulse(0f, 0.4f);
        effect.AddImpulse(0.5f, 0f);
        effect.AddImpulse(-1f, 0.4f);

        Assert.That(effect.ActiveImpulses, Is.EqualTo(0));
    }

    // Saturating sum rather than a plain one: two hits of a half read harder than one, and
    // the total approaches the authored ceiling without a clamp anywhere.
    [Test]
    public void AddImpulse_TwoOverlapping_CombineWithoutPassingFullStrength()
    {
        ShakeEffect effect = CreateEffect();
        CameraEffectOutput output = default;
        output.Clear();

        effect.AddImpulse(0.5f, 0.4f);
        effect.AddImpulse(0.5f, 0.4f);
        effect.Evaluate(Context(), ref output);

        Assert.That(effect.ActiveImpulses, Is.EqualTo(2));
        Assert.That(effect.Envelope, Is.EqualTo(0.75f).Within(0.0001f));
    }

    [Test]
    public void AddImpulse_PastCapacity_DropsTheWeakestRatherThanTheOldest()
    {
        ShakeEffect effect = CreateEffect();

        // Eight is the fixed capacity. The first is the loud one and must survive.
        effect.AddImpulse(1f, 10f);
        for (int i = 0; i < 7; i++)
            effect.AddImpulse(0.1f, 10f);

        effect.AddImpulse(0.5f, 10f);

        CameraEffectOutput output = default;
        output.Clear();
        effect.Evaluate(Context(), ref output);

        Assert.That(effect.ActiveImpulses, Is.EqualTo(8));
        // Still saturated by the impulse of full strength, which a queue dropping the oldest
        // would have thrown away.
        Assert.That(effect.Envelope, Is.EqualTo(1f).Within(0.0001f));
    }

    [Test]
    public void AddImpulse_WeakerThanEverythingLive_IsTurnedAway()
    {
        ShakeEffect effect = CreateEffect();

        for (int i = 0; i < 8; i++)
            effect.AddImpulse(0.9f, 10f);

        effect.AddImpulse(0.01f, 10f);

        Assert.That(effect.ActiveImpulses, Is.EqualTo(8));
    }

    [Test]
    public void Evaluate_AfterTheImpulseRunsOut_ReturnsToRest()
    {
        ShakeEffect effect = CreateEffect();
        CameraEffectOutput output = default;
        output.Clear();

        effect.AddImpulse(1f, 0.1f);

        // Three frames of 0.05s take it past the 0.1s the impulse was given.
        for (int i = 0; i < 3; i++)
        {
            output.Clear();
            effect.Evaluate(Context(), ref output);
        }

        Assert.That(effect.ActiveImpulses, Is.EqualTo(0));

        output.Clear();
        effect.Evaluate(Context(), ref output);
        Assert.That(TotalMovement(output), Is.EqualTo(0f));
    }

    // The first frame of an impulse renders at full strength: time moves on after drawing, so
    // a hit does not arrive already a frame old.
    [Test]
    public void Evaluate_FirstFrame_RendersAtFullStrength()
    {
        ShakeEffect effect = CreateEffect();
        CameraEffectOutput output = default;
        output.Clear();

        effect.AddImpulse(1f, 0.4f);
        effect.Evaluate(Context(), ref output);

        Assert.That(effect.Envelope, Is.EqualTo(1f).Within(0.0001f));
    }

    // What the player's slider at zero does. The stack hands the effect a weight of nothing
    // rather than skipping it, so the impulse has to spend itself on schedule and leave no
    // shake waiting to play when the slider goes back up.
    [Test]
    public void Evaluate_AtZeroWeight_ContributesNothingAndStillExpires()
    {
        ShakeEffect effect = CreateEffect();
        CameraEffectOutput output = default;
        output.Clear();

        effect.AddImpulse(1f, 0.1f);

        for (int i = 0; i < 3; i++)
        {
            output.Clear();
            effect.Evaluate(Context(weight: 0f), ref output);
            Assert.That(TotalMovement(output), Is.EqualTo(0f));
        }

        Assert.That(effect.ActiveImpulses, Is.EqualTo(0));
    }

    [Test]
    public void Reset_DropsEverythingInFlight()
    {
        ShakeEffect effect = CreateEffect();
        CameraEffectOutput output = default;
        output.Clear();
        effect.AddImpulse(1f, 10f);
        effect.Evaluate(Context(), ref output);

        effect.Reset();

        Assert.That(effect.ActiveImpulses, Is.EqualTo(0));
        Assert.That(effect.Envelope, Is.EqualTo(0f));

        output.Clear();
        effect.Evaluate(Context(), ref output);
        Assert.That(TotalMovement(output), Is.EqualTo(0f));
    }

    // Re-tuning is what the inspector does while the game runs. It has to change the size of
    // the shake without disturbing the impulse already playing.
    [Test]
    public void ApplyTuning_ChangesAmplitudeAndLeavesImpulsesRunning()
    {
        ShakeEffect quiet = CreateEffect();
        ShakeEffect loud = CreateEffect();
        CameraEffectOutput quietOutput = default;
        quietOutput.Clear();
        CameraEffectOutput loudOutput = default;
        loudOutput.Clear();

        quiet.AddImpulse(1f, 10f);
        loud.AddImpulse(1f, 10f);

        loud.ApplyTuning(
            pitchDegrees: 40f,
            yawDegrees: 40f,
            rollDegrees: 20f,
            positionAmplitude: 0.5f,
            fovAmplitude: 30f,
            frequency: 18f,
            falloffExponent: 1f);

        quiet.Evaluate(Context(), ref quietOutput);
        loud.Evaluate(Context(), ref loudOutput);

        Assert.That(loud.ActiveImpulses, Is.EqualTo(1));
        Assert.That(TotalMovement(loudOutput), Is.GreaterThan(TotalMovement(quietOutput)));
    }

    [Test]
    public void ApplyTuning_BelowTheFloor_IsClampedRatherThanTaken()
    {
        ShakeEffect effect = CreateEffect();

        // Negative amplitudes would flip the shake; a falloff under 1 would make an impulse
        // swell towards its end instead of fading.
        effect.ApplyTuning(
            pitchDegrees: -5f,
            yawDegrees: -5f,
            rollDegrees: -5f,
            positionAmplitude: -1f,
            fovAmplitude: -1f,
            frequency: 18f,
            falloffExponent: 0.1f);

        CameraEffectOutput output = default;
        output.Clear();
        effect.AddImpulse(1f, 0.4f);
        effect.Evaluate(Context(), ref output);

        Assert.That(TotalMovement(output), Is.EqualTo(0f));
    }
}
