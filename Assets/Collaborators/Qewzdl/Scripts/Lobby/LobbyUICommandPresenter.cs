using UnityEngine;

[DisallowMultipleComponent]
public sealed class LobbyUICommandPresenter : MonoBehaviour
{
    [Header("References")]
    [SerializeField] private LobbyUI lobbyUI;
    [SerializeField] private MonoBehaviour readServiceSource;
    [SerializeField] private MonoBehaviour commandServiceSource;

    private ILobbyReadService readService;
    private ILobbyCommandService commandService;
    private bool constructed;
    private bool isSubscribed;

    public void Construct(
        LobbyUI lobbyUI,
        ILobbyReadService readService,
        ILobbyCommandService commandService)
    {
        Unsubscribe();

        this.lobbyUI = lobbyUI;
        this.readService = readService;
        this.commandService = commandService;
        constructed = true;

        if (isActiveAndEnabled)
            Subscribe();
    }

    public void Dispose()
    {
        Unsubscribe();
        constructed = false;
        lobbyUI = null;
        readService = null;
        commandService = null;
    }

    private void OnEnable()
    {
        if (!constructed)
            return;

        ResolveSerializedSources();
        Subscribe();
    }

    private void OnDisable()
    {
        Unsubscribe();
    }

    private void ResolveSerializedSources()
    {
        if (readService == null)
            readService = readServiceSource as ILobbyReadService;

        if (commandService == null)
            commandService = commandServiceSource as ILobbyCommandService;
    }

    private void Subscribe()
    {
        if (!constructed || isSubscribed)
            return;

        if (!HasRequiredReferences())
            return;

        lobbyUI.ReadyClicked += HandleReadyClicked;
        lobbyUI.StartGameClicked += HandleStartGameClicked;
        lobbyUI.LeaveLobbyClicked += HandleLeaveLobbyClicked;
        lobbyUI.DifficultySelected += HandleDifficultySelected;
        lobbyUI.LobbyVisibilityToggleClicked += HandleLobbyVisibilityToggleClicked;
        lobbyUI.PlayerKickRequested += HandlePlayerKickRequested;

        isSubscribed = true;
    }

    private void Unsubscribe()
    {
        if (!isSubscribed || lobbyUI == null)
            return;

        lobbyUI.ReadyClicked -= HandleReadyClicked;
        lobbyUI.StartGameClicked -= HandleStartGameClicked;
        lobbyUI.LeaveLobbyClicked -= HandleLeaveLobbyClicked;
        lobbyUI.DifficultySelected -= HandleDifficultySelected;
        lobbyUI.LobbyVisibilityToggleClicked -= HandleLobbyVisibilityToggleClicked;
        lobbyUI.PlayerKickRequested -= HandlePlayerKickRequested;

        isSubscribed = false;
    }

    private void HandleReadyClicked()
    {
        if (!HasRequiredReferences())
            return;

        if (readService.Phase != LobbyPhase.Open)
            return;

        if (!readService.TryGetLocalPlayer(out LobbyPlayerData localPlayer))
        {
            Debug.LogWarning("Local lobby player was not found.", this);
            return;
        }

        commandService.SetReady(!localPlayer.IsReady);
    }

    private void HandleStartGameClicked()
    {
        if (!HasRequiredReferences())
            return;

        if (readService.Phase != LobbyPhase.Open)
            return;

        commandService.StartGame();
    }

    private void HandleDifficultySelected(int difficultyId)
    {
        if (!HasRequiredReferences())
            return;

        if (readService.Phase != LobbyPhase.Open)
            return;

        commandService.SetDifficulty(difficultyId);
    }

    private void HandleLobbyVisibilityToggleClicked()
    {
        if (!HasRequiredReferences())
            return;

        if (readService.Phase != LobbyPhase.Open)
            return;

        commandService.SetLobbyPublic(!readService.Settings.IsPublic);
    }

    private void HandlePlayerKickRequested(ulong clientId)
    {
        if (!HasRequiredReferences())
            return;

        if (readService.Phase != LobbyPhase.Open)
            return;

        commandService.KickPlayer(clientId);
    }

    private void HandleLeaveLobbyClicked()
    {
        if (!HasRequiredReferences())
            return;

        commandService.LeaveLobby();
    }

    private bool HasRequiredReferences()
    {
        bool valid = true;

        if (lobbyUI == null)
        {
            Debug.LogError($"{nameof(LobbyUICommandPresenter)} is missing {nameof(LobbyUI)} reference.", this);
            valid = false;
        }

        if (readService == null)
        {
            Debug.LogError($"{nameof(LobbyUICommandPresenter)} is missing {nameof(ILobbyReadService)} reference.", this);
            valid = false;
        }

        if (commandService == null)
        {
            Debug.LogError($"{nameof(LobbyUICommandPresenter)} is missing {nameof(ILobbyCommandService)} reference.", this);
            valid = false;
        }

        return valid;
    }
}
