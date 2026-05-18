using UnityEngine;

public sealed class LobbySceneFeature : SceneRuntimeFeature
{
    [SerializeField] private LobbyState lobbyState;
    [SerializeField] private LobbyController lobbyController;
    [SerializeField] private NetworkLobbyService lobbyService;
    [SerializeField] private LobbyUI lobbyUi;

    public override void Install(ProjectContext context)
    {
        if (context == null)
            return;

        if (lobbyController != null)
            lobbyController.Construct(context.SessionService);

        if (lobbyService != null)
            lobbyService.Construct(lobbyState, lobbyController, context.SessionService);

        if (lobbyUi != null && lobbyService != null)
            lobbyUi.Construct(lobbyService, lobbyService);
    }
}
