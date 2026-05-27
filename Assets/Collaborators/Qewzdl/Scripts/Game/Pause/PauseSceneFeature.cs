using UnityEngine;

public sealed class PauseSceneFeature : SceneRuntimeFeature
{
    [SerializeField] private GamePauseService pauseService;
    [SerializeField] private PauseMenuUI pauseMenu;
    [SerializeField] private PauseServiceConsumer[] pauseConsumers;

    protected override bool InstallFeature(ProjectContext context)
    {
        GameStateMachine stateMachine = context.StateMachine;
        INetworkSessionService sessionService = context.SessionService;

        bool valid = true;
        valid &= RequireReference(pauseService, nameof(pauseService));
        valid &= RequireReference(pauseMenu, nameof(pauseMenu));
        valid &= RequireReference(stateMachine, nameof(ProjectContext.StateMachine));
        valid &= RequireService(sessionService, nameof(ProjectContext.SessionService));

        if (!valid)
            return false;

        pauseService.Construct(stateMachine);
        pauseMenu.Construct(pauseService, sessionService);

        return InstallPauseConsumers();
    }

    private bool InstallPauseConsumers()
    {
        if (pauseConsumers == null)
            return true;

        bool valid = true;

        for (int i = 0; i < pauseConsumers.Length; i++)
        {
            PauseServiceConsumer consumer = pauseConsumers[i];

            if (consumer == null)
            {
                LogMissingReference($"{nameof(pauseConsumers)}[{i}]");
                valid = false;
                continue;
            }

            consumer.BindPauseService(pauseService);
        }

        return valid;
    }
}