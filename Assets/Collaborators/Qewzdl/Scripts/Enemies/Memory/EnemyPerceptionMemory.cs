using UnityEngine;

public sealed class EnemyPerceptionMemory
{
    private EnemyTarget visualMemoryTarget;

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

    public bool TryStartVisualMemoryGracePeriod(EnemyTarget target, float duration)
    {
        if (target == null || duration <= 0f)
        {
            return false;
        }

        visualMemoryTarget = target;
        IsUsingVisualMemory = true;
        VisualMemoryTimeRemaining = duration;

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

    private Vector3 GetTargetPosition(EnemyTarget target)
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