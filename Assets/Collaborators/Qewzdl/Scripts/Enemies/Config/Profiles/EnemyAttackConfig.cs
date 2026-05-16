using UnityEngine;

[CreateAssetMenu(
    menuName = "Wherever I Am/Enemies/Profiles/Enemy Attack Config",
    fileName = "EnemyAttackConfig"
)]
public class EnemyAttackConfig : ScriptableObject
{
    [Min(0f)] public float attackDistance = 1.6f;
    [Min(0f)] public float attackCooldown = 1.5f;

    public void Validate(float stoppingDistance = 0f)
    {
        attackDistance = Mathf.Max(attackDistance, stoppingDistance);
        attackCooldown = Mathf.Max(0f, attackCooldown);
    }

    private void OnValidate()
    {
        Validate();
    }
}