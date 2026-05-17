using UnityEngine;

[DisallowMultipleComponent]
public class EnemyHearingSensor : MonoBehaviour, IEnemyPerceptionSensor
{
    [SerializeField] private EnemyNoiseWorldService noiseWorldService;

    public void Construct(EnemyNoiseWorldService service)
    {
        noiseWorldService = service;
    }

    public bool TryFindBestStimulus(EnemyConfig config, out EnemyPerceptionStimulus stimulus)
    {
        if (!TryGetNoiseWorldService(out EnemyNoiseWorldService service))
        {
            stimulus = EnemyPerceptionStimulus.None;
            return false;
        }

        return service.TryFindBestNoise(
            transform.position,
            config,
            out stimulus
        );
    }

    private bool TryGetNoiseWorldService(out EnemyNoiseWorldService service)
    {
        if (noiseWorldService == null)
        {
            noiseWorldService = FindFirstObjectByType<EnemyNoiseWorldService>();
        }

        service = noiseWorldService;
        return service != null;
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
