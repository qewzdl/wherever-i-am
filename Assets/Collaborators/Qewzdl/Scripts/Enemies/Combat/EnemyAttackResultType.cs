public enum EnemyAttackResultType : byte
{
    None = 0,

    Started = 1,
    Busy = 2,
    CooldownActive = 3,

    InvalidTarget = 10,
    OutOfRange = 11,
    MissingEffect = 12,
    EffectRejected = 13,
    LineOfHitBlocked = 14,

    Hit = 20,
    Interrupted = 21
}