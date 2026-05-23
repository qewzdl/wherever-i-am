using UnityEngine;

public sealed class LobbySceneFeature : SceneRuntimeFeature
{
    [SerializeField] private LobbyState lobbyState;
    [SerializeField] private LobbyController lobbyController;
    [SerializeField] private NetworkLobbyService lobbyService;
    [SerializeField] private LobbyUI lobbyUi;
    [SerializeField] private LobbyUICommandPresenter lobbyCommandPresenter;

    protected override bool InstallFeature(ProjectContext context)
    {
        INetworkSessionService sessionService = context.SessionService;

        bool valid = true;
        valid &= RequireReference(lobbyState, nameof(lobbyState));
        valid &= RequireReference(lobbyController, nameof(lobbyController));
        valid &= RequireReference(lobbyService, nameof(lobbyService));
        valid &= RequireReference(lobbyUi, nameof(lobbyUi));
        valid &= RequireReference(lobbyCommandPresenter, nameof(lobbyCommandPresenter));
        valid &= RequireService(sessionService, nameof(ProjectContext.SessionService));

        if (!valid)
            return false;

        lobbyController.Construct(sessionService);
        lobbyService.Construct(lobbyState, lobbyController, sessionService);
        lobbyUi.Construct(lobbyService);
        lobbyCommandPresenter.Construct(lobbyUi, lobbyService, lobbyService);

        return true;
    }
}
