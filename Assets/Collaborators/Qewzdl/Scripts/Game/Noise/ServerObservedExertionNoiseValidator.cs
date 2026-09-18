using Unity.Netcode;
using UnityEngine;

// Whether a player has worked hard enough recently to be short of breath,
// judged by the server from the authoritative transform.
//
// Breathing is the one noise in this game the client asks for. Everything else
// is derived server-side precisely so that nothing has to be trusted, and the
// reason this one is different is that the breath and the sound of it have to
// be the same event: a cough the server invented would land at a different
// moment from the cough the player hears themselves make, and two coughs a
// third of a second apart is worse than either.
//
// So the client says when, and this says whether it is allowed to. Exertion is
// measured here from nothing but how fast the body has been moving - the same
// speed bands the footsteps are judged by - so being winded cannot be claimed
// by somebody who has been walking. What a modified client can still do is
// stay quiet: refuse to ask, and breathe silently. That is a smaller prize
// than it looks, because the footsteps it cannot suppress are louder and carry
// further, and it is the ceiling of this approach rather than an oversight.
//
// ponytail: suppression is possible; closing it means the server running the
// whole breath rhythm and replicating each breath back for playback, which is
// a fair amount of machinery for the half of the cheat that matters least.
[DisallowMultipleComponent]
[RequireComponent(typeof(NetworkObject))]
public sealed class ServerObservedExertionNoiseValidator :
    NetworkBehaviour,
    IGameplayNoiseRequestValidator
{
    [Header("Observation")]
    [Tooltip("Left empty, this object's own transform is watched.")]
    [SerializeField] private Transform observedTransform;

    // The same asset the legs are driven from. Running here is the speed
    // running actually is, and the tank empties at the rate it actually
    // empties - a second copy of those numbers would agree until the first
    // time somebody balanced one of them.
    [SerializeField] private PlayerMovementProfile movement;

    [Tooltip(
        "Share of the tank that has to be gone before a breath may be heard. " +
        "Deliberately looser than the client's own threshold: this is a " +
        "check against claims nobody could honestly make, not a second " +
        "opinion about when somebody is tired.")]
    [SerializeField, Range(0f, 1f)] private float windedBelow = 0.5f;

    // What the server believes is left, from watching alone. It does not have
    // to match the client's tank exactly and is not meant to - see
    // ObservedExertion, which is where the arithmetic and the reasoning live.
    private readonly ObservedExertion exertion = new();

    public override void OnNetworkSpawn()
    {
        exertion.Reset();
    }

    public override void OnNetworkDespawn()
    {
        exertion.Reset();
    }

    private void Update()
    {
        if (!IsServer || !IsSpawned || movement == null)
            return;

        Transform observed = observedTransform != null ? observedTransform : transform;
        exertion.Sample(observed.position, Time.time, movement);
    }

    public bool CanEmitNoiseServer(
        GameplayNoiseEmitter emitter,
        GameplayNoisePreset preset,
        ulong senderClientId)
    {
        if (!IsServer ||
            !IsSpawned ||
            !isActiveAndEnabled ||
            emitter == null ||
            emitter.NetworkObject != NetworkObject)
        {
            return false;
        }

        if (senderClientId != OwnerClientId)
            return false;

        return movement != null && exertion.Stamina <= windedBelow;
    }
}
