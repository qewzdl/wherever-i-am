using Unity.Netcode;
using UnityEngine;

[DisallowMultipleComponent]
public class EnemyTarget : MonoBehaviour
{
    [SerializeField] private Transform aimPoint;
    [SerializeField] private Transform[] visibilityPoints;
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
    }

    public int GetVisibilityPointsNonAlloc(Vector3[] results, float targetHeightOffset)
    {
        if (results == null || results.Length == 0)
        {
            return 0;
        }

        int count = 0;

        if (visibilityPoints != null)
        {
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
        }

        if (count > 0)
        {
            return count;
        }

        results[count] = AimPosition;
        count++;

        if (count < results.Length && targetHeightOffset > 0f)
        {
            results[count] = transform.position + Vector3.up * targetHeightOffset;
            count++;
        }

        if (count < results.Length)
        {
            results[count] = transform.position;
            count++;
        }

        return count;
    }

    private void CacheNetworkObject()
    {
        cachedNetworkObject = GetComponentInParent<NetworkObject>();
    }

#if UNITY_EDITOR
    private void OnValidate()
    {
        CacheNetworkObject();
    }

    private void OnDrawGizmosSelected()
    {
        Transform point = aimPoint != null ? aimPoint : transform;

        Gizmos.color = Color.green;
        Gizmos.DrawSphere(point.position, 0.08f);

        if (visibilityPoints == null)
        {
            return;
        }

        Gizmos.color = Color.cyan;

        for (int i = 0; i < visibilityPoints.Length; i++)
        {
            Transform visibilityPoint = visibilityPoints[i];

            if (visibilityPoint == null)
            {
                continue;
            }

            Gizmos.DrawSphere(visibilityPoint.position, 0.06f);
        }
    }
#endif
}