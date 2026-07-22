using System.Text;
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
    private IReplicatedPlayerHidingStateService hidingState;
    private bool invalidVisibilityConfigurationLogged;

    public Transform AimPoint => aimPoint != null ? aimPoint : transform;
    public Vector3 AimPosition => AimPoint.position;
    public bool CanBeDetected =>
        canBeDetected && (hidingState == null || !hidingState.IsHidden);
    public HidingTransitionState HidingState =>
        hidingState != null
            ? hidingState.HidingState
            : HidingTransitionState.Available;
    public bool IsEnteringHiding =>
        HidingState == HidingTransitionState.Entering;

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
        CacheHidingState();
        ValidateVisibilityConfiguration();
    }

    public int GetVisibilityPointsNonAlloc(Vector3[] results, float targetHeightOffset)
    {
        if (results == null || results.Length == 0)
        {
            return 0;
        }

        if (!ValidateVisibilityConfiguration())
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
            return AddColliderBoundsVisibilityPoints(results, targetHeightOffset);
        }

        return 0;
    }

    public bool TryGetVisibilityBounds(out Bounds bounds)
    {
        bounds = default;

        if (!ValidateVisibilityConfiguration())
        {
            return false;
        }

        if (useColliderBoundsVisibility && TryGetCombinedVisibilityBounds(out bounds))
        {
            return true;
        }

        if (visibilityPoints == null)
        {
            return false;
        }

        bool hasPoint = false;

        for (int i = 0; i < visibilityPoints.Length; i++)
        {
            Transform point = visibilityPoints[i];

            if (point == null)
            {
                continue;
            }

            if (!hasPoint)
            {
                bounds = new Bounds(point.position, Vector3.zero);
                hasPoint = true;
                continue;
            }

            bounds.Encapsulate(point.position);
        }

        return hasPoint;
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

            AddPoint(results, ref count, point.position);
        }

        return count;
    }

    private int AddColliderBoundsVisibilityPoints(Vector3[] results, float targetHeightOffset)
    {
        if (!TryGetCombinedVisibilityBounds(out Bounds bounds))
        {
            return 0;
        }

        Vector3 boundsCenter = bounds.center;
        Vector3 extents = bounds.extents;

        float inset = Mathf.Max(0f, boundsInset);

        float x = Mathf.Max(0f, extents.x - inset);
        float z = Mathf.Max(0f, extents.z - inset);

        float minY = bounds.min.y + inset;
        float maxY = bounds.max.y - inset;

        if (minY > maxY)
        {
            minY = boundsCenter.y;
            maxY = boundsCenter.y;
        }

        float focusedY = Mathf.Clamp(
            bounds.min.y + Mathf.Max(0f, targetHeightOffset),
            minY,
            maxY
        );

        Vector3 focusedCenter = new(boundsCenter.x, focusedY, boundsCenter.z);
        Vector3 topPoint = new(boundsCenter.x, maxY, boundsCenter.z);
        Vector3 bottomPoint = new(boundsCenter.x, minY, boundsCenter.z);
        Vector3 upperPoint = new(boundsCenter.x, Mathf.Lerp(focusedY, maxY, 0.5f), boundsCenter.z);

        int count = 0;

        AddPoint(results, ref count, focusedCenter);
        AddPoint(results, ref count, topPoint);
        AddPoint(results, ref count, bottomPoint);

        AddPoint(results, ref count, focusedCenter + transform.right * x);
        AddPoint(results, ref count, focusedCenter - transform.right * x);

        AddPoint(results, ref count, focusedCenter + transform.forward * z);
        AddPoint(results, ref count, focusedCenter - transform.forward * z);

        AddPoint(results, ref count, upperPoint);

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

        if (!useColliderBoundsVisibility || visibilityColliders == null)
        {
            return false;
        }

        bool hasBounds = false;

        for (int i = 0; i < visibilityColliders.Length; i++)
        {
            Collider visibilityCollider = visibilityColliders[i];

            if (!IsValidVisibilityCollider(visibilityCollider))
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

    private bool ValidateVisibilityConfiguration(bool logErrors = true)
    {
        bool hasVisibilityPoints = HasValidVisibilityPointSource();
        bool hasVisibilityColliders = HasValidVisibilityColliderSource();

        if (hasVisibilityPoints || hasVisibilityColliders)
        {
            invalidVisibilityConfigurationLogged = false;
            return true;
        }

        if (logErrors)
        {
            LogInvalidVisibilityConfiguration();
        }

        return false;
    }

    private bool HasValidVisibilityPointSource()
    {
        if (visibilityPoints == null)
        {
            return false;
        }

        for (int i = 0; i < visibilityPoints.Length; i++)
        {
            if (visibilityPoints[i] != null)
            {
                return true;
            }
        }

        return false;
    }

    private bool HasValidVisibilityColliderSource()
    {
        if (!useColliderBoundsVisibility || visibilityColliders == null)
        {
            return false;
        }

        for (int i = 0; i < visibilityColliders.Length; i++)
        {
            if (IsValidVisibilityCollider(visibilityColliders[i]))
            {
                return true;
            }
        }

        return false;
    }

    private bool IsValidVisibilityCollider(Collider visibilityCollider)
    {
        if (visibilityCollider == null || !visibilityCollider.enabled)
        {
            return false;
        }

        if (!includeTriggerColliders && visibilityCollider.isTrigger)
        {
            return false;
        }

        return true;
    }

    private void LogInvalidVisibilityConfiguration()
    {
        if (invalidVisibilityConfigurationLogged)
        {
            return;
        }

        invalidVisibilityConfigurationLogged = true;

        StringBuilder builder = new();

        builder.AppendLine($"{nameof(EnemyTarget)} has invalid visibility configuration:");
        builder.AppendLine($"- Assign at least one valid {nameof(visibilityPoints)} item.");
        builder.AppendLine($"- Or enable {nameof(useColliderBoundsVisibility)} and assign at least one valid {nameof(visibilityColliders)} item.");
        builder.AppendLine("Enemy vision will ignore this target until visibility sources are configured.");

        Debug.LogError(builder.ToString(), this);
    }

    private void CacheNetworkObject()
    {
        cachedNetworkObject = GetComponentInParent<NetworkObject>();
    }

    public bool TryGetCurrentHidingPlace(
        out HidingPlaceInteractable hidingPlace
    )
    {
        hidingPlace = null;

        if (hidingState == null ||
            !hidingState.IsInHidingSequence ||
            hidingState.HidingPlaceNetworkObjectId ==
            HidingPlaceInteractable.NoOccupantNetworkObjectId)
        {
            return false;
        }

        NetworkObject targetNetworkObject = NetworkObject;
        NetworkManager manager = targetNetworkObject != null
            ? targetNetworkObject.NetworkManager
            : null;

        if (manager == null ||
            manager.SpawnManager == null ||
            !manager.SpawnManager.SpawnedObjects.TryGetValue(
                hidingState.HidingPlaceNetworkObjectId,
                out NetworkObject hidingObject) ||
            hidingObject == null)
        {
            return false;
        }

        hidingPlace =
            hidingObject.GetComponent<HidingPlaceInteractable>();
        return hidingPlace != null;
    }

    private void CacheHidingState()
    {
        MonoBehaviour[] behaviours = GetComponentsInParent<MonoBehaviour>(true);

        for (int i = 0; i < behaviours.Length; i++)
        {
            if (behaviours[i] is IReplicatedPlayerHidingStateService service)
            {
                hidingState = service;
                return;
            }
        }
    }

#if UNITY_EDITOR
    private void Reset()
    {
        CacheNetworkObject();
        CacheHidingState();
        boundsInset = Mathf.Max(0f, boundsInset);
        ValidateVisibilityConfiguration();
    }

    private void OnValidate()
    {
        boundsInset = Mathf.Max(0f, boundsInset);

        CacheNetworkObject();
        CacheHidingState();
        ValidateVisibilityConfiguration();
    }

    private void OnDrawGizmosSelected()
    {
        Vector3[] previewPoints = new Vector3[8];
        int pointCount = GetVisibilityPointsNonAlloc(previewPoints, 0f);

        Gizmos.color = Color.green;
        Gizmos.DrawSphere(AimPosition, 0.08f);

        Gizmos.color = Color.cyan;

        for (int i = 0; i < pointCount; i++)
        {
            Gizmos.DrawSphere(previewPoints[i], 0.06f);
        }

        if (TryGetVisibilityBounds(out Bounds bounds))
        {
            Gizmos.color = new Color(0f, 1f, 1f, 0.25f);
            Gizmos.DrawWireCube(bounds.center, bounds.size);
        }
    }
#endif
}
