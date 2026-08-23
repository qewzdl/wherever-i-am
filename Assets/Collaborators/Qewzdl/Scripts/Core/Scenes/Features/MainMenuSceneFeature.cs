using UnityEngine;

public sealed class MainMenuSceneFeature : SceneRuntimeFeature
{
    [SerializeField] private MainMenuDocument mainMenu;

    protected override bool ValidateFeature(SceneFeatureContext context)
    {
        bool valid = true;
        valid &= RequireReference(mainMenu, nameof(mainMenu));
        valid &= RequireService<INetworkSessionService>(context, out _);
        valid &= RequireService<INetworkSessionReadService>(context, out _);
        valid &= RequireService<IUiErrorService>(context, out _);
        valid &= RequireService<ISettingsScreen>(context, out _);

        return valid;
    }

    protected override bool InstallFeature(SceneFeatureContext context)
    {
        INetworkSessionService sessionService = context.Services.Resolve<INetworkSessionService>();
        INetworkSessionReadService sessionReadService =
            context.Services.Resolve<INetworkSessionReadService>();
        IUiErrorService errorService = context.Services.Resolve<IUiErrorService>();
        ISettingsScreen settingsScreen = context.Services.Resolve<ISettingsScreen>();
        mainMenu.Construct(
            sessionService,
            errorService,
            settingsScreen,
            sessionReadService);
        return true;
    }

    protected override void UninstallFeature(SceneFeatureContext context)
    {
        mainMenu?.Dispose();
    }
}
