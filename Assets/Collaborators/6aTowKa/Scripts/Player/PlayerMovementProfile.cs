using UnityEngine;

// What a player's legs can do, in one asset.
//
// The speeds live here rather than on the controller because they are not only
// the controller's business any more: how loud a footstep is depends on which
// of them the player is using, and that judgement is made on the server by a
// component that has never heard of PlayerController. Two places holding their
// own copy of "walking is five" is a pair of numbers that agree until the first
// time somebody balances one of them.
[CreateAssetMenu(
    fileName = "PlayerMovementProfile",
    menuName = "Wherever I Am/Player/Player Movement Profile")]
public sealed class PlayerMovementProfile : ScriptableObject
{
    [Header("Speeds")]
    [Tooltip("What walking is, before anything scales it.")]
    [SerializeField, Min(0f)] private float walkSpeed = 5f;

    // Multipliers rather than speeds, because walking is not a constant at
    // runtime: dragging something heavy scales the controller's base speed, and
    // a run written as an absolute would ignore that - you would sprint a
    // wardrobe across the house at full pace. Written this way the penalty
    // composes, and a runner dragging a wardrobe is genuinely slower and
    // genuinely quieter, without anybody arranging for it.
    [SerializeField, Min(1f)] private float runSpeedMultiplier = 1.5f;

    [Tooltip(
        "Crouching is the quiet gait, so this is what silence costs. Applied " +
        "instead of running rather than on top of it: a crouched player moves " +
        "at one speed whatever else they are holding down.")]
    [SerializeField, Range(0f, 1f)] private float crouchSpeedMultiplier = 0.55f;

    [Header("Stamina")]
    [Tooltip("Seconds of running a full tank buys.")]
    [SerializeField, Min(0.1f)] private float runSeconds = 8f;

    [Tooltip(
        "Seconds of standing still to fill from empty, before the curve and " +
        "the per-condition rates below are applied. Those multiply it, so " +
        "this is the best case rather than the only case.")]
    [SerializeField, Min(0.1f)] private float recoverySeconds = 14f;

    [Tooltip(
        "How fast the tank fills at each point along itself, as a " +
        "multiplier on the base rate. Read at the CURRENT level, so the " +
        "left of the curve is an empty tank and the right is a full one. " +
        "Flat at one is the straight line it used to be. Low on the left " +
        "makes emptying it something you regret for a while; low on the " +
        "right makes the last of it slow to come back, which is gentler " +
        "moment to moment and harsher over a whole match. An empty curve " +
        "counts as flat.")]
    [SerializeField] private AnimationCurve recoveryCurve = AnimationCurve.Linear(0f, 1f, 1f, 1f);

    [Header("Recovery By Condition")]
    [Tooltip("Standing still. The reference the other three are read against.")]
    [SerializeField, Range(0f, 3f)] private float recoveryStanding = 1f;

    [Tooltip("Crouching still - the best rest there is, and the slowest.")]
    [SerializeField, Range(0f, 3f)] private float recoveryCrouchingStill = 1.4f;

    [SerializeField, Range(0f, 3f)] private float recoveryWalking = 0.6f;

    [SerializeField, Range(0f, 3f)] private float recoveryCrouchWalking = 0.85f;

    [Tooltip(
        "What running is worth on an empty tank, as a share of a fresh run. " +
        "Never zero: a run that stops dead reads as the game taking the key " +
        "away, and the player cannot see why because there is no bar to look " +
        "at. Slowing down is the same information, given in the one channel " +
        "they have - how fast the world is going past.")]
    [SerializeField, Range(0.1f, 1f)] private float exhaustedRunScale = 0.45f;

    [Tooltip(
        "How much has to come back before an emptied tank will run again. " +
        "Without it a player taps the key for a step of speed each time it " +
        "ticks over zero, which is faster than walking and looks absurd.")]
    [SerializeField, Range(0f, 1f)] private float recoveredEnoughToRun = 0.25f;

    [Header("Tiredness")]
    [Tooltip(
        "Share of the tank under which everything slows down, not only " +
        "running. Walking away from something while winded should not be the " +
        "same walk as walking towards it fresh.")]
    [SerializeField, Range(0f, 1f)] private float tiredBelow = 0.35f;

    [Tooltip(
        "What every gait is worth on an empty tank. Compounds with the run " +
        "scale above, so a spent sprint is slowed twice - once for being a " +
        "sprint nobody has the breath for, once for being tired. Keep it " +
        "mild.")]
    [SerializeField, Range(0.25f, 1f)] private float exhaustedOverallScale = 0.85f;

    public float TiredBelow => Mathf.Clamp01(tiredBelow);
    public float ExhaustedOverallScale => Mathf.Clamp(exhaustedOverallScale, 0.25f, 1f);

    // One above the threshold, sliding to the exhausted share at empty.
    public float OverallScaleAtStamina(float normalizedStamina)
    {
        float threshold = TiredBelow;

        if (threshold <= 0f || normalizedStamina >= threshold)
            return 1f;

        return Mathf.Lerp(
            ExhaustedOverallScale,
            1f,
            Mathf.Clamp01(normalizedStamina / threshold));
    }

    public float RunSeconds => Mathf.Max(0.1f, runSeconds);
    public float RecoverySeconds => Mathf.Max(0.1f, recoverySeconds);

    // Rest is a thing you choose, and the four ways of choosing it are worth
    // different amounts. Crouching still is the best because it is also the
    // most expensive: you are low, slow and committed while you take it.
    public float RecoveryRateFor(bool isCrouching, bool isMoving)
    {
        if (isCrouching)
            return isMoving ? recoveryCrouchWalking : recoveryCrouchingStill;

        return isMoving ? recoveryWalking : recoveryStanding;
    }

    // A quarter, and the number matters more than it looks.
    //
    // The curve multiplies the fill RATE and is read at the current level, so
    // time is the integral of one over it. A dip does not cost proportionally
    // - it costs inversely, and it costs it across the whole stretch it
    // covers. Somebody drawing a curve down to a twentieth is picturing "a bit
    // slower" and getting twenty times longer, which is how a fourteen second
    // refill became sixty-nine seconds of standing still before the game would
    // let them run again.
    //
    // At a quarter the worst a curve can do is quadruple a stretch. That is a
    // punishment somebody can feel and still believe; it is also the floor
    // that keeps zero from meaning never, which is what it meant before this
    // existed at all.
    public const float MinimumRecoveryRate = 0.25f;

    // A curve with nothing in it is a curve nobody has drawn, and the honest
    // reading of that is "no opinion" rather than "multiply by zero".
    public float RecoveryCurveAt(float normalizedStamina)
    {
        if (recoveryCurve == null || recoveryCurve.length == 0)
            return 1f;

        return Mathf.Max(
            MinimumRecoveryRate,
            recoveryCurve.Evaluate(Mathf.Clamp01(normalizedStamina)));
    }

    // Whether the shape somebody drew would have stranded the player at empty
    // if the floor were not there. Worth saying out loud in the editor rather
    // than quietly correcting and letting them believe the curve is what they
    // drew.
    public bool RecoveryCurveStrandsAtEmpty()
    {
        return recoveryCurve != null &&
               recoveryCurve.length > 0 &&
               recoveryCurve.Evaluate(0f) < MinimumRecoveryRate;
    }
    public float ExhaustedRunScale => Mathf.Clamp(exhaustedRunScale, 0.1f, 1f);
    public float RecoveredEnoughToRun => Mathf.Clamp01(recoveredEnoughToRun);

    // Full tank runs at the full multiplier, empty at the exhausted share of
    // it, and everything between is a straight line. There is no bar, so the
    // player reads this as the world slowing down - which is what being out of
    // breath is.
    public float RunScaleAtStamina(float normalizedStamina)
    {
        float scale = Mathf.Lerp(
            ExhaustedRunScale,
            1f,
            Mathf.Clamp01(normalizedStamina));

        return 1f + (RunSpeedMultiplier - 1f) * scale;
    }

    public float WalkSpeed => Mathf.Max(0f, walkSpeed);
    public float RunSpeedMultiplier => Mathf.Max(1f, runSpeedMultiplier);
    public float CrouchSpeedMultiplier => Mathf.Clamp01(crouchSpeedMultiplier);

    public float RunSpeed => WalkSpeed * RunSpeedMultiplier;
    public float CrouchSpeed => WalkSpeed * CrouchSpeedMultiplier;

    // The thresholds are worked out rather than typed.
    //
    // A number somebody enters by hand is a fourth and fifth copy of the
    // ladder, free to drift away from it the moment a speed is tuned. Halfway
    // between two gaits is also the best place to stand: the speed a server
    // measures is a position divided by a frame, and it wobbles - through
    // acceleration, through a shove from a carried item, through whatever the
    // network did to the last two positions. Sitting at the midpoint leaves
    // the widest margin on both sides before a wobble is heard as the wrong
    // gait.
    public float HeardAsRunningFrom => (WalkSpeed + RunSpeed) * 0.5f;
    public float HeardAsWalkingFrom => (CrouchSpeed + WalkSpeed) * 0.5f;

    // The one question the noise side asks. Given how fast somebody is
    // actually travelling, how are they moving?
    public PlayerGait GaitFromObservedSpeed(float observedSpeed)
    {
        if (observedSpeed >= HeardAsRunningFrom)
            return PlayerGait.Running;

        if (observedSpeed >= HeardAsWalkingFrom)
            return PlayerGait.Walking;

        return PlayerGait.Silent;
    }

    // Applied to whatever the controller's base speed currently is, which is
    // not always walkSpeed - see the note on the multipliers.
    public float ScaleFor(bool isCrouching, bool isRunning, float normalizedStamina)
    {
        if (isCrouching)
            return CrouchSpeedMultiplier;

        return isRunning ? RunScaleAtStamina(normalizedStamina) : 1f;
    }

    // Everything the legs are worth right now: the gait, and then how tired
    // the legs are.
    public float TotalScaleFor(bool isCrouching, bool isRunning, float normalizedStamina)
    {
        return ScaleFor(isCrouching, isRunning, normalizedStamina) *
               OverallScaleAtStamina(normalizedStamina);
    }

#if UNITY_EDITOR
    private void OnValidate()
    {
        walkSpeed = Mathf.Max(0f, walkSpeed);
        runSpeedMultiplier = Mathf.Max(1f, runSpeedMultiplier);
        crouchSpeedMultiplier = Mathf.Clamp01(crouchSpeedMultiplier);

        // A ladder whose rungs touch makes every band above meaningless: two
        // gaits at one speed cannot be told apart by the only thing that tells
        // them apart.
        if (Mathf.Approximately(runSpeedMultiplier, 1f))
            Debug.LogWarning($"{name}: running is the same speed as walking.", this);

        if (Mathf.Approximately(crouchSpeedMultiplier, 1f))
            Debug.LogWarning($"{name}: crouching is the same speed as walking.", this);
    }
#endif
}
