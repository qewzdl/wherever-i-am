using UnityEngine;

public readonly struct EnemyDoorNavigationResult
{
    private EnemyDoorNavigationResult(
        bool isHandled,
        bool shouldStop,
        bool hasOverrideDestination,
        Vector3 overrideDestination
    )
    {
        IsHandled = isHandled;
        ShouldStop = shouldStop;
        HasOverrideDestination = hasOverrideDestination;
        OverrideDestination = overrideDestination;
    }

    public bool IsHandled { get; }
    public bool ShouldStop { get; }
    public bool HasOverrideDestination { get; }
    public Vector3 OverrideDestination { get; }

    public static EnemyDoorNavigationResult None => default;

    public static EnemyDoorNavigationResult Stop()
    {
        return new EnemyDoorNavigationResult(true, true, false, default);
    }

    public static EnemyDoorNavigationResult MoveTo(Vector3 destination)
    {
        return new EnemyDoorNavigationResult(true, false, true, destination);
    }
}
