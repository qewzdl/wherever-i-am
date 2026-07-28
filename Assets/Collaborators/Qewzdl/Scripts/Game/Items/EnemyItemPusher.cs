using System.Collections.Generic;
using UnityEngine;

[DisallowMultipleComponent]
public sealed class EnemyItemPusher : MonoBehaviour
{
    private const int MaximumDetectedColliders = 32;

    [Header("References")]
    [SerializeField] private Collider bodyCollider;

    [Header("Detection")]
    [SerializeField] private LayerMask pushableLayers = 1 << 10;

    private readonly Collider[] detectedColliders =
        new Collider[MaximumDetectedColliders];
    private readonly HashSet<ItemNavigationObstacle> visitedItems = new();

    private void Awake()
    {
        CacheComponents();
    }

    public bool IsDirectApproachBlockedByItem(Vector3 destination)
    {
        return HasItemInDirectCorridor(destination);
    }

    public bool HasNavigationBlockerOnRoute(Vector3[] routeCorners)
    {
        return TryGetItemAlongRoute(routeCorners, GetCorridorRadius(), out _);
    }

    private bool TryGetItemAlongRoute(
        Vector3[] routeCorners,
        float corridorRadius,
        out ItemNavigationObstacle blockingItem)
    {
        blockingItem = null;

        if (routeCorners == null || routeCorners.Length < 2)
        {
            return false;
        }

        Bounds bodyBounds = GetBodyBounds();
        float bodyCenterOffset =
            bodyBounds.center.y - transform.position.y;

        for (int i = 0; i < routeCorners.Length - 1; i++)
        {
            if (TryGetItemAlongSegment(
                    routeCorners[i],
                    routeCorners[i + 1],
                    corridorRadius,
                    bodyBounds.extents.y,
                    bodyCenterOffset,
                    out blockingItem))
            {
                return true;
            }
        }

        return false;
    }

    private float GetCorridorRadius()
    {
        Bounds bodyBounds = GetBodyBounds();
        return Mathf.Max(
            0.05f,
            Mathf.Max(bodyBounds.extents.x, bodyBounds.extents.z));
    }

    private bool HasItemInDirectCorridor(Vector3 destination)
    {
        Bounds bodyBounds = GetBodyBounds();
        float bodyRadius = Mathf.Max(
            0.05f,
            Mathf.Max(bodyBounds.extents.x, bodyBounds.extents.z));

        return TryGetItemAlongSegment(
            transform.position,
            destination,
            bodyRadius + 0.05f,
            bodyBounds.extents.y,
            bodyBounds.center.y - transform.position.y,
            out _);
    }

    private bool TryGetItemAlongSegment(
        Vector3 start,
        Vector3 end,
        float corridorRadius,
        float halfHeight,
        float bodyCenterOffset,
        out ItemNavigationObstacle blockingItem)
    {
        blockingItem = null;
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
                !itemNavigation.IsBlockingNavigation)
            {
                continue;
            }

            blockingItem = itemNavigation;
            return true;
        }

        return false;
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

    private static ItemNavigationObstacle GetItem(Collider candidate)
    {
        return candidate != null
            ? candidate.GetComponentInParent<ItemNavigationObstacle>()
            : null;
    }

    private void CacheComponents()
    {
        if (bodyCollider == null)
        {
            bodyCollider = GetComponent<Collider>();
        }
    }

#if UNITY_EDITOR
    private void Reset()
    {
        CacheComponents();
    }

    private void OnValidate()
    {
        CacheComponents();
    }
#endif
}
