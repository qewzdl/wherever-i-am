using UnityEngine;

public sealed class MainMenuSceneFeature : SceneRuntimeFeature
{
    [SerializeField] private MainMenuUI mainMenu;

    protected override bool InstallFeature(ProjectContext context)
    {
        INetworkSessionService sessionService = context.SessionService;
        IUiErrorService errorService = context.UiErrors;

        bool valid = true;
        valid &= RequireReference(mainMenu, nameof(mainMenu));
        valid &= RequireService(sessionService, nameof(ProjectContext.SessionService));
        valid &= RequireService(errorService, nameof(ProjectContext.UiErrors));

        if (!valid)
            return false;

        mainMenu.Construct(sessionService, errorService);
        return true;
    }
}