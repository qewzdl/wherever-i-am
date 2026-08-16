using UnityEngine;

public sealed class EnemyPerceptionMemory
{
    private EnemyTarget visualMemoryTarget;
    private Vector3 visualMemoryFrozenPosition;
    private bool visualMemoryTracksLiveTarget = true;

    public EnemyPerceptionStimulus CurrentStimulus { get; private set; } = EnemyPerceptionStimulus.None;

    public float LastVisibleTime { get; private set; } = -1f;
    public float LastHeardTime { get; private set; } = -1f;

    public bool IsUsingVisualMemory { get; private set; }
    public float VisualMemoryTimeRemaining { get; private set; }

    public void SetCurrentStimulus(EnemyPerceptionStimulus stimulus, float serverTime)
    {
        CurrentStimulus = stimulus;

        if (!stimulus.HasStimulus)
        {
            return;
        }

        if (stimulus.Source == EnemyPerceptionSource.Vision)
        {
            LastVisibleTime = serverTime;
            return;
        }

        if (stimulus.Source == EnemyPerceptionSource.Hearing)
        {
            LastHeardTime = serverTime;
        }
    }

    public void ClearCurrentStimulus()
    {
        CurrentStimulus = EnemyPerceptionStimulus.None;
    }

    // trackLiveTargetPosition decides what the grace period is worth: following
    // the target through walls, or holding the spot where it was last seen.
    // See GetTargetPosition for which one the game is built around.
    public bool TryStartVisualMemoryGracePeriod(
        EnemyTarget target,
        float duration,
        bool trackLiveTargetPosition = true
    )
    {
        if (target == null || duration <= 0f)
        {
            return false;
        }

        visualMemoryTracksLiveTarget = trackLiveTargetPosition;

        if (IsUsingVisualMemory && visualMemoryTarget == target)
        {
            return true;
        }

        visualMemoryTarget = target;
        IsUsingVisualMemory = true;
        VisualMemoryTimeRemaining = duration;

        // Taken once, here, because this is the moment sight was lost. Sampling
        // it later would just be the live position under another name.
        visualMemoryFrozenPosition = GetLiveTargetPosition(target);

        return true;
    }

    public bool TickVisualMemory(
        float deltaTime,
        out EnemyTarget rememberedTarget,
        out Vector3 rememberedPosition,
        out bool hasRememberedPosition
    )
    {
        rememberedTarget = visualMemoryTarget;
        rememberedPosition = default;
        hasRememberedPosition = false;

        if (!IsUsingVisualMemory)
        {
            return rememberedTarget != null;
        }

        if (!IsTargetValid(visualMemoryTarget))
        {
            CancelVisualMemory();
            return false;
        }

        VisualMemoryTimeRemaining -= deltaTime;

        rememberedPosition = GetTargetPosition(visualMemoryTarget);
        hasRememberedPosition = true;

        if (VisualMemoryTimeRemaining > 0f)
        {
            return true;
        }

        CancelVisualMemory();
        return false;
    }

    public Vector3 GetVisualMemoryTargetPosition()
    {
        if (visualMemoryTarget == null)
        {
            return default;
        }

        return GetTargetPosition(visualMemoryTarget);
    }

    public void CancelVisualMemory()
    {
        visualMemoryTarget = null;
        IsUsingVisualMemory = false;
        VisualMemoryTimeRemaining = 0f;
        visualMemoryFrozenPosition = default;
    }

    public void ClearAll()
    {
        ClearCurrentStimulus();
        CancelVisualMemory();

        LastVisibleTime = -1f;
        LastHeardTime = -1f;
    }

    private bool IsTargetValid(EnemyTarget target)
    {
        return target != null && target.IsValidNetworkTarget;
    }

    // By default this hands out the target's live position, not the position
    // where it was last seen. For visualTargetMemoryDuration after line of
    // sight breaks, Chase and Attack therefore track the target exactly,
    // through walls. That is the intended feel - the grace period doubles as
    // the window in which the enemy cannot be shaken off - and it is still the
    // default, so turning it off changes how hard the enemy is to lose.
    //
    // visualMemoryTracksLiveTarget on EnemyVisionConfig switches to the honest
    // reading: hold the point where sight broke and let the player leave it.
    // The length of either behaviour is visualTargetMemoryDuration.
    private Vector3 GetTargetPosition(EnemyTarget target)
    {
        if (target == null)
        {
            return default;
        }

        if (!visualMemoryTracksLiveTarget)
        {
            return visualMemoryFrozenPosition;
        }

        return GetLiveTargetPosition(target);
    }

    private static Vector3 GetLiveTargetPosition(EnemyTarget target)
    {
        if (target == null)
        {
            return default;
        }

        if (target.NetworkObject != null && target.NetworkObject.IsSpawned)
        {
            return target.NetworkObject.transform.position;
        }

        return target.transform.position;
    }
}