using UnityEngine;

// Camera roll (bank) driven by how fast the player is turning the mouse, not by movement.
// Sharp turns bank hard, slow turns barely bank at all, and the roll relaxes back to zero
// on its own — same smoothing approach CameraLook already uses for pitch/yaw.
public sealed class RollEffect : ICameraEffect
{
    private float maxRollDegrees;
    private float yawRateForMaxRoll;
    private float smoothTime;

    private float currentRoll;
    private float rollVelocity;

    public RollEffect(float maxRollDegrees, float yawRateForMaxRoll, float smoothTime)
    {
        ApplyTuning(maxRollDegrees, yawRateForMaxRoll, smoothTime);
    }

    // The authored tuning, separated from construction so the inspector can hand it over
    // again while the game runs. Only numbers the effect multiplies by - nothing here seeds
    // state, so a value can change mid-motion without the effect losing its place.
    public void ApplyTuning(float maxRollDegrees, float yawRateForMaxRoll, float smoothTime)
    {
        this.maxRollDegrees = maxRollDegrees;
        this.yawRateForMaxRoll = Mathf.Max(1f, yawRateForMaxRoll);
        this.smoothTime = Mathf.Max(0f, smoothTime);
    }

    public string DebugName => "Roll";

    public bool Enabled { get; set; } = true;

    public float CrouchMultiplier { get; set; } = 1f;

    public float HidingMultiplier { get; set; } = 1f;

    public float UserMultiplier { get; set; } = 1f;

    public void Evaluate(in CameraEffectContext context, ref CameraEffectOutput output)
    {
        float normalizedRate = Mathf.Clamp(context.YawRate / yawRateForMaxRoll, -1f, 1f);
        float targetRoll = -normalizedRate * maxRollDegrees * context.Weight;

        currentRoll = smoothTime <= 0f
            ? targetRoll
            : Mathf.SmoothDamp(currentRoll, targetRoll, ref rollVelocity, smoothTime, Mathf.Infinity, context.DeltaTime);

        output.RotationOffset += new Vector3(0f, 0f, currentRoll);
    }

    public void Reset()
    {
        currentRoll = 0f;
        rollVelocity = 0f;
    }
}
