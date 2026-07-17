using System.Collections;
using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;

public abstract class DraggableObject : InteractableObject
{
    protected NetworkVariable<bool> netIsDragging = new(
        false,
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Server
    );

    protected Rigidbody rb { get; private set; }
    protected PlayerInteraction playerInteraction;

    private Transform holdPointTransform;
    private PlayerController playerController;
    private float originalPlayerSpeed;
    private float originalMass;

    private Vector3[] velocityBuffer;
    private int velocityBufferIndex;
    private Vector3 previousPosition;
    private InteractionContext draggableContext;

    // Items currently being dragged on this client. PlayerController clips its velocity against
    // them (physical contact is disabled while dragging, so the solver can't do it).
    public static readonly List<DraggableObject> ActiveDraggedObjects = new(4);

    public Collider[] Colliders
    {
        get { return itemColliders; }
    }

    // Real velocity of the item for player-side clipping. While dragged, rb.linearVelocity holds
    // the commanded follow speed (nonzero even when a wall blocks the item), so the measured
    // per-tick displacement is used instead.
    public Vector3 CurrentVelocity
    {
        get
        {
            if (rb == null)
                return Vector3.zero;

            if (netIsDragging.Value)
                return measuredVelocity;

            return rb.linearVelocity;
        }
    }

    private Vector3 measuredVelocity;

    // Configured mass from data (rb.mass is temporarily lowered while a drag starts).
    public float Mass
    {
        get { return originalMass; }
    }

    private Collider[] itemColliders;
    private int playerLayerMask;
    private readonly List<(Collider item, Collider player)> ignoredCollisionPairs = new(16);
    private readonly Collider[] nearbyPlayerColliders = new Collider[8];

    private void OnValidate()
    {
        if (data != null)
        {
            if (data is not DraggableObjectData)
                Debug.LogError($"Data for {name} must be of type DraggableObjectData!", this);
        }
    }

    protected virtual void Awake()
    {
        rb = GetComponent<Rigidbody>();
        rb.mass = ((DraggableObjectData)data).Mass;
        originalMass = ((DraggableObjectData)data).Mass;

        NetworkObject networkObject = GetComponent<NetworkObject>();

        if (networkObject != null)
            networkObject.DontDestroyWithOwner = true;

        int samples = Mathf.Max(1, (int)((DraggableObjectData)data).ThrowVelocitySamples);
        velocityBuffer = new Vector3[samples]; //create buffer

        itemColliders = GetComponentsInChildren<Collider>(true);
        playerLayerMask = LayerMask.GetMask("Player");
    }

    private void FixedUpdate()
    {
        if (!netIsDragging.Value) return;

        if (IsClient && IsOwner && holdPointTransform != null)
            TickDragging();
    }

    // Safety net: if a player still collides while dragging (spawned mid-drag, or IgnoreCollision
    // got reset by a collider toggle), ignore that pair on the spot.
    private void OnCollisionEnter(Collision collision)
    {
        if (!netIsDragging.Value) return;

        if (collision.collider.GetComponentInParent<PlayerController>() == null) return;

        IgnoreCollisionWith(collision.collider);
    }

    // While carried, the item must not physically touch players at all: any real contact either
    // pushes the player (solver impulses, depenetration) or stops them (their blocking-normal clip).
    // Collisions are ignored pairwise and the item slides around players manually instead.
    private void HandleDraggingCollisionChanged(bool previousValue, bool newValue)
    {
        if (newValue)
            EnableDraggedPhysicsMode();
        else
            DisableDraggedPhysicsMode();
    }

    private void EnableDraggedPhysicsMode()
    {
        IgnoreCollisionWithAllPlayers();

        if (!ActiveDraggedObjects.Contains(this))
            ActiveDraggedObjects.Add(this);
    }

    private void DisableDraggedPhysicsMode()
    {
        RestoreIgnoredCollisions();
        ActiveDraggedObjects.Remove(this);
    }

    private void IgnoreCollisionWithAllPlayers()
    {
        NetworkManager manager = NetworkManager;

        if (manager == null || manager.SpawnManager == null)
            return;

        foreach (NetworkObject playerObject in manager.SpawnManager.SpawnedObjectsList)
        {
            if (playerObject == null ||
                !playerObject.TryGetComponent(out PlayerNetwork _))
            {
                continue;
            }

            Collider[] playerColliders =
                playerObject.GetComponentsInChildren<Collider>();

            for (int j = 0; j < playerColliders.Length; j++)
                IgnoreCollisionWith(playerColliders[j]);
        }
    }

    private void IgnoreCollisionWith(Collider playerCollider)
    {
        if (playerCollider.isTrigger) return;

        for (int i = 0; i < itemColliders.Length; i++)
        {
            Collider itemCollider = itemColliders[i];

            if (itemCollider.isTrigger) continue;

            if (ignoredCollisionPairs.Contains((itemCollider, playerCollider))) continue;

            Physics.IgnoreCollision(itemCollider, playerCollider, true);
            ignoredCollisionPairs.Add((itemCollider, playerCollider));
        }
    }

    private void RestoreIgnoredCollisions()
    {
        for (int i = 0; i < ignoredCollisionPairs.Count; i++)
        {
            (Collider item, Collider player) pair = ignoredCollisionPairs[i];

            if (pair.item != null && pair.player != null)
                Physics.IgnoreCollision(pair.item, pair.player, false);
        }

        ignoredCollisionPairs.Clear();
    }

    // Manual collide-and-slide against player capsules (same projection math the player uses
    // against walls). Runs on the owner only, on the velocity the script is about to apply.
    private Vector3 SlideAroundPlayers(Vector3 velocity)
    {
        float deltaTime = Time.fixedDeltaTime;
        Bounds bounds = GetCombinedColliderBounds();
        float searchRadius = bounds.extents.magnitude + velocity.magnitude * deltaTime + 0.1f;

        int count = Physics.OverlapSphereNonAlloc(
            bounds.center,
            searchRadius,
            nearbyPlayerColliders,
            playerLayerMask,
            QueryTriggerInteraction.Ignore
        );

        if (count == 0)
            return velocity;

        // Two passes handle being squeezed between two players (clipping against one may
        // redirect the velocity into the other).
        for (int iteration = 0; iteration < 2; iteration++)
        {
            bool clippedAny = false;
            Vector3 predictedOffset = velocity * deltaTime;

            for (int p = 0; p < count; p++)
            {
                Collider playerCollider = nearbyPlayerColliders[p];

                if (playerCollider == null) continue;

                for (int i = 0; i < itemColliders.Length; i++)
                {
                    Collider itemCollider = itemColliders[i];

                    if (itemCollider.isTrigger) continue;

                    Transform itemColliderTransform = itemCollider.transform;

                    // Test the pose the item would have next tick, so entry is prevented
                    // before any real overlap happens.
                    bool overlapped = Physics.ComputePenetration(
                        itemCollider,
                        itemColliderTransform.position + predictedOffset,
                        itemColliderTransform.rotation,
                        playerCollider,
                        playerCollider.transform.position,
                        playerCollider.transform.rotation,
                        out Vector3 separationDirection,
                        out float separationDistance
                    );

                    if (!overlapped) continue;

                    // separationDirection points away from the player, toward the item.
                    float intoPlayerSpeed = Vector3.Dot(velocity, separationDirection);

                    if (intoPlayerSpeed < 0f)
                    {
                        velocity -= separationDirection * intoPlayerSpeed;
                        clippedAny = true;
                    }
                }
            }

            if (!clippedAny)
                break;
        }

        velocity += ComputePlayerSeparationVelocity(count, deltaTime);

        return velocity;
    }

    // If the item is already overlapping a player right now (lag, teleport), gently push
    // the item out. The player is never moved.
    private Vector3 ComputePlayerSeparationVelocity(int nearbyCount, float deltaTime)
    {
        const float maxSeparationSpeed = 2f;
        Vector3 separationVelocity = Vector3.zero;

        for (int p = 0; p < nearbyCount; p++)
        {
            Collider playerCollider = nearbyPlayerColliders[p];

            if (playerCollider == null) continue;

            for (int i = 0; i < itemColliders.Length; i++)
            {
                Collider itemCollider = itemColliders[i];

                if (itemCollider.isTrigger) continue;

                Transform itemColliderTransform = itemCollider.transform;

                bool overlapped = Physics.ComputePenetration(
                    itemCollider,
                    itemColliderTransform.position,
                    itemColliderTransform.rotation,
                    playerCollider,
                    playerCollider.transform.position,
                    playerCollider.transform.rotation,
                    out Vector3 separationDirection,
                    out float separationDistance
                );

                if (!overlapped) continue;

                float separationSpeed = Mathf.Min(separationDistance / deltaTime, maxSeparationSpeed);
                separationVelocity += separationDirection * separationSpeed;
            }
        }

        return Vector3.ClampMagnitude(separationVelocity, maxSeparationSpeed);
    }

    private Bounds GetCombinedColliderBounds()
    {
        Bounds bounds = new Bounds(rb.position, Vector3.zero);
        bool hasBounds = false;

        for (int i = 0; i < itemColliders.Length; i++)
        {
            Collider itemCollider = itemColliders[i];

            if (itemCollider.isTrigger) continue;

            if (hasBounds)
            {
                bounds.Encapsulate(itemCollider.bounds);
            }
            else
            {
                bounds = itemCollider.bounds;
                hasBounds = true;
            }
        }

        return bounds;
    }

    public override void OnNetworkSpawn()
    {
        // Runs on every client: non-owner proxies are kinematic (NetworkRigidbody) and would
        // shove local players via the solver unless the pairs are ignored locally too.
        netIsDragging.OnValueChanged += HandleDraggingCollisionChanged;

        if (netIsDragging.Value)
            EnableDraggedPhysicsMode();
    }

    public override void OnNetworkDespawn()
    {
        netIsDragging.OnValueChanged -= HandleDraggingCollisionChanged;
    }

    protected override void OnOwnershipChanged(ulong previous, ulong current)
    {
        base.OnOwnershipChanged(previous, current);

        if (!IsServer ||
            current != NetworkManager.ServerClientId ||
            !netIsDragging.Value)
        {
            return;
        }

        netIsDragging.Value = false;
    }

    public override void OnDestroy()
    {
        RestoreIgnoredCollisions();
        ActiveDraggedObjects.Remove(this);
        base.OnDestroy();
    }

    public override void OnInteract(InteractionContext context)
    {
        if (netIsDragging.Value) return;

        if (NetworkManager == null)
        {
            Debug.LogWarning("NetworkManager is not initialized. Cannot interact with draggable object.");
            return;
        }

        if (context.HitPoint == null)
        {
            Debug.LogWarning("HitPoint is null in InteractionContext. Cannot interact with draggable object.");
            return;
        }

        draggableContext = context;
        RequestOwnershipServerRpc();
    }

    public void OnUninteract()
    {
        Uninteract();
    }

    private void Interact(InteractionContext context)
    {
        if (holdPointTransform == null)
        {
            var go = new GameObject("HoldPoint_Local");
            holdPointTransform = go.transform;
        }
        playerInteraction = context.PlayerInteraction;

        holdPointTransform.position = context.HitPoint.position;
        CheckStartDistance(context.RayOriginPosition);

        holdPointTransform.SetParent(context.PlayerCameraTransform, worldPositionStays: true);

        playerController = context.PlayerController;
        SetupPlayerBeforeDragging();
    }

    private void Uninteract()
    {
        if (!IsOwner) return;

        if (netIsDragging.Value)
            StopDragging(applyThrow: true);
    }

    private void SetupPlayerBeforeDragging()
    {
        originalPlayerSpeed = playerController.GetSpeed();
        float speedMultiplier = 1f / (1f + ((DraggableObjectData)data).Mass * 0.3f);
        playerController.SetSpeed(originalPlayerSpeed * speedMultiplier);
    }

    private void RestorePlayerAfterDragging()
    {
        if (playerController != null)
            playerController.SetSpeed(originalPlayerSpeed);
    }

    private void CheckStartDistance(Vector3 rayOrigin)
    {
        if (holdPointTransform == null) return;

        float distance = Vector3.Distance(rayOrigin, holdPointTransform.transform.position);
        if (distance < ((DraggableObjectData)data).MinDistance)
        {
            holdPointTransform.position = rayOrigin + (holdPointTransform.position - rayOrigin).normalized * ((DraggableObjectData)data).MinDistance;
        }
    }

    #region ServerLogic

    //RPC

    [Rpc(SendTo.Server)]
    private void RequestOwnershipServerRpc(RpcParams rpcParams = default)
    {
        if (netIsDragging.Value || !CanStartDragging())
        {
            DenyDraggingRpc(RpcTarget.Single(rpcParams.Receive.SenderClientId, RpcTargetUse.Temp));
            return;
        }

        GetComponent<NetworkObject>().ChangeOwnership(rpcParams.Receive.SenderClientId);
        netIsDragging.Value = true;
        StartDraggingOwnerRpc();
    }

    // Overridable by subclasses that need to reject a drag start (e.g. item already picked up).
    protected virtual bool CanStartDragging()
    {
        return true;
    }

    [Rpc(SendTo.SpecifiedInParams)]
    private void DenyDraggingRpc(RpcParams rpcParams = default)
    {
        draggableContext.PlayerInteraction.DenyDragging();
    }

    [Rpc(SendTo.Server)]
    private void ResetNetIsDraggingServerRpc(RpcParams rpcParams = default)
    {
        var networkObject = GetComponent<NetworkObject>();

        if (networkObject.OwnerClientId != rpcParams.Receive.SenderClientId)
            return;

        netIsDragging.Value = false;
    }

    [Rpc(SendTo.Owner)]
    private void StartDraggingOwnerRpc()
    {
        Interact(draggableContext);
        StartDragging();
        playerInteraction.ConfirmDragging(this);
    }

    #endregion

    // Client Logic 

    private void StartDragging()
    {
        rb.useGravity = false;
        rb.linearDamping = 5f;
        rb.angularDamping = 10f;

        originalMass = rb.mass;
        rb.mass = 0.1f;

        previousPosition = rb.position;
        measuredVelocity = Vector3.zero;
        ResetVelocityBuffer();

        StartCoroutine(LerpMassCoroutine(from: rb.mass, to: originalMass, duration: 0.4f));
    }

    private void TickDragging()
    {
        Vector3 targetPos = holdPointTransform.position;
        float distanceToTarget = Vector3.Distance(rb.position, targetPos);

        if (distanceToTarget > ((DraggableObjectData)data).MaxDragDistance)
        {
            StopDragging(applyThrow: false);
            return;
        }

        Vector3 frameVelocity = (rb.position - previousPosition) / Time.fixedDeltaTime;
        velocityBuffer[velocityBufferIndex % velocityBuffer.Length] = frameVelocity;
        velocityBufferIndex++;
        previousPosition = rb.position;
        measuredVelocity = frameVelocity;

        Vector3 delta = targetPos - rb.position;
        Vector3 desiredSpeed = delta * ((DraggableObjectData)data).FollowSpeedMultiplier;

        Vector3 followVelocity = Vector3.ClampMagnitude(desiredSpeed, ((DraggableObjectData)data).MaxFollowSpeed);
        followVelocity = SlideAroundPlayers(followVelocity);

        rb.linearVelocity = followVelocity;
    }

    private void StopDragging(bool applyThrow)
    {
        StopAllCoroutines();

        rb.useGravity = true;
        rb.linearDamping = 0f;
        rb.angularDamping = 0.05f;
        rb.mass = originalMass;

        if (applyThrow)
        {
            rb.linearVelocity = GetAveragedVelocity();
        }
        else
        {
            rb.linearVelocity = Vector3.zero;
        }

        RestorePlayerAfterDragging();
        CleanupHoldPoint();
        playerInteraction.Undrag();
        ResetNetIsDraggingServerRpc();
    }

    // Coroutines

    private IEnumerator LerpMassCoroutine(float from, float to, float duration)
    {
        float elapsed = 0f;
        while (elapsed < duration)
        {
            elapsed += Time.fixedDeltaTime;
            rb.mass = Mathf.Lerp(from, to, elapsed / duration);
            yield return new WaitForFixedUpdate();
        }
        rb.mass = to;
    }

    // Utilities 

    private void CleanupHoldPoint()
    {
        if (holdPointTransform != null)
        {
            holdPointTransform.SetParent(null);
            Destroy(holdPointTransform.gameObject);
            holdPointTransform = null;
        }
    }

    private void ResetVelocityBuffer()
    {
        velocityBufferIndex = 0;
        for (int i = 0; i < velocityBuffer.Length; i++)
            velocityBuffer[i] = Vector3.zero;
    }

    private Vector3 GetAveragedVelocity()
    {
        Vector3 sum = Vector3.zero;
        foreach (var v in velocityBuffer)
            sum += v;
        return sum / velocityBuffer.Length;
    }

    // Gizmos 

    private void OnDrawGizmos()
    {
        if (!Application.isPlaying) return;

        if (holdPointTransform != null)
        {
            Gizmos.color = Color.green;
            Gizmos.DrawWireSphere(holdPointTransform.position, 0.12f);
            Gizmos.DrawLine(transform.position, holdPointTransform.position);
        }

        if (rb != null)
        {
            Gizmos.color = netIsDragging.Value ? Color.yellow : Color.red;
            Gizmos.DrawWireSphere(rb.position, 0.12f);
        }

        // Радиус автосброса
        Gizmos.color = new Color(1, 0, 0, 0.1f);
        if (holdPointTransform != null)
            Gizmos.DrawWireSphere(holdPointTransform.position, ((DraggableObjectData)data).MaxDragDistance);
    }
}
