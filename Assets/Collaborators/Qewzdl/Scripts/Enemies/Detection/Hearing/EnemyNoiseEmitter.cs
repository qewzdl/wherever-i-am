using Unity.Netcode;
using UnityEngine;

[DisallowMultipleComponent]
public class EnemyNoiseEmitter : MonoBehaviour
{
    [SerializeField, Min(0f)] private float radius = 8f;
    [SerializeField, Min(0f)] private float loudness = 1f;
    [SerializeField] private GameplayNoiseSourceType sourceType = GameplayNoiseSourceType.Player;
    [SerializeField] private EnemyTarget sourceTarget;
    [SerializeField] private GameplayNoiseWorldService noiseWorldService;

    public void Construct(GameplayNoiseWorldService service)
    {
        noiseWorldService = service;
    }

    public bool RaiseNoiseServer()
    {
        if (!TryGetNoiseWorldService(out GameplayNoiseWorldService service))
        {
            return false;
        }

        return service.TryRaiseNoiseServer(
            transform.position,
            radius,
            loudness,
            sourceType,
            GetSourceNetworkObjectId(),
            GetSourceClientId(),
            GetSourceObject()
        );
    }

    public bool RaiseNoiseServer(Vector3 position)
    {
        if (!TryGetNoiseWorldService(out GameplayNoiseWorldService service))
        {
            return false;
        }

        return service.TryRaiseNoiseServer(
            position,
            radius,
            loudness,
            sourceType,
            GetSourceNetworkObjectId(),
            GetSourceClientId(),
            GetSourceObject()
        );
    }

    public bool RaiseNoiseServer(float customRadius, float customLoudness)
    {
        if (!TryGetNoiseWorldService(out GameplayNoiseWorldService service))
        {
            return false;
        }

        return service.TryRaiseNoiseServer(
            transform.position,
            customRadius,
            customLoudness,
            sourceType,
            GetSourceNetworkObjectId(),
            GetSourceClientId(),
            GetSourceObject()
        );
    }

    private bool TryGetNoiseWorldService(out GameplayNoiseWorldService service)
    {
        if (noiseWorldService == null)
        {
            noiseWorldService = FindFirstObjectByType<GameplayNoiseWorldService>();
        }

        service = noiseWorldService;
        return service != null;
    }

    private ulong GetSourceNetworkObjectId()
    {
        if (!TryGetSourceNetworkObject(out NetworkObject networkObject))
        {
            return GameplayNoiseEvent.NoNetworkObjectId;
        }

        return networkObject.NetworkObjectId;
    }

    private ulong GetSourceClientId()
    {
        if (!TryGetSourceNetworkObject(out NetworkObject networkObject))
        {
            return GameplayNoiseEvent.NoClientId;
        }

        return networkObject.OwnerClientId;
    }

    private bool TryGetSourceNetworkObject(out NetworkObject networkObject)
    {
        networkObject = sourceTarget != null
            ? sourceTarget.NetworkObject
            : GetComponentInParent<NetworkObject>();

        return networkObject != null && networkObject.IsSpawned;
    }

    private Object GetSourceObject()
    {
        return sourceTarget != null ? sourceTarget : this;
    }

#if UNITY_EDITOR
    private void OnValidate()
    {
        radius = Mathf.Max(0f, radius);
        loudness = Mathf.Max(0f, loudness);
    }

    private void OnDrawGizmosSelected()
    {
        Gizmos.color = Color.blue;
        Gizmos.DrawWireSphere(transform.position, radius);
    }
#endif
}
