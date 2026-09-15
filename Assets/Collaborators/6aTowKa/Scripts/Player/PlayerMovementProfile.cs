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

    [Tooltip("Seconds of not running it takes to fill again from empty.")]
    [SerializeField, Min(0.1f)] private float recoverySeconds = 14f;

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

    public float RunSeconds => Mathf.Max(0.1f, runSeconds);
    public float RecoverySeconds => Mathf.Max(0.1f, recoverySeconds);
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
