using System.Collections.Generic;
using Unity.Collections;
using Unity.Netcode;
using UnityEngine;

// Being caught takes one player out of the match, not the match itself. The
// survivors carry on and the caught player watches them; the match is only
// lost once there is nobody left to finish it.
[DisallowMultipleComponent]
[RequireComponent(typeof(NetworkObject))]
public sealed class PlayerEnemyAttackReceiver :
    NetworkBehaviour,
    IEnemyAttackReceiver,
    IHidingEntryEligibility
{
    private static readonly List<PlayerEnemyAttackReceiver> RegisteredPlayers = new();

    [SerializeField] private GameResultType hitResult = GameResultType.Defeat;
    [SerializeField] private string hitReason = "A player was caught by an enemy";
    [SerializeField] private string lastPlayerCaughtReason =
        "Every player was caught by an enemy";
    [SerializeField] private bool logReceivedAttack = true;

    private readonly NetworkVariable<bool> eliminated = new(
        false,
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Server
    );

    // The name every client can read in the match. Nothing else in the game
    // scene replicates one, and a spectator on a client has no lobby left to
    // ask - the host is the only one holding the admitted names.
    private readonly NetworkVariable<FixedString32Bytes> displayName = new(
        default,
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Server
    );

    private NetworkObject networkObject;
    private readonly PlayerEnemyAttackCompletionGate completionGate = new();
    private bool isEliminated;
    private bool eliminationApplied;

    public static IReadOnlyList<PlayerEnemyAttackReceiver> All => RegisteredPlayers;

    public bool IsEliminated => isEliminated;

    public string DisplayName
    {
        get
        {
            string replicated = displayName.Value.ToString();

            return string.IsNullOrEmpty(replicated)
                ? PlayerDisplayName.Fallback(OwnerClientId)
                : replicated;
        }
    }

    public bool CanEnterHiding =>
        isActiveAndEnabled && !isEliminated;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetRegisteredPlayers()
    {
        RegisteredPlayers.Clear();
    }

    private void Awake()
    {
        networkObject = GetComponent<NetworkObject>();
    }

    public override void OnNetworkSpawn()
    {
        eliminated.OnValueChanged += HandleEliminatedChanged;
        isEliminated = eliminated.Value;

        if (IsServer)
            displayName.Value = PlayerDisplayName.Resolve(OwnerClientId);

        if (!RegisteredPlayers.Contains(this))
        {
            RegisteredPlayers.Add(this);
        }

        if (isEliminated)
        {
            ApplyElimination();
        }
    }

    public override void OnNetworkDespawn()
    {
        eliminated.OnValueChanged -= HandleEliminatedChanged;
        RegisteredPlayers.Remove(this);
    }

    public bool TryReceiveEnemyAttack(EnemyAttackContext context)
    {
        if (!context.IsValid || isEliminated)
        {
            return false;
        }

        if (networkObject == null)
        {
            networkObject = GetComponent<NetworkObject>();
        }

        if (networkObject == null ||
            !networkObject.IsSpawned ||
            NetworkManager == null ||
            !NetworkManager.IsServer)
        {
            return false;
        }

        if (!NetworkObjectServiceContext.TryResolveSessionService(
                NetworkManager,
                out IMatchCompletionService matchCompletionService) ||
            matchCompletionService == null ||
            !matchCompletionService.IsMatchRunning)
        {
            return false;
        }

        EliminateServerOnly();

        if (!HasPlayerInPlay(RegisteredPlayers))
        {
            completionGate.TryComplete(
                matchCompletionService,
                hitResult,
                networkObject.OwnerClientId,
                lastPlayerCaughtReason
            );
        }

        if (logReceivedAttack)
        {
            Debug.Log(
                $"Player received enemy attack: {context.TargetDebugName}.",
                this
            );
        }

        return true;
    }

    internal static bool HasPlayerInPlay(
        IReadOnlyList<PlayerEnemyAttackReceiver> players)
    {
        if (players == null)
        {
            return false;
        }

        for (int i = 0; i < players.Count; i++)
        {
            PlayerEnemyAttackReceiver player = players[i];

            if (player != null && !player.IsEliminated)
            {
                return true;
            }
        }

        return false;
    }

    private void EliminateServerOnly()
    {
        isEliminated = true;

        if (IsSpawned && IsServer)
        {
            eliminated.Value = true;
        }

        ApplyElimination();
    }

    private void HandleEliminatedChanged(bool previousValue, bool nextValue)
    {
        isEliminated = nextValue;

        if (nextValue)
        {
            ApplyElimination();
        }
    }

    // Runs on every peer: the body has to leave play for the enemy, for the
    // survivors looking at it, and for the physics they walk through.
    private void ApplyElimination()
    {
        if (eliminationApplied)
        {
            return;
        }

        eliminationApplied = true;

        EnemyTarget enemyTarget = GetComponentInChildren<EnemyTarget>(true);

        if (enemyTarget != null)
        {
            enemyTarget.SetDetectable(false);
        }

        PlayerGazeNetwork gaze = GetComponent<PlayerGazeNetwork>();

        if (gaze != null)
        {
            gaze.SetInPlay(false);
        }

        TakeBodyOutOfPlay();

        if (IsOwner)
        {
            PlayerSpectatorView.AttachTo(gameObject);
        }
    }

    private void TakeBodyOutOfPlay()
    {
        Renderer[] renderers = GetComponentsInChildren<Renderer>(true);

        for (int i = 0; i < renderers.Length; i++)
        {
            renderers[i].enabled = false;
        }

        Collider[] colliders = GetComponentsInChildren<Collider>(true);

        for (int i = 0; i < colliders.Length; i++)
        {
            colliders[i].enabled = false;
        }

        // Without its colliders the body would fall out of the world for as
        // long as the match lasts, and keep replicating the fall.
        Rigidbody body = GetComponent<Rigidbody>();

        if (body != null)
        {
            body.isKinematic = true;
        }
    }

    private void OnValidate()
    {
        if (string.IsNullOrWhiteSpace(hitReason))
        {
            hitReason = "A player was caught by an enemy";
        }

        if (string.IsNullOrWhiteSpace(lastPlayerCaughtReason))
        {
            lastPlayerCaughtReason = "Every player was caught by an enemy";
        }

        if (hitResult == GameResultType.None)
        {
            hitResult = GameResultType.Defeat;
        }
    }
}

internal sealed class PlayerEnemyAttackCompletionGate
{
    private bool hasAcceptedHit;
    private bool isCompletingHit;

    internal bool CanAttempt => !hasAcceptedHit && !isCompletingHit;

    internal bool TryComplete(
        IMatchCompletionService matchCompletionService,
        GameResultType hitResult,
        ulong caughtClientId,
        string hitReason)
    {
        if (!CanAttempt ||
            matchCompletionService == null ||
            !matchCompletionService.IsMatchRunning)
        {
            return false;
        }

        MatchOutcome outcome = MatchOutcomeFactory.FromPlayerCaught(
            hitResult,
            caughtClientId,
            hitReason
        );
        if (!outcome.HasResult)
        {
            return false;
        }

        isCompletingHit = true;
        try
        {
            if (!matchCompletionService.CompleteMatchServerOnly(
                    outcome.ToGameResultData(),
                    hitReason))
            {
                return false;
            }

            hasAcceptedHit = true;
            return true;
        }
        finally
        {
            isCompletingHit = false;
        }
    }
}
