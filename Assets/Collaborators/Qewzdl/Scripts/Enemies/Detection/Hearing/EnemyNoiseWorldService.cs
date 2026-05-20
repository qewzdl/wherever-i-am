using System.Collections.Generic;
using System.Text;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.SceneManagement;

[DisallowMultipleComponent]
public sealed class EnemyNoiseWorldService : MonoBehaviour, IEnemyValidatedComponent
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

    private readonly List<EnemyNoiseEvent> noises = new();

    private NetworkManager subscribedNetworkManager;

    private bool initialized;
    private bool networkCallbacksSubscribed;
    private bool invalidStaticConfigurationLogged;

    public bool IsInitialized => initialized;

    public bool IsConfigured =>
        ValidateStaticDependencies(false) &&
        ValidateRuntimeDependencies(false);

    public bool Construct(NetworkManager manager)
    {
        networkManager = manager;

        if (!ValidateRuntimeDependencies())
        {
            initialized = false;
            DisableUntilConfigured();
            return false;
        }

        if (!enabled)
        {
            enabled = true;
        }

        Initialize();
        return initialized;
    }

    public void Initialize()
    {
        if (initialized)
        {
            return;
        }

        if (!ValidateRuntimeDependencies())
        {
            DisableUntilConfigured();
            return;
        }

        initialized = true;

        SceneManager.sceneUnloaded -= HandleSceneUnloaded;
        SceneManager.sceneUnloaded += HandleSceneUnloaded;

        SubscribeToNetworkCallbacks();

        if (clearOnInitialize)
        {
            ClearIfAllowed("initialize");
        }
    }

    public bool ValidateStaticDependencies()
    {
        return ValidateStaticDependencies(true);
    }

    public bool ValidateRuntimeDependencies()
    {
        return ValidateRuntimeDependencies(true);
    }

    private void Awake()
    {
        if (networkManager != null)
        {
            Initialize();
        }
    }

    private void Start()
    {
        if (initialized)
        {
            return;
        }

        if (networkManager != null)
        {
            Initialize();
            return;
        }

        ValidateRuntimeDependencies();
        DisableUntilConfigured();
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
        if (clearOnDestroy)
        {
            Clear("destroy");
        }

        SceneManager.sceneUnloaded -= HandleSceneUnloaded;
        UnsubscribeFromNetworkCallbacks();

        initialized = false;
    }

    public bool TryRaiseNoiseServer(
        Vector3 position,
        float radius,
        float loudness = 1f,
        EnemyTarget sourceTarget = null,
        Object sourceObject = null
    )
    {
        EnemyNoiseEvent noiseEvent = new EnemyNoiseEvent(
            position,
            radius,
            loudness,
            Time.time,
            sourceTarget,
            sourceObject
        );

        return TryRegisterNoiseServer(noiseEvent);
    }

    public bool TryRegisterNoiseServer(EnemyNoiseEvent noiseEvent)
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
        EnemyConfig config,
        out EnemyPerceptionStimulus stimulus
    )
    {
        stimulus = EnemyPerceptionStimulus.None;

        if (!CanUseServerWorld() || config == null || !config.hearingEnabled)
        {
            return false;
        }

        float now = Time.time;
        float bestScore = 0f;
        EnemyNoiseEvent bestNoise = default;
        bool hasBestNoise = false;

        for (int i = noises.Count - 1; i >= 0; i--)
        {
            EnemyNoiseEvent noise = noises[i];

            if (now - noise.CreatedAtTime > config.hearingMemoryDuration)
            {
                noises.RemoveAt(i);
                continue;
            }

            if (!noise.IsValid || noise.Loudness < config.minimumNoiseLoudness)
            {
                continue;
            }

            float distance = Vector3.Distance(listenerPosition, noise.Position);
            float effectiveRadius = Mathf.Min(config.hearingRadius, noise.Radius);

            if (distance > effectiveRadius)
            {
                continue;
            }

            float normalizedDistance = distance / Mathf.Max(0.001f, effectiveRadius);
            float score = noise.Loudness * (1f - normalizedDistance);

            if (score <= bestScore)
            {
                continue;
            }

            bestScore = score;
            bestNoise = noise;
            hasBestNoise = true;
        }

        if (!hasBestNoise)
        {
            return false;
        }

        stimulus = EnemyPerceptionStimulus.ForSuspiciousPosition(
            bestNoise.Position,
            bestScore,
            EnemyPerceptionSource.Hearing
        );

        return true;
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
            Debug.Log(
                $"{nameof(EnemyNoiseWorldService)} cleared noise events on {reason}.",
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

    private bool ValidateStaticDependencies(bool logErrors)
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
            nameof(EnemyNoiseWorldService),
            builder,
            ref invalidStaticConfigurationLogged,
            logErrors,
            "Enemy noise world service is disabled until configured."
        );
    }

    private bool ValidateRuntimeDependencies(bool logErrors)
    {
        return ValidateStaticDependencies(logErrors);
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
        ValidateStaticDependencies();
    }
#endif
}