using System.Threading.Tasks;
using UnityEngine;

[DisallowMultipleComponent]
public sealed class NetworkSessionOrchestrator : MonoBehaviour, INetworkSessionService
{
    public static NetworkSessionOrchestrator Instance { get; private set; }

    [Header("References")]
    [SerializeField] private NetworkSessionFlowService sessionFlowService;

    public IServiceResolver SessionServices => sessionFlowService != null
        ? sessionFlowService.SessionServices
        : null;

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }

        Instance = this;

        HasRequiredReferences();
    }

    private void OnDestroy()
    {
        if (Instance == this)
            Instance = null;
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

    public void ShutdownToMainMenu()
    {
        if (!HasRequiredReferences())
            return;

        sessionFlowService.ShutdownToMainMenu();
    }

    public Task ShutdownToMainMenuAsync()
    {
        if (!HasRequiredReferences())
            return Task.CompletedTask;

        return sessionFlowService.ShutdownToMainMenuAsync();
    }

    internal bool ConfigureSessionScopeController(
        ServiceScope globalScope,
        IGameMapSessionService gameMapService,
        IGameplayNoiseService gameplayNoiseService)
    {
        if (!HasRequiredReferences())
            return false;

        return sessionFlowService.ConfigureSessionScopeController(
            globalScope,
            gameMapService,
            gameplayNoiseService);
    }

    internal void DisposeSessionScopeController()
    {
        sessionFlowService?.DisposeSessionScopeController();
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
