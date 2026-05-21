using UnityEngine;

[CreateAssetMenu(
    menuName = "Wherever I Am/Enemies/Profiles/Enemy Attack Config",
    fileName = "EnemyAttackConfig"
)]
public class EnemyAttackConfig : ScriptableObject
{
    [Header("Range")]
    [Min(0f)] public float attackDistance = 1.6f;

    [Tooltip("Extra distance allowed only at commit validation. Useful for fair but not pixel-perfect melee hits.")]
    [Min(0f)] public float attackCommitDistanceTolerance = 0.2f;

    [Header("Line Of Hit")]
    [Tooltip("Server-side obstruction validation at attack commit. Disable only for attacks that intentionally ignore geometry.")]
    public bool validateLineOfHit = true;

    [Tooltip("Layers that can block a melee hit between enemy and target. Assign solid world/door/wall layers.")]
    public LayerMask attackLineOfHitBlockingMask = ~0;

    [Tooltip("World-space vertical offset from enemy position used as melee hit origin.")]
    [Min(0f)] public float attackLineOfHitOriginHeight = 1.2f;

    [Tooltip("Trigger handling for melee obstruction raycasts.")]
    public QueryTriggerInteraction attackLineOfHitTriggerInteraction = QueryTriggerInteraction.Ignore;

    [Header("Timing")]
    [Min(0f)] public float attackCooldown = 1.5f;

    [Tooltip("Delay before the actual attack result is resolved. Player can dodge during this window.")]
    [Min(0f)] public float attackWindupDuration = 0.35f;

    [Tooltip("Small presentation window for the actual hit/swing moment.")]
    [Min(0f)] public float attackCommitDuration = 0.1f;

    [Tooltip("Time before the enemy can fully return to chase/next attack flow.")]
    [Min(0f)] public float attackRecoveryDuration = 0.55f;

    [Tooltip("Small phase used for interrupted attack feedback before recovery.")]
    [Min(0f)] public float attackInterruptedDuration = 0.15f;

    public float CommitMaxDistance => attackDistance + attackCommitDistanceTolerance;

    public void Validate(float stoppingDistance = 0f)
    {
        attackDistance = Mathf.Max(attackDistance, stoppingDistance);
        attackCommitDistanceTolerance = Mathf.Max(0f, attackCommitDistanceTolerance);

        attackLineOfHitOriginHeight = Mathf.Max(0f, attackLineOfHitOriginHeight);

        if (validateLineOfHit && attackLineOfHitBlockingMask.value == 0)
        {
            Debug.LogError(
                $"{nameof(EnemyAttackConfig)} '{name}' has enabled " +
                $"{nameof(validateLineOfHit)} but empty {nameof(attackLineOfHitBlockingMask)}. " +
                "Assign solid world/door/wall layers or disable line-of-hit validation intentionally.",
                this
            );
        }

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