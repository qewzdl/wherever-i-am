public readonly struct EnemyStimulusResolveContext
{
    public EnemyConfig Config { get; }
    public EnemyBlackboard Blackboard { get; }
    public EnemyState CurrentState { get; }

    public EnemyPerceptionStimulus VisionStimulus { get; }
    public bool HasVisionStimulus { get; }

    public EnemyPerceptionStimulus HearingStimulus { get; }
    public bool HasHearingStimulus { get; }

    public float ServerTime { get; }

    public EnemyStimulusResolveContext(
        EnemyConfig config,
        EnemyBlackboard blackboard,
        EnemyState currentState,
        EnemyPerceptionStimulus visionStimulus,
        bool hasVisionStimulus,
        EnemyPerceptionStimulus hearingStimulus,
        bool hasHearingStimulus,
        float serverTime
    )
    {
        Config = config;
        Blackboard = blackboard;
        CurrentState = currentState;
        VisionStimulus = visionStimulus;
        HasVisionStimulus = hasVisionStimulus && visionStimulus.HasStimulus;
        HearingStimulus = hearingStimulus;
        HasHearingStimulus = hasHearingStimulus && hearingStimulus.HasStimulus;
        ServerTime = serverTime;
    }
}