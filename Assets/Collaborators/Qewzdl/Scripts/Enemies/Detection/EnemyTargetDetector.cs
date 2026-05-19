using UnityEngine;

[DisallowMultipleComponent]
public class EnemyTargetDetector : MonoBehaviour
{
    [Header("Sensors")]
    [SerializeField] private EnemyVisionSensor visionSensor;
    [SerializeField] private EnemyHearingSensor hearingSensor;

    [Header("Stimulus Resolution")]
    [SerializeField] private EnemyStimulusResolverPolicy stimulusResolverPolicy;

    private readonly EnemyStimulusResolver stimulusResolver = new();

    private bool missingConfigLogged;
    private bool invalidStaticConfigurationLogged;
    private bool missingHearingSensorLogged;

    private void Awake()
    {
        ValidateStaticDependencies();
    }

    public bool TryResolveBestStimulus(
        EnemyConfig config,
        EnemyBlackboard blackboard,
        EnemyState currentState,
        out EnemyStimulusResolution resolution
    )
    {
        resolution = EnemyStimulusResolution.None;

        if (!ValidateDependencies(config))
        {
            return false;
        }

        bool hasVisionStimulus = visionSensor.TryFindBestStimulus(
            config,
            out EnemyPerceptionStimulus visionStimulus
        );

        bool hasHearingStimulus = false;
        EnemyPerceptionStimulus hearingStimulus = EnemyPerceptionStimulus.None;

        if (config.hearingEnabled)
        {
            hasHearingStimulus = hearingSensor.TryFindBestStimulus(
                config,
                out hearingStimulus
            );
        }

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
        if (!ValidateVisionDependencies(config))
        {
            return null;
        }

        return visionSensor.FindBestVisibleTarget(config);
    }

    private bool ValidateDependencies(EnemyConfig config)
    {
        if (!ValidateConfig(config))
        {
            return false;
        }

        if (!config.RequiresTargetDetector)
        {
            return false;
        }

        if (!ValidateStaticDependencies())
        {
            return false;
        }

        if (!visionSensor.ValidateRuntimeDependencies())
        {
            return false;
        }

        if (!ValidateHearingDependencies(config))
        {
            return false;
        }

        return true;
    }

    private bool ValidateVisionDependencies(EnemyConfig config)
    {
        if (!ValidateConfig(config))
        {
            return false;
        }

        if (!config.RequiresTargetDetector)
        {
            return false;
        }

        if (!ValidateStaticDependencies())
        {
            return false;
        }

        return visionSensor.ValidateRuntimeDependencies();
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
                $"{nameof(EnemyTargetDetector)} requires non-null {nameof(EnemyConfig)}.",
                this
            );
        }

        return false;
    }

    private bool ValidateStaticDependencies(bool logErrors = true)
    {
        bool isValid = true;

        if (stimulusResolverPolicy == null)
        {
            isValid = false;
        }

        if (visionSensor == null)
        {
            isValid = false;
        }

        if (isValid)
        {
            invalidStaticConfigurationLogged = false;
            return true;
        }

        if (logErrors)
        {
            LogInvalidStaticConfiguration();
        }

        return false;
    }

    private bool ValidateHearingDependencies(EnemyConfig config)
    {
        if (!config.hearingEnabled)
        {
            missingHearingSensorLogged = false;
            return true;
        }

        if (hearingSensor == null)
        {
            LogMissingHearingSensor();
            return false;
        }

        missingHearingSensorLogged = false;
        return hearingSensor.ValidateRuntimeDependencies();
    }

    private void LogInvalidStaticConfiguration()
    {
        if (invalidStaticConfigurationLogged)
        {
            return;
        }

        invalidStaticConfigurationLogged = true;

        string missingPolicy = stimulusResolverPolicy == null
            ? $"- {nameof(stimulusResolverPolicy)} is not assigned.\n"
            : string.Empty;

        string missingVision = visionSensor == null
            ? $"- {nameof(visionSensor)} is not assigned.\n"
            : string.Empty;

        Debug.LogError(
            $"{nameof(EnemyTargetDetector)} has invalid configuration:\n" +
            missingPolicy +
            missingVision +
            "Enemy perception is disabled until configured.",
            this
        );
    }

    private void LogMissingHearingSensor()
    {
        if (missingHearingSensorLogged)
        {
            return;
        }

        missingHearingSensorLogged = true;

        Debug.LogError(
            $"{nameof(EnemyTargetDetector)} requires {nameof(EnemyHearingSensor)} " +
            "because hearing is enabled in enemy config.",
            this
        );
    }

#if UNITY_EDITOR
    private void Reset()
    {
        ValidateStaticDependencies();
    }

    private void OnValidate()
    {
        ValidateStaticDependencies();
    }
#endif
}