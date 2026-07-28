using System.Text;
using UnityEngine;

[CreateAssetMenu(menuName = "Wherever I Am/Enemies/Enemy Config", fileName = "EnemyConfig")]
public class EnemyConfig : ScriptableObject
{
    [Header("Behavior")]
    [SerializeField] private EnemyBehaviorMode behaviorMode = EnemyBehaviorMode.Standard;

    [Header("Profiles")]
    [SerializeField] private EnemyMovementConfig movementProfile;
    [SerializeField] private EnemyNavigationConfig navigationProfile;
    [SerializeField] private EnemyVisionConfig visionProfile;
    [SerializeField] private EnemyHearingConfig hearingProfile;
    [SerializeField] private EnemyInvestigationConfig investigationProfile;
    [SerializeField] private EnemyPatrolConfig patrolProfile;
    [SerializeField] private EnemyPostureConfig postureProfile;
    [SerializeField] private EnemyAttackTimingConfig attackTimingProfile;
    [SerializeField] private EnemyAttackHitValidationConfig attackHitValidationProfile;

    public EnemyBehaviorMode BehaviorMode => behaviorMode;
    public bool IsPatrolOnlyEnemy => behaviorMode == EnemyBehaviorMode.PatrolOnly;
    public bool RequiresTargetDetector => !IsPatrolOnlyEnemy;

    public EnemyMovementConfig MovementProfile => movementProfile;
    public EnemyNavigationConfig NavigationProfile => navigationProfile;
    public EnemyVisionConfig VisionProfile => visionProfile;
    public EnemyHearingConfig HearingProfile => hearingProfile;
    public EnemyInvestigationConfig InvestigationProfile => investigationProfile;
    public EnemyPatrolConfig PatrolProfile => patrolProfile;
    public EnemyPostureConfig PostureProfile => postureProfile;
    public EnemyAttackTimingConfig AttackTimingProfile => attackTimingProfile;
    public EnemyAttackHitValidationConfig AttackHitValidationProfile => attackHitValidationProfile;

    public bool HasAllProfiles =>
        movementProfile != null &&
        navigationProfile != null &&
        visionProfile != null &&
        hearingProfile != null &&
        investigationProfile != null &&
        patrolProfile != null &&
        postureProfile != null &&
        attackTimingProfile != null &&
        attackHitValidationProfile != null;

    public bool HasRequiredProfiles =>
        movementProfile != null &&
        navigationProfile != null &&
        patrolProfile != null &&
        postureProfile != null &&
        (
            IsPatrolOnlyEnemy ||
            (
                visionProfile != null &&
                hearingProfile != null &&
                investigationProfile != null &&
                attackTimingProfile != null &&
                attackHitValidationProfile != null
            )
        );

    public float patrolSpeed => movementProfile.patrolSpeed;
    public float chaseSpeed => movementProfile.chaseSpeed;
    public float acceleration => movementProfile.acceleration;
    public float angularSpeed => movementProfile.angularSpeed;
    public float stoppingDistance => movementProfile.stoppingDistance;
    public float navigationRepathInterval => navigationProfile.repathInterval;
    public float navigationDestinationRepathDistance =>
        navigationProfile.destinationRepathDistance;
    public float navigationNavMeshSampleRadius => navigationProfile.navMeshSampleRadius;
    public int navigationMaximumPathQueriesPerRepath =>
        navigationProfile.maximumPathQueriesPerRepath;
    public float navigationDirectPathCheckInterval =>
        navigationProfile.directPathCheckInterval;
    public float navigationProgressSampleInterval =>
        navigationProfile.progressSampleInterval;
    public float navigationMinimumProgressDistance =>
        navigationProfile.minimumProgressDistance;
    public float navigationStuckTimeout => navigationProfile.stuckTimeout;
    public float navigationDirectMovementTimeout =>
        navigationProfile.directMovementTimeout;

    public float detectionRadius => visionProfile.detectionRadius;
    public float horizontalViewAngle => visionProfile.horizontalViewAngle;
    public float verticalViewAngle => visionProfile.verticalViewAngle;
    public float loseTargetDistance => Mathf.Max(visionProfile.loseTargetDistance, detectionRadius);
    public float targetHeightOffset => visionProfile.targetHeightOffset;
    public float targetRefreshInterval => visionProfile.targetRefreshInterval;
    public float visualTargetMemoryDuration => visionProfile.visualTargetMemoryDuration;

    public float investigationReachDistance => Mathf.Max(
        investigationProfile.investigationReachDistance,
        stoppingDistance
    );

    public float investigationRepathInterval => investigationProfile.investigationRepathInterval;
    public float investigationBranchRadius => investigationProfile.investigationBranchRadius;
    public int investigationBranchPointCount => investigationProfile.investigationBranchPointCount;
    public float investigationLeafRadius => investigationProfile.investigationLeafRadius;
    public int investigationLeafPointCountPerBranch => investigationProfile.investigationLeafPointCountPerBranch;
    public float investigationSearchSpeed => investigationProfile.investigationSearchSpeed;

    public bool hearingEnabled => hearingProfile.hearingEnabled;
    public float hearingRadius => hearingProfile.hearingRadius;
    public float hearingMemoryDuration => hearingProfile.hearingMemoryDuration;
    public float minimumNoiseLoudness => hearingProfile.minimumNoiseLoudness;

    public float attackDistance => Mathf.Max(
        attackHitValidationProfile.attackDistance,
        stoppingDistance
    );

    public float attackCooldown => attackTimingProfile.attackCooldown;
    public float attackWindupDuration => attackTimingProfile.attackWindupDuration;
    public float attackCommitDuration => attackTimingProfile.attackCommitDuration;
    public float attackRecoveryDuration => attackTimingProfile.attackRecoveryDuration;
    public float attackInterruptedDuration => attackTimingProfile.attackInterruptedDuration;

    public float attackCommitMaxDistance => Mathf.Max(
        attackHitValidationProfile.CommitMaxDistance,
        attackDistance
    );

    public bool attackLineOfHitValidationEnabled => attackHitValidationProfile.validateLineOfHit;
    public LayerMask attackLineOfHitBlockingMask => attackHitValidationProfile.attackLineOfHitBlockingMask;
    public float attackLineOfHitOriginHeight => attackHitValidationProfile.attackLineOfHitOriginHeight;
    public QueryTriggerInteraction attackLineOfHitTriggerInteraction =>
        attackHitValidationProfile.attackLineOfHitTriggerInteraction;

    public float patrolPointReachDistance => patrolProfile.patrolPointReachDistance;
    public float patrolRouteVariation => patrolProfile.patrolRouteVariation;
    public float patrolEdgeClearance => patrolProfile.patrolEdgeClearance;
    public float patrolMaxDetourRatio => patrolProfile.patrolMaxDetourRatio;
    public float patrolIntermediatePointSpacing =>
        patrolProfile.patrolIntermediatePointSpacing;
    public int patrolRouteSampleAttempts => patrolProfile.patrolRouteSampleAttempts;
    public float patrolStopDuration => patrolProfile.patrolStopDuration;
    public float patrolStopWanderRadius => patrolProfile.patrolStopWanderRadius;
    public float patrolStopWanderSpeed => patrolProfile.patrolStopWanderSpeed;
    public float patrolStopWanderPointReachDistance => patrolProfile.patrolStopWanderPointReachDistance;
    public int patrolStopWanderSampleAttempts => patrolProfile.patrolStopWanderSampleAttempts;
    public float patrolStopWanderMinDistanceFromEnemy => patrolProfile.patrolStopWanderMinDistanceFromEnemy;

    public bool crawlingEnabled => postureProfile.crawlingEnabled;

    public float standingToCrawlingTransitionDuration =>
        postureProfile.standingToCrawlingTransitionDuration;

    public float crawlingToStandingTransitionDuration =>
        postureProfile.crawlingToStandingTransitionDuration;

    public float standingAgentHeight => postureProfile.standingAgentHeight;
    public float standingAgentRadius => postureProfile.standingAgentRadius;
    public float standingAgentBaseOffset => postureProfile.standingAgentBaseOffset;

    public float crawlingAgentHeight => postureProfile.crawlingAgentHeight;
    public float crawlingAgentRadius => postureProfile.crawlingAgentRadius;
    public float crawlingAgentBaseOffset => postureProfile.crawlingAgentBaseOffset;

    public float crawlingSpeedMultiplier => postureProfile.crawlingSpeedMultiplier;
    public float postureNavMeshSampleRadius => postureProfile.postureNavMeshSampleRadius;
    public float postureSwitchSampleRadius => postureProfile.postureSwitchSampleRadius;

    public float minPostureDuration => postureProfile.minPostureDuration;
    public float standingRecoveryCheckInterval => postureProfile.standingRecoveryCheckInterval;

    public LayerMask standingClearanceMask => postureProfile.standingClearanceMask;
    public float standingClearanceSkin => postureProfile.standingClearanceSkin;
    public QueryTriggerInteraction standingClearanceTriggerInteraction =>
        postureProfile.standingClearanceTriggerInteraction;

    public float standingBodyColliderHeight => postureProfile.standingBodyColliderHeight;
    public float standingBodyColliderRadius => postureProfile.standingBodyColliderRadius;
    public Vector3 standingBodyColliderCenter => postureProfile.standingBodyColliderCenter;

    public float crawlingBodyColliderHeight => postureProfile.crawlingBodyColliderHeight;
    public float crawlingBodyColliderRadius => postureProfile.crawlingBodyColliderRadius;
    public Vector3 crawlingBodyColliderCenter => postureProfile.crawlingBodyColliderCenter;

    public float GetPostureTransitionDuration(EnemyPosture fromPosture, EnemyPosture toPosture)
    {
        return postureProfile.GetTransitionDuration(fromPosture, toPosture);
    }

    public bool TryGetValidationError(out string error)
    {
        if (HasRequiredProfiles)
        {
            error = string.Empty;
            return false;
        }

        StringBuilder builder = new();

        builder.AppendLine(
            $"{nameof(EnemyConfig)} '{name}' has missing profiles for {behaviorMode} behavior:"
        );

        AppendMissingProfile(builder, movementProfile, nameof(movementProfile));
        AppendMissingProfile(builder, navigationProfile, nameof(navigationProfile));
        AppendMissingProfile(builder, patrolProfile, nameof(patrolProfile));
        AppendMissingProfile(builder, postureProfile, nameof(postureProfile));

        if (RequiresTargetDetector)
        {
            AppendMissingProfile(builder, visionProfile, nameof(visionProfile));
            AppendMissingProfile(builder, hearingProfile, nameof(hearingProfile));
            AppendMissingProfile(builder, investigationProfile, nameof(investigationProfile));
            AppendMissingProfile(builder, attackTimingProfile, nameof(attackTimingProfile));
            AppendMissingProfile(builder, attackHitValidationProfile, nameof(attackHitValidationProfile));
        }

        error = builder.ToString();
        return true;
    }

    public void ValidateProfiles()
    {
        movementProfile?.Validate();
        navigationProfile?.Validate();
        visionProfile?.Validate();
        hearingProfile?.Validate();
        investigationProfile?.Validate(stoppingDistance);
        patrolProfile?.Validate();
        postureProfile?.Validate();
        attackHitValidationProfile?.Validate(stoppingDistance);
        attackTimingProfile?.Validate();
    }

    private static void AppendMissingProfile(
        StringBuilder builder,
        ScriptableObject profile,
        string fieldName
    )
    {
        if (profile != null)
        {
            return;
        }

        builder.Append("- ");
        builder.AppendLine(fieldName);
    }

    private void OnValidate()
    {
        if (!HasRequiredProfiles)
        {
            return;
        }

        ValidateProfiles();
    }
}
