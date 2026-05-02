using UnityEngine;

public class LobbyCompositionRoot : MonoBehaviour
{
    [Header("Session")]
    [SerializeField] private NetworkSessionOrchestrator sessionService;

    [Header("Lobby")]
    [SerializeField] private LobbyState lobbyState;
    [SerializeField] private LobbyController lobbyController;
    [SerializeField] private NetworkLobbyService lobbyService;

    [Header("UI")]
    [SerializeField] private LobbyUI lobbyUI;

    private void Awake()
    {
        ResolveReferences();
        Compose();
    }

    private void ResolveReferences()
    {
        if (sessionService == null)
            sessionService = FindFirstObjectByType<NetworkSessionOrchestrator>();

        if (lobbyState == null)
            lobbyState = FindFirstObjectByType<LobbyState>();

        if (lobbyController == null)
            lobbyController = FindFirstObjectByType<LobbyController>();

        if (lobbyService == null)
            lobbyService = FindFirstObjectByType<NetworkLobbyService>();

        if (lobbyUI == null)
            lobbyUI = FindFirstObjectByType<LobbyUI>();
    }

    private void Compose()
    {
        if (sessionService == null)
        {
            Debug.LogError("NetworkSessionOrchestrator was not found.");
            return;
        }

        if (lobbyState == null)
        {
            Debug.LogError("LobbyState was not found.");
            return;
        }

        if (lobbyController == null)
        {
            Debug.LogError("LobbyController was not found.");
            return;
        }

        if (lobbyService == null)
        {
            Debug.LogError("NetworkLobbyService was not found.");
            return;
        }

        if (lobbyUI == null)
        {
            Debug.LogError("LobbyUI was not found.");
            return;
        }

        lobbyController.Construct(sessionService);
        lobbyService.Construct(lobbyState, lobbyController, sessionService);
        lobbyUI.Construct(lobbyService, lobbyService);
    }
}