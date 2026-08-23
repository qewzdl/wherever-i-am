using UnityEngine;

public sealed class LobbySceneFeature : SceneRuntimeFeature
{
    [SerializeField] private LobbyState lobbyState;
    [SerializeField] private LobbyController lobbyController;
    [SerializeField] private NetworkLobbyService lobbyService;
    [SerializeField] private LobbyUI lobbyUi;
    [SerializeField] private LobbyUICommandPresenter lobbyCommandPresenter;

    // Optional on purpose. The stage is the room behind the column; a lobby
    // with nobody standing in it still takes players, still starts matches and
    // still says so, and failing to install the scene over a missing set of
    // capsules would be the tail wagging the dog.
    [SerializeField] private LobbyStage lobbyStage;

    protected override bool ValidateFeature(SceneFeatureContext context)
    {
        bool valid = true;
        valid &= RequireReference(lobbyState, nameof(lobbyState));
        valid &= RequireReference(lobbyController, nameof(lobbyController));
        valid &= RequireReference(lobbyService, nameof(lobbyService));
        valid &= RequireReference(lobbyUi, nameof(lobbyUi));
        valid &= RequireReference(lobbyCommandPresenter, nameof(lobbyCommandPresenter));
        valid &= RequireService<INetworkSessionService>(context, out _);
        valid &= RequireService<INetworkSessionReadService>(context, out _);
        valid &= RequireService<INetworkSessionAdmissionService>(context, out _);

        if (lobbyController != null)
            valid &= lobbyController.ValidateConfiguration();

        return valid;
    }

    protected override bool InstallFeature(SceneFeatureContext context)
    {
        INetworkSessionService sessionService = context.Services.Resolve<INetworkSessionService>();
        INetworkSessionReadService sessionReadService =
            context.Services.Resolve<INetworkSessionReadService>();
        INetworkSessionAdmissionService admissionService =
            context.Services.Resolve<INetworkSessionAdmissionService>();

        if (!lobbyController.Construct(sessionService, admissionService))
            return false;

        lobbyService.Construct(lobbyState, lobbyController, sessionService);
        context.Register<ILobbyReadService>(lobbyService);
        context.Register<ILobbyCommandService>(lobbyService);

        ILobbyReadService readService = context.Services.Resolve<ILobbyReadService>();
        ILobbyCommandService commandService = context.Services.Resolve<ILobbyCommandService>();
        lobbyUi.Construct(readService, sessionReadService);
        lobbyCommandPresenter.Construct(lobbyUi, readService, commandService);

        if (lobbyStage != null)
            lobbyStage.Construct(readService);

        return true;
    }

    protected override void UninstallFeature(SceneFeatureContext context)
    {
        RunCleanup(() => lobbyStage?.Dispose(), lobbyStage);
        RunCleanup(() => lobbyCommandPresenter?.Dispose(), lobbyCommandPresenter);
        RunCleanup(() => lobbyUi?.Dispose(), lobbyUi);
        RunCleanup(() => lobbyService?.Dispose(), lobbyService);
        RunCleanup(() => lobbyController?.Dispose(), lobbyController);
    }
}
