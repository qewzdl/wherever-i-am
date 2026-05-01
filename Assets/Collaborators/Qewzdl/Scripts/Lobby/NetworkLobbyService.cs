using System;
using Unity.Netcode;
using UnityEngine;

public class NetworkLobbyService : MonoBehaviour, ILobbyService
{
    [SerializeField] private LobbyState lobbyState;
    [SerializeField] private LobbyController lobbyController;

    public event Action LobbyChanged;

    public int PlayerCount => lobbyState != null && lobbyState.Players != null
        ? lobbyState.Players.Count
        : 0;

    public bool IsHost => NetworkManager.Singleton != null && NetworkManager.Singleton.IsHost;

    public bool CanStartGame => lobbyController != null && lobbyController.CanStartGame();

    private void Awake()
    {
        ResolveReferences();
    }

    private void OnEnable()
    {
        ResolveReferences();

        if (lobbyState != null)
            lobbyState.PlayersChanged += HandleLobbyChanged;
    }

    private void OnDisable()
    {
        if (lobbyState != null)
            lobbyState.PlayersChanged -= HandleLobbyChanged;
    }

    public LobbyPlayerData GetPlayer(int index)
    {
        if (lobbyState == null || lobbyState.Players == null)
        {
            Debug.LogError("LobbyState is missing.");
            return default;
        }

        if (index < 0 || index >= lobbyState.Players.Count)
        {
            Debug.LogError($"Lobby player index out of range: {index}");
            return default;
        }

        return lobbyState.Players[index];
    }

    public void SetReady(bool isReady)
    {
        if (lobbyController == null)
        {
            Debug.LogError("LobbyController is missing.");
            return;
        }

        lobbyController.RequestSetReadyRpc(isReady);
    }

    public void SetCharacter(int characterId)
    {
        if (lobbyController == null)
        {
            Debug.LogError("LobbyController is missing.");
            return;
        }

        lobbyController.RequestSetCharacterRpc(characterId);
    }

    public void StartGame()
    {
        if (lobbyController == null)
        {
            Debug.LogError("LobbyController is missing.");
            return;
        }

        lobbyController.RequestStartGameRpc();
    }

    public void LeaveLobby()
    {
        NetworkSessionOrchestrator.Instance.ShutdownToMainMenu();
    }

    private void ResolveReferences()
    {
        if (lobbyState == null)
            lobbyState = GetComponent<LobbyState>();

        if (lobbyController == null)
            lobbyController = GetComponent<LobbyController>();
    }

    private void HandleLobbyChanged()
    {
        LobbyChanged?.Invoke();
    }
}