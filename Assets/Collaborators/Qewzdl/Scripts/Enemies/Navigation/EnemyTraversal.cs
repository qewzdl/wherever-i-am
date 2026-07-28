using UnityEngine;

internal enum EnemyTraversalKind
{
    Door = 0,
    Posture = 1,
    PushableBarrier = 2
}

internal enum EnemyTraversalStatus
{
    None = 0,
    MovingToEntry = 1,
    Waiting = 2,
    Traversing = 3,
    Completed = 4,
    Failed = 5
}

internal readonly struct EnemyTraversalPlan
{
    public EnemyTraversalKind Kind { get; }
    public EnemyTraversalStatus Status { get; }
    public Vector3 Entry { get; }
    public Vector3 Exit { get; }

    public EnemyTraversalPlan(
        EnemyTraversalKind kind,
        EnemyTraversalStatus status,
        Vector3 entry,
        Vector3 exit)
    {
        Kind = kind;
        Status = status;
        Entry = entry;
        Exit = exit;
    }
}

internal interface IEnemyTraversalHandler
{
    EnemyTraversalKind Kind { get; }
    bool IsActive { get; }
    void Cancel();
}
