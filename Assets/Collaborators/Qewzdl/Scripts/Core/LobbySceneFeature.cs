using UnityEngine;

public sealed class LobbySceneFeature : SceneRuntimeFeature
{
    [SerializeField] private LobbyState lobbyState;
    [SerializeField] private LobbyController lobbyController;
    [SerializeField] private NetworkLobbyService lobbyService;
    [SerializeField] private LobbyUI lobbyUi;
    [SerializeField] private LobbyUICommandPresenter lobbyCommandPresenter;

    protected override bool ValidateFeature(SceneFeatureContext context)
    {
        bool valid = true;
        valid &= RequireReference(lobbyState, nameof(lobbyState));
        valid &= RequireReference(lobbyController, nameof(lobbyController));
        valid &= RequireReference(lobbyService, nameof(lobbyService));
        valid &= RequireReference(lobbyUi, nameof(lobbyUi));
        valid &= RequireReference(lobbyCommandPresenter, nameof(lobbyCommandPresenter));
        valid &= RequireService<INetworkSessionService>(context, out _);

        if (lobbyController != null)
            valid &= lobbyController.ValidateConfiguration();

        return valid;
    }

    protected override bool InstallFeature(SceneFeatureContext context)
    {
        INetworkSessionService sessionService = context.Services.Resolve<INetworkSessionService>();

        if (!lobbyController.Construct(sessionService))
            return false;

        lobbyService.Construct(lobbyState, lobbyController, sessionService);
        lobbyUi.Construct(lobbyService);
        lobbyCommandPresenter.Construct(lobbyUi, lobbyService, lobbyService);

        return true;
    }

    protected override void UninstallFeature(SceneFeatureContext context)
    {
        RunCleanup(() => lobbyCommandPresenter?.Dispose(), lobbyCommandPresenter);
        RunCleanup(() => lobbyUi?.Dispose(), lobbyUi);
        RunCleanup(() => lobbyService?.Dispose(), lobbyService);
        RunCleanup(() => lobbyController?.Dispose(), lobbyController);
    }
}
