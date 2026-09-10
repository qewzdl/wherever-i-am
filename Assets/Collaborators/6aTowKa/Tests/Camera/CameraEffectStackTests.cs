using NUnit.Framework;
using UnityEngine;

public sealed class CameraEffectStackTests
{
    private sealed class FakeCameraEffect : ICameraEffect
    {
        public Vector3 PositionContribution;
        public Vector3 RotationContribution;
        public float FovContribution;
        public int ResetCallCount;
        public int EvaluateCallCount;

        public string DebugName => "Fake";

        public bool Enabled { get; set; } = true;

        public float CrouchMultiplier { get; set; } = 1f;

        public float HidingMultiplier { get; set; } = 1f;

        public float UserMultiplier { get; set; } = 1f;

        public void Evaluate(in CameraEffectContext context, ref CameraEffectOutput output)
        {
            EvaluateCallCount++;
            output.PositionOffset += PositionContribution * context.Weight;
            output.RotationOffset += RotationContribution * context.Weight;
            output.FovOffset += FovContribution * context.Weight;
        }

        public void Reset()
        {
            ResetCallCount++;
        }
    }

    private static CameraEffectContext EmptyContext(bool isCrouching = false, bool isHiding = false, float weight = 1f)
    {
        return new CameraEffectContext(
            deltaTime: 0.016f,
            localVelocity: Vector3.zero,
            horizontalSpeed: 0f,
            moveInput: Vector2.zero,
            yawRate: 0f,
            isGrounded: true,
            isCrouching: isCrouching,
            isHiding: isHiding,
            weight: weight);
    }

    [Test]
    public void Evaluate_Crouching_ScalesEachEffectByItsOwnMultiplier()
    {
        CameraEffectStack stack = new();
        FakeCameraEffect halved = new() { FovContribution = 10f, CrouchMultiplier = 0.5f };
        FakeCameraEffect untouched = new() { FovContribution = 3f, CrouchMultiplier = 1f };

        stack.Add(halved);
        stack.Add(untouched);

        CameraEffectOutput output = stack.Evaluate(EmptyContext(isCrouching: true));

        Assert.That(output.FovOffset, Is.EqualTo(8f).Within(0.0001f));
    }

    [Test]
    public void Evaluate_NotCrouching_IgnoresCrouchMultiplier()
    {
        CameraEffectStack stack = new();
        FakeCameraEffect effect = new() { FovContribution = 10f, CrouchMultiplier = 0f };
        stack.Add(effect);

        CameraEffectOutput output = stack.Evaluate(EmptyContext());

        Assert.That(output.FovOffset, Is.EqualTo(10f).Within(0.0001f));
    }

    [Test]
    public void Evaluate_Crouching_MultipliesOnTopOfGlobalWeight()
    {
        CameraEffectStack stack = new();
        FakeCameraEffect effect = new() { FovContribution = 10f, CrouchMultiplier = 0.5f };
        stack.Add(effect);

        CameraEffectOutput output = stack.Evaluate(EmptyContext(isCrouching: true, weight: 0.5f));

        Assert.That(output.FovOffset, Is.EqualTo(2.5f).Within(0.0001f));
    }

    [Test]
    public void Evaluate_Hiding_ScalesEachEffectByItsOwnMultiplier()
    {
        CameraEffectStack stack = new();
        FakeCameraEffect quietened = new() { FovContribution = 10f, HidingMultiplier = 0.5f };
        FakeCameraEffect untouched = new() { FovContribution = 3f, HidingMultiplier = 1f };

        stack.Add(quietened);
        stack.Add(untouched);

        CameraEffectOutput output = stack.Evaluate(EmptyContext(isHiding: true));

        Assert.That(output.FovOffset, Is.EqualTo(8f).Within(0.0001f));
    }

    [Test]
    public void Evaluate_NotHiding_IgnoresHidingMultiplier()
    {
        CameraEffectStack stack = new();
        FakeCameraEffect effect = new() { FovContribution = 10f, HidingMultiplier = 0f };
        stack.Add(effect);

        CameraEffectOutput output = stack.Evaluate(EmptyContext());

        Assert.That(output.FovOffset, Is.EqualTo(10f).Within(0.0001f));
    }

    [Test]
    public void Evaluate_CrouchingWhileHiding_AppliesBothMultipliers()
    {
        CameraEffectStack stack = new();
        FakeCameraEffect effect = new() { FovContribution = 10f, CrouchMultiplier = 0.5f, HidingMultiplier = 0.4f };
        stack.Add(effect);

        CameraEffectOutput output = stack.Evaluate(EmptyContext(isCrouching: true, isHiding: true));

        Assert.That(output.FovOffset, Is.EqualTo(2f).Within(0.0001f));
    }

    // The multipliers open up past 1 in the inspector, so an effect can be made louder in a
    // state rather than only quieter.
    [Test]
    public void Evaluate_MultiplierAboveOne_AmplifiesTheEffect()
    {
        CameraEffectStack stack = new();
        FakeCameraEffect effect = new() { FovContribution = 10f, CrouchMultiplier = 2.5f };
        stack.Add(effect);

        CameraEffectOutput output = stack.Evaluate(EmptyContext(isCrouching: true));

        Assert.That(output.FovOffset, Is.EqualTo(25f).Within(0.0001f));
    }

    [Test]
    public void Evaluate_UserMultiplier_ScalesEachEffectByItsOwn()
    {
        CameraEffectStack stack = new();
        FakeCameraEffect quietened = new() { FovContribution = 10f, UserMultiplier = 0.5f };
        FakeCameraEffect untouched = new() { FovContribution = 3f, UserMultiplier = 1f };

        stack.Add(quietened);
        stack.Add(untouched);

        CameraEffectOutput output = stack.Evaluate(EmptyContext());

        Assert.That(output.FovOffset, Is.EqualTo(8f).Within(0.0001f));
    }

    // The player's slider is not a state the way crouching is: it applies standing, crouched
    // or hidden, and stacks with whichever of those is true.
    [Test]
    public void Evaluate_UserMultiplierWithCrouchAndHiding_AppliesAllThree()
    {
        CameraEffectStack stack = new();
        FakeCameraEffect effect = new()
        {
            FovContribution = 10f,
            UserMultiplier = 0.5f,
            CrouchMultiplier = 0.5f,
            HidingMultiplier = 0.4f
        };
        stack.Add(effect);

        CameraEffectOutput output = stack.Evaluate(EmptyContext(isCrouching: true, isHiding: true));

        Assert.That(output.FovOffset, Is.EqualTo(1f).Within(0.0001f));
    }

    // Turned down to nothing is not the same as turned off. The effect still runs, so its
    // phase keeps advancing and a shake impulse still expires on time - the slider can go
    // back up mid-stride without the view jumping.
    [Test]
    public void Evaluate_UserMultiplierOfZero_ContributesNothingButStillRuns()
    {
        CameraEffectStack stack = new();
        FakeCameraEffect effect = new() { FovContribution = 10f, UserMultiplier = 0f };
        stack.Add(effect);

        CameraEffectOutput output = stack.Evaluate(EmptyContext());

        Assert.That(output.FovOffset, Is.EqualTo(0f).Within(0.0001f));
        Assert.That(effect.EvaluateCallCount, Is.EqualTo(1));
    }

    // The contrast with the test above: the checkbox does skip the effect entirely, which is
    // why it also resets it rather than leaving stale state to resume from.
    [Test]
    public void Evaluate_DisabledEffect_IsNotEvaluatedAtAll()
    {
        CameraEffectStack stack = new();
        FakeCameraEffect effect = new() { FovContribution = 10f, Enabled = false };
        stack.Add(effect);

        stack.Evaluate(EmptyContext());

        Assert.That(effect.EvaluateCallCount, Is.EqualTo(0));
    }

    [Test]
    public void Evaluate_EmptyStack_ReturnsZero()
    {
        CameraEffectStack stack = new();

        CameraEffectOutput output = stack.Evaluate(EmptyContext());

        Assert.That(output.PositionOffset, Is.EqualTo(Vector3.zero));
        Assert.That(output.RotationOffset, Is.EqualTo(Vector3.zero));
        Assert.That(output.FovOffset, Is.EqualTo(0f));
    }

    [Test]
    public void Evaluate_SumsContributionsFromAllEffects()
    {
        CameraEffectStack stack = new();
        FakeCameraEffect first = new()
        {
            PositionContribution = new Vector3(1f, 0f, 0f),
            RotationContribution = new Vector3(0f, 2f, 0f),
            FovContribution = 3f
        };
        FakeCameraEffect second = new()
        {
            PositionContribution = new Vector3(0f, 5f, 0f),
            RotationContribution = new Vector3(0f, 0f, 7f),
            FovContribution = 4f
        };

        stack.Add(first);
        stack.Add(second);

        CameraEffectOutput output = stack.Evaluate(EmptyContext());

        Assert.That(output.PositionOffset, Is.EqualTo(new Vector3(1f, 5f, 0f)));
        Assert.That(output.RotationOffset, Is.EqualTo(new Vector3(0f, 2f, 7f)));
        Assert.That(output.FovOffset, Is.EqualTo(7f));
    }

    [Test]
    public void Evaluate_ResultIsOrderIndependent()
    {
        CameraEffectContext context = EmptyContext();

        CameraEffectStack forwardOrder = new();
        FakeCameraEffect a1 = new() { PositionContribution = new Vector3(1f, 0f, 0f), FovContribution = 2f };
        FakeCameraEffect b1 = new() { PositionContribution = new Vector3(0f, 3f, 0f), FovContribution = -1f };
        forwardOrder.Add(a1);
        forwardOrder.Add(b1);

        CameraEffectStack reverseOrder = new();
        FakeCameraEffect a2 = new() { PositionContribution = new Vector3(1f, 0f, 0f), FovContribution = 2f };
        FakeCameraEffect b2 = new() { PositionContribution = new Vector3(0f, 3f, 0f), FovContribution = -1f };
        reverseOrder.Add(b2);
        reverseOrder.Add(a2);

        CameraEffectOutput forwardOutput = forwardOrder.Evaluate(context);
        CameraEffectOutput reverseOutput = reverseOrder.Evaluate(context);

        Assert.That(forwardOutput.PositionOffset, Is.EqualTo(reverseOutput.PositionOffset));
        Assert.That(forwardOutput.FovOffset, Is.EqualTo(reverseOutput.FovOffset));
    }

    [Test]
    public void Remove_ExcludesEffectContribution()
    {
        CameraEffectStack stack = new();
        FakeCameraEffect stays = new() { FovContribution = 1f };
        FakeCameraEffect removed = new() { FovContribution = 10f };

        stack.Add(stays);
        stack.Add(removed);
        stack.Remove(removed);

        CameraEffectOutput output = stack.Evaluate(EmptyContext());

        Assert.That(output.FovOffset, Is.EqualTo(1f));
    }

    [Test]
    public void Evaluate_DisabledEffect_ContributesNothing()
    {
        CameraEffectStack stack = new();
        FakeCameraEffect disabled = new() { FovContribution = 10f, Enabled = false };
        FakeCameraEffect enabled = new() { FovContribution = 1f };

        stack.Add(disabled);
        stack.Add(enabled);

        CameraEffectOutput output = stack.Evaluate(EmptyContext());

        Assert.That(output.FovOffset, Is.EqualTo(1f));
    }

    [Test]
    public void Evaluate_ReenabledEffect_ContributesAgain()
    {
        CameraEffectStack stack = new();
        FakeCameraEffect effect = new() { FovContribution = 5f, Enabled = false };
        stack.Add(effect);

        effect.Enabled = true;
        CameraEffectOutput output = stack.Evaluate(EmptyContext());

        Assert.That(output.FovOffset, Is.EqualTo(5f));
    }

    [Test]
    public void ResetAll_ResetsEveryRegisteredEffect()
    {
        CameraEffectStack stack = new();
        FakeCameraEffect first = new();
        FakeCameraEffect second = new();

        stack.Add(first);
        stack.Add(second);

        stack.ResetAll();

        Assert.That(first.ResetCallCount, Is.EqualTo(1));
        Assert.That(second.ResetCallCount, Is.EqualTo(1));
    }
}
