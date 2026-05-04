using UnityEngine;

[CreateAssetMenu(menuName = "Game/Enemies/Enemy Config", fileName = "EnemyConfig")]
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
    }
}