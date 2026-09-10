using NUnit.Framework;
using UnityEngine;

public sealed class HeadBobFigureEightEffectTests
{
    private static CameraEffectContext ContextWithSpeed(float horizontalSpeed, bool isGrounded = true, float weight = 1f, float deltaTime = 0.1f)
    {
        return new CameraEffectContext(
            deltaTime: deltaTime,
            localVelocity: Vector3.zero,
            horizontalSpeed: horizontalSpeed,
            moveInput: Vector2.zero,
            yawRate: 0f,
            isGrounded: isGrounded,
            isCrouching: false,
            isHiding: false,
            weight: weight);
    }

    // Fixed seed so per-step jitter is reproducible across test runs and, where a test
    // compares two effect instances (e.g. standing vs. crouching), identical between them.
    private const int FixedJitterSeed = 12345;

    private static HeadBobFigureEightEffect NewEffect(float envelopeSmoothTime = 0f, float stepJitterRange = 0.15f, float rollAmplitudeDegrees = 1f, float impactFraction = 0.3f, bool swayFollowsImpact = true, int randomSeed = FixedJitterSeed)
    {
        return new HeadBobFigureEightEffect(
            verticalAmplitude: 0.02f,
            horizontalAmplitude: 0.015f,
            rollAmplitudeDegrees: rollAmplitudeDegrees,
            pitchAmplitudeDegrees: 0.6f,
            yawAmplitudeDegrees: 0.5f,
            frequency: 0.4f,
            crouchFrequency: 0.6f,
            fullAmplitudeSpeed: 5f,
            envelopeSmoothTime: envelopeSmoothTime,
            stepJitterRange: stepJitterRange,
            impactFraction: impactFraction,
            swayFollowsImpact: swayFollowsImpact,
            randomSeed: randomSeed);
    }

    [Test]
    public void Evaluate_Stationary_ProducesNoOffset()
    {
        HeadBobFigureEightEffect effect = NewEffect();
        CameraEffectOutput output = default;
        output.Clear();

        effect.Evaluate(ContextWithSpeed(0f), ref output);

        Assert.That(output.PositionOffset, Is.EqualTo(Vector3.zero));
    }

    [Test]
    public void Evaluate_Airborne_ProducesNoOffsetRegardlessOfSpeed()
    {
        HeadBobFigureEightEffect effect = NewEffect();
        CameraEffectOutput output = default;
        output.Clear();

        effect.Evaluate(ContextWithSpeed(5f, isGrounded: false), ref output);

        Assert.That(output.PositionOffset, Is.EqualTo(Vector3.zero));
    }

    [Test]
    public void Evaluate_Moving_ProducesBothVerticalAndHorizontalOffset()
    {
        HeadBobFigureEightEffect effect = NewEffect();
        bool sawVertical = false;
        bool sawHorizontal = false;

        for (int i = 0; i < 50; i++)
        {
            CameraEffectOutput output = default;
            output.Clear();
            effect.Evaluate(ContextWithSpeed(5f), ref output);

            Assert.That(output.PositionOffset.y, Is.LessThanOrEqualTo(0f));

            if (output.PositionOffset.y < 0f)
                sawVertical = true;

            if (Mathf.Abs(output.PositionOffset.x) > 0.0001f)
                sawHorizontal = true;
        }

        Assert.That(sawVertical, Is.True);
        Assert.That(sawHorizontal, Is.True);
    }

    // Crouch scaling is no longer this effect's job — the stack folds it into Weight. This
    // covers the mechanism it now rides on; the crouch behaviour itself lives in
    // CameraEffectStackTests.
    [Test]
    public void Evaluate_HalfWeight_HalvesBothAmplitudes()
    {
        HeadBobFigureEightEffect full = NewEffect();
        HeadBobFigureEightEffect half = NewEffect();
        CameraEffectOutput fullOutput = default;
        fullOutput.Clear();
        CameraEffectOutput halfOutput = default;
        halfOutput.Clear();

        full.Evaluate(ContextWithSpeed(5f), ref fullOutput);
        half.Evaluate(ContextWithSpeed(5f, weight: 0.5f), ref halfOutput);

        Assert.That(halfOutput.PositionOffset.y, Is.EqualTo(fullOutput.PositionOffset.y * 0.5f).Within(0.0001f));
        Assert.That(halfOutput.PositionOffset.x, Is.EqualTo(fullOutput.PositionOffset.x * 0.5f).Within(0.0001f));
    }

    [Test]
    public void Evaluate_SameSeed_ProducesIdenticalSequence()
    {
        HeadBobFigureEightEffect first = NewEffect(randomSeed: 777);
        HeadBobFigureEightEffect second = NewEffect(randomSeed: 777);

        for (int i = 0; i < 30; i++)
        {
            CameraEffectOutput firstOutput = default;
            firstOutput.Clear();
            first.Evaluate(ContextWithSpeed(5f), ref firstOutput);

            CameraEffectOutput secondOutput = default;
            secondOutput.Clear();
            second.Evaluate(ContextWithSpeed(5f), ref secondOutput);

            Assert.That(secondOutput.PositionOffset.y, Is.EqualTo(firstOutput.PositionOffset.y).Within(0.0001f));
        }
    }

    [Test]
    public void Evaluate_AcrossFootfalls_PeakDepthVariesFromStepToStep()
    {
        HeadBobFigureEightEffect effect = NewEffect();
        System.Collections.Generic.List<float> peakDepths = new();
        float previous = 0f;
        float beforePrevious = 0f;

        // Dense sampling so consecutive footfall peaks (local maxima of dip depth) can be
        // picked out of the raw curve.
        for (int i = 0; i < 800; i++)
        {
            CameraEffectOutput output = default;
            output.Clear();
            effect.Evaluate(ContextWithSpeed(5f, deltaTime: 0.01f), ref output);

            float depth = -output.PositionOffset.y;

            if (previous > beforePrevious && previous > depth)
                peakDepths.Add(previous);

            beforePrevious = previous;
            previous = depth;
        }

        Assert.That(peakDepths.Count, Is.GreaterThan(2), "Test setup should produce several footfalls to compare.");

        float minPeak = peakDepths[0];
        float maxPeak = peakDepths[0];
        foreach (float peak in peakDepths)
        {
            minPeak = Mathf.Min(minPeak, peak);
            maxPeak = Mathf.Max(maxPeak, peak);
        }

        // Without jitter every footfall dips to exactly the same peak depth; with it, peaks
        // spread out across the configured ±15% range.
        Assert.That(maxPeak - minPeak, Is.GreaterThan(0.0005f));
    }

    // At speed 5 with frequency 0.4 the phase advances 4*PI per second, so one footfall
    // (PI of phase) takes 0.25s — 250 steps at dt 0.001.
    private const int StepsPerFootfall = 250;

    private static int IndexOfPeakHorizontal(HeadBobFigureEightEffect effect, int steps)
    {
        int peakIndex = 0;
        float peakValue = 0f;

        for (int i = 0; i < steps; i++)
        {
            CameraEffectOutput output = default;
            output.Clear();
            effect.Evaluate(ContextWithSpeed(5f, deltaTime: 0.001f), ref output);

            if (Mathf.Abs(output.PositionOffset.x) > peakValue)
            {
                peakValue = Mathf.Abs(output.PositionOffset.x);
                peakIndex = i;
            }
        }

        return peakIndex;
    }

    [Test]
    public void Evaluate_SwayFollowsImpact_ReachesPeakEarlierWithinTheFootfall()
    {
        HeadBobFigureEightEffect warped = NewEffect(stepJitterRange: 0f, swayFollowsImpact: true);
        HeadBobFigureEightEffect even = NewEffect(stepJitterRange: 0f, swayFollowsImpact: false);

        int warpedPeak = IndexOfPeakHorizontal(warped, StepsPerFootfall);
        int evenPeak = IndexOfPeakHorizontal(even, StepsPerFootfall);

        // Even glide peaks halfway through the footfall; warped peaks at impactFraction (0.3).
        Assert.That(evenPeak, Is.EqualTo(StepsPerFootfall / 2).Within(15));
        Assert.That(warpedPeak, Is.EqualTo((int)(StepsPerFootfall * 0.3f)).Within(15));
        Assert.That(warpedPeak, Is.LessThan(evenPeak));
    }

    [Test]
    public void Evaluate_SwayFollowsImpact_StaysContinuousAcrossFootfalls()
    {
        HeadBobFigureEightEffect effect = NewEffect(stepJitterRange: 0f, swayFollowsImpact: true);
        float previousHorizontal = 0f;
        bool hasPrevious = false;

        // Several footfalls, so the warp's 1 -> 0 boundary is crossed repeatedly.
        for (int i = 0; i < StepsPerFootfall * 6; i++)
        {
            CameraEffectOutput output = default;
            output.Clear();
            effect.Evaluate(ContextWithSpeed(5f, deltaTime: 0.001f), ref output);

            if (hasPrevious)
                Assert.That(Mathf.Abs(output.PositionOffset.x - previousHorizontal), Is.LessThan(0.0005f), $"Sway jumped at step {i}.");

            previousHorizontal = output.PositionOffset.x;
            hasPrevious = true;
        }
    }

    // Regression: the recovery has to cover the full amplitude in whatever is left of the
    // footfall, so a large impactFraction used to squeeze it into less than a frame and the
    // head popped back up. The constructor now caps the value; this pins the cap down at the
    // worst setting a user can dial in.
    [Test]
    public void Evaluate_HighestImpactFraction_NeverMovesFarInASingleFrame()
    {
        HeadBobFigureEightEffect effect = NewEffect(stepJitterRange: 0f, impactFraction: 0.95f);
        Vector3 previous = Vector3.zero;
        bool hasPrevious = false;

        // dt 1/60 is the worst realistic case: a coarse frame lands inside the narrow recovery.
        for (int i = 0; i < 600; i++)
        {
            CameraEffectOutput output = default;
            output.Clear();
            effect.Evaluate(ContextWithSpeed(5f, deltaTime: 1f / 60f), ref output);

            if (hasPrevious)
            {
                // Amplitude is 0.02m vertical / 0.015m horizontal; a third of it in one frame
                // is already a visible pop.
                Assert.That(Mathf.Abs(output.PositionOffset.y - previous.y), Is.LessThan(0.007f), $"Vertical popped at step {i}.");
                Assert.That(Mathf.Abs(output.PositionOffset.x - previous.x), Is.LessThan(0.005f), $"Sway popped at step {i}.");
            }

            previous = output.PositionOffset;
            hasPrevious = true;
        }
    }

    [Test]
    public void Reset_ClearsPhaseAndEnvelope()
    {
        HeadBobFigureEightEffect effect = NewEffect(envelopeSmoothTime: 0.15f);
        CameraEffectOutput warmup = default;
        warmup.Clear();

        for (int i = 0; i < 10; i++)
            effect.Evaluate(ContextWithSpeed(5f), ref warmup);

        effect.Reset();

        CameraEffectOutput afterReset = default;
        afterReset.Clear();
        effect.Evaluate(ContextWithSpeed(0f), ref afterReset);

        Assert.That(afterReset.PositionOffset, Is.EqualTo(Vector3.zero));
    }
}
