using System;
using Unity.Netcode;
using UnityEngine;

public class NetworkLobbyService : MonoBehaviour, ILobbyReadService, ILobbyCommandService
{
    [SerializeField] private LobbyState lobbyState;
    [SerializeField] private LobbyController lobbyController;

    private INetworkSessionService sessionService;
    private bool isSubscribedToLobbyState;

    public event Action LobbyChanged;
    public event Action PlayersChanged;
    public event Action SettingsChanged;
    public event Action OwnerChanged;
    public event Action StartAvailabilityChanged;
    public event Action PhaseChanged;

    public int PlayerCount => lobbyState != null && lobbyState.Players != null
        ? lobbyState.Players.Count
        : 0;

    public bool IsLocalPlayerRoomOwner
    {
        get
        {
            if (NetworkManager.Singleton == null || lobbyState == null)
                return false;

            return lobbyState.RoomOwnerClientId.Value == NetworkManager.Singleton.LocalClientId;
        }
    }

    public bool CanStartGame => lobbyState != null && lobbyState.CanStartGame.Value;

    public LobbyPhase Phase => lobbyState != null
        ? lobbyState.Phase.Value
        : LobbyPhase.Closed;

    public LobbySettingsData Settings => lobbyState != null
        ? lobbyState.Settings.Value
        : LobbySettingsData.CreateDefault();

    private void Awake()
    {
        ResolveReferences();
    }

    private void OnEnable()
    {
        ResolveReferences();
        SubscribeToLobbyState();
    }

    private void OnDisable()
    {
        UnsubscribeFromLobbyState();
    }

    public void Construct(
        LobbyState lobbyState,
        LobbyController lobbyController,
        INetworkSessionService sessionService)
    {
        UnsubscribeFromLobbyState();

        this.lobbyState = lobbyState;
        this.lobbyController = lobbyController;
        this.sessionService = sessionService;

        if (isActiveAndEnabled)
            SubscribeToLobbyState();

        RaiseLobbyChanged();
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

    public bool TryGetLocalPlayer(out LobbyPlayerData player)
    {
        player = default;

        if (NetworkManager.Singleton == null || lobbyState == null || lobbyState.Players == null)
            return false;

        ulong localClientId = NetworkManager.Singleton.LocalClientId;

        if (!lobbyState.TryGetPlayerIndex(localClientId, out int index))
            return false;

        player = lobbyState.Players[index];
        return true;
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

    public void SetGameMode(int gameModeId)
    {
        if (lobbyController == null)
        {
            Debug.LogError("LobbyController is missing.");
            return;
        }

        lobbyController.RequestSetGameModeRpc(gameModeId);
    }

    public void SetMap(int mapId)
    {
        if (lobbyController == null)
        {
            Debug.LogError("LobbyController is missing.");
            return;
        }

        lobbyController.RequestSetMapRpc(mapId);
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
        if (sessionService == null)
        {
            Debug.LogError("Network session service is missing.");
            return;
        }

        sessionService.ShutdownToMainMenu();
    }

    private void ResolveReferences()
    {
        if (lobbyState == null)
            lobbyState = GetComponent<LobbyState>();

        if (lobbyController == null)
            lobbyController = GetComponent<LobbyController>();
    }

    private void SubscribeToLobbyState()
    {
        if (isSubscribedToLobbyState || lobbyState == null)
            return;

        lobbyState.LobbyChanged += HandleLobbyChanged;
        lobbyState.PlayersChanged += HandlePlayersChanged;
        lobbyState.SettingsChanged += HandleSettingsChanged;
        lobbyState.OwnerChanged += HandleOwnerChanged;
        lobbyState.StartAvailabilityChanged += HandleStartAvailabilityChanged;
        lobbyState.PhaseChanged += HandlePhaseChanged;

        isSubscribedToLobbyState = true;
    }

    private void UnsubscribeFromLobbyState()
    {
        if (!isSubscribedToLobbyState || lobbyState == null)
            return;

        lobbyState.LobbyChanged -= HandleLobbyChanged;
        lobbyState.PlayersChanged -= HandlePlayersChanged;
        lobbyState.SettingsChanged -= HandleSettingsChanged;
        lobbyState.OwnerChanged -= HandleOwnerChanged;
        lobbyState.StartAvailabilityChanged -= HandleStartAvailabilityChanged;
        lobbyState.PhaseChanged -= HandlePhaseChanged;

        isSubscribedToLobbyState = false;
    }

    private void HandleLobbyChanged()
    {
        RaiseLobbyChanged();
    }

    private void HandlePlayersChanged()
    {
        PlayersChanged?.Invoke();
    }

    private void HandleSettingsChanged()
    {
        SettingsChanged?.Invoke();
    }

    private void HandleOwnerChanged()
    {
        OwnerChanged?.Invoke();
    }

    private void HandleStartAvailabilityChanged()
    {
        StartAvailabilityChanged?.Invoke();
    }

    private void HandlePhaseChanged()
    {
        PhaseChanged?.Invoke();
    }

    private void RaiseLobbyChanged()
    {
        LobbyChanged?.Invoke();
    }
}