using System.Collections.Generic;
using System.Text;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.SceneManagement;

[DisallowMultipleComponent]
public sealed class GameplayNoiseWorldService : MonoBehaviour, IGameplayNoiseService
{
    [Header("Storage")]
    [SerializeField, Min(1)] private int maxStoredNoises = 128;

    [Header("Network")]
    [SerializeField] private NetworkManager networkManager;

    [Header("Cleanup")]
    [SerializeField] private bool clearOnInitialize = true;
    [SerializeField] private bool clearOnDestroy = true;
    [Tooltip("Allows cleanup in editor/offline scene runs where NetworkManager is not listening yet.")]
    [SerializeField] private bool clearWithoutActiveNetworkSession = true;

    [Header("Debug")]
    [SerializeField] private bool logLifecycleCleanup;

    private readonly List<GameplayNoiseEvent> noises = new();

    private NetworkManager subscribedNetworkManager;

    private bool initialized;
    private bool networkCallbacksSubscribed;
    private bool invalidConfigurationLogged;

    public bool IsInitialized => initialized;

    public bool IsConfigured => ValidateRuntimeDependencies(false);

    public bool Construct(NetworkManager manager)
    {
        if (networkManager != manager && initialized)
            Shutdown();

        networkManager = manager;
        invalidConfigurationLogged = false;

        if (!enabled)
        {
            enabled = true;
        }

        return Initialize();
    }

    public void Shutdown()
    {
        if (clearOnDestroy)
            Clear("shutdown");

        SceneManager.sceneUnloaded -= HandleSceneUnloaded;
        UnsubscribeFromNetworkCallbacks();
        initialized = false;
    }

    public void DisposeRuntime()
    {
        Shutdown();
        networkManager = null;
        invalidConfigurationLogged = false;
    }

    public bool Initialize()
    {
        if (initialized)
        {
            return true;
        }

        if (!ValidateRuntimeDependencies())
        {
            DisableUntilConfigured();
            return false;
        }

        initialized = true;

        SceneManager.sceneUnloaded -= HandleSceneUnloaded;
        SceneManager.sceneUnloaded += HandleSceneUnloaded;

        SubscribeToNetworkCallbacks();

        if (clearOnInitialize)
        {
            ClearIfAllowed("initialize");
        }

        return true;
    }

    private void Awake()
    {
        ValidateRuntimeDependencies();
    }

    private void OnEnable()
    {
        if (!initialized)
        {
            return;
        }

        SceneManager.sceneUnloaded -= HandleSceneUnloaded;
        SceneManager.sceneUnloaded += HandleSceneUnloaded;

        SubscribeToNetworkCallbacks();
    }

    private void OnDisable()
    {
        SceneManager.sceneUnloaded -= HandleSceneUnloaded;
        UnsubscribeFromNetworkCallbacks();
    }

    private void OnDestroy()
    {
        DisposeRuntime();
    }

    public bool ValidateRuntimeDependencies()
    {
        return ValidateRuntimeDependencies(true);
    }

    public bool TryRaiseNoiseServer(
        Vector3 position,
        float radius,
        float loudness,
        GameplayNoiseSourceType sourceType,
        ulong sourceNetworkObjectId = GameplayNoiseEvent.NoNetworkObjectId,
        ulong sourceClientId = GameplayNoiseEvent.NoClientId,
        Object sourceObject = null
    )
    {
        GameplayNoiseEvent noiseEvent = new(
            position,
            radius,
            loudness,
            Time.time,
            sourceType,
            sourceNetworkObjectId,
            sourceClientId,
            sourceObject
        );

        return TryRegisterNoiseServer(noiseEvent);
    }

    public bool TryRegisterNoiseServer(GameplayNoiseEvent noiseEvent)
    {
        if (!CanUseServerWorld())
        {
            return false;
        }

        if (!noiseEvent.IsValid)
        {
            return false;
        }

        int storageCapacity = Mathf.Max(1, maxStoredNoises);

        if (noises.Count >= storageCapacity)
        {
            noises.RemoveAt(0);
        }

        noises.Add(noiseEvent);
        return true;
    }

    public bool TryFindBestNoise(
        Vector3 listenerPosition,
        float hearingRadius,
        float memoryDuration,
        float minimumLoudness,
        out GameplayNoiseEvent bestNoise,
        out float bestScore
    )
    {
        bestNoise = default;
        bestScore = 0f;

        hearingRadius = Mathf.Max(0f, hearingRadius);
        memoryDuration = Mathf.Max(0f, memoryDuration);
        minimumLoudness = Mathf.Max(0f, minimumLoudness);

        if (!CanUseServerWorld() || hearingRadius <= 0f)
        {
            return false;
        }

        float now = Time.time;
        bool hasBestNoise = false;

        for (int i = noises.Count - 1; i >= 0; i--)
        {
            GameplayNoiseEvent noise = noises[i];

            float age = now - noise.CreatedAtTime;

            if (age > memoryDuration)
            {
                continue;
            }

            if (!noise.IsValid || noise.Loudness < minimumLoudness)
            {
                continue;
            }

            float distance = Vector3.Distance(listenerPosition, noise.Position);
            float effectiveRadius = Mathf.Min(hearingRadius, noise.Radius);

            if (distance > effectiveRadius)
            {
                continue;
            }

            float score = ScoreNoise(
                noise.Loudness,
                distance,
                effectiveRadius,
                age,
                memoryDuration
            );

            if (score <= bestScore)
            {
                continue;
            }

            bestScore = score;
            bestNoise = noise;
            hasBestNoise = true;
        }

        return hasBestNoise;
    }

    // Loud, close and recent, in that order of what the enemy goes to look at.
    //
    // Age used to count for nothing: a noise scored the same on its last
    // remembered frame as on its first, so one loud bang held every enemy in
    // range for the whole memory duration and a quieter, nearer, newer sound
    // could not displace it. Fading to nothing at the memory horizon also
    // removes the cliff, where a noise went from top candidate to forgotten
    // between two frames.
    //
    // Internal rather than private so the scoring can be checked without
    // standing up a server; TryFindBestNoise needs one.
    internal static float ScoreNoise(
        float loudness,
        float distance,
        float effectiveRadius,
        float age,
        float memoryDuration
    )
    {
        float normalizedDistance = distance / Mathf.Max(0.001f, effectiveRadius);
        float freshness = Mathf.Clamp01(
            1f - age / Mathf.Max(0.001f, memoryDuration)
        );

        return loudness * (1f - normalizedDistance) * freshness;
    }

    public void Clear()
    {
        Clear("manual clear");
    }

    private void ClearIfAllowed(string reason)
    {
        if (!CanClearForCurrentContext())
        {
            return;
        }

        Clear(reason);
    }

    private void Clear(string reason)
    {
        if (noises.Count == 0)
        {
            return;
        }

        noises.Clear();

        if (logLifecycleCleanup)
        {
            RuntimeLog.Info(
                $"{nameof(GameplayNoiseWorldService)} cleared noise events on {reason}.",
                this
            );
        }
    }

    private bool CanUseServerWorld()
    {
        if (!ValidateRuntimeDependencies())
        {
            DisableUntilConfigured();
            return false;
        }

        SubscribeToNetworkCallbacks();

        return networkManager.IsListening && networkManager.IsServer;
    }

    private bool CanClearForCurrentContext()
    {
        if (!ValidateRuntimeDependencies())
        {
            return false;
        }

        if (!networkManager.IsListening)
        {
            return clearWithoutActiveNetworkSession;
        }

        return true;
    }

    private bool ValidateRuntimeDependencies(bool logErrors)
    {
        StringBuilder builder = new();

        if (networkManager == null)
        {
            EnemyValidationLogger.AppendMissingDependency(
                builder,
                nameof(networkManager)
            );
        }

        return EnemyValidationLogger.ValidateAndLog(
            this,
            nameof(GameplayNoiseWorldService),
            builder,
            ref invalidConfigurationLogged,
            logErrors,
            "Gameplay noise world service is disabled until configured."
        );
    }

    private void SubscribeToNetworkCallbacks()
    {
        if (!ValidateRuntimeDependencies())
        {
            return;
        }

        NetworkManager currentNetworkManager = networkManager;

        if (networkCallbacksSubscribed && subscribedNetworkManager == currentNetworkManager)
        {
            return;
        }

        UnsubscribeFromNetworkCallbacks();

        subscribedNetworkManager = currentNetworkManager;
        subscribedNetworkManager.OnServerStopped += HandleServerStopped;
        subscribedNetworkManager.OnClientStopped += HandleClientStopped;
        subscribedNetworkManager.OnClientDisconnectCallback += HandleClientDisconnected;

        networkCallbacksSubscribed = true;
    }

    private void UnsubscribeFromNetworkCallbacks()
    {
        if (!networkCallbacksSubscribed)
        {
            return;
        }

        if (subscribedNetworkManager != null)
        {
            subscribedNetworkManager.OnServerStopped -= HandleServerStopped;
            subscribedNetworkManager.OnClientStopped -= HandleClientStopped;
            subscribedNetworkManager.OnClientDisconnectCallback -= HandleClientDisconnected;
        }

        subscribedNetworkManager = null;
        networkCallbacksSubscribed = false;
    }

    private void HandleSceneUnloaded(Scene scene)
    {
        if (scene.handle != gameObject.scene.handle)
        {
            return;
        }

        Clear("scene unload");
    }

    private void HandleServerStopped(bool wasHost)
    {
        Clear("server stopped");
    }

    private void HandleClientStopped(bool wasHost)
    {
        Clear("client stopped");
    }

    private void HandleClientDisconnected(ulong clientId)
    {
        if (!ValidateRuntimeDependencies())
        {
            return;
        }

        if (clientId != networkManager.LocalClientId)
        {
            return;
        }

        Clear("local client disconnect");
    }

    private void DisableUntilConfigured()
    {
        enabled = false;
    }

#if UNITY_EDITOR
    private void OnValidate()
    {
        maxStoredNoises = Mathf.Max(1, maxStoredNoises);
        ValidateRuntimeDependencies(false);
    }
#endif
}
