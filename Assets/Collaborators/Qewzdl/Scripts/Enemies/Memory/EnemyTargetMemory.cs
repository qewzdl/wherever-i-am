public sealed class EnemyTargetMemory
{
    public const ulong NoTargetClientId = ulong.MaxValue;

    public EnemyTarget CurrentTarget { get; private set; }
    public EnemyTargetIdentity CurrentTargetIdentity { get; private set; } = EnemyTargetIdentity.None;

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

    public void SetTarget(EnemyTarget target)
    {
        CurrentTarget = target;
        CurrentTargetIdentity = EnemyTargetIdentity.FromTarget(target);
    }

    public void RefreshConfirmedTarget(EnemyTarget target)
    {
        if (target == null)
        {
            ClearTargetOnly();
            return;
        }

        if (CurrentTarget == target && CurrentTargetIdentity.HasTarget)
        {
            return;
        }

        SetTarget(target);
    }

    public void ClearTargetOnly()
    {
        CurrentTarget = null;
        CurrentTargetIdentity = EnemyTargetIdentity.None;
    }

    public void ForgetCurrentTargetButKeepLastKnownPosition()
    {
        ClearTargetOnly();
    }

    public void ClearAll()
    {
        ClearTargetOnly();
    }
}