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
    [Range(1f, 360f)] public float viewAngle = 110f;
    [Min(0f)] public float loseTargetDistance = 16f;
    [Min(0f)] public float targetHeightOffset = 1.2f;
    [Min(0.05f)] public float targetRefreshInterval = 0.25f;

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

    private void OnValidate()
    {
        loseTargetDistance = Mathf.Max(loseTargetDistance, detectionRadius);
        attackDistance = Mathf.Max(attackDistance, stoppingDistance);
        targetRefreshInterval = Mathf.Max(0.05f, targetRefreshInterval);

        investigationReachDistance = Mathf.Max(investigationReachDistance, stoppingDistance);
        investigationRepathInterval = Mathf.Max(0.05f, investigationRepathInterval);
        investigationBranchRadius = Mathf.Max(0f, investigationBranchRadius);
        investigationBranchPointCount = Mathf.Max(0, investigationBranchPointCount);
        investigationLeafRadius = Mathf.Max(0f, investigationLeafRadius);
        investigationLeafPointCountPerBranch = Mathf.Max(0, investigationLeafPointCountPerBranch);
        investigationSearchSpeed = Mathf.Max(0f, investigationSearchSpeed);

        hearingRadius = Mathf.Max(0f, hearingRadius);
        hearingMemoryDuration = Mathf.Max(0f, hearingMemoryDuration);
        minimumNoiseLoudness = Mathf.Max(0f, minimumNoiseLoudness);
    }
}