using Unity.Netcode;
using Unity.Netcode.Components;
using UnityEngine;

[DisallowMultipleComponent]
[RequireComponent(typeof(NetworkObject))]
[RequireComponent(typeof(NetworkTransform))]
[RequireComponent(typeof(Rigidbody))]
public sealed class PlayerHidingController :
    PlayerNetworkComponent,
    IPlayerSignalListener,
    IReplicatedPlayerHidingStateService
{
    private readonly NetworkVariable<PlayerHidingSnapshot> hidingState = new(
        PlayerHidingSnapshot.NotHidden,
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Server
    );

    [Header("Runtime References")]
    [SerializeField] private NetworkTransform networkTransform;
    [SerializeField] private Rigidbody playerBody;
    [SerializeField] private PlayerController playerController;
    [SerializeField] private PlayerInteraction playerInteraction;

    private PlayerHidingEffects hidingEffects;
    private bool listensToHidingState;

    public bool IsHidden => hidingState.Value.IsHidden;

    public ulong HidingPlaceNetworkObjectId =>
        hidingState.Value.HidingPlaceNetworkObjectId;

    private void Awake()
    {
        ResolveReferences();
    }

    protected override void OnPostInit(PlayerOrchestrator orchestrator)
    {
        SubscribeToHidingState();
        ApplyHidingState(PlayerHidingSnapshot.NotHidden, hidingState.Value);
    }

    public override void OnNetworkSpawn()
    {
        base.OnNetworkSpawn();
        SubscribeToHidingState();
        ApplyHidingState(PlayerHidingSnapshot.NotHidden, hidingState.Value);
    }

    public override void OnNetworkDespawn()
    {
        if (IsServer)
        {
            ReleaseCurrentHidingPlaceServer();
        }

        UnsubscribeFromHidingState();
        hidingEffects?.Restore();
        base.OnNetworkDespawn();
    }

    public void Cleanup()
    {
        UnsubscribeFromHidingState();
        hidingEffects?.Restore();
    }

    public void RequestExitHiding()
    {
        if (!IsHidden || !IsSpawned || !IsOwner)
        {
            return;
        }

        RequestExitHidingServerRpc();
    }

    internal bool EnterHidingServer(
        HidingPlaceInteractable hidingPlace,
        Vector3 hidingPosition,
        Quaternion hidingRotation,
        bool hidePlayerVisuals,
        bool disablePlayerColliders
    )
    {
        if (!IsServer ||
            !IsSpawned ||
            hidingPlace == null ||
            !hidingPlace.IsSpawned ||
            IsHidden ||
            HasActiveItemInteractionServer())
        {
            return false;
        }

        hidingState.Value = new PlayerHidingSnapshot(
            true,
            hidingPlace.NetworkObjectId,
            hidePlayerVisuals,
            disablePlayerColliders
        );

        ApplyServerPose(hidingPosition, hidingRotation);
        TeleportOwnerRpc(hidingPosition, hidingRotation);
        return true;
    }

    internal bool ExitHidingServer(
        HidingPlaceInteractable hidingPlace,
        Vector3 exitPosition,
        Quaternion exitRotation,
        bool teleportToExit
    )
    {
        if (!IsServer || !IsSpawned || !IsHidden || hidingPlace == null)
        {
            return false;
        }

        if (HidingPlaceNetworkObjectId != hidingPlace.NetworkObjectId)
        {
            return false;
        }

        hidingState.Value = PlayerHidingSnapshot.NotHidden;

        if (teleportToExit)
        {
            ApplyServerPose(exitPosition, exitRotation);
            TeleportOwnerRpc(exitPosition, exitRotation);
        }

        return true;
    }

    internal static bool IsClientHidden(
        NetworkManager networkManager,
        ulong clientId
    )
    {
        if (networkManager == null ||
            !networkManager.IsServer ||
            !networkManager.ConnectedClients.TryGetValue(
                clientId,
                out NetworkClient client
            ) ||
            client.PlayerObject == null)
        {
            return false;
        }

        PlayerHidingController hidingController =
            client.PlayerObject.GetComponent<PlayerHidingController>();

        return hidingController != null && hidingController.IsHidden;
    }

    [Rpc(
        SendTo.Server,
        InvokePermission = RpcInvokePermission.Owner
    )]
    private void RequestExitHidingServerRpc()
    {
        if (!TryGetCurrentHidingPlace(out HidingPlaceInteractable hidingPlace))
        {
            hidingState.Value = PlayerHidingSnapshot.NotHidden;
            return;
        }

        hidingPlace.TryExitServer(this, teleportToExit: true);
    }

    [Rpc(
        SendTo.Owner,
        InvokePermission = RpcInvokePermission.Server
    )]
    private void TeleportOwnerRpc(Vector3 position, Quaternion rotation)
    {
        ResolveReferences();

        if (networkTransform != null &&
            networkTransform.IsSpawned &&
            networkTransform.IsOwner)
        {
            networkTransform.Teleport(
                position,
                rotation,
                transform.localScale
            );

            return;
        }

        transform.SetPositionAndRotation(position, rotation);
    }

    private void ReleaseCurrentHidingPlaceServer()
    {
        if (!IsHidden)
        {
            return;
        }

        if (TryGetCurrentHidingPlace(out HidingPlaceInteractable hidingPlace))
        {
            hidingPlace.ReleaseOccupantForPlayerDespawnServer(this);
            return;
        }

        if (IsSpawned)
        {
            hidingState.Value = PlayerHidingSnapshot.NotHidden;
        }
    }

    private bool TryGetCurrentHidingPlace(
        out HidingPlaceInteractable hidingPlace
    )
    {
        hidingPlace = null;

        NetworkManager manager = NetworkManager;

        if (manager == null ||
            manager.SpawnManager == null ||
            HidingPlaceNetworkObjectId ==
            HidingPlaceInteractable.NoOccupantNetworkObjectId ||
            !manager.SpawnManager.SpawnedObjects.TryGetValue(
                HidingPlaceNetworkObjectId,
                out NetworkObject hidingPlaceObject
            ))
        {
            return false;
        }

        hidingPlace = hidingPlaceObject.GetComponent<HidingPlaceInteractable>();
        return hidingPlace != null;
    }

    private void ApplyServerPose(Vector3 position, Quaternion rotation)
    {
        ResolveReferences();

        if (playerBody != null)
        {
            playerBody.position = position;
            playerBody.rotation = rotation;
            playerBody.linearVelocity = Vector3.zero;
            playerBody.angularVelocity = Vector3.zero;
            return;
        }

        transform.SetPositionAndRotation(position, rotation);
    }

    private bool HasActiveItemInteractionServer()
    {
        NetworkManager manager = NetworkManager;

        if (manager == null ||
            !manager.IsServer ||
            manager.SpawnManager == null)
        {
            return false;
        }

        foreach (NetworkObject spawnedObject in
                 manager.SpawnManager.SpawnedObjects.Values)
        {
            if (spawnedObject == null ||
                spawnedObject.OwnerClientId != OwnerClientId)
            {
                continue;
            }

            PickupItem pickupItem =
                spawnedObject.GetComponent<PickupItem>();

            if (pickupItem != null && pickupItem.IsPickedUp)
            {
                return true;
            }

            DraggableObject draggableObject =
                spawnedObject.GetComponent<DraggableObject>();

            if (draggableObject != null &&
                draggableObject.IsBeingDragged)
            {
                return true;
            }
        }

        return false;
    }

    private void SubscribeToHidingState()
    {
        if (listensToHidingState)
        {
            return;
        }

        hidingState.OnValueChanged += ApplyHidingState;
        listensToHidingState = true;
    }

    private void UnsubscribeFromHidingState()
    {
        if (!listensToHidingState)
        {
            return;
        }

        hidingState.OnValueChanged -= ApplyHidingState;
        listensToHidingState = false;
    }

    private void ApplyHidingState(
        PlayerHidingSnapshot previousState,
        PlayerHidingSnapshot currentState
    )
    {
        ResolveReferences();

        if (states != null)
        {
            states.IsHiding = currentState.IsHidden;
        }

        if (playerController != null)
        {
            playerController.SetMovementActive(
                this,
                !currentState.IsHidden
            );
        }

        if (playerInteraction != null)
        {
            playerInteraction.SetHidingActive(currentState.IsHidden);
        }

        hidingEffects?.Restore();

        if (!currentState.IsHidden)
        {
            return;
        }

        hidingEffects?.Apply(
            currentState.HidePlayerVisuals,
            currentState.DisablePlayerColliders
        );
    }

    private void ResolveReferences()
    {
        if (networkTransform == null)
        {
            networkTransform = GetComponent<NetworkTransform>();
        }

        if (playerBody == null)
        {
            playerBody = GetComponent<Rigidbody>();
        }

        if (playerController == null)
        {
            playerController = GetComponent<PlayerController>();
        }

        if (playerInteraction == null)
        {
            playerInteraction = GetComponent<PlayerInteraction>();
        }

        if (hidingEffects == null && playerBody != null)
        {
            hidingEffects = new PlayerHidingEffects(
                transform,
                playerBody
            );
        }
    }

#if UNITY_EDITOR
    private void OnValidate()
    {
        ResolveReferences();
    }
#endif
}
