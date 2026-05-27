public enum EnemyAttackPhase : byte
{
    Idle = 0,
    AttackWindup = 1,
    AttackCommit = 2,
    AttackRecovery = 3,
    AttackInterrupted = 4
}