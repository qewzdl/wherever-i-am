using UnityEngine;

public sealed class EnemyTargetMemory
{
    public const ulong NoTargetClientId = ulong.MaxValue;

    public EnemyTarget CurrentTarget { get; private set; }
    public EnemyTargetIdentity CurrentTargetIdentity { get; private set; } = EnemyTargetIdentity.None;

    public Vector3 LastKnownTargetPosition { get; private set; }
    public bool HasLastKnownTargetPosition { get; private set; }

    public Vector3 SecondarySuspiciousPosition { get; private set; }
    public bool HasSecondarySuspiciousPosition { get; private set; }

    public bool IsUsingVisualMemory { get; private set; }
    public float VisualMemoryTimeRemaining { get; private set; }

    public bool HasTarget => CurrentTarget != null;
    public ulong CurrentTargetClientId => CurrentTargetIdentity.OwnerClientId;

    public bool IsCurrentTargetValid
    {
        get
        {
            return CurrentTarget != null &&
                   CurrentTarget.IsValidNetworkTarget &&
                   CurrentTargetIdentity.HasTarget;
        }
    }

    public void SetTarget(EnemyTarget target, Vector3 position)
    {
        CurrentTarget = target;
        CurrentTargetIdentity = EnemyTargetIdentity.FromTarget(target);
        RememberPosition(position);
        CancelVisualMemory();
    }

    public void RefreshConfirmedTarget(Vector3 position)
    {
        RememberPosition(position);
        CancelVisualMemory();
    }

    public void RememberPosition(Vector3 position)
    {
        LastKnownTargetPosition = position;
        HasLastKnownTargetPosition = true;
    }

    public void RememberSecondarySuspiciousPosition(Vector3 position)
    {
        SecondarySuspiciousPosition = position;
        HasSecondarySuspiciousPosition = true;
    }

    public bool TryGetLastKnownTargetPosition(out Vector3 position)
    {
        position = LastKnownTargetPosition;
        return HasLastKnownTargetPosition;
    }

    public bool TryGetSecondarySuspiciousPosition(out Vector3 position)
    {
        position = SecondarySuspiciousPosition;
        return HasSecondarySuspiciousPosition;
    }

    public void StartVisualMemoryGracePeriod(float duration)
    {
        if (!HasTarget || duration <= 0f)
        {
            return;
        }

        IsUsingVisualMemory = true;
        VisualMemoryTimeRemaining = Mathf.Max(VisualMemoryTimeRemaining, duration);
    }

    public bool TickVisualMemory(float deltaTime)
    {
        if (!IsUsingVisualMemory)
        {
            return HasTarget;
        }

        if (!IsCurrentTargetValid)
        {
            ForgetCurrentTargetButKeepLastKnownPosition();
            return false;
        }

        VisualMemoryTimeRemaining -= deltaTime;

        Vector3 targetPosition = GetCurrentTargetPosition();
        RememberPosition(targetPosition);

        if (VisualMemoryTimeRemaining > 0f)
        {
            return true;
        }

        ForgetCurrentTargetButKeepLastKnownPosition();
        return false;
    }

    public Vector3 GetCurrentTargetPosition()
    {
        if (CurrentTarget == null)
        {
            return LastKnownTargetPosition;
        }

        if (CurrentTarget.NetworkObject != null && CurrentTarget.NetworkObject.IsSpawned)
        {
            return CurrentTarget.NetworkObject.transform.position;
        }

        return CurrentTarget.transform.position;
    }

    public bool PromoteSecondarySuspiciousPositionToLastKnown()
    {
        if (!HasSecondarySuspiciousPosition)
        {
            return false;
        }

        LastKnownTargetPosition = SecondarySuspiciousPosition;
        HasLastKnownTargetPosition = true;

        ClearSecondarySuspiciousPosition();
        return true;
    }

    public void ClearSecondarySuspiciousPosition()
    {
        SecondarySuspiciousPosition = default;
        HasSecondarySuspiciousPosition = false;
    }

    public void ClearTargetOnly()
    {
        CurrentTarget = null;
        CurrentTargetIdentity = EnemyTargetIdentity.None;
        CancelVisualMemory();
    }

    public void ForgetCurrentTargetButKeepLastKnownPosition()
    {
        ClearTargetOnly();
    }

    public void CancelVisualMemory()
    {
        IsUsingVisualMemory = false;
        VisualMemoryTimeRemaining = 0f;
    }

    public void ClearPrimaryInvestigationPosition()
    {
        LastKnownTargetPosition = default;
        HasLastKnownTargetPosition = false;
    }

    public void ClearAll()
    {
        ClearTargetOnly();
        ClearPrimaryInvestigationPosition();
        ClearSecondarySuspiciousPosition();
    }
}