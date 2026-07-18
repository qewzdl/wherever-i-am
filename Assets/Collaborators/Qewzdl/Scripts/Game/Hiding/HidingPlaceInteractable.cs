using System;
using Unity.Netcode;
using UnityEngine;

[DisallowMultipleComponent]
[RequireComponent(typeof(NetworkObject))]
public sealed class HidingPlaceInteractable : InteractableObject
{
    public const ulong NoOccupantNetworkObjectId = ulong.MaxValue;

    [Header("Placement")]
    [SerializeField] private Transform interactionAnchor;
    [SerializeField] private Transform hidingPoint;
    [SerializeField] private Transform exitPoint;
    [SerializeField] private Transform[] fallbackExitPoints;

    private readonly NetworkVariable<ulong> occupantNetworkObjectId = new(
        NoOccupantNetworkObjectId,
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

    public event Action<bool> OccupancyChanged;

    public bool IsOccupied =>
        occupantNetworkObjectId.Value != NoOccupantNetworkObjectId;

    public ulong OccupantNetworkObjectId => occupantNetworkObjectId.Value;

    public bool IsAvailable =>
        IsSpawned &&
        isActiveAndEnabled &&
        acceptingEntries &&
        !IsOccupied &&
        HasValidConfiguration();

    private HidingPlaceData Settings => data as HidingPlaceData;

    public override void OnNetworkSpawn()
    {
        base.OnNetworkSpawn();
        networkSceneUnloadInProgress = false;
        acceptingEntries = HasValidConfiguration();
        SubscribeToNetworkSceneUnload();
        occupantNetworkObjectId.OnValueChanged += HandleOccupantChanged;
        OccupancyChanged?.Invoke(IsOccupied);

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

        if (IsServer)
        {
            ReleaseOccupantServer(ResolveDespawnReason());
        }

        UnsubscribeFromNetworkSceneUnload();
        occupantNetworkObjectId.OnValueChanged -= HandleOccupantChanged;
        base.OnNetworkDespawn();
    }

    public override void OnInteract(InteractionContext context)
    {
        PlayerInteraction interaction = context?.PlayerInteraction;
        PlayerHidingController playerHiding =
            context?.PlayerHidingController;

        if (interaction == null ||
            playerHiding == null ||
            !interaction.CanEnterHiding())
        {
            return;
        }

        TryRequestEnter(playerHiding);
    }

    public bool TryRequestEnter(PlayerHidingController playerHiding)
    {
        if (playerHiding == null ||
            !playerHiding.IsSpawned ||
            !playerHiding.IsOwner ||
            !IsAvailable)
        {
            return false;
        }

        NetworkObjectReference playerReference = new(
            playerHiding.NetworkObject
        );

        if (IsServer)
        {
            return TryEnterServer(
                playerReference,
                playerHiding.OwnerClientId
            );
        }

        RequestEnterHidingServerRpc(playerReference);
        return true;
    }

    internal bool TryExitServer(
        PlayerHidingController playerHiding,
        bool teleportToExit
    )
    {
        if (!IsServer ||
            playerHiding == null ||
            occupantNetworkObjectId.Value !=
            playerHiding.NetworkObjectId)
        {
            return false;
        }

        HidingPlaceData settings = Settings;
        Vector3 exitPosition = playerHiding.transform.position;
        Quaternion exitRotation = playerHiding.transform.rotation;

        if (teleportToExit)
        {
            if (settings == null ||
                !exitPlacementResolver.TryResolve(
                    playerHiding,
                    exitPoint,
                    fallbackExitPoints,
                    settings.AlignPlayerRotation,
                    settings,
                    includeRecoveryPose: true,
                    out Pose exitPose))
            {
                return false;
            }

            exitPosition = exitPose.position;
            exitRotation = exitPose.rotation;
        }

        bool exited = playerHiding.ExitHidingServer(
            this,
            exitPosition,
            exitRotation,
            teleportToExit
        );

        if (!exited)
        {
            return false;
        }

        occupantNetworkObjectId.Value =
            NoOccupantNetworkObjectId;
        return true;
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

        occupantNetworkObjectId.Value =
            NoOccupantNetworkObjectId;
        return true;
    }

    [Rpc(
        SendTo.Server,
        InvokePermission = RpcInvokePermission.Everyone
    )]
    private void RequestEnterHidingServerRpc(
        NetworkObjectReference playerReference,
        RpcParams rpcParams = default
    )
    {
        TryEnterServer(
            playerReference,
            rpcParams.Receive.SenderClientId
        );
    }

    private bool TryEnterServer(
        NetworkObjectReference playerReference,
        ulong senderClientId
    )
    {
        if (!IsServer ||
            !acceptingEntries ||
            !isActiveAndEnabled ||
            !IsSpawned ||
            IsOccupied ||
            Settings == null ||
            hidingPoint == null ||
            exitPoint == null ||
            !playerReference.TryGet(out NetworkObject playerObject) ||
            playerObject == null ||
            !playerObject.IsSpawned ||
            playerObject.OwnerClientId != senderClientId)
        {
            return false;
        }

        PlayerHidingController playerHiding =
            playerObject.GetComponent<PlayerHidingController>();

        if (playerHiding == null ||
            !playerHiding.CanEnterHidingServer() ||
            !IsInsideInteractionRange(playerObject.transform.position) ||
            !lineOfSightValidator.HasLineOfSight(
                playerHiding,
                this,
                interactionAnchor != null
                    ? interactionAnchor
                    : transform,
                Settings))
        {
            return false;
        }

        HidingPlaceData settings = Settings;
        Quaternion hidingRotation = settings.AlignPlayerRotation
            ? hidingPoint.rotation
            : playerObject.transform.rotation;

        occupantNetworkObjectId.Value = playerObject.NetworkObjectId;

        if (playerHiding.EnterHidingServer(
                this,
                hidingPoint.position,
                hidingRotation,
                settings
            ))
        {
            return true;
        }

        occupantNetworkObjectId.Value =
            NoOccupantNetworkObjectId;
        return false;
    }

    private bool IsInsideInteractionRange(Vector3 playerPosition)
    {
        Transform anchor = interactionAnchor != null
            ? interactionAnchor
            : transform;
        float maxDistance = Settings.MaxInteractionDistance;

        return (playerPosition - anchor.position).sqrMagnitude <=
               maxDistance * maxDistance;
    }

    private void ReleaseOccupantServer(HidingPlaceDespawnReason reason)
    {
        if (!IsOccupied)
        {
            return;
        }

        NetworkManager manager = NetworkManager;

        if (manager != null &&
            manager.SpawnManager != null &&
            manager.SpawnManager.SpawnedObjects.TryGetValue(
                occupantNetworkObjectId.Value,
                out NetworkObject playerObject
            ))
        {
            PlayerHidingController playerHiding =
                playerObject.GetComponent<PlayerHidingController>();

            if (playerHiding != null)
            {
                bool isLifecycleCleanup =
                    reason == HidingPlaceDespawnReason.SceneUnload ||
                    reason == HidingPlaceDespawnReason.SessionShutdown;

                bool released = TryExitServer(
                    playerHiding,
                    teleportToExit: !isLifecycleCleanup
                );

                if (!released &&
                    reason == HidingPlaceDespawnReason.RuntimeDestruction)
                {
                    released =
                        playerHiding.RecoverFromMissingHidingPlaceServer();
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
        }

        occupantNetworkObjectId.Value =
            NoOccupantNetworkObjectId;
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
        HidingPlaceData settings = Settings;

        return settings != null &&
               hidingPoint != null &&
               exitPoint != null &&
               settings.ExitObstructionMask.value != 0 &&
               (!settings.RequireEntryLineOfSight ||
                settings.EntryLineOfSightBlockingMask.value != 0);
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
        if (interactionAnchor != null && Settings != null)
        {
            Gizmos.color = Color.yellow;
            Gizmos.DrawWireSphere(
                interactionAnchor.position,
                Settings.MaxInteractionDistance
            );
        }

        if (hidingPoint != null)
        {
            Gizmos.color = Color.green;
            Gizmos.DrawWireSphere(hidingPoint.position, 0.15f);
            Gizmos.DrawRay(
                hidingPoint.position,
                hidingPoint.forward * 0.5f
            );
        }

        if (exitPoint != null)
        {
            Gizmos.color = Color.cyan;
            Gizmos.DrawWireSphere(exitPoint.position, 0.15f);
            Gizmos.DrawRay(
                exitPoint.position,
                exitPoint.forward * 0.5f
            );
        }

        if (fallbackExitPoints == null)
        {
            return;
        }

        Gizmos.color = new Color(0f, 0.65f, 1f);

        for (int i = 0; i < fallbackExitPoints.Length; i++)
        {
            Transform fallbackExit = fallbackExitPoints[i];

            if (fallbackExit == null)
            {
                continue;
            }

            Gizmos.DrawWireSphere(fallbackExit.position, 0.12f);
            Gizmos.DrawRay(
                fallbackExit.position,
                fallbackExit.forward * 0.4f
            );
        }
    }
#endif

    private enum HidingPlaceDespawnReason
    {
        RuntimeDestruction = 0,
        SceneUnload = 1,
        SessionShutdown = 2
    }
}
