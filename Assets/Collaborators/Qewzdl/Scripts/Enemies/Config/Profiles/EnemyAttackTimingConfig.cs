using UnityEngine;

[CreateAssetMenu(
    menuName = "Wherever I Am/Enemies/Profiles/Enemy Attack Timing Config",
    fileName = "EnemyAttackTimingConfig"
)]
public class EnemyAttackTimingConfig : ScriptableObject
{
    [Header("Cooldown")]
    [Min(0f)] public float attackCooldown = 1.5f;

    [Header("Phases")]
    [Tooltip("Delay before the actual attack result is resolved. Player can dodge during this window.")]
    [Min(0f)] public float attackWindupDuration = 0.35f;

    [Tooltip("Small presentation window for the actual hit/swing moment.")]
    [Min(0f)] public float attackCommitDuration = 0.1f;

    [Tooltip("Time before the enemy can fully return to chase/next attack flow.")]
    [Min(0f)] public float attackRecoveryDuration = 0.55f;

    [Tooltip("Small phase used for interrupted attack feedback before recovery.")]
    [Min(0f)] public float attackInterruptedDuration = 0.15f;

    public void Validate()
    {
        attackCooldown = Mathf.Max(0f, attackCooldown);
        attackWindupDuration = Mathf.Max(0f, attackWindupDuration);
        attackCommitDuration = Mathf.Max(0f, attackCommitDuration);
        attackRecoveryDuration = Mathf.Max(0f, attackRecoveryDuration);
        attackInterruptedDuration = Mathf.Max(0f, attackInterruptedDuration);
    }

    private void OnValidate()
    {
        Validate();
    }
}