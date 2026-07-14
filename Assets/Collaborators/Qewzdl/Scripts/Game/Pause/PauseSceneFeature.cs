using UnityEngine;

public sealed class PauseSceneFeature : SceneRuntimeFeature
{
    [SerializeField] private GamePauseService pauseService;
    [SerializeField] private PauseMenuUI pauseMenu;
    [SerializeField] private PauseServiceConsumer[] pauseConsumers;

    protected override bool ValidateFeature(ProjectContext context)
    {
        GameStateMachine stateMachine = context.StateMachine;
        INetworkSessionService sessionService = context.SessionService;

        bool valid = true;
        valid &= RequireReference(pauseService, nameof(pauseService));
        valid &= RequireReference(pauseMenu, nameof(pauseMenu));
        valid &= RequireReference(stateMachine, nameof(ProjectContext.StateMachine));
        valid &= RequireService(sessionService, nameof(ProjectContext.SessionService));

        if (pauseConsumers != null)
        {
            for (int i = 0; i < pauseConsumers.Length; i++)
                valid &= RequireReference(pauseConsumers[i], $"{nameof(pauseConsumers)}[{i}]");
        }

        return valid;
    }

    protected override bool InstallFeature(ProjectContext context)
    {
        GameStateMachine stateMachine = context.StateMachine;
        INetworkSessionService sessionService = context.SessionService;

        pauseService.Construct(stateMachine);
        pauseMenu.Construct(pauseService, sessionService);

        return InstallPauseConsumers();
    }

    protected override void UninstallFeature()
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

    private bool InstallPauseConsumers()
    {
        if (pauseConsumers == null)
            return true;

        for (int i = 0; i < pauseConsumers.Length; i++)
        {
            PauseServiceConsumer consumer = pauseConsumers[i];

            if (consumer == null)
                continue;

            consumer.BindPauseService(pauseService);
        }

        return true;
    }
}
