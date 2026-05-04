public interface IEnemyAttackReceiver
{
    bool TryReceiveEnemyAttack(EnemyAttackContext context);
}