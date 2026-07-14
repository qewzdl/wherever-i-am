using UnityEngine;

public sealed class MainMenuSceneFeature : SceneRuntimeFeature
{
    [SerializeField] private MainMenuUI mainMenu;

    protected override bool ValidateFeature(ProjectContext context)
    {
        INetworkSessionService sessionService = context.SessionService;
        IUiErrorService errorService = context.UiErrors;

        bool valid = true;
        valid &= RequireReference(mainMenu, nameof(mainMenu));
        valid &= RequireService(sessionService, nameof(ProjectContext.SessionService));
        valid &= RequireService(errorService, nameof(ProjectContext.UiErrors));

        return valid;
    }

    protected override bool InstallFeature(ProjectContext context)
    {
        mainMenu.Construct(context.SessionService, context.UiErrors);
        return true;
    }

    protected override void UninstallFeature()
    {
        mainMenu?.Dispose();
    }
}
