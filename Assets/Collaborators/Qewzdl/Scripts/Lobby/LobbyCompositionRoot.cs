using UnityEngine;

public class LobbyCompositionRoot : MonoBehaviour
{
    [Header("Services")]
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
        if (lobbyService == null)
            lobbyService = FindFirstObjectByType<NetworkLobbyService>();

        if (lobbyUI == null)
            lobbyUI = FindFirstObjectByType<LobbyUI>();
    }

    private void Compose()
    {
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

        lobbyUI.Construct(lobbyService, lobbyService);
    }
}