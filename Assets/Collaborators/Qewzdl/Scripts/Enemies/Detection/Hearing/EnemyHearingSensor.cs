using UnityEngine;

[DisallowMultipleComponent]
public class EnemyHearingSensor : MonoBehaviour, IEnemyPerceptionSensor
{
    private EnemyNoiseWorldService noiseWorldService;
    private bool missingNoiseWorldServiceLogged;
    private bool missingConfigLogged;

    public bool IsConfigured => noiseWorldService != null;

    public void Construct(EnemyNoiseWorldService service)
    {
        noiseWorldService = service;
        missingNoiseWorldServiceLogged = false;

        if (!ValidateRuntimeDependencies())
        {
            enabled = false;
            return;
        }

        enabled = true;
    }

    public bool ValidateRuntimeDependencies()
    {
        if (noiseWorldService != null)
        {
            missingNoiseWorldServiceLogged = false;
            return true;
        }

        LogMissingNoiseWorldService();
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

        return noiseWorldService.TryFindBestNoise(
            transform.position,
            config,
            out stimulus
        );
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
            $"{nameof(EnemyHearingSensor)} requires {nameof(EnemyNoiseWorldService)}. " +
            "Assign it through scene composition before perception ticks.",
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