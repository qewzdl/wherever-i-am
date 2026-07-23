using UnityEngine;

[CreateAssetMenu(
    menuName = "Wherever I Am/Enemies/Profiles/Enemy Movement Config",
    fileName = "EnemyMovementConfig"
)]
public class EnemyMovementConfig : ScriptableObject
{
    [Min(0f)] public float patrolSpeed = 1.6f;
    [Min(0f)] public float chaseSpeed = 2.8f;
    [Min(0f)] public float acceleration = 12f;
    [Min(0f)] public float angularSpeed = 360f;
    [Min(0f)] public float stoppingDistance = 0.2f;

    public void Validate()
    {
        patrolSpeed = Mathf.Max(0f, patrolSpeed);
        chaseSpeed = Mathf.Max(0f, chaseSpeed);
        acceleration = Mathf.Max(0f, acceleration);
        angularSpeed = Mathf.Max(0f, angularSpeed);
        stoppingDistance = Mathf.Max(0f, stoppingDistance);
    }

    private void OnValidate()
    {
        Validate();
    }
}
