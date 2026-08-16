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
        valid &= RequireService<INetworkSessionAdmissionService>(context, out _);

        if (lobbyController != null)
            valid &= lobbyController.ValidateConfiguration();

        return valid;
    }

    protected override bool InstallFeature(SceneFeatureContext context)
    {
        INetworkSessionService sessionService = context.Services.Resolve<INetworkSessionService>();
        INetworkSessionAdmissionService admissionService =
            context.Services.Resolve<INetworkSessionAdmissionService>();

        if (!lobbyController.Construct(sessionService, admissionService))
            return false;

        lobbyService.Construct(lobbyState, lobbyController, sessionService);
        context.Register<ILobbyReadService>(lobbyService);
        context.Register<ILobbyCommandService>(lobbyService);

        ILobbyReadService readService = context.Services.Resolve<ILobbyReadService>();
        ILobbyCommandService commandService = context.Services.Resolve<ILobbyCommandService>();
        lobbyUi.Construct(readService);
        lobbyCommandPresenter.Construct(lobbyUi, readService, commandService);

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
