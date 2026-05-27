using UnityEngine;

[CreateAssetMenu(
    menuName = "Wherever I Am/Enemies/Combat/Debug Attack Effect",
    fileName = "EnemyDebugAttackEffect"
)]
public sealed class EnemyDebugAttackEffect : EnemyAttackEffect
{
    public override bool TryApply(EnemyAttackContext context)
    {
        if (!context.IsValid)
        {
            return false;
        }

        Debug.Log(
            $"Enemy attack effect applied to target {context.TargetDebugName}.",
            context.Source
        );

        return true;
    }
}
