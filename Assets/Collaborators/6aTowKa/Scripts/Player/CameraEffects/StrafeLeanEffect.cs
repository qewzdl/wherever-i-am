using UnityEngine;

// Strafe lean: the body tips into sideways movement, so the horizon banks the opposite way.
// Driven by the strafe axis of the move input rather than by mouse turn rate — that is
// RollEffect's job, and the two stack on the same axis without knowing about each other.
//
// Input rather than actual velocity: it reacts the instant the key goes down, and
// PlayerController already zeroes its move input while movement is blocked, so there is no
// phantom lean during cutscenes or dialogue.
public sealed class StrafeLeanEffect : ICameraEffect
{
    private float maxAngleDegrees;
    private float positionOffset;
    private float smoothTime;

    private float currentLean;
    private float leanVelocity;

    public StrafeLeanEffect(float maxAngleDegrees, float positionOffset, float smoothTime)
    {
        ApplyTuning(maxAngleDegrees, positionOffset, smoothTime);
    }

    // See RollEffect.ApplyTuning: authored numbers only, re-appliable while the game runs.
    public void ApplyTuning(float maxAngleDegrees, float positionOffset, float smoothTime)
    {
        this.maxAngleDegrees = Mathf.Max(0f, maxAngleDegrees);
        this.positionOffset = positionOffset;
        this.smoothTime = Mathf.Max(0f, smoothTime);
    }

    public string DebugName => "StrafeLean";

    public bool Enabled { get; set; } = true;

    public float CrouchMultiplier { get; set; } = 1f;

    public float HidingMultiplier { get; set; } = 1f;

    public float UserMultiplier { get; set; } = 1f;

    public void Evaluate(in CameraEffectContext context, ref CameraEffectOutput output)
    {
        // Move input is normalised, so a diagonal only pushes x to about 0.7 and leans less
        // than a pure sideways strafe does — which is the behaviour we want, for free.
        float strafe = Mathf.Clamp(context.MoveInput.x, -1f, 1f);
        float targetLean = strafe * context.Weight;

        if (smoothTime <= 0f)
        {
            currentLean = targetLean;
        }
        else
        {
            currentLean = Mathf.SmoothDamp(currentLean, targetLean, ref leanVelocity, smoothTime, Mathf.Infinity, context.DeltaTime);
        }

        // Positive Z rolls anticlockwise (the top of the head goes left), which is exactly what
        // leaning to the right looks like from the inside — hence the negated strafe.
        output.RotationOffset += new Vector3(0f, 0f, -currentLean * maxAngleDegrees);
        // The head also shifts a little the way it leans, not against it.
        output.PositionOffset += new Vector3(currentLean * positionOffset, 0f, 0f);
    }

    public void Reset()
    {
        currentLean = 0f;
        leanVelocity = 0f;
    }
}
