using UnityEngine;
using UnityEngine.Serialization;

[CreateAssetMenu(menuName = "Wherever I Am/Enemies/Enemy Config", fileName = "EnemyConfig")]
public class EnemyConfig : ScriptableObject
{
    [Header("Movement")]
    [Min(0f)] public float patrolSpeed = 1.6f;
    [Min(0f)] public float chaseSpeed = 2.8f;
    [Min(0f)] public float acceleration = 12f;
    [Min(0f)] public float angularSpeed = 360f;
    [Min(0f)] public float stoppingDistance = 0.2f;

    [Header("Detection")]
    [Min(0f)] public float detectionRadius = 12f;

    [FormerlySerializedAs("viewAngle")]
    [Range(1f, 360f)] public float horizontalViewAngle = 110f;

    [Range(1f, 180f)] public float verticalViewAngle = 80f;

    [Min(0f)] public float loseTargetDistance = 16f;
    [Min(0f)] public float targetHeightOffset = 1.2f;
    [Min(0.05f)] public float targetRefreshInterval = 0.25f;
    [Min(0f)] public float visualTargetMemoryDuration = 2f;

    [Header("Investigation")]
    [Min(0f)] public float investigationReachDistance = 0.75f;
    [Min(0.05f)] public float investigationRepathInterval = 0.25f;

    [FormerlySerializedAs("investigationSearchRadius")]
    [Min(0f)] public float investigationBranchRadius = 2.5f;

    [FormerlySerializedAs("investigationSearchPointCount")]
    [Min(0)] public int investigationBranchPointCount = 3;

    [Min(0f)] public float investigationLeafRadius = 1.5f;
    [Min(0)] public int investigationLeafPointCountPerBranch = 3;

    [Min(0f)] public float investigationSearchSpeed = 1.7f;

    [Header("Hearing")]
    public bool hearingEnabled = true;
    [Min(0f)] public float hearingRadius = 10f;
    [Min(0f)] public float hearingMemoryDuration = 3f;
    [Min(0f)] public float minimumNoiseLoudness = 0.1f;

    [Header("Attack")]
    [Min(0f)] public float attackDistance = 1.6f;
    [Min(0f)] public float attackCooldown = 1.5f;

    [Header("Patrol")]
    [Min(0f)] public float patrolPointReachDistance = 0.4f;
    [Min(0f)] public float patrolStopDuration = 4f;
    [Min(0f)] public float patrolStopWanderRadius = 2f;
    [Min(0f)] public float patrolStopWanderSpeed = 1.2f;
    [Min(0f)] public float patrolStopWanderPointReachDistance = 0.35f;
    [Min(1)] public int patrolStopWanderSampleAttempts = 12;
    [Min(0f)] public float patrolStopWanderMinDistanceFromEnemy = 0.75f;

    [Header("Posture")]
    public bool crawlingEnabled = true;

    [Min(0.1f)] public float standingAgentHeight = 2f;
    [Min(0.05f)] public float standingAgentRadius = 0.35f;
    public float standingAgentBaseOffset = 0f;

    [Min(0.1f)] public float crawlingAgentHeight = 0.75f;
    [Min(0.05f)] public float crawlingAgentRadius = 0.35f;
    public float crawlingAgentBaseOffset = 0f;

    [Min(0.05f)] public float crawlingSpeedMultiplier = 0.55f;
    [Min(0.1f)] public float postureNavMeshSampleRadius = 1.25f;

    [Min(0.1f)] public float standingBodyColliderHeight = 2f;
    [Min(0.05f)] public float standingBodyColliderRadius = 0.35f;
    public Vector3 standingBodyColliderCenter = new(0f, 1f, 0f);

    [Min(0.1f)] public float crawlingBodyColliderHeight = 0.75f;
    [Min(0.05f)] public float crawlingBodyColliderRadius = 0.35f;
    public Vector3 crawlingBodyColliderCenter = new(0f, 0.375f, 0f);

    private void OnValidate()
    {
        loseTargetDistance = Mathf.Max(loseTargetDistance, detectionRadius);
        attackDistance = Mathf.Max(attackDistance, stoppingDistance);
        targetRefreshInterval = Mathf.Max(0.05f, targetRefreshInterval);
        visualTargetMemoryDuration = Mathf.Max(0f, visualTargetMemoryDuration);

        horizontalViewAngle = Mathf.Clamp(horizontalViewAngle, 1f, 360f);
        verticalViewAngle = Mathf.Clamp(verticalViewAngle, 1f, 180f);

        investigationReachDistance = Mathf.Max(investigationReachDistance, stoppingDistance);
        investigationRepathInterval = Mathf.Max(0.05f, investigationRepathInterval);
        investigationBranchRadius = Mathf.Max(0f, investigationBranchRadius);
        investigationBranchPointCount = Mathf.Max(0, investigationBranchPointCount);
        investigationLeafRadius = Mathf.Max(0f, investigationLeafRadius);
        investigationLeafPointCountPerBranch = Mathf.Max(0, investigationLeafPointCountPerBranch);
        investigationSearchSpeed = Mathf.Max(0f, investigationSearchSpeed);

        patrolPointReachDistance = Mathf.Max(0f, patrolPointReachDistance);
        patrolStopDuration = Mathf.Max(0f, patrolStopDuration);
        patrolStopWanderRadius = Mathf.Max(0f, patrolStopWanderRadius);
        patrolStopWanderSpeed = Mathf.Max(0f, patrolStopWanderSpeed);
        patrolStopWanderPointReachDistance = Mathf.Max(0f, patrolStopWanderPointReachDistance);
        patrolStopWanderSampleAttempts = Mathf.Max(1, patrolStopWanderSampleAttempts);
        patrolStopWanderMinDistanceFromEnemy = Mathf.Max(0f, patrolStopWanderMinDistanceFromEnemy);

        hearingRadius = Mathf.Max(0f, hearingRadius);
        hearingMemoryDuration = Mathf.Max(0f, hearingMemoryDuration);
        minimumNoiseLoudness = Mathf.Max(0f, minimumNoiseLoudness);

        standingAgentHeight = Mathf.Max(0.1f, standingAgentHeight);
        standingAgentRadius = Mathf.Max(0.05f, standingAgentRadius);

        crawlingAgentHeight = Mathf.Max(0.1f, crawlingAgentHeight);
        crawlingAgentRadius = Mathf.Max(0.05f, crawlingAgentRadius);

        crawlingSpeedMultiplier = Mathf.Max(0.05f, crawlingSpeedMultiplier);
        postureNavMeshSampleRadius = Mathf.Max(0.1f, postureNavMeshSampleRadius);

        standingBodyColliderHeight = Mathf.Max(0.1f, standingBodyColliderHeight);
        standingBodyColliderRadius = Mathf.Max(0.05f, standingBodyColliderRadius);

        crawlingBodyColliderHeight = Mathf.Max(0.1f, crawlingBodyColliderHeight);
        crawlingBodyColliderRadius = Mathf.Max(0.05f, crawlingBodyColliderRadius);
    }
}