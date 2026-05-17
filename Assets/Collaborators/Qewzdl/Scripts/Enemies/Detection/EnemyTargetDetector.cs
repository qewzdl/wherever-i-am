using UnityEngine;

[DisallowMultipleComponent]
public class EnemyTargetDetector : MonoBehaviour
{
    [Header("Sensors")]
    [SerializeField] private EnemyVisionSensor visionSensor;
    [SerializeField] private EnemyHearingSensor hearingSensor;

    [Header("Stimulus Resolution")]
    [SerializeField] private EnemyStimulusResolverPolicy stimulusResolverPolicy = new();

    private readonly EnemyStimulusResolver stimulusResolver = new();

    private void Awake()
    {
        CacheSensors();
    }

    public bool TryResolveBestStimulus(
        EnemyConfig config,
        EnemyBlackboard blackboard,
        EnemyState currentState,
        out EnemyStimulusResolution resolution
    )
    {
        resolution = EnemyStimulusResolution.None;

        CacheSensors();

        EnemyPerceptionStimulus visionStimulus = EnemyPerceptionStimulus.None;
        bool hasVisionStimulus = visionSensor != null &&
                                 visionSensor.TryFindBestStimulus(config, out visionStimulus);

        EnemyPerceptionStimulus hearingStimulus = EnemyPerceptionStimulus.None;
        bool hasHearingStimulus = hearingSensor != null &&
                                  hearingSensor.TryFindBestStimulus(config, out hearingStimulus);

        EnemyStimulusResolveContext resolveContext = new(
            config,
            blackboard,
            currentState,
            visionStimulus,
            hasVisionStimulus,
            hearingStimulus,
            hasHearingStimulus,
            Time.time
        );

        resolution = stimulusResolver.Resolve(resolveContext, stimulusResolverPolicy);
        return resolution.HasResolution;
    }

    public bool TryFindBestStimulus(EnemyConfig config, out EnemyPerceptionStimulus stimulus)
    {
        stimulus = EnemyPerceptionStimulus.None;

        if (!TryResolveBestStimulus(
                config,
                null,
                EnemyState.Idle,
                out EnemyStimulusResolution resolution
            ))
        {
            return false;
        }

        stimulus = resolution.PrimaryStimulus;
        return stimulus.HasStimulus;
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

        if (stimulusResolverPolicy == null)
        {
            stimulusResolverPolicy = new EnemyStimulusResolverPolicy();
        }
    }
#endif
}
