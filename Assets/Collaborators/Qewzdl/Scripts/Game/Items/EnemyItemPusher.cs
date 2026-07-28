using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;

[DisallowMultipleComponent]
[RequireComponent(typeof(NetworkObject))]
[DefaultExecutionOrder(-100)]
public sealed class EnemyItemPusher : MonoBehaviour
{
    private const int MaximumDetectedColliders = 32;
    private const int MaximumLineHits = 32;
    private const float MinimumDirectionSqrMagnitude = 0.0001f;

    [Header("References")]
    [SerializeField] private NetworkObject networkObject;
    [SerializeField] private Collider bodyCollider;

    [Header("Detection")]
    [SerializeField] private LayerMask pushableLayers = 1 << 10;
    [SerializeField, Min(0f)] private float probeDistance = 2.25f;
    [SerializeField, Min(0.05f)] private float probeRadius = 0.7f;
    [SerializeField, Range(-1f, 1f)] private float minimumForwardDot;

    private readonly Collider[] detectedColliders =
        new Collider[MaximumDetectedColliders];
    private readonly RaycastHit[] lineHits =
        new RaycastHit[MaximumLineHits];
    private readonly HashSet<ItemNavigationObstacle> visitedItems = new();

    private IEnemyDirectMovementIntentSource directMovementIntentSource;

    public int ReservationOwnerId => gameObject.GetInstanceID();

    public bool IsPushingAnyItem { get; private set; }
    public float CurrentPushResistance { get; private set; }

    private void Awake()
    {
        CacheComponents();
    }

    private void FixedUpdate()
    {
        if (!CanRunServerPush() ||
            !TryGetDirectMovementDirection(out Vector3 direction, out _))
        {
            IsPushingAnyItem = false;
            CurrentPushResistance = 0f;
            return;
        }

        IsPushingAnyItem = PrepareItemsForPhysicalContact(direction);
    }

    public bool TryGetDirectMovementDirection(
        out Vector3 direction,
        out float speed)
    {
        if (directMovementIntentSource == null)
        {
            CacheDirectMovementIntentSource();
        }

        if (directMovementIntentSource == null ||
            !directMovementIntentSource.TryGetEnemyDirectMovementIntent(
                out Vector3 destination,
                out speed))
        {
            direction = default;
            speed = 0f;
            return false;
        }

        direction = destination - transform.position;
        direction.y = 0f;

        if (direction.sqrMagnitude < MinimumDirectionSqrMagnitude)
        {
            direction = default;
            speed = 0f;
            return false;
        }

        direction.Normalize();
        speed = Mathf.Max(0f, speed);
        return true;
    }

    public bool IsDirectApproachBlockedByItem(Vector3 destination)
    {
        return HasItemInDirectCorridor(
            destination,
            requireNavigationBlocker: true,
            requirePushable: false);
    }

    public bool HasNavigationBlockerOnRoute(Vector3[] routeCorners)
    {
        if (routeCorners == null || routeCorners.Length < 2)
        {
            return false;
        }

        Bounds bodyBounds = GetBodyBounds();
        float bodyRadius = Mathf.Max(
            0.05f,
            Mathf.Max(bodyBounds.extents.x, bodyBounds.extents.z));
        float bodyCenterOffset =
            bodyBounds.center.y - transform.position.y;

        for (int i = 0; i < routeCorners.Length - 1; i++)
        {
            if (HasItemAlongSegment(
                    routeCorners[i],
                    routeCorners[i + 1],
                    bodyRadius,
                    bodyBounds.extents.y,
                    bodyCenterOffset,
                    requireNavigationBlocker: true,
                    requirePushable: false))
            {
                return true;
            }
        }

        return false;
    }

    private bool CanRunServerPush()
    {
        NetworkManager manager = networkObject != null
            ? networkObject.NetworkManager
            : null;

        return networkObject != null &&
               networkObject.IsSpawned &&
               manager != null &&
               manager.IsServer;
    }

    private bool PrepareItemsForPhysicalContact(Vector3 direction)
    {
        visitedItems.Clear();
        GetProbeBox(
            direction,
            out Vector3 center,
            out Vector3 halfExtents,
            out Quaternion orientation
        );

        int count = Physics.OverlapBoxNonAlloc(
            center,
            halfExtents,
            detectedColliders,
            orientation,
            pushableLayers,
            QueryTriggerInteraction.Ignore
        );
        bool foundPushableItem = false;
        float pushResistance = 0f;

        for (int i = 0; i < count; i++)
        {
            Collider candidate = detectedColliders[i];
            ItemNavigationObstacle itemNavigation = GetItem(candidate);

            if (itemNavigation == null ||
                !visitedItems.Add(itemNavigation) ||
                !itemNavigation.CanBePushedByEnemyNow ||
                !IsForwardFrom(transform.position, candidate, direction) ||
                !HasClearPushChainLine(candidate) ||
                !itemNavigation.TryBeginPhysicalEnemyPushServer(
                    ReservationOwnerId))
            {
                continue;
            }

            foundPushableItem = true;
            pushResistance += itemNavigation.EnemyPushResistance;
        }

        CurrentPushResistance = pushResistance;
        return foundPushableItem;
    }

    private bool HasItemInDirectCorridor(
        Vector3 destination,
        bool requireNavigationBlocker,
        bool requirePushable)
    {
        Bounds bodyBounds = GetBodyBounds();
        float bodyRadius = Mathf.Max(
            0.05f,
            Mathf.Max(bodyBounds.extents.x, bodyBounds.extents.z));

        return HasItemAlongSegment(
            transform.position,
            destination,
            bodyRadius + 0.05f,
            bodyBounds.extents.y,
            bodyBounds.center.y - transform.position.y,
            requireNavigationBlocker,
            requirePushable);
    }

    private bool HasItemAlongSegment(
        Vector3 start,
        Vector3 end,
        float corridorRadius,
        float halfHeight,
        float bodyCenterOffset,
        bool requireNavigationBlocker,
        bool requirePushable)
    {
        Vector3 direction = end - start;
        direction.y = 0f;
        float distance = direction.magnitude;

        if (distance < 0.01f)
        {
            return false;
        }

        direction /= distance;
        Vector3 center =
            start + direction * (distance * 0.5f);
        center.y =
            (start.y + end.y) * 0.5f + bodyCenterOffset;

        int count = Physics.OverlapBoxNonAlloc(
            center,
            new Vector3(
                Mathf.Max(0.05f, corridorRadius),
                Mathf.Max(0.05f, halfHeight),
                distance * 0.5f),
            detectedColliders,
            Quaternion.LookRotation(direction, Vector3.up),
            pushableLayers,
            QueryTriggerInteraction.Ignore
        );
        visitedItems.Clear();

        for (int i = 0; i < count; i++)
        {
            ItemNavigationObstacle itemNavigation =
                GetItem(detectedColliders[i]);

            if (itemNavigation == null ||
                !visitedItems.Add(itemNavigation) ||
                requireNavigationBlocker &&
                !itemNavigation.IsBlockingNavigation ||
                requirePushable &&
                !itemNavigation.CanBePushedByEnemyNow)
            {
                continue;
            }

            return true;
        }

        return false;
    }

    private void GetProbeBox(
        Vector3 direction,
        out Vector3 center,
        out Vector3 halfExtents,
        out Quaternion orientation)
    {
        Bounds bounds = GetBodyBounds();
        float distance = Mathf.Max(0.05f, probeDistance);
        float lateralRadius = Mathf.Max(0.05f, probeRadius);

        center =
            bounds.center +
            direction * ((distance - lateralRadius) * 0.5f);
        halfExtents = new Vector3(
            lateralRadius,
            Mathf.Max(0.05f, bounds.extents.y),
            (distance + lateralRadius) * 0.5f
        );
        orientation = Quaternion.LookRotation(direction, Vector3.up);
    }

    private Bounds GetBodyBounds()
    {
        return bodyCollider != null
            ? bodyCollider.bounds
            : new Bounds(
                transform.position + Vector3.up,
                new Vector3(1f, 2f, 1f)
            );
    }

    private bool HasClearPushChainLine(Collider candidate)
    {
        if (!TryGetLine(candidate, out Vector3 origin, out Vector3 delta))
        {
            return true;
        }

        int count = Physics.RaycastNonAlloc(
            origin,
            delta.normalized,
            lineHits,
            delta.magnitude + 0.05f,
            Physics.DefaultRaycastLayers,
            QueryTriggerInteraction.Ignore
        );

        for (int i = 0; i < count; i++)
        {
            Collider hitCollider = lineHits[i].collider;

            if (hitCollider == null ||
                hitCollider.transform.IsChildOf(transform))
            {
                continue;
            }

            ItemNavigationObstacle itemNavigation = GetItem(hitCollider);

            if (itemNavigation == null ||
                !itemNavigation.CanBePushedByEnemyNow)
            {
                return false;
            }
        }

        return true;
    }

    private bool TryGetLine(
        Collider candidate,
        out Vector3 origin,
        out Vector3 delta)
    {
        if (candidate == null)
        {
            origin = default;
            delta = default;
            return false;
        }

        Bounds bodyBounds = GetBodyBounds();
        origin =
            bodyBounds.center +
            Vector3.up * Mathf.Max(0.1f, bodyBounds.extents.y * 0.5f);
        delta = candidate.bounds.center - origin;
        return delta.sqrMagnitude >= MinimumDirectionSqrMagnitude;
    }

    private bool IsForwardFrom(
        Vector3 origin,
        Collider candidate,
        Vector3 direction)
    {
        if (candidate == null)
        {
            return false;
        }

        Vector3 toItem = candidate.bounds.center - origin;
        toItem.y = 0f;

        return toItem.sqrMagnitude < MinimumDirectionSqrMagnitude ||
               Vector3.Dot(toItem.normalized, direction) >= minimumForwardDot;
    }

    private static ItemNavigationObstacle GetItem(Collider candidate)
    {
        return candidate != null
            ? candidate.GetComponentInParent<ItemNavigationObstacle>()
            : null;
    }

    private void CacheComponents()
    {
        if (networkObject == null)
        {
            networkObject = GetComponent<NetworkObject>();
        }

        if (bodyCollider == null)
        {
            bodyCollider = GetComponent<Collider>();
        }

        CacheDirectMovementIntentSource();
    }

    private void CacheDirectMovementIntentSource()
    {
        MonoBehaviour[] components = GetComponents<MonoBehaviour>();

        for (int i = 0; i < components.Length; i++)
        {
            if (components[i] is IEnemyDirectMovementIntentSource source)
            {
                directMovementIntentSource = source;
                return;
            }
        }

        directMovementIntentSource = null;
    }

    private void OnDisable()
    {
        IsPushingAnyItem = false;
        CurrentPushResistance = 0f;
    }

#if UNITY_EDITOR
    private void Reset()
    {
        CacheComponents();
    }

    private void OnValidate()
    {
        probeDistance = Mathf.Max(0f, probeDistance);
        probeRadius = Mathf.Max(0.05f, probeRadius);
        CacheComponents();
    }
#endif
}
