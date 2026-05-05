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