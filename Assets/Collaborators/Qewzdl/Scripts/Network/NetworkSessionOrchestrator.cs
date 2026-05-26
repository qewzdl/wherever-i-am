using System.Threading.Tasks;
using UnityEngine;

[DisallowMultipleComponent]
public sealed class NetworkSessionOrchestrator : MonoBehaviour, INetworkSessionService
{
    public static NetworkSessionOrchestrator Instance { get; private set; }

    [Header("References")]
    [SerializeField] private NetworkSessionFlowService sessionFlowService;

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

    public void StartGame()
    {
        if (!HasRequiredReferences())
            return;

        sessionFlowService.StartGame();
    }

    public void ShutdownToMainMenu()
    {
        if (!HasRequiredReferences())
            return;

        sessionFlowService.ShutdownToMainMenu();
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
