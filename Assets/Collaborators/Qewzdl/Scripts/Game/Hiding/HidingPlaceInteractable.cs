using System;
using Unity.Netcode;
using UnityEngine;

[DisallowMultipleComponent]
[RequireComponent(typeof(NetworkObject))]
[RequireComponent(typeof(HidingPlaceNavigationObstacle))]
public sealed class HidingPlaceInteractable : InteractableObject
{
    public const ulong NoOccupantNetworkObjectId = ulong.MaxValue;

    [Header("Placement")]
    [SerializeField] private Transform interactionAnchor;
    [SerializeField] private Transform hidingPoint;
    [SerializeField] private Transform cameraAnchor;
    [SerializeField] private Transform exitPoint;
    [SerializeField] private Transform[] fallbackExitPoints;

    private readonly NetworkVariable<ulong> occupantNetworkObjectId = new(
        NoOccupantNetworkObjectId,
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Server
    );
    private readonly NetworkVariable<HidingTransitionState> transitionState =
        new(
            HidingTransitionState.Available,
            NetworkVariableReadPermission.Everyone,
            NetworkVariableWritePermission.Server
        );
    private readonly HidingEntryLineOfSightValidator lineOfSightValidator =
        new();
    private readonly HidingExitPlacementResolver exitPlacementResolver =
        new();

    private bool acceptingEntries;
    private bool networkSceneUnloadInProgress;
    private bool listensToNetworkSceneUnload;
    private bool transitionPending;
    private bool blockedExitLogged;
    private double transitionCompletesAt;
    private Pose pendingExitPose;
    private bool pendingExitTeleport;
    private ulong pendingLocalPlayerNetworkObjectId =
        NoOccupantNetworkObjectId;

    public event Action<bool> OccupancyChanged;
    public event Action<
        HidingTransitionState,
        HidingTransitionState> StateChanged;
    public event Action<
        HidingNoiseCue,
        PlayerHidingController> ServerNoiseRequested;

    public HidingPlaceData Configuration => data as HidingPlaceData;
    public Transform CameraAnchor => cameraAnchor;
    public HidingTransitionState State => transitionState.Value;
    public bool IsOpen =>
        State == HidingTransitionState.Entering ||
        State == HidingTransitionState.Exiting;
    public bool IsOccupied =>
        occupantNetworkObjectId.Value != NoOccupantNetworkObjectId;
    public ulong OccupantNetworkObjectId => occupantNetworkObjectId.Value;
    public Vector3 EnemyInvestigationPosition =>
        interactionAnchor != null
            ? interactionAnchor.position
            : transform.position;

    public bool IsAvailable =>
        IsSpawned &&
        isActiveAndEnabled &&
        acceptingEntries &&
        State == HidingTransitionState.Available &&
        !IsOccupied &&
        HasValidConfiguration();

    public override void OnNetworkSpawn()
    {
        base.OnNetworkSpawn();

        networkSceneUnloadInProgress = false;
        transitionPending = false;
        pendingLocalPlayerNetworkObjectId =
            NoOccupantNetworkObjectId;
        acceptingEntries = HasValidConfiguration();

        SubscribeToNetworkSceneUnload();
        occupantNetworkObjectId.OnValueChanged += HandleOccupantChanged;
        transitionState.OnValueChanged += HandleStateChanged;

        OccupancyChanged?.Invoke(IsOccupied);
        StateChanged?.Invoke(State, State);

        if (!acceptingEntries && IsServer)
        {
            Debug.LogError(
                $"{nameof(HidingPlaceInteractable)} '{name}' has an " +
                "invalid runtime configuration and will reject entries.",
                this
            );
        }
    }

    public override void OnNetworkDespawn()
    {
        acceptingEntries = false;
        CancelTransition();
        CancelPendingLocalHidingRequest();

        if (IsServer)
        {
            ReleaseOccupantServer(ResolveDespawnReason());
        }

        UnsubscribeFromNetworkSceneUnload();
        occupantNetworkObjectId.OnValueChanged -= HandleOccupantChanged;
        transitionState.OnValueChanged -= HandleStateChanged;
        base.OnNetworkDespawn();
    }

    private void Update()
    {
        if (!IsServer ||
            !IsSpawned ||
            !transitionPending ||
            Time.unscaledTimeAsDouble < transitionCompletesAt)
        {
            return;
        }

        transitionPending = false;

        switch (State)
        {
            case HidingTransitionState.Entering:
                CompleteEnterServer();
                break;

            case HidingTransitionState.Exiting:
                CompleteExitServer();
                break;
        }
    }

    public override void OnInteract(InteractionContext context)
    {
        PlayerInteraction interaction = context?.PlayerInteraction;
        IPlayerHidingCommandService hidingCommands =
            context?.PlayerHidingCommands;

        if (interaction == null ||
            hidingCommands == null ||
            context.PlayerActionGate == null ||
            !interaction.CanEnterHiding())
        {
            return;
        }

        TryRequestEnter(hidingCommands);
    }

    public bool TryRequestEnter(
        IPlayerHidingCommandService hidingCommands
    )
    {
        if (hidingCommands == null ||
            !hidingCommands.IsSpawned ||
            !hidingCommands.IsOwner ||
            !IsAvailable)
        {
            return false;
        }

        if (!hidingCommands.TryBeginHidingRequest())
        {
            return false;
        }

        pendingLocalPlayerNetworkObjectId =
            hidingCommands.NetworkObjectId;

        if (IsServer)
        {
            bool accepted = TryEnterServer(
                hidingCommands.NetworkObjectId,
                hidingCommands.OwnerClientId);

            if (!accepted)
            {
                hidingCommands.CancelHidingRequest();
                pendingLocalPlayerNetworkObjectId =
                    NoOccupantNetworkObjectId;
            }

            return accepted;
        }

        RequestEnterHidingServerRpc(hidingCommands.NetworkObjectId);
        return true;
    }

    internal bool TryExitServer(
        PlayerHidingController playerHiding,
        bool teleportToExit
    )
    {
        if (!IsServer ||
            !IsSpawned ||
            State != HidingTransitionState.Occupied ||
            playerHiding == null ||
            occupantNetworkObjectId.Value !=
            playerHiding.NetworkObjectId)
        {
            return false;
        }

        HidingPlaceData settings = Configuration;
        Pose exitPose = new(
            playerHiding.transform.position,
            playerHiding.transform.rotation
        );

        if (teleportToExit &&
            (settings == null ||
             !exitPlacementResolver.TryResolve(
                 playerHiding,
                 exitPoint,
                 fallbackExitPoints,
                 settings.AlignPlayerRotation,
                 settings,
                 includeRecoveryPose: true,
                 out exitPose)))
        {
            LogBlockedExit(playerHiding);
            return false;
        }

        if (!playerHiding.BeginExitingHidingServer(this, settings))
        {
            return false;
        }

        pendingExitPose = exitPose;
        pendingExitTeleport = teleportToExit;
        SetStateServer(HidingTransitionState.Exiting);
        RaiseServerNoise(HidingNoiseCue.Exit, playerHiding);
        ScheduleTransition(settings.ExitDuration);
        return true;
    }

    public bool TryInvestigateServer(Vector3 enemyPosition)
    {
        HidingPlaceData settings = Configuration;

        if (!IsServer ||
            !IsSpawned ||
            settings == null ||
            !settings.EnemiesCanInvestigate ||
            State != HidingTransitionState.Occupied)
        {
            return false;
        }

        float maxDistance = settings.EnemyInvestigationDistance;
        if ((enemyPosition - EnemyInvestigationPosition).sqrMagnitude >
            maxDistance * maxDistance)
        {
            return false;
        }

        return TryGetOccupant(out PlayerHidingController occupant) &&
               TryExitServer(occupant, teleportToExit: true);
    }

    internal bool ReleaseOccupantForPlayerDespawnServer(
        PlayerHidingController playerHiding
    )
    {
        if (!IsServer ||
            playerHiding == null ||
            occupantNetworkObjectId.Value !=
            playerHiding.NetworkObjectId)
        {
            return false;
        }

        CancelTransition();
        occupantNetworkObjectId.Value =
            NoOccupantNetworkObjectId;
        SetStateServer(HidingTransitionState.Available);
        return true;
    }

    [Rpc(
        SendTo.Server,
        InvokePermission = RpcInvokePermission.Everyone
    )]
    private void RequestEnterHidingServerRpc(
        ulong playerNetworkObjectId,
        RpcParams rpcParams = default
    )
    {
        ulong senderClientId = rpcParams.Receive.SenderClientId;

        if (TryEnterServer(playerNetworkObjectId, senderClientId))
        {
            return;
        }

        RejectHidingRequestRpc(
            playerNetworkObjectId,
            RpcTarget.Single(senderClientId, RpcTargetUse.Temp));
    }

    [Rpc(
        SendTo.SpecifiedInParams,
        InvokePermission = RpcInvokePermission.Server
    )]
    private void RejectHidingRequestRpc(
        ulong playerNetworkObjectId,
        RpcParams rpcParams = default
    )
    {
        NetworkManager manager = NetworkManager;

        if (manager == null ||
            manager.SpawnManager == null ||
            !manager.SpawnManager.SpawnedObjects.TryGetValue(
                playerNetworkObjectId,
                out NetworkObject playerObject) ||
            playerObject == null)
        {
            return;
        }

        IPlayerHidingCommandService commands =
            playerObject.GetComponent<PlayerHidingController>();
        commands?.CancelHidingRequest();

        if (pendingLocalPlayerNetworkObjectId ==
            playerNetworkObjectId)
        {
            pendingLocalPlayerNetworkObjectId =
                NoOccupantNetworkObjectId;
        }
    }

    private bool TryEnterServer(
        ulong playerNetworkObjectId,
        ulong senderClientId
    )
    {
        NetworkManager manager = NetworkManager;

        if (!IsServer ||
            !IsAvailable ||
            manager == null ||
            manager.SpawnManager == null ||
            !manager.SpawnManager.SpawnedObjects.TryGetValue(
                playerNetworkObjectId,
                out NetworkObject playerObject) ||
            playerObject == null ||
            !playerObject.IsSpawned ||
            playerObject.OwnerClientId != senderClientId)
        {
            return false;
        }

        PlayerHidingController playerHiding =
            playerObject.GetComponent<PlayerHidingController>();
        HidingPlaceData settings = Configuration;

        if (playerHiding == null ||
            settings == null ||
            !playerHiding.CanEnterHidingServer() ||
            !IsInsideInteractionRange(playerObject.transform.position) ||
            !lineOfSightValidator.HasLineOfSight(
                playerHiding,
                this,
                interactionAnchor != null
                    ? interactionAnchor
                    : transform,
                settings))
        {
            return false;
        }

        occupantNetworkObjectId.Value = playerObject.NetworkObjectId;

        if (!playerHiding.BeginEnteringHidingServer(this, settings))
        {
            occupantNetworkObjectId.Value =
                NoOccupantNetworkObjectId;
            return false;
        }

        SetStateServer(HidingTransitionState.Entering);
        RaiseServerNoise(HidingNoiseCue.Enter, playerHiding);
        ScheduleTransition(settings.EnterDuration);
        return true;
    }

    private void CompleteEnterServer()
    {
        HidingPlaceData settings = Configuration;

        if (State != HidingTransitionState.Entering ||
            settings == null ||
            !TryGetOccupant(out PlayerHidingController playerHiding))
        {
            ResetAvailableServer();
            return;
        }

        Quaternion hidingRotation = settings.AlignPlayerRotation
            ? hidingPoint.rotation
            : playerHiding.transform.rotation;

        if (!playerHiding.CompleteEnteringHidingServer(
                this,
                hidingPoint.position,
                hidingRotation,
                settings))
        {
            playerHiding.RecoverFromMissingHidingPlaceServer();
            ResetAvailableServer();
            return;
        }

        SetStateServer(HidingTransitionState.Occupied);
    }

    private void CompleteExitServer()
    {
        if (State != HidingTransitionState.Exiting ||
            !TryGetOccupant(out PlayerHidingController playerHiding))
        {
            ResetAvailableServer();
            return;
        }

        if (!playerHiding.CompleteExitingHidingServer(
                this,
                pendingExitPose.position,
                pendingExitPose.rotation,
                pendingExitTeleport))
        {
            playerHiding.RecoverFromMissingHidingPlaceServer();
        }

        ResetAvailableServer();
    }

    private void ScheduleTransition(float duration)
    {
        duration = Mathf.Max(0f, duration);

        if (duration <= 0f)
        {
            transitionPending = false;

            if (State == HidingTransitionState.Entering)
            {
                CompleteEnterServer();
            }
            else if (State == HidingTransitionState.Exiting)
            {
                CompleteExitServer();
            }

            return;
        }

        transitionCompletesAt =
            Time.unscaledTimeAsDouble + duration;
        transitionPending = true;
    }

    private void CancelTransition()
    {
        transitionPending = false;
        transitionCompletesAt = 0d;
        pendingExitPose = default;
        pendingExitTeleport = false;
    }

    private void ResetAvailableServer()
    {
        CancelTransition();
        occupantNetworkObjectId.Value =
            NoOccupantNetworkObjectId;
        SetStateServer(HidingTransitionState.Available);
        blockedExitLogged = false;
    }

    // Every exit route being occupied is a legitimate state - the occupant
    // simply stays put - but it looks identical to a broken interact key from
    // the outside. The enemy check retries this every tick, so say it once
    // per occupancy instead of once per frame.
    private void LogBlockedExit(PlayerHidingController playerHiding)
    {
        if (blockedExitLogged)
        {
            return;
        }

        blockedExitLogged = true;

        Debug.LogWarning(
            $"{nameof(HidingPlaceInteractable)} '{name}' is keeping player " +
            $"{playerHiding.NetworkObjectId} inside: no exit anchor, fallback " +
            "or recovery pose is currently collision-free.",
            this
        );
    }

    private bool TryGetOccupant(
        out PlayerHidingController playerHiding
    )
    {
        playerHiding = null;
        NetworkManager manager = NetworkManager;

        if (!IsOccupied ||
            manager == null ||
            manager.SpawnManager == null ||
            !manager.SpawnManager.SpawnedObjects.TryGetValue(
                occupantNetworkObjectId.Value,
                out NetworkObject playerObject) ||
            playerObject == null)
        {
            return false;
        }

        playerHiding =
            playerObject.GetComponent<PlayerHidingController>();
        return playerHiding != null;
    }

    private bool IsInsideInteractionRange(Vector3 playerPosition)
    {
        Transform anchor = interactionAnchor != null
            ? interactionAnchor
            : transform;
        float maxDistance = Configuration.MaxInteractionDistance;

        return (playerPosition - anchor.position).sqrMagnitude <=
               maxDistance * maxDistance;
    }

    private void RaiseServerNoise(
        HidingNoiseCue cue,
        PlayerHidingController playerHiding
    )
    {
        if (!IsServer || playerHiding == null)
        {
            return;
        }

        ServerNoiseRequested?.Invoke(cue, playerHiding);
    }

    private void ReleaseOccupantServer(HidingPlaceDespawnReason reason)
    {
        if (!IsOccupied)
        {
            return;
        }

        CancelTransition();

        if (TryGetOccupant(out PlayerHidingController playerHiding))
        {
            bool isLifecycleCleanup =
                reason == HidingPlaceDespawnReason.SceneUnload ||
                reason == HidingPlaceDespawnReason.SessionShutdown;
            bool released;

            if (isLifecycleCleanup)
            {
                released =
                    playerHiding.CleanupHidingSequenceServer(this);
            }
            else
            {
                released = TryForceRuntimeExitServer(playerHiding);
            }

            if (!released && !isLifecycleCleanup)
            {
                Debug.LogError(
                    $"{nameof(HidingPlaceInteractable)} '{name}' " +
                    $"could not safely release player " +
                    $"{playerHiding.NetworkObjectId} during " +
                    "runtime destruction.",
                    this
                );
            }
        }

        occupantNetworkObjectId.Value =
            NoOccupantNetworkObjectId;
        SetStateServer(HidingTransitionState.Available);
    }

    private bool TryForceRuntimeExitServer(
        PlayerHidingController playerHiding
    )
    {
        HidingPlaceData settings = Configuration;

        if (settings != null &&
            exitPlacementResolver.TryResolve(
                playerHiding,
                exitPoint,
                fallbackExitPoints,
                settings.AlignPlayerRotation,
                settings,
                includeRecoveryPose: true,
                out Pose exitPose) &&
            playerHiding.ForceExitHidingServer(
                this,
                exitPose.position,
                exitPose.rotation,
                teleportToExit: true))
        {
            return true;
        }

        return playerHiding.RecoverFromMissingHidingPlaceServer();
    }

    private HidingPlaceDespawnReason ResolveDespawnReason()
    {
        NetworkManager manager = NetworkManager;

        if (manager == null ||
            manager.ShutdownInProgress ||
            !manager.IsListening)
        {
            return HidingPlaceDespawnReason.SessionShutdown;
        }

        if (networkSceneUnloadInProgress ||
            !gameObject.scene.IsValid() ||
            !gameObject.scene.isLoaded)
        {
            return HidingPlaceDespawnReason.SceneUnload;
        }

        return HidingPlaceDespawnReason.RuntimeDestruction;
    }

    private bool HasValidConfiguration()
    {
        HidingPlaceData settings = Configuration;

        return settings != null &&
               hidingPoint != null &&
               cameraAnchor != null &&
               exitPoint != null &&
               settings.ExitObstructionMask.value != 0 &&
               (!settings.RequireEntryLineOfSight ||
                settings.EntryLineOfSightBlockingMask.value != 0);
    }

    private void SetStateServer(HidingTransitionState nextState)
    {
        if (!IsServer || transitionState.Value == nextState)
        {
            return;
        }

        transitionState.Value = nextState;
    }

    private void SubscribeToNetworkSceneUnload()
    {
        if (listensToNetworkSceneUnload ||
            NetworkManager == null ||
            NetworkManager.SceneManager == null)
        {
            return;
        }

        NetworkManager.SceneManager.OnUnload += HandleNetworkSceneUnload;
        listensToNetworkSceneUnload = true;
    }

    private void UnsubscribeFromNetworkSceneUnload()
    {
        if (!listensToNetworkSceneUnload)
        {
            return;
        }

        if (NetworkManager != null &&
            NetworkManager.SceneManager != null)
        {
            NetworkManager.SceneManager.OnUnload -= HandleNetworkSceneUnload;
        }

        listensToNetworkSceneUnload = false;
    }

    private void HandleNetworkSceneUnload(
        ulong clientId,
        string sceneName,
        AsyncOperation operation
    )
    {
        if (!string.Equals(
                sceneName,
                gameObject.scene.name,
                StringComparison.Ordinal))
        {
            return;
        }

        networkSceneUnloadInProgress = true;
        acceptingEntries = false;
    }

    private void HandleOccupantChanged(
        ulong previousOccupant,
        ulong currentOccupant
    )
    {
        bool wasOccupied =
            previousOccupant != NoOccupantNetworkObjectId;
        bool isOccupied =
            currentOccupant != NoOccupantNetworkObjectId;

        if (wasOccupied != isOccupied)
        {
            OccupancyChanged?.Invoke(isOccupied);
        }

        if (pendingLocalPlayerNetworkObjectId ==
            NoOccupantNetworkObjectId ||
            currentOccupant == NoOccupantNetworkObjectId)
        {
            return;
        }

        if (currentOccupant == pendingLocalPlayerNetworkObjectId)
        {
            pendingLocalPlayerNetworkObjectId =
                NoOccupantNetworkObjectId;
            return;
        }

        CancelPendingLocalHidingRequest();
    }

    private void CancelPendingLocalHidingRequest()
    {
        ulong playerNetworkObjectId =
            pendingLocalPlayerNetworkObjectId;
        pendingLocalPlayerNetworkObjectId =
            NoOccupantNetworkObjectId;

        NetworkManager manager = NetworkManager;

        if (playerNetworkObjectId == NoOccupantNetworkObjectId ||
            manager == null ||
            manager.SpawnManager == null ||
            !manager.SpawnManager.SpawnedObjects.TryGetValue(
                playerNetworkObjectId,
                out NetworkObject playerObject) ||
            playerObject == null)
        {
            return;
        }

        IPlayerHidingCommandService commands =
            playerObject.GetComponent<PlayerHidingController>();
        commands?.CancelHidingRequest();
    }

    private void HandleStateChanged(
        HidingTransitionState previousState,
        HidingTransitionState currentState
    )
    {
        StateChanged?.Invoke(previousState, currentState);
    }

#if UNITY_EDITOR
    private void Reset()
    {
        interactionAnchor = transform;
    }

    private void OnValidate()
    {
        if (interactionAnchor == null)
        {
            interactionAnchor = transform;
        }
    }

    private void OnDrawGizmosSelected()
    {
        if (interactionAnchor != null && Configuration != null)
        {
            Gizmos.color = Color.yellow;
            Gizmos.DrawWireSphere(
                interactionAnchor.position,
                Configuration.MaxInteractionDistance
            );
        }

        DrawAnchor(hidingPoint, Color.green, 0.15f);
        DrawAnchor(cameraAnchor, Color.magenta, 0.12f);
        DrawAnchor(exitPoint, Color.cyan, 0.15f);

        if (fallbackExitPoints == null)
        {
            return;
        }

        for (int i = 0; i < fallbackExitPoints.Length; i++)
        {
            DrawAnchor(
                fallbackExitPoints[i],
                new Color(0f, 0.65f, 1f),
                0.12f
            );
        }
    }

    private static void DrawAnchor(
        Transform anchor,
        Color color,
        float radius
    )
    {
        if (anchor == null)
        {
            return;
        }

        Gizmos.color = color;
        Gizmos.DrawWireSphere(anchor.position, radius);
        Gizmos.DrawRay(
            anchor.position,
            anchor.forward * 0.5f
        );
    }
#endif

    private enum HidingPlaceDespawnReason
    {
        RuntimeDestruction = 0,
        SceneUnload = 1,
        SessionShutdown = 2
    }
}
