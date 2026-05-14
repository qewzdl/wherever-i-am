using UnityEngine;

public sealed class EnemyInvestigateState : IEnemyStateHandler
{
    private readonly EnemyBrainContext context;

    private Vector3 currentInvestigationPosition;
    private bool hasInvestigationPosition;
    private bool isWaitingAtInvestigationPoint;
    private float waitTimer;
    private float repathTimer;

    public EnemyState State => EnemyState.Investigate;

    public EnemyInvestigateState(EnemyBrainContext context)
    {
        this.context = context;
    }

    public void Enter()
    {
        ResetRuntimeState();

        if (!TryResolveInvestigationPosition(out currentInvestigationPosition))
        {
            FinishInvestigation();
            return;
        }

        hasInvestigationPosition = true;
        MoveToInvestigationPosition(forceRepath: true);
    }

    public void Tick(float deltaTime)
    {
        if (context.TargetMemory.HasTarget)
        {
            context.ChangeState(EnemyState.Chase);
            return;
        }

        if (!hasInvestigationPosition)
        {
            if (!TryResolveInvestigationPosition(out currentInvestigationPosition))
            {
                FinishInvestigation();
                return;
            }

            hasInvestigationPosition = true;
            MoveToInvestigationPosition(forceRepath: true);
        }

        if (isWaitingAtInvestigationPoint)
        {
            TickWaiting(deltaTime);
            return;
        }

        TickMoving(deltaTime);
    }

    public void Exit()
    {
        ResetRuntimeState();
    }

    private void TickMoving(float deltaTime)
    {
        repathTimer -= deltaTime;

        if (repathTimer <= 0f)
        {
            MoveToInvestigationPosition(forceRepath: true);
        }

        if (!context.Navigator.HasReached(context.Config.investigationReachDistance))
        {
            return;
        }

        StartWaitingAtInvestigationPoint();
    }

    private void TickWaiting(float deltaTime)
    {
        waitTimer -= deltaTime;

        context.Navigator.Stop();

        if (waitTimer > 0f)
        {
            return;
        }

        context.TargetMemory.ClearPrimaryInvestigationPosition();

        if (context.TargetMemory.PromoteSecondarySuspiciousPositionToLastKnown())
        {
            if (context.TargetMemory.TryGetLastKnownTargetPosition(out currentInvestigationPosition))
            {
                hasInvestigationPosition = true;
                isWaitingAtInvestigationPoint = false;
                MoveToInvestigationPosition(forceRepath: true);
                return;
            }
        }

        FinishInvestigation();
    }

    private bool TryResolveInvestigationPosition(out Vector3 position)
    {
        if (context.TargetMemory.TryGetLastKnownTargetPosition(out position))
        {
            return true;
        }

        if (context.TargetMemory.PromoteSecondarySuspiciousPositionToLastKnown())
        {
            return context.TargetMemory.TryGetLastKnownTargetPosition(out position);
        }

        position = default;
        return false;
    }

    private void MoveToInvestigationPosition(bool forceRepath)
    {
        if (!hasInvestigationPosition)
        {
            return;
        }

        if (!forceRepath && repathTimer > 0f)
        {
            return;
        }

        repathTimer = Mathf.Max(0.05f, context.Config.investigationRepathInterval);

        if (!context.Navigator.TryMoveTo(
            currentInvestigationPosition,
            context.Config.chaseSpeed
        ))
        {
            FinishInvestigation();
        }
    }

    private void StartWaitingAtInvestigationPoint()
    {
        isWaitingAtInvestigationPoint = true;
        waitTimer = context.Config.investigationWaitDuration;

        context.Navigator.ResetPath();
        context.Navigator.Stop();

        if (waitTimer <= 0f)
        {
            TickWaiting(0f);
        }
    }

    private void FinishInvestigation()
    {
        context.ClearAllTargetMemory();
        context.ReturnToDefaultBehaviour();
    }

    private void ResetRuntimeState()
    {
        currentInvestigationPosition = default;
        hasInvestigationPosition = false;
        isWaitingAtInvestigationPoint = false;
        waitTimer = 0f;
        repathTimer = 0f;
    }
}