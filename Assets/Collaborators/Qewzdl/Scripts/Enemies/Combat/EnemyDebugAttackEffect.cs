using UnityEngine;

[CreateAssetMenu(
    menuName = "Wherever I Am/Enemies/Combat/Debug Attack Effect",
    fileName = "EnemyDebugAttackEffect"
)]
public sealed class EnemyDebugAttackEffect : EnemyAttackEffect
{
    public override bool TryApply(EnemyAttackContext context)
    {
        if (!RuntimeDebugBuildGuard.IsEnabled)
        {
            return false;
        }

        if (!context.IsValid)
        {
            return false;
        }

        RuntimeLog.Info(
            $"Enemy attack effect applied to target {context.TargetDebugName}.",
            context.Source
        );

        return true;
    }
}
