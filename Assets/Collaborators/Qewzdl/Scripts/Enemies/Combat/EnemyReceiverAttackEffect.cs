using UnityEngine;

[CreateAssetMenu(
    menuName = "Game/Enemies/Combat/Receiver Attack Effect",
    fileName = "EnemyReceiverAttackEffect"
)]
public sealed class EnemyReceiverAttackEffect : EnemyAttackEffect
{
    [SerializeField] private bool logMissingReceiver = true;

    public override bool TryApply(EnemyAttackContext context)
    {
        if (!context.IsValid)
        {
            return false;
        }

        IEnemyAttackReceiver receiver = context.Target.GetComponentInParent<IEnemyAttackReceiver>();

        if (receiver == null)
        {
            if (logMissingReceiver)
            {
                Debug.LogWarning(
                    $"Enemy attacked target {context.TargetDebugName}, " +
                    $"but target has no {nameof(IEnemyAttackReceiver)}.",
                    context.Source
                );
            }

            return false;
        }

        return receiver.TryReceiveEnemyAttack(context);
    }
}