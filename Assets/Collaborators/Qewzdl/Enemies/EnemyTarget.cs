using Unity.Netcode;
using UnityEngine;

[DisallowMultipleComponent]
public class EnemyTarget : MonoBehaviour
{
    [SerializeField] private Transform aimPoint;
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
    }
#endif
}