using Unity.Netcode;
using UnityEngine;

[DisallowMultipleComponent]
public class EnemyTarget : MonoBehaviour
{
    [Header("Aim")]
    [SerializeField] private Transform aimPoint;

    [Header("Visibility")]
    [SerializeField] private Transform[] visibilityPoints;
    [SerializeField] private Collider[] visibilityColliders;
    [SerializeField] private bool useColliderBoundsVisibility = true;
    [SerializeField] private bool includeTriggerColliders;
    [SerializeField, Min(0f)] private float boundsInset = 0.08f;

    [Header("Detection")]
    [SerializeField] private bool canBeDetected = true;

    private NetworkObject cachedNetworkObject;

    public Transform AimPoint => aimPoint != null ? aimPoint : transform;
    public Vector3 AimPosition => AimPoint.position;
    public bool CanBeDetected => canBeDetected;

    public NetworkObject NetworkObject
    {
        get
        {
            if (cachedNetworkObject == null)
            {
                CacheNetworkObject();
            }

            return cachedNetworkObject;
        }
    }

    public bool IsValidNetworkTarget
    {
        get
        {
            NetworkObject networkObject = NetworkObject;
            return networkObject != null && networkObject.IsSpawned;
        }
    }

    private void Awake()
    {
        CacheNetworkObject();
        CacheVisibilityColliders();
    }

    public int GetVisibilityPointsNonAlloc(Vector3[] results, float targetHeightOffset)
    {
        if (results == null || results.Length == 0)
        {
            return 0;
        }

        int count = AddExplicitVisibilityPoints(results);

        if (count > 0)
        {
            return count;
        }

        if (useColliderBoundsVisibility)
        {
            count = AddColliderBoundsVisibilityPoints(results);

            if (count > 0)
            {
                return count;
            }
        }

        return AddFallbackVisibilityPoints(results, targetHeightOffset);
    }

    private int AddExplicitVisibilityPoints(Vector3[] results)
    {
        int count = 0;

        if (visibilityPoints == null)
        {
            return count;
        }

        for (int i = 0; i < visibilityPoints.Length && count < results.Length; i++)
        {
            Transform point = visibilityPoints[i];

            if (point == null)
            {
                continue;
            }

            results[count] = point.position;
            count++;
        }

        return count;
    }

    private int AddColliderBoundsVisibilityPoints(Vector3[] results)
    {
        CacheVisibilityColliders();

        if (visibilityColliders == null || visibilityColliders.Length == 0)
        {
            return 0;
        }

        if (!TryGetCombinedVisibilityBounds(out Bounds bounds))
        {
            return 0;
        }

        Vector3 center = bounds.center;
        Vector3 extents = bounds.extents;

        float inset = Mathf.Max(0f, boundsInset);

        float x = Mathf.Max(0f, extents.x - inset);
        float y = Mathf.Max(0f, extents.y - inset);
        float z = Mathf.Max(0f, extents.z - inset);

        int count = 0;

        AddPoint(results, ref count, center + Vector3.up * y);

        AddPoint(results, ref count, center);
        AddPoint(results, ref count, center - Vector3.up * y);

        AddPoint(results, ref count, center + transform.right * x);
        AddPoint(results, ref count, center - transform.right * x);

        AddPoint(results, ref count, center + transform.forward * z);
        AddPoint(results, ref count, center - transform.forward * z);

        AddPoint(results, ref count, center + Vector3.up * (y * 0.5f));

        return count;
    }

    private int AddFallbackVisibilityPoints(Vector3[] results, float targetHeightOffset)
    {
        int count = 0;

        AddPoint(results, ref count, AimPosition);

        if (aimPoint == null && targetHeightOffset > 0f)
        {
            float safeHeightOffset = Mathf.Min(targetHeightOffset, 0.6f);
            AddPoint(results, ref count, transform.position + Vector3.up * safeHeightOffset);
        }

        return count;
    }

    private void AddPoint(Vector3[] results, ref int count, Vector3 point)
    {
        if (count >= results.Length)
        {
            return;
        }

        results[count] = point;
        count++;
    }

    private bool TryGetCombinedVisibilityBounds(out Bounds combinedBounds)
    {
        combinedBounds = default;
        bool hasBounds = false;

        for (int i = 0; i < visibilityColliders.Length; i++)
        {
            Collider visibilityCollider = visibilityColliders[i];

            if (visibilityCollider == null || !visibilityCollider.enabled)
            {
                continue;
            }

            if (!includeTriggerColliders && visibilityCollider.isTrigger)
            {
                continue;
            }

            if (!hasBounds)
            {
                combinedBounds = visibilityCollider.bounds;
                hasBounds = true;
                continue;
            }

            combinedBounds.Encapsulate(visibilityCollider.bounds);
        }

        return hasBounds;
    }

    private void CacheNetworkObject()
    {
        cachedNetworkObject = GetComponentInParent<NetworkObject>();
    }

    private void CacheVisibilityColliders()
    {
        if (!useColliderBoundsVisibility)
        {
            return;
        }

        if (visibilityColliders != null && visibilityColliders.Length > 0)
        {
            return;
        }

        visibilityColliders = GetComponentsInChildren<Collider>();
    }

#if UNITY_EDITOR
    private void Reset()
    {
        CacheNetworkObject();
        CacheVisibilityColliders();
    }

    private void OnValidate()
    {
        boundsInset = Mathf.Max(0f, boundsInset);

        CacheNetworkObject();

        if (useColliderBoundsVisibility)
        {
            CacheVisibilityColliders();
        }
    }

    private void OnDrawGizmosSelected()
    {
        Vector3[] previewPoints = new Vector3[8];
        int pointCount = GetVisibilityPointsNonAlloc(previewPoints, 0.6f);

        Gizmos.color = Color.green;
        Gizmos.DrawSphere(AimPosition, 0.08f);

        Gizmos.color = Color.cyan;

        for (int i = 0; i < pointCount; i++)
        {
            Gizmos.DrawSphere(previewPoints[i], 0.06f);
        }

        if (TryGetCombinedVisibilityBounds(out Bounds bounds))
        {
            Gizmos.color = new Color(0f, 1f, 1f, 0.25f);
            Gizmos.DrawWireCube(bounds.center, bounds.size);
        }
    }
#endif
}