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
    [SerializeField] private CapsuleCollider bodyCollider;
    [SerializeField] private PlayerController playerController;
    [SerializeField] private PlayerInteraction playerInteraction;

    private readonly HidingExitPlacementResolver exitPlacementResolver = new();
    private PlayerHidingEffects hidingEffects;
    private MonoBehaviour[] entryEligibilityProviders;
    private bool listensToHidingState;
    private bool hasRecoveryPose;
    private Pose recoveryPose;
    private LayerMask recoveryObstructionMask = ~0;
    private QueryTriggerInteraction recoveryTriggerInteraction =
        QueryTriggerInteraction.Ignore;
    private float recoveryCollisionSkin = 0.02f;

    public bool IsHidden => hidingState.Value.IsHidden;

    public ulong HidingPlaceNetworkObjectId =>
        hidingState.Value.HidingPlaceNetworkObjectId;

    internal Vector3 EntryValidationOrigin
    {
        get
        {
            ResolveReferences();

            return bodyCollider != null
                ? bodyCollider.bounds.center
                : transform.position;
        }
    }

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
        RestoreLocalHidingState();
        ClearRecoveryPose();
        base.OnNetworkDespawn();
    }

    public void Cleanup()
    {
        UnsubscribeFromHidingState();
        RestoreLocalHidingState();
        ClearRecoveryPose();
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
        HidingPlaceData settings
    )
    {
        if (!IsServer ||
            !IsSpawned ||
            hidingPlace == null ||
            !hidingPlace.IsSpawned ||
            settings == null ||
            !CanEnterHidingServer())
        {
            return false;
        }

        recoveryPose = new Pose(
            transform.position,
            transform.rotation
        );
        hasRecoveryPose = true;
        recoveryObstructionMask = settings.ExitObstructionMask;
        recoveryTriggerInteraction = settings.ExitTriggerInteraction;
        recoveryCollisionSkin = settings.ExitCollisionSkin;

        hidingState.Value = new PlayerHidingSnapshot(
            true,
            hidingPlace.NetworkObjectId,
            settings.HidePlayerVisuals,
            settings.DisablePlayerColliders
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

        ClearRecoveryPose();
        return true;
    }

    internal bool CanEnterHidingServer()
    {
        ResolveReferences();

        if (!IsServer ||
            !IsSpawned ||
            !isActiveAndEnabled ||
            IsHidden ||
            bodyCollider == null ||
            playerController == null)
        {
            return false;
        }

        if (states != null &&
            (states.IsHiding ||
             states.IsDragging ||
             states.IsCarrying))
        {
            return false;
        }

        if (playerController != null &&
            !playerController.IsMovementActive)
        {
            return false;
        }

        CacheEntryEligibilityProviders();
        bool hasEligibilityProvider = false;

        for (int i = 0; i < entryEligibilityProviders.Length; i++)
        {
            MonoBehaviour behaviour = entryEligibilityProviders[i];

            if (behaviour is not IHidingEntryEligibility eligibility)
            {
                continue;
            }

            hasEligibilityProvider = true;

            if (!eligibility.CanEnterHiding)
            {
                return false;
            }
        }

        return hasEligibilityProvider &&
               !HasActiveItemInteractionServer();
    }

    internal bool TryGetRecoveryPose(out Pose pose)
    {
        pose = recoveryPose;
        return hasRecoveryPose;
    }

    internal bool TryBuildExitCapsule(
        Pose rootPose,
        float collisionSkin,
        out Vector3 pointA,
        out Vector3 pointB,
        out float radius
    )
    {
        ResolveReferences();

        pointA = default;
        pointB = default;
        radius = 0f;

        if (bodyCollider == null)
        {
            return false;
        }

        Transform colliderTransform = bodyCollider.transform;
        Vector3 currentCenter = colliderTransform.TransformPoint(
            bodyCollider.center
        );
        Vector3 rootRelativeCenter =
            Quaternion.Inverse(transform.rotation) *
            (currentCenter - transform.position);
        Vector3 candidateCenter =
            rootPose.position +
            rootPose.rotation * rootRelativeCenter;

        Vector3 colliderAxis;
        float axisScale;
        float radiusScale;
        Vector3 scale = colliderTransform.lossyScale;
        float scaleX = Mathf.Abs(scale.x);
        float scaleY = Mathf.Abs(scale.y);
        float scaleZ = Mathf.Abs(scale.z);

        switch (bodyCollider.direction)
        {
            case 0:
                colliderAxis = Vector3.right;
                axisScale = scaleX;
                radiusScale = Mathf.Max(scaleY, scaleZ);
                break;

            case 2:
                colliderAxis = Vector3.forward;
                axisScale = scaleZ;
                radiusScale = Mathf.Max(scaleX, scaleY);
                break;

            default:
                colliderAxis = Vector3.up;
                axisScale = scaleY;
                radiusScale = Mathf.Max(scaleX, scaleZ);
                break;
        }

        Vector3 currentWorldAxis =
            colliderTransform.TransformDirection(colliderAxis).normalized;
        Vector3 rootRelativeAxis =
            Quaternion.Inverse(transform.rotation) * currentWorldAxis;
        Vector3 candidateAxis =
            (rootPose.rotation * rootRelativeAxis).normalized;

        float skin = Mathf.Max(0f, collisionSkin);
        float unskinnedRadius =
            bodyCollider.radius * radiusScale;
        float unskinnedHeight = Mathf.Max(
            bodyCollider.height * axisScale,
            unskinnedRadius * 2f
        );

        radius = Mathf.Max(0.01f, unskinnedRadius - skin);
        float height = Mathf.Max(
            radius * 2f,
            unskinnedHeight - skin * 2f
        );
        float halfSegment = Mathf.Max(0f, height * 0.5f - radius);

        pointA = candidateCenter + candidateAxis * halfSegment;
        pointB = candidateCenter - candidateAxis * halfSegment;
        return true;
    }

    internal bool RecoverFromMissingHidingPlaceServer()
    {
        if (!IsServer || !IsSpawned || !IsHidden)
        {
            return false;
        }

        if (!hasRecoveryPose)
        {
            Debug.LogError(
                $"{nameof(PlayerHidingController)} cannot recover player " +
                $"{NetworkObjectId}: the hiding recovery pose is missing.",
                this
            );
            return false;
        }

        Pose currentPose = new(
            transform.position,
            transform.rotation
        );
        if (!exitPlacementResolver.TryResolveEmergency(
                this,
                recoveryPose,
                currentPose,
                recoveryObstructionMask,
                recoveryTriggerInteraction,
                recoveryCollisionSkin,
                out Pose safePose))
        {
            Debug.LogWarning(
                $"{nameof(PlayerHidingController)} is keeping player " +
                $"{NetworkObjectId} hidden because no collision-free " +
                "emergency exit is currently available.",
                this
            );
            return false;
        }

        hidingState.Value = PlayerHidingSnapshot.NotHidden;
        ApplyServerPose(
            safePose.position,
            safePose.rotation
        );
        TeleportOwnerRpc(
            safePose.position,
            safePose.rotation
        );
        ClearRecoveryPose();
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
            RecoverFromMissingHidingPlaceServer();
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
            ClearRecoveryPose();
            return;
        }

        if (IsSpawned)
        {
            hidingState.Value = PlayerHidingSnapshot.NotHidden;
        }

        ClearRecoveryPose();
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

    private void ClearRecoveryPose()
    {
        hasRecoveryPose = false;
        recoveryPose = default;
        recoveryObstructionMask = ~0;
        recoveryTriggerInteraction = QueryTriggerInteraction.Ignore;
        recoveryCollisionSkin = 0.02f;
    }

    private void CacheEntryEligibilityProviders()
    {
        if (entryEligibilityProviders != null)
        {
            return;
        }

        entryEligibilityProviders = GetComponents<MonoBehaviour>();
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

    private void RestoreLocalHidingState()
    {
        ResolveReferences();

        if (states != null)
        {
            states.IsHiding = false;
        }

        if (playerController != null)
        {
            playerController.SetMovementActive(this, true);
        }

        playerInteraction?.SetHidingActive(false);
        hidingEffects?.Restore();
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

        if (bodyCollider == null)
        {
            bodyCollider = GetComponentInChildren<CapsuleCollider>(true);
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

        CacheEntryEligibilityProviders();
    }

#if UNITY_EDITOR
    private void OnValidate()
    {
        ResolveReferences();
    }
#endif
}
