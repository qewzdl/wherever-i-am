using UnityEngine;

public sealed class EnemyAttackCooldown
{
    private float cooldownTimer;

    public bool IsActive => cooldownTimer > 0f;

    public void Tick(float deltaTime)
    {
        if (cooldownTimer <= 0f)
        {
            return;
        }

        cooldownTimer = Mathf.Max(0f, cooldownTimer - deltaTime);
    }

    public void Start(float duration)
    {
        cooldownTimer = Mathf.Max(0f, duration);
    }

    public void Reset()
    {
        cooldownTimer = 0f;
    }
}