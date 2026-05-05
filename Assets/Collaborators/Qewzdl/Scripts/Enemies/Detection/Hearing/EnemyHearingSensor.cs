using UnityEngine;

[DisallowMultipleComponent]
public class EnemyHearingSensor : MonoBehaviour, IEnemyPerceptionSensor
{
    public bool TryFindBestStimulus(EnemyConfig config, out EnemyPerceptionStimulus stimulus)
    {
        return EnemyNoiseSystem.TryFindBestNoise(
            transform.position,
            config,
            out stimulus
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