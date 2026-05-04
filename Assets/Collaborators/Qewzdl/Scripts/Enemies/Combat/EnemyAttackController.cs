using Unity.Netcode;
using UnityEngine;

[DisallowMultipleComponent]
public class EnemyAttackController : MonoBehaviour
{
    [SerializeField] private EnemyAttackEffect attackEffect;

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

        float distanceToTarget = Vector3.Distance(
            attackerPosition,
            targetNetworkObject.transform.position
        );

        if (distanceToTarget > config.attackDistance)
        {
            return false;
        }

        cooldownTimer = config.attackCooldown;

        EnemyAttackContext context = new EnemyAttackContext(
            target,
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

        return attackEffect.TryApply(context);
    }

    public void ResetCooldown()
    {
        cooldownTimer = 0f;
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
            $"Attack against client {context.TargetClientId} was validated but no result was applied.",
            this
        );
    }
}