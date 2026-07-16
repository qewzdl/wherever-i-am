using UnityEngine;

public sealed class PauseSceneFeature : SceneRuntimeFeature
{
    [SerializeField] private GamePauseService pauseService;
    [SerializeField] private PauseMenuUI pauseMenu;
    [SerializeField] private PauseServiceConsumer[] pauseConsumers;

    protected override bool ValidateFeature(SceneFeatureContext context)
    {
        bool valid = true;
        valid &= RequireReference(pauseService, nameof(pauseService));
        valid &= RequireReference(pauseMenu, nameof(pauseMenu));
        valid &= RequireService<IGameStateService>(context, out _);
        valid &= RequireService<INetworkSessionService>(context, out _);
        valid &= RequireService<IPlayerScopeRegistry>(context, out _);

        if (pauseConsumers != null)
        {
            for (int i = 0; i < pauseConsumers.Length; i++)
                valid &= RequireReference(pauseConsumers[i], $"{nameof(pauseConsumers)}[{i}]");
        }

        return valid;
    }

    protected override bool InstallFeature(SceneFeatureContext context)
    {
        IGameStateService stateService = context.Services.Resolve<IGameStateService>();
        INetworkSessionService sessionService = context.Services.Resolve<INetworkSessionService>();
        IPlayerScopeRegistry playerScopes = context.Services.Resolve<IPlayerScopeRegistry>();

        pauseService.Construct(stateService);
        context.Register<IPauseService>(pauseService);

        IPauseService pauseServiceContract = context.Services.Resolve<IPauseService>();
        pauseMenu.Construct(pauseServiceContract, sessionService, playerScopes);

        return InstallPauseConsumers(pauseServiceContract);
    }

    protected override void UninstallFeature(SceneFeatureContext context)
    {
        if (pauseConsumers != null)
        {
            for (int i = pauseConsumers.Length - 1; i >= 0; i--)
            {
                PauseServiceConsumer consumer = pauseConsumers[i];

                if (consumer != null)
                    RunCleanup(() => consumer.BindPauseService(null), consumer);
            }
        }

        RunCleanup(() => pauseMenu?.Dispose(), pauseMenu);
        RunCleanup(() => pauseService?.Dispose(), pauseService);
    }

    private bool InstallPauseConsumers(IPauseService pauseServiceContract)
    {
        if (pauseConsumers == null)
            return true;

        for (int i = 0; i < pauseConsumers.Length; i++)
        {
            PauseServiceConsumer consumer = pauseConsumers[i];

            if (consumer == null)
                continue;

            consumer.BindPauseService(pauseServiceContract);
        }

        return true;
    }
}
