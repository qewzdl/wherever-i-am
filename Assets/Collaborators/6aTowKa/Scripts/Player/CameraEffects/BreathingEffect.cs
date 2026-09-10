using UnityEngine;

// Idle breathing: a slow time-driven sine that lifts the view a few millimetres on the
// inhale and settles it on the exhale, with a small trailing pitch nod and a very slow,
// unrelated roll so it doesn't read as one clean sine wave.
//
// It fades out as the player picks up speed — standing still it's the only thing moving the
// camera; walking, the head bob takes over and two competing vertical wobbles would just
// fight. There's no exertion input yet (no sprint/scare system), so for now it's only the
// calm resting rhythm.
public sealed class BreathingEffect : ICameraEffect
{
    // Roll runs at an unrelated slow rate so its beat against the main cycle never repeats
    // cleanly.
    private const float RollFrequencyRatio = 0.37f;

    private readonly float radiansPerSecond;
    private readonly float verticalAmplitude;
    private readonly float pitchDegrees;
    private readonly float swayRollDegrees;
    private readonly float moveSuppressSpeed;
    private readonly float suppressSmoothTime;

    // Roll keeps its own accumulator rather than scaling the main phase. Scaling a phase that
    // wraps at 2*PI would make the scaled angle jump on every wrap — a visible flick once per
    // breath — because 2*PI * RollFrequencyRatio is not itself a whole turn.
    private float phase;
    private float rollPhase;
    private float restEnvelope;
    private float restEnvelopeVelocity;

    public BreathingEffect(
        float breathsPerMinute,
        float verticalAmplitude,
        float pitchDegrees,
        float swayRollDegrees,
        float moveSuppressSpeed,
        float suppressSmoothTime)
    {
        radiansPerSecond = Mathf.Max(0.01f, breathsPerMinute) / 60f * Mathf.PI * 2f;
        this.verticalAmplitude = Mathf.Max(0f, verticalAmplitude);
        this.pitchDegrees = Mathf.Max(0f, pitchDegrees);
        this.swayRollDegrees = Mathf.Max(0f, swayRollDegrees);
        this.moveSuppressSpeed = Mathf.Max(0.01f, moveSuppressSpeed);
        this.suppressSmoothTime = Mathf.Max(0f, suppressSmoothTime);
    }

    public string DebugName => "Breathing";

    public bool Enabled { get; set; } = true;

    public float CrouchMultiplier { get; set; } = 1f;

    public float HidingMultiplier { get; set; } = 1f;

    public void Evaluate(in CameraEffectContext context, ref CameraEffectOutput output)
    {
        // 1 at a standstill, 0 once moving at moveSuppressSpeed or faster.
        float targetRest = 1f - Mathf.Clamp01(context.HorizontalSpeed / moveSuppressSpeed);

        restEnvelope = suppressSmoothTime <= 0f
            ? targetRest
            : Mathf.SmoothDamp(restEnvelope, targetRest, ref restEnvelopeVelocity, suppressSmoothTime, Mathf.Infinity, context.DeltaTime);

        phase += radiansPerSecond * context.DeltaTime;
        if (phase > Mathf.PI * 2f)
            phase -= Mathf.PI * 2f;

        rollPhase += radiansPerSecond * RollFrequencyRatio * context.DeltaTime;
        if (rollPhase > Mathf.PI * 2f)
            rollPhase -= Mathf.PI * 2f;

        float scale = restEnvelope * context.Weight;

        float breath = Mathf.Sin(phase);
        float vertical = breath * verticalAmplitude * scale;
        // In phase with the vertical rise, but negated: positive X = look down, and the head
        // tips up as the chest lifts.
        float pitch = -breath * pitchDegrees * scale;
        float roll = Mathf.Sin(rollPhase) * swayRollDegrees * scale;

        output.PositionOffset += new Vector3(0f, vertical, 0f);
        output.RotationOffset += new Vector3(pitch, 0f, roll);
    }

    public void Reset()
    {
        phase = 0f;
        rollPhase = 0f;
        restEnvelope = 0f;
        restEnvelopeVelocity = 0f;
    }
}
