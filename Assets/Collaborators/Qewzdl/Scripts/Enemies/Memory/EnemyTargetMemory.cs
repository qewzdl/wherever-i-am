using UnityEngine;

public sealed class EnemyTargetMemory
{
    public const ulong NoTargetClientId = EnemyTargetIdentity.NoTargetClientId;

    public EnemyTarget CurrentTarget { get; private set; }
    public EnemyTargetIdentity CurrentTargetIdentity { get; private set; } = EnemyTargetIdentity.None;
    public EnemyTargetObservation LastObservation { get; private set; }

    public bool HasTarget => CurrentTarget != null;
    public ulong CurrentTargetClientId => CurrentTargetIdentity.OwnerClientId;
    public bool HasLastObservation => LastObservation.IsValid;

    public bool IsCurrentTargetValid
    {
        get
        {
            return CurrentTarget != null &&
                   CurrentTarget.CanBeDetected &&
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

    public void RememberObservation(
        EnemyTarget target,
        Vector3 position,
        Vector3 forward,
        float serverTime
    )
    {
        if (target == null)
        {
            return;
        }

        Vector3 flatForward = Vector3.ProjectOnPlane(forward, Vector3.up);

        if (flatForward.sqrMagnitude <= 0.001f)
        {
            flatForward = Vector3.forward;
        }

        LastObservation = new EnemyTargetObservation(
            target,
            EnemyTargetIdentity.FromTarget(target),
            position,
            flatForward.normalized,
            serverTime
        );
    }

    public bool TryGetLastObservation(out EnemyTargetObservation observation)
    {
        observation = LastObservation;
        return observation.IsValid;
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
        LastObservation = default;
    }
}

public readonly struct EnemyTargetObservation
{
    public EnemyTarget Target { get; }
    public EnemyTargetIdentity TargetIdentity { get; }
    public Vector3 Position { get; }
    public Vector3 Forward { get; }
    public float ServerTime { get; }
    public bool IsValid { get; }

    public EnemyTargetObservation(
        EnemyTarget target,
        EnemyTargetIdentity targetIdentity,
        Vector3 position,
        Vector3 forward,
        float serverTime
    )
    {
        Target = target;
        TargetIdentity = targetIdentity;
        Position = position;
        Forward = forward;
        ServerTime = serverTime;
        // Keep the pose usable if the observed NetworkObject despawns later.
        // Unity's destroyed-object null semantics must not invalidate the
        // server's last honest observation and send the state to another
        // player.
        IsValid = target != null;
    }
}
