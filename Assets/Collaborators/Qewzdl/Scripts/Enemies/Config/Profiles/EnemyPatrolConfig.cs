using UnityEngine;

[CreateAssetMenu(
    menuName = "Wherever I Am/Enemies/Profiles/Enemy Patrol Config",
    fileName = "EnemyPatrolConfig"
)]
public class EnemyPatrolConfig : ScriptableObject
{
    [Min(0f)] public float patrolPointReachDistance = 0.4f;
    [Min(0f)] public float patrolStopDuration = 4f;
    [Min(0f)] public float patrolStopWanderRadius = 2f;
    [Min(0f)] public float patrolStopWanderSpeed = 1.2f;
    [Min(0f)] public float patrolStopWanderPointReachDistance = 0.35f;
    [Min(1)] public int patrolStopWanderSampleAttempts = 12;
    [Min(0f)] public float patrolStopWanderMinDistanceFromEnemy = 0.75f;

    public void Validate()
    {
        patrolPointReachDistance = Mathf.Max(0f, patrolPointReachDistance);
        patrolStopDuration = Mathf.Max(0f, patrolStopDuration);
        patrolStopWanderRadius = Mathf.Max(0f, patrolStopWanderRadius);
        patrolStopWanderSpeed = Mathf.Max(0f, patrolStopWanderSpeed);
        patrolStopWanderPointReachDistance = Mathf.Max(0f, patrolStopWanderPointReachDistance);
        patrolStopWanderSampleAttempts = Mathf.Max(1, patrolStopWanderSampleAttempts);
        patrolStopWanderMinDistanceFromEnemy = Mathf.Max(0f, patrolStopWanderMinDistanceFromEnemy);
    }

    private void OnValidate()
    {
        Validate();
    }
}