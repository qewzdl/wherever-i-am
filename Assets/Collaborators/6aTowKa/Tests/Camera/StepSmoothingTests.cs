using NUnit.Framework;
using UnityEngine;

public sealed class StepSmoothingTests
{
    private const float Frame = 1f / 60f;

    [Test]
    public void AfterAStepUp_TheViewStaysWhereItWas_AndCatchesUpWithoutOvershooting()
    {
        StepSmoothing smoothing = new(smoothTime: 0.12f);
        smoothing.Follow(0f, Frame);
        smoothing.SteppedUp(0.2f);

        // The body interpolates: the rise shows over two frames.
        smoothing.Follow(0.1f, Frame);
        Assert.That(0.1f + smoothing.Offset, Is.EqualTo(0f).Within(0.0001f), "The view moved with the first half of the rise.");
        smoothing.Follow(0.2f, Frame);
        Assert.That(0.2f + smoothing.Offset, Is.InRange(0f, 0.02f), "The view jumped with the second half of the rise.");

        for (int frame = 0; frame < 24; frame++)
        {
            smoothing.Follow(0.2f, Frame);
            Assert.That(smoothing.Offset, Is.LessThanOrEqualTo(0f), "The view went past the body.");
        }

        Assert.That(Mathf.Abs(smoothing.Offset), Is.LessThan(0.2f * 0.05f), "The view is still behind the body after 0.4 s.");
    }

    [Test]
    public void ARiseWithoutAStep_IsNotSmoothed()
    {
        StepSmoothing smoothing = new();
        smoothing.Follow(0f, Frame);
        smoothing.Follow(0.2f, Frame);

        Assert.That(smoothing.Offset, Is.EqualTo(0f));
    }
}
