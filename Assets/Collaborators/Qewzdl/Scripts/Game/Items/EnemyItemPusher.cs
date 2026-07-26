using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.AI;

[DisallowMultipleComponent]
[RequireComponent(typeof(NetworkObject))]
[RequireComponent(typeof(NavMeshAgent))]
public sealed class EnemyItemPusher : MonoBehaviour
{
    private const int MaximumDetectedColliders = 16;
    private const int MaximumLineOfSightHits = 16;
    private const float MinimumDirectionSqrMagnitude = 0.0001f;

    [Header("References")]
    [SerializeField] private NetworkObject networkObject;
    [SerializeField] private NavMeshAgent agent;
    [SerializeField] private Collider bodyCollider;

    [Header("Detection")]
    [SerializeField] private LayerMask pushableLayers = 1 << 10;
    [SerializeField, Min(0f)] private float probeDistance = 2.25f;
    [SerializeField, Min(0.05f)] private float probeRadius = 0.7f;
    [SerializeField, Range(-1f, 1f)] private float minimumForwardDot;

    private readonly Collider[] detectedColliders =
        new Collider[MaximumDetectedColliders];
    private readonly RaycastHit[] lineOfSightHits =
        new RaycastHit[MaximumLineOfSightHits];
    private readonly HashSet<ItemNavigationObstacle> visitedItems = new();

    private HashSet<ItemNavigationObstacle> activePushes = new();
    private HashSet<ItemNavigationObstacle> currentPushes = new();
    private IEnemyPushNavigationIntentSource navigationIntentSource;
    private ItemNavigationObstacle authorizedPushItem;
    private int sourceId;

    public bool IsPushingAnyItem => activePushes.Count > 0;
    public bool HasAuthorizedPush => authorizedPushItem != null;

    public bool TryGetActivePushDirection(out Vector3 direction)
    {
        if (authorizedPushItem == null ||
            !activePushes.Contains(authorizedPushItem))
        {
            direction = default;
            return false;
        }

        direction = authorizedPushItem.transform.position - transform.position;
        direction.y = 0f;

        if (direction.sqrMagnitude < MinimumDirectionSqrMagnitude)
        {
            direction = default;
            return false;
        }

        direction.Normalize();
        return true;
    }

    public bool IsDirectApproachBlockedByItem(Vector3 destination)
    {
        Vector3 direction = destination - transform.position;
        direction.y = 0f;

        float distance = direction.magnitude;

        if (distance < 0.01f)
        {
            return false;
        }

        direction /= distance;

        Bounds bounds = bodyCollider != null
            ? bodyCollider.bounds
            : new Bounds(
                transform.position + Vector3.up,
                new Vector3(1f, 2f, 1f)
            );
        float approachRadius = agent != null
            ? Mathf.Max(0.05f, agent.radius)
            : Mathf.Max(0.05f, bounds.extents.x);
        Vector3 center =
            transform.position +
            direction * (distance * 0.5f);
        center.y = bounds.min.y + approachRadius;

        Vector3 halfExtents = new(
            approachRadius,
            approachRadius,
            distance * 0.5f
        );
        Quaternion orientation = Quaternion.LookRotation(direction, Vector3.up);

        int count = Physics.OverlapBoxNonAlloc(
            center,
            halfExtents,
            detectedColliders,
            orientation,
            pushableLayers,
            QueryTriggerInteraction.Ignore
        );

        for (int i = 0; i < count; i++)
        {
            Collider candidate = detectedColliders[i];
            ItemNavigationObstacle itemNavigation = candidate != null
                ? candidate.GetComponentInParent<ItemNavigationObstacle>()
                : null;

            if (itemNavigation != null &&
                (itemNavigation.IsBlockingNavigation ||
                 itemNavigation.IsBeingPushedByEnemy))
            {
                return true;
            }
        }

        return false;
    }

    public bool TryFindPushableItemNear(
        Vector3 position,
        Vector3 routeDirection,
        out ItemNavigationObstacle pushableItem)
    {
        pushableItem = null;
        routeDirection.y = 0f;

        if (routeDirection.sqrMagnitude < MinimumDirectionSqrMagnitude)
        {
            return false;
        }

        routeDirection.Normalize();

        float radius = Mathf.Max(0.1f, probeDistance + probeRadius);
        int count = Physics.OverlapSphereNonAlloc(
            position,
            radius,
            detectedColliders,
            pushableLayers,
            QueryTriggerInteraction.Ignore
        );

        for (int i = 0; i < count; i++)
        {
            Collider candidate = detectedColliders[i];
            ItemNavigationObstacle itemNavigation = candidate != null
                ? candidate.GetComponentInParent<ItemNavigationObstacle>()
                : null;

            if (itemNavigation == null ||
                !itemNavigation.CanBePushedByEnemyNow ||
                !HasClearPushLine(candidate, itemNavigation))
            {
                continue;
            }

            Vector3 toItem = candidate.bounds.center - position;
            toItem.y = 0f;

            if (toItem.sqrMagnitude < MinimumDirectionSqrMagnitude ||
                Vector3.Dot(toItem.normalized, routeDirection) >= minimumForwardDot)
            {
                pushableItem = itemNavigation;
                return true;
            }
        }

        return false;
    }

    public void AuthorizePush(ItemNavigationObstacle itemNavigation)
    {
        if (itemNavigation == null || itemNavigation == authorizedPushItem)
        {
            return;
        }

        ReleaseAllPushes();
        authorizedPushItem = itemNavigation;
    }

    public void CancelAuthorizedPush()
    {
        ReleaseAllPushes();
        authorizedPushItem = null;
    }

    private void Awake()
    {
        sourceId = GetInstanceID();
        CacheComponents();
    }

    private void FixedUpdate()
    {
        if (authorizedPushItem == null ||
            !CanRunServerPush() ||
            !TryGetMovementDirection(out Vector3 direction))
        {
            ReleaseAllPushes();
            return;
        }

        PushNearbyItems(direction);
    }

    private bool CanRunServerPush()
    {
        NetworkManager manager = networkObject != null
            ? networkObject.NetworkManager
            : null;

        return networkObject != null &&
               networkObject.IsSpawned &&
               manager != null &&
               manager.IsServer &&
               agent != null &&
               agent.enabled &&
               agent.isOnNavMesh;
    }

    private bool TryGetMovementDirection(out Vector3 direction)
    {
        if (navigationIntentSource == null)
        {
            CacheNavigationIntentSource();
        }

        if (navigationIntentSource != null &&
            navigationIntentSource.TryGetEnemyPushNavigationIntent(
                out Vector3 destination))
        {
            direction = destination - transform.position;
        }
        else
        {
            if (agent == null || agent.isStopped || !agent.hasPath)
            {
                direction = default;
                return false;
            }

            direction = agent.destination - transform.position;
        }

        direction.y = 0f;

        if (direction.sqrMagnitude < MinimumDirectionSqrMagnitude)
        {
            direction = default;
            return false;
        }

        direction.Normalize();
        return true;
    }

    private void PushNearbyItems(Vector3 direction)
    {
        visitedItems.Clear();
        currentPushes.Clear();
        bool wasPushingAuthorizedItem =
            activePushes.Contains(authorizedPushItem);

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

        for (int i = 0; i < count; i++)
        {
            Collider candidate = detectedColliders[i];
            ItemNavigationObstacle itemNavigation = candidate != null
                ? candidate.GetComponentInParent<ItemNavigationObstacle>()
                : null;

            if (itemNavigation == null ||
                itemNavigation != authorizedPushItem ||
                !visitedItems.Add(itemNavigation) ||
                !IsInFront(candidate, direction) ||
                !HasClearPushLine(candidate, itemNavigation) ||
                !itemNavigation.TryBeginEnemyPushServer(sourceId))
            {
                continue;
            }

            currentPushes.Add(itemNavigation);
        }

        foreach (ItemNavigationObstacle previous in activePushes)
        {
            if (previous != null && !currentPushes.Contains(previous))
            {
                previous.ReleaseEnemyPushServer(sourceId);
            }
        }

        (activePushes, currentPushes) = (currentPushes, activePushes);

        if (wasPushingAuthorizedItem &&
            !activePushes.Contains(authorizedPushItem))
        {
            authorizedPushItem = null;
        }
    }

    private bool IsInFront(Collider candidate, Vector3 direction)
    {
        Vector3 toItem = candidate.bounds.center - transform.position;
        toItem.y = 0f;

        return toItem.sqrMagnitude < MinimumDirectionSqrMagnitude ||
               Vector3.Dot(toItem.normalized, direction) >= minimumForwardDot;
    }

    private void GetProbeBox(
        Vector3 direction,
        out Vector3 center,
        out Vector3 halfExtents,
        out Quaternion orientation)
    {
        Bounds bounds = bodyCollider != null
            ? bodyCollider.bounds
            : new Bounds(
                transform.position + Vector3.up,
                new Vector3(1f, 2f, 1f)
            );
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

    private bool HasClearPushLine(
        Collider candidate,
        ItemNavigationObstacle expectedItem)
    {
        if (candidate == null || expectedItem == null)
        {
            return false;
        }

        Vector3 origin;

        if (bodyCollider != null)
        {
            Bounds bodyBounds = bodyCollider.bounds;
            origin =
                bodyBounds.center +
                Vector3.up * Mathf.Max(0.1f, bodyBounds.extents.y * 0.5f);
        }
        else
        {
            origin = transform.position + Vector3.up;
        }

        Vector3 delta = candidate.bounds.center - origin;
        float distance = delta.magnitude;

        if (distance < 0.01f)
        {
            return true;
        }

        int count = Physics.RaycastNonAlloc(
            origin,
            delta / distance,
            lineOfSightHits,
            distance + 0.05f,
            Physics.DefaultRaycastLayers,
            QueryTriggerInteraction.Ignore
        );

        float closestDistance = float.PositiveInfinity;
        Collider closestCollider = null;

        for (int i = 0; i < count; i++)
        {
            Collider hitCollider = lineOfSightHits[i].collider;

            if (hitCollider == null ||
                hitCollider.transform.IsChildOf(transform) ||
                lineOfSightHits[i].distance >= closestDistance)
            {
                continue;
            }

            closestDistance = lineOfSightHits[i].distance;
            closestCollider = hitCollider;
        }

        return closestCollider == null ||
               closestCollider.GetComponentInParent<ItemNavigationObstacle>() ==
               expectedItem;
    }

    private void ReleaseAllPushes()
    {
        foreach (ItemNavigationObstacle item in activePushes)
        {
            if (item != null)
            {
                item.ReleaseEnemyPushServer(sourceId);
            }
        }

        foreach (ItemNavigationObstacle item in currentPushes)
        {
            if (item != null)
            {
                item.ReleaseEnemyPushServer(sourceId);
            }
        }

        activePushes.Clear();
        currentPushes.Clear();
        visitedItems.Clear();
    }

    private void CacheComponents()
    {
        if (networkObject == null)
        {
            networkObject = GetComponent<NetworkObject>();
        }

        if (agent == null)
        {
            agent = GetComponent<NavMeshAgent>();
        }

        if (bodyCollider == null)
        {
            bodyCollider = GetComponent<Collider>();
        }

        if (navigationIntentSource == null)
        {
            CacheNavigationIntentSource();
        }
    }

    private void CacheNavigationIntentSource()
    {
        MonoBehaviour[] components = GetComponents<MonoBehaviour>();

        for (int i = 0; i < components.Length; i++)
        {
            if (components[i] is IEnemyPushNavigationIntentSource source)
            {
                navigationIntentSource = source;
                break;
            }
        }
    }

    private void OnDisable()
    {
        CancelAuthorizedPush();
    }

    private void OnDestroy()
    {
        CancelAuthorizedPush();
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
        minimumForwardDot = Mathf.Clamp(minimumForwardDot, -1f, 1f);
        CacheComponents();
    }
#endif
}
