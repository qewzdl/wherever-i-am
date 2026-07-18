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

    private readonly NetworkVariable<ulong> occupantNetworkObjectId = new(
        NoOccupantNetworkObjectId,
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Server
    );

    public event Action<bool> OccupancyChanged;

    public bool IsOccupied =>
        occupantNetworkObjectId.Value != NoOccupantNetworkObjectId;

    public ulong OccupantNetworkObjectId => occupantNetworkObjectId.Value;

    private HidingPlaceData Settings => data as HidingPlaceData;

    public override void OnNetworkSpawn()
    {
        base.OnNetworkSpawn();
        occupantNetworkObjectId.OnValueChanged += HandleOccupantChanged;
        OccupancyChanged?.Invoke(IsOccupied);
    }

    public override void OnNetworkDespawn()
    {
        if (IsServer)
        {
            ReleaseOccupantServer();
        }

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
            Settings == null ||
            hidingPoint == null ||
            exitPoint == null)
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
        Transform resolvedExitPoint = exitPoint != null
            ? exitPoint
            : transform;

        Quaternion exitRotation =
            settings != null && settings.AlignPlayerRotation
                ? resolvedExitPoint.rotation
                : playerHiding.transform.rotation;

        bool exited = playerHiding.ExitHidingServer(
            this,
            resolvedExitPoint.position,
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
            playerHiding.IsHidden ||
            !IsInsideInteractionRange(playerObject.transform.position))
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
                settings.HidePlayerVisuals,
                settings.DisablePlayerColliders
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

    private void ReleaseOccupantServer()
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
                TryExitServer(
                    playerHiding,
                    teleportToExit: false
                );
            }
        }

        occupantNetworkObjectId.Value =
            NoOccupantNetworkObjectId;
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
    }
#endif
}
