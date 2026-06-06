using Unity.Netcode;
using UnityEngine;

[DisallowMultipleComponent]
public class EnemyNoiseEmitter : MonoBehaviour
{
    [SerializeField, Min(0f)] private float radius = 8f;
    [SerializeField, Min(0f)] private float loudness = 1f;
    [SerializeField] private GameplayNoiseSourceType sourceType = GameplayNoiseSourceType.Player;
    [SerializeField] private EnemyTarget sourceTarget;

    private GameplayNoiseWorldService noiseWorldService;
    private bool missingNoiseWorldServiceLogged;

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
        if (noiseWorldService != null && noiseWorldService.IsInitialized)
        {
            service = noiseWorldService;
            return true;
        }

        ProjectContext context = ProjectContext.Instance;
        noiseWorldService = context != null
            ? context.GameplayNoiseWorld
            : null;

        service = noiseWorldService;

        if (service != null && service.IsInitialized)
        {
            missingNoiseWorldServiceLogged = false;
            return true;
        }

        if (!missingNoiseWorldServiceLogged)
        {
            missingNoiseWorldServiceLogged = true;

            Debug.LogError(
                $"{nameof(EnemyNoiseEmitter)} requires initialized " +
                $"{nameof(GameplayNoiseWorldService)} from {nameof(ProjectContext)}.",
                this
            );
        }

        return false;
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
