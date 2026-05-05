using UnityEngine;

[DisallowMultipleComponent]
public class EnemyTargetDetector : MonoBehaviour
{
    [Header("Sensors")]
    [SerializeField] private EnemyVisionSensor visionSensor;
    [SerializeField] private EnemyHearingSensor hearingSensor;

    [Header("Priority")]
    [SerializeField] private bool preferVisionOverHearing = true;

    private void Awake()
    {
        CacheSensors();
    }

    public bool TryFindBestStimulus(EnemyConfig config, out EnemyPerceptionStimulus stimulus)
    {
        stimulus = EnemyPerceptionStimulus.None;

        CacheSensors();

        EnemyPerceptionStimulus visionStimulus = EnemyPerceptionStimulus.None;
        bool hasVisionStimulus = visionSensor != null &&
                                 visionSensor.TryFindBestStimulus(config, out visionStimulus);

        if (preferVisionOverHearing && hasVisionStimulus)
        {
            stimulus = visionStimulus;
            return true;
        }

        EnemyPerceptionStimulus hearingStimulus = EnemyPerceptionStimulus.None;
        bool hasHearingStimulus = hearingSensor != null &&
                                  hearingSensor.TryFindBestStimulus(config, out hearingStimulus);

        if (!hasVisionStimulus && !hasHearingStimulus)
        {
            return false;
        }

        if (hasVisionStimulus && !hasHearingStimulus)
        {
            stimulus = visionStimulus;
            return true;
        }

        if (!hasVisionStimulus)
        {
            stimulus = hearingStimulus;
            return true;
        }

        stimulus = visionStimulus.Score >= hearingStimulus.Score
            ? visionStimulus
            : hearingStimulus;

        return true;
    }

    public EnemyTarget FindBestVisibleTarget(EnemyConfig config)
    {
        CacheSensors();
        return visionSensor != null ? visionSensor.FindBestVisibleTarget(config) : null;
    }

    private void CacheSensors()
    {
        if (visionSensor == null)
        {
            visionSensor = GetComponent<EnemyVisionSensor>();
        }

        if (hearingSensor == null)
        {
            hearingSensor = GetComponent<EnemyHearingSensor>();
        }
    }

#if UNITY_EDITOR
    private void Reset()
    {
        CacheSensors();
    }

    private void OnValidate()
    {
        CacheSensors();
    }
#endif
}
