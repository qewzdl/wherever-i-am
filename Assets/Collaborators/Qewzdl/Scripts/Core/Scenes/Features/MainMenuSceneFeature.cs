using UnityEngine;

public sealed class MainMenuSceneFeature : SceneRuntimeFeature
{
    [SerializeField] private MainMenuUI mainMenu;

    protected override bool ValidateFeature(SceneFeatureContext context)
    {
        bool valid = true;
        valid &= RequireReference(mainMenu, nameof(mainMenu));
        valid &= RequireService<INetworkSessionService>(context, out _);
        valid &= RequireService<IUiErrorService>(context, out _);

        return valid;
    }

    protected override bool InstallFeature(SceneFeatureContext context)
    {
        INetworkSessionService sessionService = context.Services.Resolve<INetworkSessionService>();
        IUiErrorService errorService = context.Services.Resolve<IUiErrorService>();
        mainMenu.Construct(sessionService, errorService);
        return true;
    }

    protected override void UninstallFeature(SceneFeatureContext context)
    {
        mainMenu?.Dispose();
    }
}
