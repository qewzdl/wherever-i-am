using System.Text;
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
    [SerializeField] private GameplayNoiseWorldService noiseWorldService;
    [SerializeField] private Transform noiseOrigin;

    private float lastServerEmitTime = float.NegativeInfinity;
    private bool invalidConfigurationLogged;
    private bool nonOwnerRequestLogged;
    private bool nonServerEmitLogged;

    public bool IsConfigured => ValidateStaticDependencies(false);

    public void Construct(GameplayNoiseWorldService service)
    {
        noiseWorldService = service;
        invalidConfigurationLogged = false;
    }

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

        if (!ValidateStaticDependencies())
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

    private bool ValidateStaticDependencies()
    {
        return ValidateStaticDependencies(true);
    }

    private bool ValidateStaticDependencies(bool logErrors)
    {
        StringBuilder builder = new();

        if (noiseWorldService == null)
        {
            EnemyValidationLogger.AppendMissingDependency(
                builder,
                nameof(noiseWorldService)
            );
        }

        return EnemyValidationLogger.ValidateAndLog(
            this,
            nameof(GameplayNoiseEmitter),
            builder,
            ref invalidConfigurationLogged,
            logErrors,
            "Gameplay noise emitter is disabled until configured."
        );
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
        ValidateStaticDependencies();
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