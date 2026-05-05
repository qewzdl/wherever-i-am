using Unity.Netcode;
using UnityEngine;

[DisallowMultipleComponent]
public class EnemyAttackController : MonoBehaviour
{
    [SerializeField] private EnemyAttackEffect attackEffect;

    [Tooltip("If enabled, failed attack effects still consume cooldown. Keep disabled for most gameplay cases.")]
    [SerializeField] private bool consumeCooldownOnFailedEffect;

    private float cooldownTimer;
    private bool warnedAboutMissingAttackEffect;

    public void Tick(float deltaTime)
    {
        if (cooldownTimer <= 0f)
        {
            return;
        }

        cooldownTimer = Mathf.Max(0f, cooldownTimer - deltaTime);
    }

    public bool TryAttack(
        EnemyTarget target,
        EnemyConfig config,
        Vector3 attackerPosition,
        Component logContext
    )
    {
        if (cooldownTimer > 0f || target == null || config == null)
        {
            return false;
        }

        NetworkObject targetNetworkObject = target.NetworkObject;

        if (targetNetworkObject == null || !targetNetworkObject.IsSpawned)
        {
            return false;
        }

        EnemyTargetIdentity targetIdentity = EnemyTargetIdentity.FromNetworkObject(targetNetworkObject);

        if (!targetIdentity.HasTarget)
        {
            return false;
        }

        float distanceToTarget = Vector3.Distance(
            attackerPosition,
            targetNetworkObject.transform.position
        );

        if (distanceToTarget > config.attackDistance)
        {
            return false;
        }

        EnemyAttackContext context = new EnemyAttackContext(
            target,
            targetIdentity,
            targetNetworkObject,
            config,
            attackerPosition,
            logContext != null ? logContext : this
        );

        if (attackEffect == null)
        {
            WarnAboutMissingAttackEffect(context);
            return false;
        }

        bool attackApplied = attackEffect.TryApply(context);

        if (!attackApplied)
        {
            if (consumeCooldownOnFailedEffect)
            {
                StartCooldown(config);
            }

            return false;
        }

        StartCooldown(config);
        return true;
    }

    public void ResetCooldown()
    {
        cooldownTimer = 0f;
    }

    private void StartCooldown(EnemyConfig config)
    {
        if (config == null)
        {
            return;
        }

        cooldownTimer = config.attackCooldown;
    }

    private void WarnAboutMissingAttackEffect(EnemyAttackContext context)
    {
        if (warnedAboutMissingAttackEffect)
        {
            return;
        }

        warnedAboutMissingAttackEffect = true;

        Debug.LogWarning(
            $"{nameof(EnemyAttackController)} has no {nameof(EnemyAttackEffect)} assigned. " +
            $"Attack against target {context.TargetDebugName} was validated but no result was applied.",
            this
        );
    }
}