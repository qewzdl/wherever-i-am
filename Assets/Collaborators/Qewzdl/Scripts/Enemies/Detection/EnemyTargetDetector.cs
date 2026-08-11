using UnityEngine;

[DisallowMultipleComponent]
public class EnemyTargetDetector : MonoBehaviour, IEnemyValidatedComponent
{
    [Header("Sensors")]
    [SerializeField] private EnemyVisionSensor visionSensor;
    [SerializeField] private EnemyHearingSensor hearingSensor;

    [Header("Stimulus Resolution")]
    [SerializeField] private EnemyStimulusResolverPolicy stimulusResolverPolicy;

    private readonly EnemyStimulusResolver stimulusResolver = new();
    private readonly EnemyTargetSelector targetSelector = new();

    private bool missingConfigLogged;
    private bool invalidStaticConfigurationLogged;
    private bool invalidRuntimeConfigurationLogged;
    private bool missingHearingSensorLogged;

    public bool IsConfigured =>
        ValidateStaticDependencies(false) &&
        ValidateRuntimeDependencies(false);

    public void Construct(IGameplayNoiseService noiseService)
    {
        hearingSensor?.Construct(noiseService);
    }

    private void Awake()
    {
        if (!ValidateStaticDependencies())
        {
            enabled = false;
        }
    }

    public bool ValidateStaticDependencies()
    {
        return ValidateStaticDependencies(true);
    }

    public bool ValidateRuntimeDependencies()
    {
        return ValidateRuntimeDependencies(true);
    }

    public bool ValidateRuntimeDependencies(EnemyConfig config)
    {
        if (!ValidateConfig(config))
        {
            return false;
        }

        if (!config.RequiresTargetDetector)
        {
            return true;
        }

        if (!ValidateRuntimeDependencies())
        {
            return false;
        }

        if (!ValidateHearingDependencies(config))
        {
            return false;
        }

        return true;
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

        EnemyTarget currentTarget = blackboard?.TargetMemory.CurrentTarget;
        bool hasVisionStimulus = visionSensor.TryFindBestStimulus(
            config,
            currentTarget,
            out EnemyPerceptionStimulus visionStimulus,
            out EnemyPerceptionStimulus currentTargetVisionStimulus
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
        resolution = ApplyTargetSelection(
            resolution,
            config,
            blackboard,
            currentState,
            resolveContext,
            currentTargetVisionStimulus
        );

        return resolution.HasResolution;
    }

    // The single gate every confirmed target passes through. The resolver
    // decides what the senses are reporting; this decides whether the enemy is
    // allowed to act on a change of person.
    private EnemyStimulusResolution ApplyTargetSelection(
        EnemyStimulusResolution resolution,
        EnemyConfig config,
        EnemyBlackboard blackboard,
        EnemyState currentState,
        EnemyStimulusResolveContext resolveContext,
        EnemyPerceptionStimulus currentTargetVisionStimulus
    )
    {
        if (blackboard == null ||
            resolution.Action != EnemyStimulusResolutionAction.ChaseConfirmedTarget)
        {
            return resolution;
        }

        EnemyTarget currentTarget = blackboard.TargetMemory.CurrentTarget;
        EnemyTarget candidate = resolution.PrimaryStimulus.Target;

        if (!blackboard.TargetMemory.IsCurrentTargetValid)
        {
            currentTarget = null;
        }

        bool hasCurrentScore =
            currentTarget != null &&
            currentTarget != candidate &&
            currentTargetVisionStimulus.HasStimulus;

        float currentScore = hasCurrentScore
            ? currentTargetVisionStimulus.Score
            : 0f;

        if (targetSelector.ShouldSwitchTo(
                currentTarget,
                candidate,
                resolution.PrimaryStimulus.Score,
                currentScore,
                hasCurrentScore,
                currentState,
                resolveContext.ServerTime,
                stimulusResolverPolicy.AllowSwitchToNewVisibleTarget,
                stimulusResolverPolicy.TargetSwitchMinimumHoldDuration,
                stimulusResolverPolicy.TargetSwitchScoreAdvantage
            ))
        {
            targetSelector.NotifyCommitted(candidate, resolveContext.ServerTime);
            return resolution;
        }

        // A refused challenger is not evidence that the current target was
        // seen. If the held target was present in the same scan, continue from
        // that real stimulus. Otherwise resolve hearing on its own and let the
        // normal no-stimulus path start visual memory. Fabricating a vision
        // hit at LastKnownTargetPosition kept refreshing stale targets forever.
        if (currentTargetVisionStimulus.HasStimulus)
        {
            return EnemyStimulusResolution.Chase(
                currentTargetVisionStimulus,
                resolution.SecondaryStimulus,
                resolution.HasSecondaryStimulus
            );
        }

        EnemyStimulusResolveContext withoutRefusedVision = new(
            config,
            blackboard,
            currentState,
            EnemyPerceptionStimulus.None,
            false,
            resolveContext.HearingStimulus,
            resolveContext.HasHearingStimulus,
            resolveContext.ServerTime
        );

        return stimulusResolver.Resolve(
            withoutRefusedVision,
            stimulusResolverPolicy
        );
    }

    public void ResetTargetSelection()
    {
        targetSelector.Clear();
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
        return ValidateRuntimeDependencies(config);
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

        if (!ValidateRuntimeDependencies())
        {
            return false;
        }

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
                $"{nameof(EnemyTargetDetector)} requires non-null {nameof(EnemyConfig)}.",
                this
            );
        }

        return false;
    }

    private bool ValidateStaticDependencies(bool logErrors)
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

    private bool ValidateRuntimeDependencies(bool logErrors)
    {
        if (!ValidateStaticDependencies(logErrors))
        {
            return false;
        }

        if (visionSensor.ValidateRuntimeDependencies())
        {
            invalidRuntimeConfigurationLogged = false;
            return true;
        }

        if (logErrors)
        {
            LogInvalidRuntimeConfiguration();
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

    private void LogInvalidRuntimeConfiguration()
    {
        if (invalidRuntimeConfigurationLogged)
        {
            return;
        }

        invalidRuntimeConfigurationLogged = true;

        Debug.LogError(
            $"{nameof(EnemyTargetDetector)} has invalid runtime dependencies:\n" +
            $"- {nameof(visionSensor)} runtime dependencies are invalid.\n" +
            "Enemy perception is disabled until runtime dependencies are configured.",
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
            $"{nameof(EnemyTargetDetector)} has invalid configuration:\n" +
            $"- {nameof(hearingSensor)} is not assigned while hearing is enabled.\n" +
            "Enemy perception is disabled until configured.",
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
