using UnityEngine;

[DisallowMultipleComponent]
public class EnemyHearingSensor : MonoBehaviour, IEnemyPerceptionSensor
{
    private GameplayNoiseWorldService noiseWorldService;
    private bool missingNoiseWorldServiceLogged;
    private bool missingConfigLogged;

    public bool IsConfigured => TryResolveNoiseWorldService(false);

    public bool ValidateStaticDependencies()
    {
        return true;
    }

    public bool ValidateRuntimeDependencies()
    {
        return TryResolveNoiseWorldService(true);
    }

    private bool TryResolveNoiseWorldService(bool logErrors)
    {
        if (noiseWorldService != null && noiseWorldService.IsInitialized)
        {
            missingNoiseWorldServiceLogged = false;
            return true;
        }

        ProjectContext context = ProjectContext.Instance;
        GameplayNoiseWorldService service = context != null
            ? context.GameplayNoiseWorld
            : null;

        if (service != null && service.IsInitialized)
        {
            noiseWorldService = service;
            missingNoiseWorldServiceLogged = false;
            return true;
        }

        noiseWorldService = null;

        if (logErrors)
        {
            LogMissingNoiseWorldService();
        }

        return false;
    }

    public bool TryFindBestStimulus(EnemyConfig config, out EnemyPerceptionStimulus stimulus)
    {
        stimulus = EnemyPerceptionStimulus.None;

        if (!ValidateConfig(config))
        {
            return false;
        }

        if (!config.hearingEnabled)
        {
            return false;
        }

        if (!ValidateRuntimeDependencies())
        {
            return false;
        }

        if (!noiseWorldService.TryFindBestNoise(
                transform.position,
                config.hearingRadius,
                config.hearingMemoryDuration,
                config.minimumNoiseLoudness,
                out GameplayNoiseEvent noiseEvent,
                out float score
            ))
        {
            return false;
        }

        stimulus = EnemyPerceptionStimulus.ForSuspiciousPosition(
            noiseEvent.Position,
            score,
            EnemyPerceptionSource.Hearing
        );

        return true;
    }

    private bool ValidateConfig(EnemyConfig config)
    {
        if (config != null)
        {
            missingConfigLogged = false;
            return true;
        }

        if (!missingConfigLogged)
        {
            missingConfigLogged = true;

            Debug.LogError(
                $"{nameof(EnemyHearingSensor)} requires non-null {nameof(EnemyConfig)}.",
                this
            );
        }

        return false;
    }

    private void LogMissingNoiseWorldService()
    {
        if (missingNoiseWorldServiceLogged)
        {
            return;
        }

        missingNoiseWorldServiceLogged = true;

        Debug.LogError(
            $"{nameof(EnemyHearingSensor)} requires {nameof(GameplayNoiseWorldService)}. " +
            $"Configure it in {nameof(ProjectContext)} before perception ticks.",
            this
        );
    }

#if UNITY_EDITOR
    private void OnDrawGizmosSelected()
    {
        NetworkEnemyController controller = GetComponent<NetworkEnemyController>();
        EnemyConfig config = controller != null ? controller.Config : null;

        if (config == null || !config.hearingEnabled)
        {
            return;
        }

        Gizmos.color = Color.blue;
        Gizmos.DrawWireSphere(transform.position, config.hearingRadius);
    }
#endif
}
