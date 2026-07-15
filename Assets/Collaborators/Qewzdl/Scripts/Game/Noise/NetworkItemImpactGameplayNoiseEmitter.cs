using System.Collections.Generic;
using UnityEngine;

[DisallowMultipleComponent]
[RequireComponent(typeof(NetworkItemImpactSoundEmitter))]
public sealed class NetworkItemImpactGameplayNoiseEmitter : MonoBehaviour
{
    [SerializeField] private NetworkItemImpactSoundEmitter impactSoundEmitter;
    [SerializeField] private ItemImpactNoiseProfile noiseProfile;

    private readonly Dictionary<GameplayNoisePreset, float> lastEmitTimes = new();
    private IGameplayNoiseService noiseWorldService;
    private bool invalidConfigurationLogged;
    private bool missingNoiseWorldServiceLogged;

    public bool IsConfigured =>
        impactSoundEmitter != null &&
        noiseProfile != null &&
        noiseProfile.HasAnyNoise;

    public void Construct(IGameplayNoiseService service)
    {
        noiseWorldService = service;
        missingNoiseWorldServiceLogged = false;
    }

    public void ReleaseGameplayNoiseService()
    {
        noiseWorldService = null;
    }

    private void Awake()
    {
        CacheComponents();
    }

    private void OnEnable()
    {
        CacheComponents();
        Subscribe();
        ValidateDependencies();
    }

    private void OnDisable()
    {
        Unsubscribe();
        ResetRuntimeState();
    }

    private void HandleServerImpactAccepted(
        Vector3 position,
        ItemImpactSoundId impactId)
    {
        if (!ValidateDependencies() ||
            !impactSoundEmitter.IsServer ||
            !noiseProfile.TryGetPreset(
                impactId,
                out GameplayNoisePreset preset) ||
            !CanEmitByCooldown(preset) ||
            !TryResolveNoiseWorldService())
        {
            return;
        }

        bool emitted = noiseWorldService.TryRaiseNoiseServer(
            position,
            preset.Radius,
            preset.Loudness,
            preset.SourceType,
            impactSoundEmitter.NetworkObjectId,
            impactSoundEmitter.OwnerClientId,
            this
        );

        if (emitted)
        {
            lastEmitTimes[preset] = Time.time;
        }
    }

    private void Subscribe()
    {
        if (impactSoundEmitter == null)
        {
            return;
        }

        impactSoundEmitter.ServerImpactAccepted -= HandleServerImpactAccepted;
        impactSoundEmitter.ServerImpactAccepted += HandleServerImpactAccepted;
    }

    private void Unsubscribe()
    {
        if (impactSoundEmitter != null)
        {
            impactSoundEmitter.ServerImpactAccepted -= HandleServerImpactAccepted;
        }
    }

    private void CacheComponents()
    {
        if (impactSoundEmitter == null)
        {
            impactSoundEmitter = GetComponent<NetworkItemImpactSoundEmitter>();
        }
    }

    private bool ValidateDependencies()
    {
        CacheComponents();

        if (IsConfigured)
        {
            invalidConfigurationLogged = false;
            return true;
        }

        if (!invalidConfigurationLogged)
        {
            invalidConfigurationLogged = true;

            Debug.LogError(
                $"{nameof(NetworkItemImpactGameplayNoiseEmitter)} requires " +
                $"{nameof(NetworkItemImpactSoundEmitter)} and a configured " +
                $"{nameof(ItemImpactNoiseProfile)}.",
                this
            );
        }

        return false;
    }

    private bool TryResolveNoiseWorldService()
    {
        if (noiseWorldService != null && noiseWorldService.IsInitialized)
        {
            missingNoiseWorldServiceLogged = false;
            return true;
        }

        if (!missingNoiseWorldServiceLogged)
        {
            missingNoiseWorldServiceLogged = true;

            Debug.LogError(
                $"{nameof(NetworkItemImpactGameplayNoiseEmitter)} requires an initialized " +
                $"{nameof(IGameplayNoiseService)} from its item NetworkObject context.",
                this
            );
        }

        return false;
    }

    private bool CanEmitByCooldown(GameplayNoisePreset preset)
    {
        float cooldown = preset.ServerCooldown;

        if (cooldown <= 0f)
        {
            return true;
        }

        float lastEmitTime = lastEmitTimes.TryGetValue(
            preset,
            out float emitTime)
            ? emitTime
            : float.NegativeInfinity;

        return Time.time - lastEmitTime >= cooldown;
    }

    private void ResetRuntimeState()
    {
        lastEmitTimes.Clear();
        missingNoiseWorldServiceLogged = false;
    }

#if UNITY_EDITOR
    private void Reset()
    {
        CacheComponents();
    }

    private void OnValidate()
    {
        CacheComponents();
    }
#endif
}
