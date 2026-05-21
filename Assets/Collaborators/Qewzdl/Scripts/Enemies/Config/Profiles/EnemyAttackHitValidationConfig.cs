using UnityEngine;

[CreateAssetMenu(
    menuName = "Wherever I Am/Enemies/Profiles/Enemy Attack Hit Validation Config",
    fileName = "EnemyAttackHitValidationConfig"
)]
public class EnemyAttackHitValidationConfig : ScriptableObject
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

    public float CommitMaxDistance => attackDistance + attackCommitDistanceTolerance;

    public void Validate(float stoppingDistance = 0f)
    {
        attackDistance = Mathf.Max(attackDistance, stoppingDistance);
        attackCommitDistanceTolerance = Mathf.Max(0f, attackCommitDistanceTolerance);
        attackLineOfHitOriginHeight = Mathf.Max(0f, attackLineOfHitOriginHeight);

        if (validateLineOfHit && attackLineOfHitBlockingMask.value == 0)
        {
            Debug.LogError(
                $"{nameof(EnemyAttackHitValidationConfig)} '{name}' has enabled " +
                $"{nameof(validateLineOfHit)} but empty {nameof(attackLineOfHitBlockingMask)}. " +
                "Assign solid world/door/wall layers or disable line-of-hit validation intentionally.",
                this
            );
        }
    }

    private void OnValidate()
    {
        Validate();
    }
}