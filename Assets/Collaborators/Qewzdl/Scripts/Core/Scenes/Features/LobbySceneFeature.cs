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

    // Made here rather than placed in the scene: it says this lobby is open
    // for exactly as long as this lobby exists, and it has nothing to
    // configure. None at all for a singleplayer game.
    private LanLobbyBeacon beacon;

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
        valid &= RequireService<INetworkSessionInfo>(context, out _);
        valid &= RequireService<ISettingsScreen>(context, out _);
        valid &= RequireService<IGameMapSessionService>(context, out _);

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
        SessionMode mode = context.Services.Resolve<INetworkSessionInfo>().Mode;

        if (!lobbyController.Construct(
                sessionService,
                admissionService,
                context.Services.Resolve<IGameMapSessionService>(),
                mode))
        {
            return false;
        }

        lobbyService.Construct(lobbyState, lobbyController, sessionService);
        context.Register<ILobbyReadService>(lobbyService);
        context.Register<ILobbyCommandService>(lobbyService);

        ILobbyReadService readService = context.Services.Resolve<ILobbyReadService>();
        ILobbyCommandService commandService = context.Services.Resolve<ILobbyCommandService>();
        lobbyUi.Construct(
            readService,
            sessionReadService,
            context.Services.Resolve<ISettingsScreen>(),
            mode);
        lobbyCommandPresenter.Construct(lobbyUi, readService, commandService);

        if (lobbyStage != null)
            lobbyStage.Construct(readService);

        // A singleplayer game is nobody else's to find.
        beacon = mode == SessionMode.Multiplayer
            ? LanLobbyBeacon.Announce(readService)
            : null;

        return true;
    }

    protected override void UninstallFeature(SceneFeatureContext context)
    {
        RunCleanup(() => beacon?.Stop(), beacon);
        beacon = null;

        RunCleanup(() => lobbyStage?.Dispose(), lobbyStage);
        RunCleanup(() => lobbyCommandPresenter?.Dispose(), lobbyCommandPresenter);
        RunCleanup(() => lobbyUi?.Dispose(), lobbyUi);
        RunCleanup(() => lobbyService?.Dispose(), lobbyService);
        RunCleanup(() => lobbyController?.Dispose(), lobbyController);
    }
}
