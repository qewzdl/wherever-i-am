using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;

[DisallowMultipleComponent]
[RequireComponent(typeof(NetworkObject))]
public sealed class GameplayNoiseEmitter : NetworkBehaviour
{
    [Header("Default Noise")]
    [SerializeField] private GameplayNoisePreset defaultPreset;

    [Header("References")]
    [SerializeField] private Transform noiseOrigin;

    [Header("Owner Requests")]
    [Tooltip("Required for client-owned requests. Leave empty for server-only emission.")]
    [SerializeField] private MonoBehaviour ownerRequestValidatorBehaviour;
    [Tooltip("Additional presets the owner is allowed to request. The default preset is always included.")]
    [SerializeField] private List<GameplayNoisePreset> ownerRequestPresets = new();

    private readonly Dictionary<GameplayNoisePreset, float> lastPresetEmitTimes = new();
    private IGameplayNoiseService noiseWorldService;
    private IGameplayNoiseRequestValidator ownerRequestValidator;
    private float lastRawEmitTime = float.NegativeInfinity;
    private bool invalidConfigurationLogged;
    private bool invalidPresetLogged;
    private bool invalidOwnerRequestValidatorLogged;
    private bool nonOwnerRequestLogged;
    private bool nonServerEmitLogged;

    public bool IsConfigured => ValidateRuntimeDependencies(false);
    public GameplayNoisePreset DefaultPreset => defaultPreset;
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

    public override void OnNetworkSpawn()
    {
        if (noiseWorldService == null)
        {
            NetworkObjectServiceContext.TryResolveSessionService(
                NetworkManager,
                out noiseWorldService);
        }

        ResetCooldowns();
    }

    public override void OnNetworkDespawn()
    {
        ResetCooldowns();
        noiseWorldService = null;
    }

    public void Construct(IGameplayNoiseService service)
    {
        noiseWorldService = service;
        invalidConfigurationLogged = false;
    }

    public bool TryEmitServer()
    {
        return TryEmitServer(defaultPreset);
    }

    public bool TryEmitServer(GameplayNoisePreset preset)
    {
        return TryEmitServer(GetNoisePosition(), preset);
    }

    public bool TryEmitServer(
        Vector3 position,
        GameplayNoisePreset preset)
    {
        if (!ValidatePreset(preset))
        {
            return false;
        }

        return TryEmitServerInternal(
            position,
            preset.Radius,
            preset.Loudness,
            preset.SourceType,
            preset.ServerCooldown,
            preset
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
            defaultPreset
        );
    }

    public bool TryEmitServer(
        Vector3 position,
        float noiseRadius,
        float noiseLoudness,
        GameplayNoiseSourceType noiseSourceType
    )
    {
        float cooldown = defaultPreset != null
            ? defaultPreset.ServerCooldown
            : 0f;

        return TryEmitServerInternal(
            position,
            noiseRadius,
            noiseLoudness,
            noiseSourceType,
            cooldown,
            null
        );
    }

    private bool TryEmitServerInternal(
        Vector3 position,
        float noiseRadius,
        float noiseLoudness,
        GameplayNoiseSourceType noiseSourceType,
        float serverCooldown,
        GameplayNoisePreset preset)
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

        if (!CanEmitByCooldown(preset, serverCooldown))
        {
            return false;
        }

        bool emitted = noiseWorldService.TryRaiseNoiseServer(
            position,
            noiseRadius,
            noiseLoudness,
            noiseSourceType,
            NetworkObjectId,
            OwnerClientId,
            this
        );

        if (emitted)
        {
            RecordEmitTime(preset);
        }

        return emitted;
    }

    public bool RequestEmitFromOwner()
    {
        return RequestEmitFromOwner(defaultPreset);
    }

    public bool RequestEmitFromOwner(GameplayNoisePreset preset)
    {
        if (IsServer)
        {
            return TryEmitServer(preset);
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

        if (!TryGetOwnerPresetIndex(preset, out int presetIndex))
        {
            LogInvalidPreset(preset);
            return false;
        }

        RequestEmitFromOwnerRpc(presetIndex);
        return true;
    }

    [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Owner)]
    private void RequestEmitFromOwnerRpc(
        int presetIndex,
        RpcParams rpcParams = default)
    {
        if (rpcParams.Receive.SenderClientId != OwnerClientId ||
            !TryGetOwnerPreset(presetIndex, out GameplayNoisePreset preset))
        {
            return;
        }

        if (!ValidateRuntimeDependencies())
        {
            return;
        }

        if (!ValidateOwnerRequestServer(
                preset,
                rpcParams.Receive.SenderClientId))
        {
            return;
        }

        TryEmitServer(preset);
    }

    private bool ValidateOwnerRequestServer(
        GameplayNoisePreset preset,
        ulong senderClientId)
    {
        CacheOwnerRequestValidator();

        if (ownerRequestValidator == null)
        {
            LogInvalidOwnerRequestValidator();
            return false;
        }

        return ownerRequestValidator.CanEmitNoiseServer(
            this,
            preset,
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

    private bool CanEmitByCooldown(
        GameplayNoisePreset preset,
        float serverCooldown)
    {
        if (serverCooldown <= 0f)
        {
            return true;
        }

        float lastEmitTime = lastRawEmitTime;

        if (preset != null)
        {
            lastEmitTime = lastPresetEmitTimes.TryGetValue(
                preset,
                out float presetEmitTime)
                ? presetEmitTime
                : float.NegativeInfinity;
        }

        return Time.time - lastEmitTime >= serverCooldown;
    }

    private void RecordEmitTime(GameplayNoisePreset preset)
    {
        if (preset != null)
        {
            lastPresetEmitTimes[preset] = Time.time;
            return;
        }

        lastRawEmitTime = Time.time;
    }

    private bool TryGetOwnerPresetIndex(
        GameplayNoisePreset preset,
        out int presetIndex)
    {
        presetIndex = -1;

        if (!ValidatePreset(preset))
        {
            return false;
        }

        if (preset == defaultPreset)
        {
            presetIndex = 0;
            return true;
        }

        if (ownerRequestPresets == null)
        {
            return false;
        }

        for (int i = 0; i < ownerRequestPresets.Count; i++)
        {
            if (ownerRequestPresets[i] != preset)
            {
                continue;
            }

            presetIndex = i + 1;
            return true;
        }

        return false;
    }

    private bool TryGetOwnerPreset(
        int presetIndex,
        out GameplayNoisePreset preset)
    {
        preset = null;

        if (presetIndex == 0)
        {
            preset = defaultPreset;
            return ValidatePreset(preset);
        }

        int additionalIndex = presetIndex - 1;

        if (ownerRequestPresets == null ||
            additionalIndex < 0 ||
            additionalIndex >= ownerRequestPresets.Count)
        {
            return false;
        }

        preset = ownerRequestPresets[additionalIndex];
        return ValidatePreset(preset);
    }

    private bool ValidatePreset(GameplayNoisePreset preset)
    {
        if (preset != null && preset.IsValid)
        {
            invalidPresetLogged = false;
            return true;
        }

        LogInvalidPreset(preset);
        return false;
    }

    private Vector3 GetNoisePosition()
    {
        return noiseOrigin != null
            ? noiseOrigin.position
            : transform.position;
    }

    private void ResetCooldowns()
    {
        lastPresetEmitTimes.Clear();
        lastRawEmitTime = float.NegativeInfinity;
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

        if (logErrors && !invalidConfigurationLogged)
        {
            invalidConfigurationLogged = true;

            Debug.LogError(
                $"{nameof(GameplayNoiseEmitter)} requires an initialized " +
                $"{nameof(IGameplayNoiseService)} from its NetworkObject context.",
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

    private void LogInvalidPreset(GameplayNoisePreset preset)
    {
        if (invalidPresetLogged)
        {
            return;
        }

        invalidPresetLogged = true;

        string reason = preset == null
            ? "no preset is assigned"
            : $"preset '{preset.name}' has an unknown source type, zero radius, or zero loudness";

        Debug.LogWarning(
            $"{nameof(GameplayNoiseEmitter)} cannot emit noise because {reason}.",
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

        float radius = defaultPreset != null
            ? defaultPreset.Radius
            : 0f;

        Gizmos.DrawWireSphere(
            noiseOrigin != null ? noiseOrigin.position : transform.position,
            radius
        );
    }
#endif
}
