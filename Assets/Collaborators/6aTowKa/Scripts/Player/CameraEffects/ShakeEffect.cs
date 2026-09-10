using UnityEngine;

// Impulse shake. Every other effect in the stack is derived from what the player is doing;
// this one is told. External code calls AddImpulse when something hits the view - a door
// slammed, a grab, a scare - and the effect plays that impulse out over its own lifetime.
// It never inspects the context looking for a reason to fire.
//
// Impulses are kept apart rather than poured into one shared charge, because strength and
// length are separate things: a distant rumble is weak and long, a slam is hard and short,
// and a single decaying value cannot say both.
public sealed class ShakeEffect : ICameraEffect
{
    // Unity's Perlin noise repeats every 256 units, so the sample position can be wrapped
    // there and land on exactly the same value - no pop mid-shake, and the accumulator never
    // grows large enough to lose float precision over a long session.
    private const float NoisePeriod = 256f;

    // Each channel reads its own stripe of the noise field. Sampling one stripe at different
    // time offsets instead would make every axis the same wave delayed, which reads as a
    // single shove rather than a rattle.
    private const float ChannelSpacing = 37.13f;

    // Fixed capacity, so a frame of shaking never allocates. Eight overlapping impulses is
    // already more than the eye can pick apart.
    private const int MaxImpulses = 8;

    private struct Impulse
    {
        public float Strength;
        public float Duration;
        public float Elapsed;
    }

    private float pitchDegrees;
    private float yawDegrees;
    private float rollDegrees;
    private float positionAmplitude;
    private float fovAmplitude;
    private float frequency;
    private float falloffExponent;

    // Stays out of ApplyTuning: it is where this instance reads the noise field, not how hard
    // it shakes. Moving it mid-shake would jump the view to an unrelated part of the field.
    private readonly float noiseOrigin;

    private readonly Impulse[] impulses = new Impulse[MaxImpulses];
    private int impulseCount;
    private float envelope;
    private float noiseSample;

    public ShakeEffect(
        float pitchDegrees,
        float yawDegrees,
        float rollDegrees,
        float positionAmplitude,
        float fovAmplitude,
        float frequency,
        float falloffExponent,
        float? noiseOrigin = null)
    {
        // A random starting point per instance, so two players caught in the same blast do
        // not shake in lockstep. Tests pass a fixed origin to get a repeatable sequence.
        if (noiseOrigin.HasValue)
            this.noiseOrigin = noiseOrigin.Value;
        else
            this.noiseOrigin = Random.Range(0f, NoisePeriod);

        ApplyTuning(
            pitchDegrees,
            yawDegrees,
            rollDegrees,
            positionAmplitude,
            fovAmplitude,
            frequency,
            falloffExponent);
    }

    // See RollEffect.ApplyTuning. falloffExponent shapes the fade inside an impulse, so
    // changing it while one is playing bends the tail it is already partway through - only
    // reachable from the inspector, where that is exactly what you are looking at.
    public void ApplyTuning(
        float pitchDegrees,
        float yawDegrees,
        float rollDegrees,
        float positionAmplitude,
        float fovAmplitude,
        float frequency,
        float falloffExponent)
    {
        this.pitchDegrees = Mathf.Max(0f, pitchDegrees);
        this.yawDegrees = Mathf.Max(0f, yawDegrees);
        this.rollDegrees = Mathf.Max(0f, rollDegrees);
        this.positionAmplitude = Mathf.Max(0f, positionAmplitude);
        this.fovAmplitude = Mathf.Max(0f, fovAmplitude);
        this.frequency = Mathf.Max(0.01f, frequency);
        // Below 1 an impulse would swell towards its end instead of fading.
        this.falloffExponent = Mathf.Max(1f, falloffExponent);
    }

    public string DebugName => "Shake";

    public bool Enabled { get; set; } = true;

    public float CrouchMultiplier { get; set; } = 1f;

    public float HidingMultiplier { get; set; } = 1f;

    public float UserMultiplier { get; set; } = 1f;

    // Combined strength of everything currently playing, 0..1. Exposed for debug readouts
    // and tests; it is the value the last Evaluate rendered with.
    public float Envelope => envelope;

    public int ActiveImpulses => impulseCount;

    // The only way this effect ever starts. Strength is a fraction of the authored
    // amplitudes, duration is how long this particular impulse lives.
    public void AddImpulse(float strength, float duration)
    {
        strength = Mathf.Clamp01(strength);

        if (strength <= 0f || duration <= 0f)
            return;

        Impulse impulse = new Impulse
        {
            Strength = strength,
            Duration = duration,
            Elapsed = 0f
        };

        if (impulseCount < MaxImpulses)
        {
            impulses[impulseCount] = impulse;
            impulseCount++;
            return;
        }

        // Full: drop whichever live impulse is contributing least right now. Dropping the
        // oldest instead would sometimes throw away a long rumble that is still visible in
        // favour of a hit that has already spent itself.
        int weakest = 0;
        float weakestContribution = Contribution(impulses[0]);

        for (int i = 1; i < impulseCount; i++)
        {
            float contribution = Contribution(impulses[i]);

            if (contribution >= weakestContribution)
                continue;

            weakest = i;
            weakestContribution = contribution;
        }

        // The newcomer starts at its full strength, so if even that is below what it would
        // evict, nothing is gained by letting it in.
        if (strength <= weakestContribution)
            return;

        impulses[weakest] = impulse;
    }

    public void Evaluate(in CameraEffectContext context, ref CameraEffectOutput output)
    {
        if (impulseCount == 0)
            return;

        envelope = CombineEnvelope();

        if (envelope > 0f)
        {
            noiseSample += frequency * context.DeltaTime;
            if (noiseSample > NoisePeriod)
                noiseSample -= NoisePeriod;

            float magnitude = envelope * context.Weight;

            float pitch = Noise(0) * pitchDegrees * magnitude;
            float yaw = Noise(1) * yawDegrees * magnitude;
            float roll = Noise(2) * rollDegrees * magnitude;
            float horizontal = Noise(3) * positionAmplitude * magnitude;
            float vertical = Noise(4) * positionAmplitude * magnitude;

            output.RotationOffset += new Vector3(pitch, yaw, roll);
            output.PositionOffset += new Vector3(horizontal, vertical, 0f);
            output.FovOffset += Noise(5) * fovAmplitude * magnitude;
        }

        // Time moves on after drawing, so the first frame of an impulse still renders at full
        // strength instead of arriving already aged by a frame.
        AdvanceImpulses(context.DeltaTime);
    }

    public void Reset()
    {
        impulseCount = 0;
        envelope = 0f;
        noiseSample = 0f;
    }

    // Saturating sum: 1 minus the product of what each impulse leaves untouched. Two hits of
    // 0.5 give 0.75, three give 0.875 - a burst reads harder than a single hit, yet the total
    // approaches 1 without ever passing it, so the authored amplitudes stay the ceiling with
    // no clamp anywhere. A plain sum would need one, and clamping makes two hits look
    // identical to five.
    private float CombineEnvelope()
    {
        float remaining = 1f;

        for (int i = 0; i < impulseCount; i++)
            remaining *= 1f - Contribution(impulses[i]);

        return 1f - remaining;
    }

    private float Contribution(Impulse impulse)
    {
        float progress = Mathf.Clamp01(impulse.Elapsed / impulse.Duration);
        return impulse.Strength * Mathf.Pow(1f - progress, falloffExponent);
    }

    private void AdvanceImpulses(float deltaTime)
    {
        for (int i = impulseCount - 1; i >= 0; i--)
        {
            impulses[i].Elapsed += deltaTime;

            if (impulses[i].Elapsed < impulses[i].Duration)
                continue;

            // Order in the array means nothing, so a finished impulse is overwritten by the
            // last live one instead of shifting the tail down.
            impulseCount--;
            impulses[i] = impulses[impulseCount];
        }
    }

    // Perlin returns 0..1, so it is recentred to -1..1. Left as-is the shake would sit
    // off-centre and hold the camera away from its rest pose for as long as it ran.
    private float Noise(int channel)
    {
        return Mathf.PerlinNoise(noiseOrigin + channel * ChannelSpacing, noiseSample) * 2f - 1f;
    }
}
