using System;
using System.Threading.Tasks;
using UnityEngine;

[DisallowMultipleComponent]
public sealed class NetworkSessionOrchestrator : MonoBehaviour, INetworkSessionService
{
    [Header("References")]
    [SerializeField] private NetworkSessionFlowService sessionFlowService;

    internal IServiceResolver SessionServices => sessionFlowService != null
        ? sessionFlowService.SessionServices
        : null;
    internal INetworkSessionReadService SessionState => sessionFlowService != null
        ? sessionFlowService.SessionState
        : null;
    internal bool RequiresCoordinatedShutdown =>
        sessionFlowService != null && sessionFlowService.RequiresCoordinatedShutdown;

    private void Awake()
    {
        HasRequiredReferences();
    }

    public Task HostLanAsync()
    {
        if (!HasRequiredReferences())
            return Task.CompletedTask;

        return sessionFlowService.HostLanAsync();
    }

    public Task JoinLanAsync(string ip)
    {
        if (!HasRequiredReferences())
            return Task.CompletedTask;

        return sessionFlowService.JoinLanAsync(ip);
    }

    public void StartGame(int mapId)
    {
        if (!HasRequiredReferences())
            return;

        sessionFlowService.StartGame(mapId);
    }

    public void StartGame(int mapId, int difficultyId)
    {
        if (!HasRequiredReferences())
            return;

        sessionFlowService.StartGame(mapId, difficultyId);
    }

    public void ReturnToLobby()
    {
        if (!HasRequiredReferences())
            return;

        sessionFlowService.ReturnToLobby();
    }

    public void ShutdownToMainMenu()
    {
        if (!HasRequiredReferences())
            return;

        sessionFlowService.ShutdownToMainMenu();
    }

    public Task<NetworkShutdownResult> ShutdownToMainMenuAsync()
    {
        if (!HasRequiredReferences())
        {
            return Task.FromResult(NetworkShutdownResult.Failure(
                false,
                false,
                false,
                "Network session orchestrator is not configured."));
        }

        return sessionFlowService.ShutdownToMainMenuAsync();
    }

    internal Task<NetworkShutdownResult> ShutdownAfterFailureAsync(ConnectionResult failure)
    {
        if (failure == null)
            throw new ArgumentNullException(nameof(failure));

        if (!HasRequiredReferences())
        {
            return Task.FromResult(NetworkShutdownResult.Failure(
                false,
                false,
                false,
                "Network session orchestrator is not configured."));
        }

        return sessionFlowService.ShutdownAfterFailureAsync(failure);
    }

    internal Task ReportSessionReadinessFailureAsync(
        string source,
        string details)
    {
        if (!HasRequiredReferences())
            return Task.CompletedTask;

        return sessionFlowService.ReportSessionReadinessFailureAsync(
            source,
            details);
    }

    internal bool TryBeginClientSceneReadiness(
        ProjectSceneKind sceneKind,
        out string error)
    {
        if (sessionFlowService != null)
        {
            return sessionFlowService.TryBeginClientSceneReadiness(
                sceneKind,
                out error);
        }

        error = $"{nameof(NetworkSessionFlowService)} is not configured.";
        return false;
    }

    internal bool TryCommitClientSceneReadiness(
        ProjectSceneKind sceneKind,
        out string error)
    {
        if (sessionFlowService != null)
        {
            return sessionFlowService.TryCommitClientSceneReadiness(
                sceneKind,
                out error);
        }

        error = $"{nameof(NetworkSessionFlowService)} is not configured.";
        return false;
    }

    internal bool ConfigureSessionScopeController(
        ServiceScope globalScope,
        IGameMapSessionService gameMapService,
        IGameplayNoiseService gameplayNoiseService,
        SceneRuntimeScopeRegistry sceneScopes)
    {
        if (!HasRequiredReferences())
            return false;

        return sessionFlowService.ConfigureSessionScopeController(
            globalScope,
            gameMapService,
            gameplayNoiseService,
            sceneScopes);
    }

    internal bool TryGetSessionServiceScope(out ServiceScope scope)
    {
        scope = null;
        return sessionFlowService != null &&
               sessionFlowService.TryGetSessionServiceScope(out scope);
    }

    internal bool TryGetSessionServiceRegistry(out ISessionServiceRegistry registry)
    {
        registry = null;
        return sessionFlowService != null &&
               sessionFlowService.TryGetSessionServiceRegistry(out registry);
    }

    internal bool TryRegisterSessionServices(
        Action<IServiceRegistrar> registerServices,
        out SessionServiceRegistration registrations,
        out Exception failure)
    {
        registrations = null;

        if (sessionFlowService != null)
        {
            return sessionFlowService.TryRegisterSessionServices(
                registerServices,
                out registrations,
                out failure);
        }

        failure = new InvalidOperationException(
            $"{nameof(NetworkSessionFlowService)} is not configured.");

        return false;
    }

    internal bool TryOpenPlayerScope(
        ulong networkObjectId,
        ulong ownerClientId,
        bool isLocalPlayer,
        Action<IServiceRegistrar> registerReplicatedServices,
        Action<IServiceRegistrar> registerLocalServices,
        out PlayerScopeRegistration registration,
        out Exception failure)
    {
        registration = null;

        if (sessionFlowService != null)
        {
            return sessionFlowService.TryOpenPlayerScope(
                networkObjectId,
                ownerClientId,
                isLocalPlayer,
                registerReplicatedServices,
                registerLocalServices,
                out registration,
                out failure);
        }

        failure = new InvalidOperationException(
            $"{nameof(NetworkSessionFlowService)} is not configured.");

        return false;
    }

    internal void DisposeSessionScopeController()
    {
        sessionFlowService?.DisposeSessionScopeController();
    }

    internal void ForceAbortForApplicationQuit()
    {
        sessionFlowService?.ForceAbortForApplicationQuit();
    }

    private bool HasRequiredReferences()
    {
        ResolveReferences();

        if (sessionFlowService != null)
            return true;

        Debug.LogError($"{nameof(NetworkSessionOrchestrator)} is missing {nameof(NetworkSessionFlowService)} reference.", this);
        return false;
    }

    private void ResolveReferences()
    {
        if (sessionFlowService == null)
            sessionFlowService = GetComponent<NetworkSessionFlowService>();
    }

#if UNITY_EDITOR
    private void OnValidate()
    {
        ResolveReferences();
    }
#endif
}
