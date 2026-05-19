using UnityEngine;

public sealed class EnemyHearingSceneFeature : SceneRuntimeFeature
{
    [SerializeField] private EnemyNoiseWorldService noiseWorldService;
    [SerializeField] private EnemyNoiseEmitter[] noiseEmitters;
    [SerializeField] private EnemyHearingSensor[] hearingSensors;

    protected override bool InstallFeature(ProjectContext context)
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