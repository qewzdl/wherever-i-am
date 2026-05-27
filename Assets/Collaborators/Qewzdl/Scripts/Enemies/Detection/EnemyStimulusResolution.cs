public readonly struct EnemyStimulusResolution
{
    public static readonly EnemyStimulusResolution None = new(
        false,
        EnemyStimulusResolutionAction.None,
        EnemyPerceptionStimulus.None,
        EnemyPerceptionStimulus.None,
        false,
        false
    );

    public bool HasResolution { get; }
    public EnemyStimulusResolutionAction Action { get; }
    public EnemyPerceptionStimulus PrimaryStimulus { get; }
    public EnemyPerceptionStimulus SecondaryStimulus { get; }
    public bool HasSecondaryStimulus { get; }
    public bool ShouldClearCurrentTarget { get; }

    private EnemyStimulusResolution(
        bool hasResolution,
        EnemyStimulusResolutionAction action,
        EnemyPerceptionStimulus primaryStimulus,
        EnemyPerceptionStimulus secondaryStimulus,
        bool hasSecondaryStimulus,
        bool shouldClearCurrentTarget
    )
    {
        HasResolution = hasResolution;
        Action = action;
        PrimaryStimulus = primaryStimulus;
        SecondaryStimulus = secondaryStimulus;
        HasSecondaryStimulus = hasSecondaryStimulus;
        ShouldClearCurrentTarget = shouldClearCurrentTarget;
    }

    public static EnemyStimulusResolution Chase(
        EnemyPerceptionStimulus stimulus,
        EnemyPerceptionStimulus secondaryStimulus = default,
        bool hasSecondaryStimulus = false
    )
    {
        if (!stimulus.HasStimulus || !stimulus.IsConfirmedTarget || !stimulus.HasTarget)
        {
            return None;
        }

        return new EnemyStimulusResolution(
            true,
            EnemyStimulusResolutionAction.ChaseConfirmedTarget,
            stimulus,
            secondaryStimulus,
            hasSecondaryStimulus && secondaryStimulus.HasStimulus,
            false
        );
    }

    public static EnemyStimulusResolution Investigate(
        EnemyPerceptionStimulus stimulus,
        bool shouldClearCurrentTarget
    )
    {
        if (!stimulus.HasStimulus)
        {
            return None;
        }

        return new EnemyStimulusResolution(
            true,
            EnemyStimulusResolutionAction.InvestigateSuspiciousPosition,
            stimulus,
            EnemyPerceptionStimulus.None,
            false,
            shouldClearCurrentTarget
        );
    }

    public static EnemyStimulusResolution RememberSecondarySuspicion(
        EnemyPerceptionStimulus stimulus
    )
    {
        if (!stimulus.HasStimulus)
        {
            return None;
        }

        return new EnemyStimulusResolution(
            true,
            EnemyStimulusResolutionAction.RememberSecondarySuspicion,
            stimulus,
            EnemyPerceptionStimulus.None,
            false,
            false
        );
    }
}