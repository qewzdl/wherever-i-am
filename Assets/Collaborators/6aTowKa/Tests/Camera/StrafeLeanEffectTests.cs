using NUnit.Framework;
using UnityEngine;

public sealed class StrafeLeanEffectTests
{
    private static CameraEffectContext Context(float strafe, float weight = 1f, float deltaTime = 0.1f)
    {
        return new CameraEffectContext(
            deltaTime: deltaTime,
            localVelocity: Vector3.zero,
            horizontalSpeed: 0f,
            moveInput: new Vector2(strafe, 0f),
            yawRate: 0f,
            isGrounded: true,
            isCrouching: false,
            isHiding: false,
            weight: weight);
    }

    private static StrafeLeanEffect NewEffect(float smoothTime = 0f)
    {
        return new StrafeLeanEffect(
            maxAngleDegrees: 1.5f,
            positionOffset: 0.02f,
            smoothTime: smoothTime);
    }

    private static CameraEffectOutput EvaluateOnce(StrafeLeanEffect effect, CameraEffectContext context)
    {
        CameraEffectOutput output = default;
        output.Clear();
        effect.Evaluate(context, ref output);
        return output;
    }

    [Test]
    public void Evaluate_NoInput_ProducesNoOffset()
    {
        CameraEffectOutput output = EvaluateOnce(NewEffect(), Context(0f));

        Assert.That(output.RotationOffset, Is.EqualTo(Vector3.zero));
        Assert.That(output.PositionOffset, Is.EqualTo(Vector3.zero));
    }

    [Test]
    public void Evaluate_StrafingRight_BanksTheOppositeWay()
    {
        CameraEffectOutput output = EvaluateOnce(NewEffect(), Context(1f));

        // Positive Z rolls anticlockwise, so leaning right means a negative roll.
        Assert.That(output.RotationOffset.z, Is.LessThan(0f));
        // The head shifts the way it leans, not against it.
        Assert.That(output.PositionOffset.x, Is.GreaterThan(0f));
    }

    [Test]
    public void Evaluate_OppositeStrafes_MirrorEachOther()
    {
        CameraEffectOutput right = EvaluateOnce(NewEffect(), Context(1f));
        CameraEffectOutput left = EvaluateOnce(NewEffect(), Context(-1f));

        Assert.That(left.RotationOffset.z, Is.EqualTo(-right.RotationOffset.z).Within(0.000001f));
        Assert.That(left.PositionOffset.x, Is.EqualTo(-right.PositionOffset.x).Within(0.000001f));
    }

    [Test]
    public void Evaluate_Diagonal_LeansLessThanPureStrafe()
    {
        // A normalised diagonal only pushes the strafe axis to about 0.7.
        CameraEffectOutput diagonal = EvaluateOnce(NewEffect(), Context(0.7071f));
        CameraEffectOutput pure = EvaluateOnce(NewEffect(), Context(1f));

        Assert.That(Mathf.Abs(diagonal.RotationOffset.z), Is.LessThan(Mathf.Abs(pure.RotationOffset.z)));
    }

    [Test]
    public void Evaluate_HalfWeight_HalvesLean()
    {
        CameraEffectOutput full = EvaluateOnce(NewEffect(), Context(1f, weight: 1f));
        CameraEffectOutput half = EvaluateOnce(NewEffect(), Context(1f, weight: 0.5f));

        Assert.That(half.RotationOffset.z, Is.EqualTo(full.RotationOffset.z * 0.5f).Within(0.000001f));
        Assert.That(half.PositionOffset.x, Is.EqualTo(full.PositionOffset.x * 0.5f).Within(0.000001f));
    }

    [Test]
    public void Evaluate_InputReleased_RelaxesBackToNeutral()
    {
        StrafeLeanEffect effect = NewEffect(smoothTime: 0.2f);

        for (int i = 0; i < 20; i++)
            EvaluateOnce(effect, Context(1f));

        CameraEffectOutput leaned = EvaluateOnce(effect, Context(1f));
        Assert.That(Mathf.Abs(leaned.RotationOffset.z), Is.GreaterThan(0.1f), "Should have built up a lean first.");

        for (int i = 0; i < 40; i++)
            EvaluateOnce(effect, Context(0f));

        CameraEffectOutput relaxed = EvaluateOnce(effect, Context(0f));
        Assert.That(Mathf.Abs(relaxed.RotationOffset.z), Is.LessThan(0.01f));
    }

    [Test]
    public void Evaluate_Smoothed_DoesNotSnapToFullLeanImmediately()
    {
        StrafeLeanEffect smoothed = NewEffect(smoothTime: 0.2f);
        StrafeLeanEffect instant = NewEffect();

        CameraEffectOutput firstSmoothed = EvaluateOnce(smoothed, Context(1f));
        CameraEffectOutput firstInstant = EvaluateOnce(instant, Context(1f));

        Assert.That(Mathf.Abs(firstSmoothed.RotationOffset.z), Is.LessThan(Mathf.Abs(firstInstant.RotationOffset.z)));
    }

    [Test]
    public void Reset_ClearsAccumulatedLean()
    {
        StrafeLeanEffect effect = NewEffect(smoothTime: 0.2f);

        for (int i = 0; i < 20; i++)
            EvaluateOnce(effect, Context(1f));

        effect.Reset();

        CameraEffectOutput afterReset = EvaluateOnce(effect, Context(0f));

        Assert.That(afterReset.RotationOffset, Is.EqualTo(Vector3.zero));
        Assert.That(afterReset.PositionOffset, Is.EqualTo(Vector3.zero));
    }
}
