using Unity.Netcode;
using UnityEngine;

[DisallowMultipleComponent]
[RequireComponent(typeof(NetworkObject))]
public sealed class GameplayNoiseEmitter : NetworkBehaviour
{
    [Header("Noise")]
    [SerializeField] private GameplayNoiseSourceType sourceType = GameplayNoiseSourceType.Environment;
    [SerializeField, Min(0f)] private float radius = 8f;
    [SerializeField, Min(0f)] private float loudness = 1f;
    [SerializeField, Min(0f)] private float serverCooldown = 0.15f;

    [Header("References")]
    [SerializeField] private Transform noiseOrigin;

    [Header("Owner Requests")]
    [Tooltip("Required for client-owned requests. Leave empty for server-only emission.")]
    [SerializeField] private MonoBehaviour ownerRequestValidatorBehaviour;

    private GameplayNoiseWorldService noiseWorldService;
    private IGameplayNoiseRequestValidator ownerRequestValidator;
    private float lastServerEmitTime = float.NegativeInfinity;
    private bool invalidConfigurationLogged;
    private bool invalidOwnerRequestValidatorLogged;
    private bool nonOwnerRequestLogged;
    private bool nonServerEmitLogged;

    public bool IsConfigured => ValidateRuntimeDependencies(false);
    public bool CanRequestFromOwner
    {
        get
        {
            CacheOwnerRequestValidator();
            return ownerRequestValidator != null;
        }
    }

    private void Awake()
    {
        CacheOwnerRequestValidator();
    }

    public bool TryEmitServer()
    {
        return TryEmitServer(
            GetNoisePosition(),
            radius,
            loudness,
            sourceType
        );
    }

    public bool TryEmitServer(
        float noiseRadius,
        float noiseLoudness,
        GameplayNoiseSourceType noiseSourceType
    )
    {
        return TryEmitServer(
            GetNoisePosition(),
            noiseRadius,
            noiseLoudness,
            noiseSourceType
        );
    }

    public bool TryEmitServer(Vector3 position)
    {
        return TryEmitServer(
            position,
            radius,
            loudness,
            sourceType
        );
    }

    public bool TryEmitServer(
        Vector3 position,
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
            position,
            noiseRadius,
            noiseLoudness,
            noiseSourceType,
            NetworkObjectId,
            OwnerClientId,
            this
        );
    }

    public bool RequestEmitFromOwner()
    {
        if (IsServer)
        {
            return TryEmitServer();
        }

        if (!IsOwner)
        {
            LogNonOwnerRequest();
            return false;
        }

        if (!CanRequestFromOwner)
        {
            LogInvalidOwnerRequestValidator();
            return false;
        }

        RequestEmitFromOwnerServerRpc();
        return true;
    }

    [ServerRpc]
    private void RequestEmitFromOwnerServerRpc(ServerRpcParams serverRpcParams = default)
    {
        if (serverRpcParams.Receive.SenderClientId != OwnerClientId)
        {
            return;
        }

        if (!ValidateRuntimeDependencies() || !CanEmitByCooldown())
        {
            return;
        }

        if (!ValidateOwnerRequestServer(serverRpcParams.Receive.SenderClientId))
        {
            return;
        }

        TryEmitServer();
    }

    private bool ValidateOwnerRequestServer(ulong senderClientId)
    {
        CacheOwnerRequestValidator();

        if (ownerRequestValidator == null)
        {
            LogInvalidOwnerRequestValidator();
            return false;
        }

        return ownerRequestValidator.CanEmitNoiseServer(
            this,
            senderClientId
        );
    }

    private void CacheOwnerRequestValidator()
    {
        ownerRequestValidator =
            ownerRequestValidatorBehaviour as IGameplayNoiseRequestValidator;

        if (ownerRequestValidator != null)
        {
            invalidOwnerRequestValidatorLogged = false;
        }
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

    private void LogInvalidOwnerRequestValidator()
    {
        if (invalidOwnerRequestValidatorLogged)
        {
            return;
        }

        invalidOwnerRequestValidatorLogged = true;

        string reason = ownerRequestValidatorBehaviour == null
            ? "no validator is assigned"
            : $"assigned component does not implement {nameof(IGameplayNoiseRequestValidator)}";

        Debug.LogWarning(
            $"{nameof(GameplayNoiseEmitter)} rejected an owner noise request because {reason}. " +
            "Use direct server emission for authoritative actions or assign a server-side validator.",
            this
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
        CacheOwnerRequestValidator();

        if (ownerRequestValidatorBehaviour != null &&
            ownerRequestValidator == null)
        {
            LogInvalidOwnerRequestValidator();
        }
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
