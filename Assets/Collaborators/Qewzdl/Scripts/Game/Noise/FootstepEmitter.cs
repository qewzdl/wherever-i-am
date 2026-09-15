using Unity.Netcode;
using UnityEngine;

// Footsteps: what the enemy hears, and what everybody hears.
//
// Both come from the same judgement - how fast is this player actually moving
// - and that judgement is never sent. It is made from the transform, because
// the alternative is the owner telling us which kind of step it is taking,
// which hands the quietest option to anybody willing to edit their client.
// GameplayNoiseEmitter's allowlist would not catch it: it checks that a preset
// is one this object may request, and the movement validator beside it checks
// only that the player really travelled, never which preset was asked for. A
// client that always claimed to be creeping while sprinting would pass both.
//
// So nothing is claimed. Being quiet means actually moving slowly, and slowly
// is a thing anybody watching can see.
//
// Which is also why the sound needs no message of its own. The transform is
// replicated to everyone, so every client can reach the same conclusion from
// the same numbers in the same profile, and does - no rpc, nothing on the wire
// per step, and the footsteps you hear from somebody else come from the
// position you are watching them stand in. They cannot drift apart, because
// they are the same fact read twice.
//
// The stride phase will differ slightly between machines. Nobody hears two
// copies of one step, so nobody can tell.
[DisallowMultipleComponent]
[RequireComponent(typeof(NetworkObject))]
public sealed class FootstepEmitter : NetworkBehaviour
{
    [Header("Observation")]
    [Tooltip("Left empty, this object's own transform is watched.")]
    [SerializeField] private Transform observedTransform;

    // The same asset the legs are driven from, so the speed that counts as
    // running here is the speed running actually is.
    [SerializeField] private PlayerMovementProfile movement;

    [Header("Heard by the enemy")]
    [SerializeField] private GameplayNoiseEmitter noiseEmitter;
    [SerializeField] private GameplayNoisePreset runningPreset;
    [SerializeField] private GameplayNoisePreset walkingPreset;

    [Header("Heard by people")]
    [Tooltip(
        "Give each of these several clips. One clip on a loop is recognised " +
        "as one clip within about five steps, and a walk is the most repeated " +
        "sound in the game.")]
    [SerializeField] private SoundEffect runningSound;
    [SerializeField] private SoundEffect walkingSound;

    [Header("Gait")]
    [Tooltip(
        "Metres between footsteps, from the second one onwards. A stride " +
        "rather than a timer, so a player who slows down takes fewer steps " +
        "rather than the same number more quietly - which is what walking " +
        "is. The first footfall of a movement does not wait for it: " +
        "setting off is heard at once, so shortening this to make that " +
        "first step arrive sooner is the wrong lever - it speeds up every " +
        "step instead.")]
    [SerializeField, Min(0.05f)] private float strideLength = 0.9f;

    private Vector3 previousPosition;
    private float previousSampleTime;
    private bool hasPreviousSample;
    private float distanceSinceStep;
    private bool wasMoving;

    private IGameplaySoundService gameplaySound;

    public override void OnNetworkSpawn()
    {
        if (IsServer &&
            noiseEmitter != null &&
            NetworkObjectServiceContext.TryResolveSessionService(
                NetworkManager,
                out IGameplayNoiseService noiseService))
        {
            noiseEmitter.Construct(noiseService);
        }

        ResetObservation();
    }

    public override void OnNetworkDespawn()
    {
        ResetObservation();
    }

    private void Update()
    {
        if (!IsSpawned || movement == null)
            return;

        Step();
    }

    private void Step()
    {
        Transform observed = observedTransform != null ? observedTransform : transform;
        Vector3 position = observed.position;
        float now = Time.time;

        if (!hasPreviousSample)
        {
            previousPosition = position;
            previousSampleTime = now;
            hasPreviousSample = true;
            return;
        }

        float deltaTime = now - previousSampleTime;

        previousSampleTime = now;
        Vector3 displacement = position - previousPosition;
        previousPosition = position;
        displacement.y = 0f;

        if (deltaTime <= Mathf.Epsilon)
            return;

        float travelled = displacement.magnitude;
        PlayerGait gait = movement.GaitFromObservedSpeed(travelled / deltaTime);

        // Silence is the absence of an event, not an event with nothing in it -
        // so there is no silent preset, no silent clip, and nothing to play
        // them with.
        //
        // The stride is forgotten rather than kept. Creeping half a stride and
        // then standing up to walk would otherwise pay for that crouched half
        // with a step on the first loud frame, which is a footstep for ground
        // already crossed quietly.
        if (gait == PlayerGait.Silent)
        {
            distanceSinceStep = 0f;
            wasMoving = false;
            return;
        }

        distanceSinceStep += travelled;

        // The first footfall lands when you start walking, not a stride later.
        //
        // Counting from zero every time meant setting off in silence and
        // hearing nothing until a whole stride had gone by - and since the
        // gait band is not reached until nearly two metres a second, the
        // acceleration up to it was silent too. Two steps of nothing, every
        // time anybody started moving, which is the one moment a footstep is
        // most expected.
        //
        // Nothing can trigger this by twitching: crossing out of Silent needs
        // real speed, which is what the band is.
        bool startedMoving = !wasMoving;
        wasMoving = true;

        if (!startedMoving && distanceSinceStep < strideLength)
            return;

        distanceSinceStep = startedMoving
            ? 0f
            : distanceSinceStep - strideLength;

        bool isRunning = gait == PlayerGait.Running;

        if (IsServer)
            EmitNoise(isRunning);

        PlaySound(isRunning, position);
    }

    private void EmitNoise(bool isRunning)
    {
        GameplayNoisePreset preset = isRunning ? runningPreset : walkingPreset;

        if (preset != null && noiseEmitter != null)
            noiseEmitter.TryEmitServer(preset);
    }

    // Asked for on first use rather than wired: this is a leaf, and whatever
    // spawns a player may never have thought to hand it anything.
    private void PlaySound(bool isRunning, Vector3 position)
    {
        SoundEffect sound = isRunning ? runningSound : walkingSound;

        if (sound == null)
            return;

        gameplaySound ??= AudioServices.Gameplay();
        gameplaySound?.PlayAtPosition(sound, position);
    }

    private void ResetObservation()
    {
        hasPreviousSample = false;
        distanceSinceStep = 0f;
        previousPosition = Vector3.zero;
        previousSampleTime = 0f;
    }
}
