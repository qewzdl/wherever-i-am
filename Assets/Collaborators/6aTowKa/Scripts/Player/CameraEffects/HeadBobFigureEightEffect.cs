using UnityEngine;

// Classic figure-eight head bob (Half-Life/Source-style view bob): a downward dip on every
// footfall plus a side-to-side sway that completes one full left-right cycle per stride (two
// footfalls) — plotting horizontal against vertical offset traces a figure-eight/infinity
// loop. A small roll and yaw, in phase with the sway, plus a pitch nod on each footfall,
// add the weight-shifting-foot-to-foot feel.
//
// Each footfall also rolls a small random multiplier applied to both axes together (one
// footfall = one physical event, so both react to it the same way) — a perfectly repeating
// sine reads as mechanical almost immediately, and real strides never land with identical
// force.
public sealed class HeadBobFigureEightEffect : ICameraEffect
{
    private readonly float verticalAmplitude;
    private readonly float horizontalAmplitude;
    private readonly float rollAmplitudeDegrees;
    private readonly float pitchAmplitudeDegrees;
    private readonly float yawAmplitudeDegrees;
    private readonly float frequency;
    private readonly float crouchFrequency;
    private readonly float fullAmplitudeSpeed;
    private readonly float envelopeSmoothTime;
    private readonly float stepJitterRange;
    private readonly float impactFraction;
    private readonly bool swayFollowsImpact;
    private readonly System.Random random;

    private float phase;
    private float envelope;
    private float envelopeVelocity;
    private int lastStepIndex = -1;
    private float currentStepJitter = 1f;

    // randomSeed is exposed only so tests can get a reproducible jitter sequence; gameplay
    // code should leave it unset.
    public HeadBobFigureEightEffect(
        float verticalAmplitude,
        float horizontalAmplitude,
        float rollAmplitudeDegrees,
        float pitchAmplitudeDegrees,
        float yawAmplitudeDegrees,
        float frequency,
        float crouchFrequency,
        float fullAmplitudeSpeed,
        float envelopeSmoothTime,
        float stepJitterRange,
        float impactFraction,
        bool swayFollowsImpact,
        int? randomSeed = null)
    {
        this.swayFollowsImpact = swayFollowsImpact;
        this.verticalAmplitude = verticalAmplitude;
        this.horizontalAmplitude = horizontalAmplitude;
        this.rollAmplitudeDegrees = rollAmplitudeDegrees;
        this.pitchAmplitudeDegrees = pitchAmplitudeDegrees;
        this.yawAmplitudeDegrees = yawAmplitudeDegrees;
        this.frequency = Mathf.Max(0.01f, frequency);
        this.crouchFrequency = Mathf.Max(0.01f, crouchFrequency);
        this.fullAmplitudeSpeed = Mathf.Max(0.01f, fullAmplitudeSpeed);
        this.envelopeSmoothTime = Mathf.Max(0f, envelopeSmoothTime);
        this.stepJitterRange = Mathf.Clamp01(stepJitterRange);
        // Capped well below 1: the recovery has to cover the full amplitude in whatever is left
        // of the footfall, so a high value squeezes it into less than a frame and the head pops
        // back up. Small values are also the physically right end — a real step lands fast and
        // recovers slowly.
        this.impactFraction = Mathf.Clamp(impactFraction, 0.05f, 0.6f);
        random = randomSeed.HasValue ? new System.Random(randomSeed.Value) : new System.Random();
    }

    public string DebugName => "HeadBobFigureEight";

    public bool Enabled { get; set; } = true;

    public float CrouchMultiplier { get; set; } = 1f;

    public float HidingMultiplier { get; set; } = 1f;

    public void Evaluate(in CameraEffectContext context, ref CameraEffectOutput output)
    {
        float speedFactor = Mathf.Clamp01(context.HorizontalSpeed / fullAmplitudeSpeed);
        float targetEnvelope = context.IsGrounded ? speedFactor : 0f;

        envelope = envelopeSmoothTime <= 0f
            ? targetEnvelope
            : Mathf.SmoothDamp(envelope, targetEnvelope, ref envelopeVelocity, envelopeSmoothTime, Mathf.Infinity, context.DeltaTime);

        // Rhythm is the one thing crouching cannot say through Weight — a crouched stride is
        // shorter and lands more often, which is a change of tempo, not of amplitude — so this
        // is the single place the effect reads IsCrouching itself. Only the rate changes, never
        // the phase, so switching stance mid-step doesn't snap the head anywhere.
        float activeFrequency = frequency;
        if (context.IsCrouching)
            activeFrequency = crouchFrequency;

        // One full phase cycle = one full stride (two footfalls): vertical dips twice per
        // cycle (abs(sin), once per footfall), horizontal sways once per cycle (plain sin,
        // alternating left/right each stride) — together they trace a figure-eight.
        phase += context.HorizontalSpeed * activeFrequency * context.DeltaTime * Mathf.PI * 2f;

        // abs(sin) completes a half-cycle (0 -> 1 -> 0) per footfall, so each multiple of PI
        // marks the start of a new one. Rolling a fresh jitter there — instead of every
        // frame — keeps it constant for the whole footfall rather than fluttering.
        int stepIndex = Mathf.FloorToInt(phase / Mathf.PI);
        if (stepIndex != lastStepIndex)
        {
            lastStepIndex = stepIndex;
            currentStepJitter = 1f + ((float)random.NextDouble() * 2f - 1f) * stepJitterRange;
        }

        // Crouch scaling already lives in context.Weight — the stack folded it in.
        float scaledEnvelope = envelope * context.Weight * currentStepJitter;

        // Sharp drop into the footfall, slower cushioned recovery back to neutral — a plain
        // symmetric abs(sin) rises and falls at the same rate and reads as floaty, not a step
        // landing. impactFraction is the portion of the footfall spent falling; the rest is
        // the slower rise back up.
        float withinFootfall = Mathf.Repeat(phase, Mathf.PI) / Mathf.PI;
        float impactDepth;
        if (withinFootfall < impactFraction)
        {
            float fallProgress = withinFootfall / impactFraction;
            impactDepth = Mathf.Sin(fallProgress * Mathf.PI * 0.5f);
        }
        else
        {
            // Eases to a standstill at the footfall boundary instead of arriving at full speed.
            // A plain cos is steepest right at the boundary, so the head would slam upward and
            // reverse into the next drop in the same frame — at a high impactFraction, where
            // the recovery window is tiny, that reversal reads as a teleport.
            float riseProgress = (withinFootfall - impactFraction) / (1f - impactFraction);
            impactDepth = 1f - Mathf.SmoothStep(0f, 1f, riseProgress);
        }

        // The sway channels (horizontal, roll, yaw) run off a warped phase rather than the raw
        // one, so they inherit the dip's fast-then-slow rhythm instead of gliding evenly: half
        // the swing is spent during the impact, the rest during the slower recovery. The warp
        // is continuous at the footfall boundary (0 -> 0, 1 -> 1), so nothing snaps.
        float swayPhase = phase;
        if (swayFollowsImpact)
        {
            float shapedWithinFootfall;
            if (withinFootfall < impactFraction)
            {
                shapedWithinFootfall = 0.5f * (withinFootfall / impactFraction);
            }
            else
            {
                // Same easing as the recovery above, for the same reason — the sway has to
                // settle into the boundary rather than whip through it.
                float riseProgress = (withinFootfall - impactFraction) / (1f - impactFraction);
                shapedWithinFootfall = 0.5f + 0.5f * Mathf.SmoothStep(0f, 1f, riseProgress);
            }

            swayPhase = (stepIndex + shapedWithinFootfall) * Mathf.PI;
        }

        float sway = Mathf.Sin(swayPhase);

        float vertical = -impactDepth * verticalAmplitude * scaledEnvelope;
        float horizontal = sway * horizontalAmplitude * scaledEnvelope;
        // Tilts the same way the sway leans, same phase — reads as weight shifting foot to foot.
        float roll = sway * rollAmplitudeDegrees * scaledEnvelope;
        // Nose dips into every footfall, in phase with the vertical drop (positive X = look down).
        float pitch = impactDepth * pitchAmplitudeDegrees * scaledEnvelope;
        // Turns with the sway, once per stride — the head follows the lead foot.
        float yaw = sway * yawAmplitudeDegrees * scaledEnvelope;

        output.PositionOffset += new Vector3(horizontal, vertical, 0f);
        output.RotationOffset += new Vector3(pitch, yaw, roll);
    }

    public void Reset()
    {
        phase = 0f;
        envelope = 0f;
        envelopeVelocity = 0f;
        lastStepIndex = -1;
        currentStepJitter = 1f;
    }
}
