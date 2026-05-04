using Unity.Netcode;
using UnityEngine;

[DisallowMultipleComponent]
public class EnemyAttackController : MonoBehaviour
{
    private float cooldownTimer;

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

        Debug.Log($"Enemy attacked client {targetNetworkObject.OwnerClientId}.", logContext);

        // Future extension point:
        // 1. Apply server-side caught/damage state.
        // 2. Trigger ClientRpc for one-shot feedback.
        // 3. Route attack result into player health, death, capture, or scare systems.

        return true;
    }

    public void ResetCooldown()
    {
        cooldownTimer = 0f;
    }
}