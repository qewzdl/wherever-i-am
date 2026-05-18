using UnityEngine;

public sealed class GameSceneFeature : SceneRuntimeFeature
{
    [Header("Pause")]
    [SerializeField] private GamePauseService pauseService;
    [SerializeField] private PauseMenuUI pauseMenu;
    [SerializeField] private MonoBehaviour[] pauseConsumers;

    [Header("Enemy Hearing")]
    [SerializeField] private EnemyNoiseWorldService noiseWorldService;
    [SerializeField] private EnemyNoiseEmitter[] noiseEmitters;
    [SerializeField] private EnemyHearingSensor[] hearingSensors;

    public override void Install(ProjectContext context)
    {
        if (context == null)
            return;

        InstallPause(context);
        InstallEnemyHearing();
    }

    private void InstallPause(ProjectContext context)
    {
        if (pauseService == null)
            return;

        pauseService.Construct(context.StateMachine);

        if (pauseMenu != null)
            pauseMenu.Construct(pauseService, context.SessionService);

        if (pauseConsumers == null)
            return;

        for (int i = 0; i < pauseConsumers.Length; i++)
        {
            IPauseServiceConsumer consumer = RequireInterface<IPauseServiceConsumer>(
                pauseConsumers[i],
                this,
                nameof(pauseConsumers));

            consumer?.BindPauseService(pauseService);
        }
    }

    private void InstallEnemyHearing()
    {
        if (noiseWorldService == null)
            return;

        noiseWorldService.Initialize();

        if (noiseEmitters != null)
        {
            for (int i = 0; i < noiseEmitters.Length; i++)
                noiseEmitters[i]?.Construct(noiseWorldService);
        }

        if (hearingSensors != null)
        {
            for (int i = 0; i < hearingSensors.Length; i++)
                hearingSensors[i]?.Construct(noiseWorldService);
        }
    }
}
