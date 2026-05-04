using UnityEngine;

public abstract class EnemyAttackEffect : ScriptableObject, IEnemyAttackEffect
{
    public abstract bool TryApply(EnemyAttackContext context);
}