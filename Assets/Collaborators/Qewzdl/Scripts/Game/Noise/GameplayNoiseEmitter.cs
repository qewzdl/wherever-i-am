using Unity.Netcode;
using UnityEngine;

[DisallowMultipleComponent]
[RequireComponent(typeof(NetworkObject))]
public class GameplayNoiseEmitter : NetworkBehaviour
{
    [Header("Noise")]
    [SerializeField] private GameplayNoiseSourceType sourceType = GameplayNoiseSourceType.Environment;
    [SerializeField, Min(0f)] private float radius = 8f;
    [SerializeField, Min(0f)] private float loudness = 1f;
    [SerializeField, Min(0f)] private float serverCooldown = 0.15f;

    [Header("References")]
    [SerializeField] private Transform noiseOrigin;

    private GameplayNoiseWorldService noiseWorldService;
    private float lastServerEmitTime = float.NegativeInfinity;
    private bool invalidConfigurationLogged;
    private bool nonOwnerRequestLogged;
    private bool nonServerEmitLogged;

    public bool IsConfigured => ValidateRuntimeDependencies(false);

    public bool TryEmitServer()
    {
        return TryEmitServer(radius, loudness, sourceType);
    }

    public bool TryEmitServer(
        float noiseRadius,
        float noiseLoudness,
        GameplayNoiseSourceType noiseSourceType
    )
    {
        if (!IsServer)
        {
            LogNonServerEmit();
            return false;
        }

        if (!ValidateRuntimeDependencies())
        {
            return false;
        }

        if (!CanEmitByCooldown())
        {
            return false;
        }

        lastServerEmitTime = Time.time;

        return noiseWorldService.TryRaiseNoiseServer(
            GetNoisePosition(),
            noiseRadius,
            noiseLoudness,
            noiseSourceType,
            NetworkObjectId,
            OwnerClientId,
            this
        );
    }

    public void RequestEmitFromOwner()
    {
        if (IsServer)
        {
            TryEmitServer();
            return;
        }

        if (!IsOwner)
        {
            LogNonOwnerRequest();
            return;
        }

        RequestEmitFromOwnerServerRpc();
    }

    [ServerRpc]
    private void RequestEmitFromOwnerServerRpc(ServerRpcParams serverRpcParams = default)
    {
        if (serverRpcParams.Receive.SenderClientId != OwnerClientId)
        {
            return;
        }

        TryEmitServer();
    }

    private bool CanEmitByCooldown()
    {
        if (serverCooldown <= 0f)
        {
            return true;
        }

        return Time.time - lastServerEmitTime >= serverCooldown;
    }

    private Vector3 GetNoisePosition()
    {
        return noiseOrigin != null
            ? noiseOrigin.position
            : transform.position;
    }

    private bool ValidateRuntimeDependencies()
    {
        return ValidateRuntimeDependencies(true);
    }

    private bool ValidateRuntimeDependencies(bool logErrors)
    {
        if (noiseWorldService != null && noiseWorldService.IsInitialized)
        {
            invalidConfigurationLogged = false;
            return true;
        }

        ProjectContext context = ProjectContext.Instance;
        noiseWorldService = context != null
            ? context.GameplayNoiseWorld
            : null;

        if (noiseWorldService != null && noiseWorldService.IsInitialized)
        {
            invalidConfigurationLogged = false;
            return true;
        }

        noiseWorldService = null;

        if (logErrors && !invalidConfigurationLogged)
        {
            invalidConfigurationLogged = true;

            Debug.LogError(
                $"{nameof(GameplayNoiseEmitter)} requires initialized " +
                $"{nameof(GameplayNoiseWorldService)} from {nameof(ProjectContext)}.",
                this
            );
        }

        return false;
    }

    private void LogNonOwnerRequest()
    {
        if (nonOwnerRequestLogged)
        {
            return;
        }

        nonOwnerRequestLogged = true;

        Debug.LogWarning(
            $"{nameof(GameplayNoiseEmitter)} ignored owner noise request from non-owner client.",
            this
        );
    }

    private void LogNonServerEmit()
    {
        if (nonServerEmitLogged)
        {
            return;
        }

        nonServerEmitLogged = true;

        Debug.LogWarning(
            $"{nameof(GameplayNoiseEmitter)} can register gameplay noise only on server.",
            this
        );
    }

#if UNITY_EDITOR
    private void OnValidate()
    {
        radius = Mathf.Max(0f, radius);
        loudness = Mathf.Max(0f, loudness);
        serverCooldown = Mathf.Max(0f, serverCooldown);
    }

    private void OnDrawGizmosSelected()
    {
        Gizmos.color = Color.cyan;
        Gizmos.DrawWireSphere(
            noiseOrigin != null ? noiseOrigin.position : transform.position,
            radius
        );
    }
#endif
}
