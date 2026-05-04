using UnityEngine;

[CreateAssetMenu(
    menuName = "Game/Enemies/Combat/Debug Attack Effect",
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
            $"Enemy attack effect applied to client {context.TargetClientId}.",
            context.Source
        );

        return true;
    }
}