using Unity.Netcode;
using UnityEngine;

// The only instrument on the dashboard.
//
// Stamina is never drawn, on purpose: a bar turns pacing into arithmetic, and
// a player watching a number is not watching the house. What is left is heard
// instead - once the tank drops past windedBelow you start hearing yourself,
// and you go on hearing yourself after the running stops, until enough has
// come back. That last part is why the mechanic is audible rather than
// visible: being out of breath is a state you are stuck in, and you find out
// you are still in it by listening.
//
// Running itself is silent. There was a clip for it once, playing from the
// first stride onwards, and it made the quiet half of the tank as noisy as the
// spent half - which left the heavy breathing nothing to arrive against.
//
// ---------------------------------------------------------------------------
//
// The rhythm is a small state machine rather than one clip on a timer, because
// breathing is not evenly spaced and a loop that is gives itself away inside
// half a minute.
//
// An inhale and the exhale after it are one breath: the gap between them is
// almost nothing. The pause belongs BETWEEN breaths, after the exhale. Space
// them equally and it stops sounding like lungs and starts sounding like a
// metronome with a chest infection.
//
// A cough only ever follows an exhale. You cannot cough on the way in - the
// air has to be going out for there to be a cough at all - and afterwards the
// breath in comes quickly, because coughing costs the air you had. Both of
// those fall out of the transitions below rather than being special-cased.
//
// An extra exhale is the third thing that happens to a winded person: the
// chest not quite finishing in one go. It sits further from the exhale before
// it than an exhale sits from its inhale, which is the whole reason it reads
// as a second push rather than as a stutter in the file.
//
// Your own breath only, and played flat rather than positioned, because it is
// not a sound in the room - it is a sound in your head. What other players
// hear of you is your footsteps, which the enemy hears too and which are
// derived from a transform everybody already has. Breath would need a message
// of its own, and a teammate's lungs are not worth one yet.
[DisallowMultipleComponent]
public sealed class PlayerBreathingSounds : MonoBehaviour
{
    [Header("References")]
    [SerializeField] private PlayerController controller;

    // How the breath reaches the enemy. Left empty, breathing is heard by
    // nobody but the player making it, which is what it was until now.
    [SerializeField] private GameplayNoiseEmitter noiseEmitter;

    [Header("Sounds")]
    [Tooltip("One breath in. Several clips inside the asset, or it is a loop.")]
    [SerializeField] private SoundEffect inhale;

    [Tooltip("One breath out.")]
    [SerializeField] private SoundEffect exhale;

    [Tooltip("One cough. Left empty, nobody ever coughs.")]
    [SerializeField] private SoundEffect cough;

    [Header("Heard By The Enemy")]
    [Tooltip(
        "The noise a breath makes. Quiet and short-range - being out of " +
        "breath should cost a player the room they are in, not the floor.")]
    [SerializeField] private GameplayNoisePreset breathNoise;

    [Tooltip(
        "The noise a cough makes. This is the betrayal: loud, involuntary, " +
        "and paid for by a decision made half a minute earlier.")]
    [SerializeField] private GameplayNoisePreset coughNoise;

    [Header("When")]
    [Tooltip(
        "Share of the tank under which the breathing starts. Separate from " +
        "the point at which running is refused, which is empty: being short " +
        "of breath begins well before being out of it, and this is the only " +
        "warning the player gets that the tank is running down - there is no " +
        "bar to glance at.")]
    [SerializeField, Range(0f, 1f)] private float windedBelow = 0.35f;

    [Header("Rhythm")]
    [Tooltip("Inhale to the exhale that belongs to it. The shortest gap there is.")]
    [SerializeField, Min(0.05f)] private float inhaleToExhale = 0.4f;

    [Tooltip("Exhale to the next breath in. This is the pause between breaths.")]
    [SerializeField, Min(0.05f)] private float exhaleToInhale = 1.4f;

    [Tooltip(
        "Exhale to a second exhale. Longer than inhaleToExhale or the two " +
        "run together and read as one file playing twice.")]
    [SerializeField, Min(0.05f)] private float exhaleToExhale = 0.75f;

    [Tooltip("Exhale to the cough that comes out of it.")]
    [SerializeField, Min(0.05f)] private float exhaleToCough = 0.25f;

    [Tooltip(
        "Cough to the second cough. Shorter than anything else here: two " +
        "coughs in a fit are one event, not two decisions.")]
    [SerializeField, Min(0.05f)] private float coughToCough = 0.3f;

    [Tooltip("Cough to the breath in after it. Short - coughing costs air.")]
    [SerializeField, Min(0.05f)] private float coughToInhale = 0.5f;

    [Header("How Often")]
    [Tooltip("Chance an exhale is followed by another one instead of a pause.")]
    [SerializeField, Range(0f, 1f)] private float doubleExhaleChance = 0.25f;

    [Tooltip(
        "Chance an exhale turns into a cough. Rolled only on exhales that are " +
        "not already second ones, so a fit of coughing cannot build.")]
    [SerializeField, Range(0f, 1f)] private float coughChance = 0.12f;

    [Tooltip(
        "Share of the tank this spell of running has to have cost before any " +
        "cough is possible. Coughing is what hard work does to you, not what " +
        "being slightly short of breath does - jogging to a door and stopping " +
        "should never produce one, however unlucky the rolls.")]
    [SerializeField, Range(0f, 1f)] private float coughAfterSpending = 0.4f;

    [Tooltip(
        "Given that a cough happens, the chance it is a double rather than a " +
        "single. Above a half on purpose: one isolated cough sounds like " +
        "clearing your throat, and two is what a chest actually does - the " +
        "first does not finish the job.")]
    [SerializeField, Range(0f, 1f)] private float doubleCoughChance = 0.65f;

    private NetworkObject networkObject;
    private IGameplaySoundService gameplaySound;

    private readonly BreathRhythm rhythm = new();
    private float nextBreathTime;

    private void Awake()
    {
        if (controller == null)
            controller = GetComponentInParent<PlayerController>();

        networkObject = GetComponentInParent<NetworkObject>();
    }

    private void Update()
    {
        if (controller == null || !IsOurs())
            return;

        // The lockout counts as well as the threshold. They almost always
        // agree - the lockout is the threshold's floor - but a profile tuned
        // so that running is refused before the breathing starts would
        // otherwise take the speed away in silence.
        bool isWinded = controller.StaminaNormalized <= windedBelow ||
                        controller.IsWinded;

        if (!isWinded)
        {
            // Back above the line, and the next spell starts from a breath in
            // rather than from wherever the last one was interrupted. Half a
            // cycle left over from two minutes ago is not a state worth
            // keeping.
            rhythm.Reset();
            nextBreathTime = Time.time;
            return;
        }

        if (Time.time < nextBreathTime)
            return;

        Play(rhythm.Current);

        BreathRhythm.Step played = rhythm.Current;

        rhythm.Advance(
            new BreathRhythm.Chances(doubleExhaleChance, coughChance, doubleCoughChance),
            CanCough(),
            () => Random.value);

        nextBreathTime = Time.time + GapAfter(played, rhythm.Current, rhythm.IsRepeat);
    }

    // How long to wait, given what was just played and what comes next. The
    // order is BreathRhythm's business; the clock is this component's, because
    // the gaps are tuning and the order is rules.
    private float GapAfter(
        BreathRhythm.Step played,
        BreathRhythm.Step next,
        bool isRepeat)
    {
        if (next == BreathRhythm.Step.Cough)
            return isRepeat ? coughToCough : exhaleToCough;

        if (next == BreathRhythm.Step.Exhale)
            return isRepeat ? exhaleToExhale : inhaleToExhale;

        return played == BreathRhythm.Step.Cough ? coughToInhale : exhaleToInhale;
    }

    // Coughing is earned rather than rolled for.
    //
    // The chance alone would spread coughs evenly across every winded moment,
    // which puts them after a light jog as readily as after a sprint that
    // emptied the tank - and a cough after a light jog reads as a sick person
    // rather than a tired one. The gate is how much this effort actually cost,
    // not how empty the tank happens to be: a third spent from full and a
    // third spent from half are the same work, and the tank level cannot tell
    // them apart.
    private bool CanCough()
    {
        return HasClip(cough) &&
               controller.StaminaSpentInBurst >= coughAfterSpending;
    }

    // A missing clip costs its sound and nothing else. The rhythm carries on
    // around the hole, so half-filled slots while somebody is still recording
    // sound thin rather than broken - and a cough nobody has recorded is never
    // chosen at all, rather than being chosen and silently skipped.
    private void Play(BreathRhythm.Step breath)
    {
        SoundEffect sound = breath switch
        {
            BreathRhythm.Step.Inhale => inhale,
            BreathRhythm.Step.Exhale => exhale,
            _ => cough,
        };

        // The noise goes out whether or not there is a clip for it. They are
        // two audiences for one event - the player hearing themselves, and the
        // thing in the house hearing them - and a missing recording is not a
        // reason for the second one to go deaf.
        EmitNoise(breath);

        if (!HasClip(sound))
            return;

        gameplaySound ??= AudioServices.Gameplay();
        gameplaySound?.Play2D(sound);
    }

    // Asked for rather than emitted directly, because this runs on the owner
    // and the owner is a client. The server decides whether to believe it -
    // see ServerObservedExertionNoiseValidator, which will not accept a breath
    // from somebody it has not watched running.
    //
    // Exactly the same instant as the sound, which is the whole reason the
    // client asks instead of the server inventing its own rhythm: a cough the
    // server timed separately would land somewhere else, and a player would
    // hear themselves cough twice.
    private void EmitNoise(BreathRhythm.Step breath)
    {
        if (noiseEmitter == null)
            return;

        GameplayNoisePreset preset = breath == BreathRhythm.Step.Cough
            ? coughNoise
            : breathNoise;

        if (preset != null)
            noiseEmitter.RequestEmitFromOwner(preset);
    }

    private static bool HasClip(SoundEffect sound)
    {
        return sound != null;
    }

    private static bool Roll(float chance)
    {
        return chance > 0f && Random.value < chance;
    }

    // A body somebody else is driving breathes for them, on their machine.
    private bool IsOurs()
    {
        return networkObject == null ||
               !networkObject.IsSpawned ||
               networkObject.IsOwner;
    }
}
