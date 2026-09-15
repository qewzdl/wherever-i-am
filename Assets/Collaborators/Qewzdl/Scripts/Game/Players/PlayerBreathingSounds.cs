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
// Running itself is silent. There was a second clip for it once, playing from
// the first stride onwards, and it made the quiet half of the tank as noisy as
// the spent half - which left the heavy breathing nothing to arrive against.
// Silence is what makes it mean something.
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

    [Header("Sound")]
    [Tooltip("Heard whenever there is less than windedBelow left in the tank.")]
    [SerializeField] private SoundEffect windedBreath;

    [Tooltip(
        "Share of the tank under which the breathing starts. Separate from " +
        "the point at which running is refused, which is empty: being short " +
        "of breath begins well before being out of it, and this is the only " +
        "warning the player gets that the tank is running down - there is no " +
        "bar to glance at.")]
    [SerializeField, Range(0f, 1f)] private float windedBelow = 0.35f;

    [Tooltip("Seconds between the heavy breaths.")]
    [SerializeField, Min(0.1f)] private float windedInterval = 1.8f;

    private NetworkObject networkObject;
    private IGameplaySoundService gameplaySound;
    private float nextBreathTime;

    private void Awake()
    {
        if (controller == null)
            controller = GetComponentInParent<PlayerController>();

        networkObject = GetComponentInParent<NetworkObject>();
    }

    private void Update()
    {
        if (controller == null || windedBreath == null || !IsOurs())
            return;

        // The lockout counts as well as the threshold. They almost always
        // agree - the lockout is the threshold's floor - but a profile tuned
        // so that running is refused before the breathing starts would
        // otherwise take the speed away in silence.
        bool isWinded = controller.StaminaNormalized <= windedBelow ||
                        controller.IsWinded;

        if (!isWinded)
        {
            // Back above the line. The next breath starts from whenever that
            // happens rather than from a clock left running, so the first one
            // after a sprint lands when the sprint costs something.
            nextBreathTime = Time.time;
            return;
        }

        if (Time.time < nextBreathTime)
            return;

        nextBreathTime = Time.time + windedInterval;

        gameplaySound ??= AudioServices.Gameplay();
        gameplaySound?.Play2D(windedBreath);
    }

    // A body somebody else is driving breathes for them, on their machine.
    private bool IsOurs()
    {
        return networkObject == null ||
               !networkObject.IsSpawned ||
               networkObject.IsOwner;
    }
}
