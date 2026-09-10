using NUnit.Framework;
using UnityEngine;

public sealed class RollEffectTests
{
    private static CameraEffectContext ContextWithYawRate(float yawRate, float deltaTime = 0.1f)
    {
        return new CameraEffectContext(
            deltaTime: deltaTime,
            localVelocity: Vector3.zero,
            horizontalSpeed: 0f,
            moveInput: Vector2.zero,
            yawRate: yawRate,
            isGrounded: true,
            isCrouching: false,
            isHiding: false,
            weight: 1f);
    }

    [Test]
    public void Evaluate_NoTurning_StaysAtZero()
    {
        RollEffect effect = new(maxRollDegrees: 4f, yawRateForMaxRoll: 180f, smoothTime: 0f);
        CameraEffectOutput output = default;
        output.Clear();

        effect.Evaluate(ContextWithYawRate(0f), ref output);

        Assert.That(output.RotationOffset.z, Is.EqualTo(0f));
    }

    [Test]
    public void Evaluate_TurningFasterThanCap_ClampsToMaxRoll()
    {
        RollEffect effect = new(maxRollDegrees: 4f, yawRateForMaxRoll: 180f, smoothTime: 0f);
        CameraEffectOutput output = default;
        output.Clear();

        // Far beyond the yaw rate that maxes out roll — should clamp, not overshoot.
        effect.Evaluate(ContextWithYawRate(900f), ref output);

        Assert.That(Mathf.Abs(output.RotationOffset.z), Is.EqualTo(4f).Within(0.001f));
    }

    [Test]
    public void Evaluate_OppositeTurnDirections_ProduceOppositeSignRoll()
    {
        RollEffect right = new(maxRollDegrees: 4f, yawRateForMaxRoll: 180f, smoothTime: 0f);
        RollEffect left = new(maxRollDegrees: 4f, yawRateForMaxRoll: 180f, smoothTime: 0f);
        CameraEffectOutput rightOutput = default;
        rightOutput.Clear();
        CameraEffectOutput leftOutput = default;
        leftOutput.Clear();

        right.Evaluate(ContextWithYawRate(90f), ref rightOutput);
        left.Evaluate(ContextWithYawRate(-90f), ref leftOutput);

        Assert.That(rightOutput.RotationOffset.z, Is.EqualTo(-leftOutput.RotationOffset.z).Within(0.001f));
    }

    [Test]
    public void Reset_ClearsAccumulatedRoll()
    {
        RollEffect effect = new(maxRollDegrees: 4f, yawRateForMaxRoll: 180f, smoothTime: 0f);
        CameraEffectOutput output = default;
        output.Clear();
        effect.Evaluate(ContextWithYawRate(180f), ref output);

        effect.Reset();

        output.Clear();
        effect.Evaluate(ContextWithYawRate(0f), ref output);
        Assert.That(output.RotationOffset.z, Is.EqualTo(0f));
    }
}
