using UnityEngine;

[DisallowMultipleComponent]
public class EnemyHearingSensor : MonoBehaviour, IEnemyPerceptionSensor
{
    private IGameplayNoiseService noiseWorldService;
    private bool missingNoiseWorldServiceLogged;
    private bool missingConfigLogged;

    public bool IsConfigured => TryResolveNoiseWorldService(false);

    public void Construct(IGameplayNoiseService service)
    {
        noiseWorldService = service;
        missingNoiseWorldServiceLogged = false;
    }

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
                config.hearingSensitivity,
                config.hearingMemoryDuration,
                config.minimumNoiseLoudness,
                out GameplayNoiseEvent noiseEvent,
                out float score
            ))
        {
            return false;
        }

        // A noise carries a position and nothing else. Resolving its source
        // object back to the HidingPlaceInteractable handed the enemy the one
        // fact hearing cannot give it - which container the player climbed
        // into - so anyone within the entry noise radius was opened up with
        // certainty and no counterplay. Only sight arms a hiding place check.
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
            $"{nameof(EnemyHearingSensor)} requires an initialized " +
            $"{nameof(IGameplayNoiseService)} from its enemy NetworkObject context.",
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
