using NUnit.Framework;
using UnityEngine;

public sealed class BreathingEffectTests
{
    private static CameraEffectContext Context(float horizontalSpeed, float weight = 1f, float deltaTime = 0.1f)
    {
        return new CameraEffectContext(
            deltaTime: deltaTime,
            localVelocity: Vector3.zero,
            horizontalSpeed: horizontalSpeed,
            moveInput: Vector2.zero,
            yawRate: 0f,
            isGrounded: true,
            isCrouching: false,
            isHiding: false,
            weight: weight);
    }

    private static BreathingEffect NewEffect(float suppressSmoothTime = 0f)
    {
        return new BreathingEffect(
            breathsPerMinute: 14f,
            verticalAmplitude: 0.004f,
            pitchDegrees: 0.15f,
            swayRollDegrees: 0.1f,
            moveSuppressSpeed: 1.5f,
            suppressSmoothTime: suppressSmoothTime);
    }

    [Test]
    public void Evaluate_Standing_MovesTheView()
    {
        BreathingEffect effect = NewEffect();
        bool sawVertical = false;
        bool sawPitch = false;

        for (int i = 0; i < 40; i++)
        {
            CameraEffectOutput output = default;
            output.Clear();
            effect.Evaluate(Context(0f), ref output);

            if (Mathf.Abs(output.PositionOffset.y) > 0.0001f)
                sawVertical = true;

            if (Mathf.Abs(output.RotationOffset.x) > 0.0001f)
                sawPitch = true;
        }

        Assert.That(sawVertical, Is.True);
        Assert.That(sawPitch, Is.True);
    }

    [Test]
    public void Evaluate_MovingAtSuppressSpeed_ProducesNoOffset()
    {
        BreathingEffect effect = NewEffect();
        CameraEffectOutput output = default;
        output.Clear();

        effect.Evaluate(Context(5f), ref output);

        Assert.That(output.PositionOffset, Is.EqualTo(Vector3.zero));
        Assert.That(output.RotationOffset, Is.EqualTo(Vector3.zero));
    }

    [Test]
    public void Evaluate_HalfWeight_HalvesOffset()
    {
        BreathingEffect full = NewEffect();
        BreathingEffect half = NewEffect();
        CameraEffectOutput fullOutput = default;
        CameraEffectOutput halfOutput = default;

        for (int i = 0; i < 20; i++)
        {
            fullOutput = default;
            fullOutput.Clear();
            full.Evaluate(Context(0f, weight: 1f), ref fullOutput);

            halfOutput = default;
            halfOutput.Clear();
            half.Evaluate(Context(0f, weight: 0.5f), ref halfOutput);
        }

        Assert.That(halfOutput.PositionOffset.y, Is.EqualTo(fullOutput.PositionOffset.y * 0.5f).Within(0.000001f));
        Assert.That(halfOutput.RotationOffset.x, Is.EqualTo(fullOutput.RotationOffset.x * 0.5f).Within(0.000001f));
    }

    // Regression: roll used to be Sin(phase * ratio) off the wrapped main phase, so every
    // phase wrap snapped the roll angle and read as a flick once per breath.
    [Test]
    public void Evaluate_AcrossSeveralBreaths_StaysContinuous()
    {
        BreathingEffect effect = NewEffect();
        Vector3 previousPosition = Vector3.zero;
        Vector3 previousRotation = Vector3.zero;
        bool hasPrevious = false;

        // ~6 full breaths at 14 bpm, so the phase wrap is crossed several times.
        for (int i = 0; i < 2600; i++)
        {
            CameraEffectOutput output = default;
            output.Clear();
            effect.Evaluate(Context(0f, deltaTime: 0.01f), ref output);

            if (hasPrevious)
            {
                Assert.That(Mathf.Abs(output.PositionOffset.y - previousPosition.y), Is.LessThan(0.0001f), $"Vertical jumped at step {i}.");
                Assert.That(Mathf.Abs(output.RotationOffset.x - previousRotation.x), Is.LessThan(0.005f), $"Pitch jumped at step {i}.");
                Assert.That(Mathf.Abs(output.RotationOffset.z - previousRotation.z), Is.LessThan(0.005f), $"Roll jumped at step {i}.");
            }

            previousPosition = output.PositionOffset;
            previousRotation = output.RotationOffset;
            hasPrevious = true;
        }
    }

    [Test]
    public void Reset_ReturnsToFreshState()
    {
        BreathingEffect warmed = NewEffect(suppressSmoothTime: 0.4f);
        CameraEffectOutput warmup = default;

        for (int i = 0; i < 15; i++)
        {
            warmup = default;
            warmup.Clear();
            warmed.Evaluate(Context(0f), ref warmup);
        }

        warmed.Reset();

        BreathingEffect fresh = NewEffect(suppressSmoothTime: 0.4f);
        CameraEffectOutput afterReset = default;
        afterReset.Clear();
        warmed.Evaluate(Context(0f), ref afterReset);
        CameraEffectOutput freshFirst = default;
        freshFirst.Clear();
        fresh.Evaluate(Context(0f), ref freshFirst);

        Assert.That(afterReset.PositionOffset, Is.EqualTo(freshFirst.PositionOffset));
        Assert.That(afterReset.RotationOffset, Is.EqualTo(freshFirst.RotationOffset));
    }
}
