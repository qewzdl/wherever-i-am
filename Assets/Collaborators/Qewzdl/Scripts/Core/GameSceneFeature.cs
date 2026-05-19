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

    protected override bool InstallFeature(ProjectContext context)
    {
        bool pauseInstalled = InstallPause(context);
        bool hearingInstalled = InstallEnemyHearing();

        return pauseInstalled && hearingInstalled;
    }

    private bool InstallPause(ProjectContext context)
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
            IPauseServiceConsumer consumer = RequireInterface<IPauseServiceConsumer>(
                pauseConsumers[i],
                this,
                $"{nameof(pauseConsumers)}[{i}]");

            if (consumer == null)
            {
                valid = false;
                continue;
            }

            consumer.BindPauseService(pauseService);
        }

        return valid;
    }

    private bool InstallEnemyHearing()
    {
        if (!RequireReference(noiseWorldService, nameof(noiseWorldService)))
            return false;

        noiseWorldService.Initialize();

        bool emittersInstalled = InstallNoiseEmitters();
        bool sensorsInstalled = InstallHearingSensors();

        return emittersInstalled && sensorsInstalled;
    }

    private bool InstallNoiseEmitters()
    {
        if (noiseEmitters == null)
            return true;

        bool valid = true;

        for (int i = 0; i < noiseEmitters.Length; i++)
        {
            EnemyNoiseEmitter emitter = noiseEmitters[i];

            if (emitter == null)
            {
                LogMissingReference($"{nameof(noiseEmitters)}[{i}]");
                valid = false;
                continue;
            }

            emitter.Construct(noiseWorldService);
        }

        return valid;
    }

    private bool InstallHearingSensors()
    {
        if (hearingSensors == null)
            return true;

        bool valid = true;

        for (int i = 0; i < hearingSensors.Length; i++)
        {
            EnemyHearingSensor sensor = hearingSensors[i];

            if (sensor == null)
            {
                LogMissingReference($"{nameof(hearingSensors)}[{i}]");
                valid = false;
                continue;
            }

            sensor.Construct(noiseWorldService);
        }

        return valid;
    }
}