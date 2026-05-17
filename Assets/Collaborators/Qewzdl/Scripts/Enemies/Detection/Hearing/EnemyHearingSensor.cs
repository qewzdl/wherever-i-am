using UnityEngine;

[DisallowMultipleComponent]
public class EnemyHearingSensor : MonoBehaviour, IEnemyPerceptionSensor
{
    private EnemyNoiseWorldService noiseWorldService;
    private bool missingNoiseWorldServiceLogged;

    public void Construct(EnemyNoiseWorldService service)
    {
        noiseWorldService = service;
        missingNoiseWorldServiceLogged = false;

        if (noiseWorldService == null)
        {
            LogMissingNoiseWorldService();
        }
    }

    public bool TryFindBestStimulus(EnemyConfig config, out EnemyPerceptionStimulus stimulus)
    {
        stimulus = EnemyPerceptionStimulus.None;

        if (noiseWorldService == null)
        {
            LogMissingNoiseWorldService();
            return false;
        }

        return noiseWorldService.TryFindBestNoise(
            transform.position,
            config,
            out stimulus
        );
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