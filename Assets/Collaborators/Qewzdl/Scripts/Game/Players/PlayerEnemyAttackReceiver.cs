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
    IHidingEntryEligibility,
    IPlayerInPlay
{
    private static readonly List<PlayerEnemyAttackReceiver> RegisteredPlayers = new();

    private const string LastSurvivorLeftReason =
        "The last player still in play left the match";

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
    public bool IsInPlay => !isEliminated;

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
        MostPlayersThisMatch = 0;
    }

    // The most players this match has had at once. How many were left at the
    // end was the wrong question: in a match of two whose survivor had left,
    // the one caught player still there was told the solo wording, as if they
    // had played alone.
    private static int MostPlayersThisMatch;

    public static bool MatchWasPlayedAlone => PlayedAlone(MostPlayersThisMatch);

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

        MostPlayersThisMatch = Mathf.Max(MostPlayersThisMatch, RegisteredPlayers.Count);

        if (isEliminated)
        {
            ApplyElimination();
        }
    }

    public override void OnNetworkDespawn()
    {
        eliminated.OnValueChanged -= HandleEliminatedChanged;
        RegisteredPlayers.Remove(this);

        // Everybody gone is the match over; the next one counts afresh.
        if (RegisteredPlayers.Count == 0)
            MostPlayersThisMatch = 0;

        if (IsServer && !isEliminated)
            CompleteIfNobodyLeftInPlayServer();
    }

    // The last survivor leaving is the match lost for everybody still in it.
    // Until now only a catch asked whether anybody was left, so a survivor
    // who quit left the caught players watching nobody, in a match that no
    // longer had anybody who could finish it.
    private void CompleteIfNobodyLeftInPlayServer()
    {
        NetworkManager manager = NetworkManager;

        if (manager == null ||
            manager.ShutdownInProgress ||
            RegisteredPlayers.Count == 0 ||
            HasPlayerInPlay(RegisteredPlayers) ||
            !NetworkObjectServiceContext.TryResolveSessionService(
                manager,
                out IMatchCompletionService matchCompletionService))
        {
            return;
        }

        completionGate.TryComplete(
            matchCompletionService,
            hitResult,
            OwnerClientId,
            LastSurvivorLeftReason
        );
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
            LeavePlayLocally(RegisteredPlayers);
        }
    }

    // What being caught means on the caught player's own machine: their
    // hands come off the controls - no looking round, moving, reaching or
    // posture - always. Then, if anybody is still playing, somebody else's
    // eyes to watch through. Asked of the players rather than of the kind of
    // session: a multiplayer lobby started alone has nobody to watch either.
    //
    // The two used to be one thing. Letting go of the controls lived inside
    // the spectator view, so a caught player who was given no spectator view
    // kept turning the camera of a body that was no longer there.
    internal void LeavePlayLocally(IReadOnlyList<PlayerEnemyAttackReceiver> players)
    {
        StopPlaying();

        if (PlayerSpectatorView.NextTarget(players, this, null) != null)
        {
            PlayerSpectatorView.AttachTo(gameObject);
        }
    }

    // Whether the match was one person's. What the end of it says is worded
    // for one or for many by this, rather than by the kind of session.
    internal static bool PlayedAlone(int mostPlayersAtOnce)
    {
        return mostPlayersAtOnce <= 1;
    }

    private void StopPlaying()
    {
        DisableIfPresent<CameraLook>();
        DisableIfPresent<PlayerController>();
        DisableIfPresent<PlayerInteraction>();
        DisableIfPresent<PlayerInputHandler>();
        DisableIfPresent<PlayerPostureController>();
        DisableIfPresent<PlayerUI>();

        // The head bob and breathing of a body nobody is in. They also write
        // the very camera the spectator view borrows, and running after it
        // put the view back on the corpse every frame.
        DisableIfPresent<PlayerCameraEffects>();
        DisableIfPresent<UnityEngine.InputSystem.PlayerInput>();

        // Whatever was in reach at the moment of the catch stayed drawn on the
        // crosshair for the rest of the match: the one thing that would have
        // cleared it, the interaction, has just been switched off.
        if (CrosshairUI.Active != null)
            CrosshairUI.Active.ShowInteraction(null);

        // CameraLook hands the cursor back when it is switched off, which is
        // right for a menu and wrong here: watching is still playing, the end
        // of the match is still on screen, and the pause menu takes the cursor
        // back on its own when it needs it.
        Cursor.lockState = CursorLockMode.Locked;
        Cursor.visible = false;
    }

    private void DisableIfPresent<T>() where T : Behaviour
    {
        T[] behaviours = GetComponentsInChildren<T>(true);

        for (int i = 0; i < behaviours.Length; i++)
        {
            behaviours[i].enabled = false;
        }
    }

    private void TakeBodyOutOfPlay()
    {
        Renderer[] renderers = GetComponentsInChildren<Renderer>(true);

        for (int i = 0; i < renderers.Length; i++)
        {
            renderers[i].enabled = false;
        }

        // The light the body carries goes with it. Left on, it went on
        // glowing where the player was caught for the rest of the match.
        Light[] lights = GetComponentsInChildren<Light>(true);

        for (int i = 0; i < lights.Length; i++)
        {
            lights[i].enabled = false;
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
