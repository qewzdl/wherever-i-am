using System;
using UnityEngine;

public sealed class EnemyPerceptionRuntime
{
    private readonly EnemyConfig config;
    private readonly EnemyTargetDetector targetDetector;
    private readonly bool usesTargetDetection;
    private readonly EnemyBlackboard blackboard;
    private readonly Action<EnemyTargetIdentity> setTargetIdentity;

    private float targetRefreshTimer;

    public EnemyPerceptionRuntime(
        EnemyConfig config,
        EnemyTargetDetector targetDetector,
        bool usesTargetDetection,
        EnemyBlackboard blackboard,
        Action<EnemyTargetIdentity> setTargetIdentity
    )
    {
        if (usesTargetDetection && targetDetector == null)
        {
            throw new ArgumentNullException(
                nameof(targetDetector),
                $"{nameof(EnemyPerceptionRuntime)} requires {nameof(EnemyTargetDetector)} when target detection is enabled."
            );
        }

        this.config = config;
        this.targetDetector = targetDetector;
        this.usesTargetDetection = usesTargetDetection;
        this.blackboard = blackboard ?? throw new ArgumentNullException(
            nameof(blackboard),
            $"{nameof(EnemyPerceptionRuntime)} requires non-null {nameof(EnemyBlackboard)}."
        );
        this.setTargetIdentity = setTargetIdentity;
    }

    public EnemyPerceptionDecision Tick(float deltaTime, EnemyState currentState)
    {
        deltaTime = Mathf.Max(0f, deltaTime);

        EnemyPerceptionDecision visualMemoryDecision = TickVisualTargetMemory(
            deltaTime,
            currentState
        );

        if (visualMemoryDecision.HasDecision)
        {
            return visualMemoryDecision;
        }

        if (!usesTargetDetection)
        {
            return EnemyPerceptionDecision.None;
        }

        return TickTargetRefresh(deltaTime, currentState);
    }

    public void ResetRuntimeState()
    {
        targetRefreshTimer = 0f;
    }

    private EnemyPerceptionDecision TickVisualTargetMemory(
        float deltaTime,
        EnemyState currentState
    )
    {
        EnemyPerceptionMemory perceptionMemory = blackboard.PerceptionMemory;

        if (!perceptionMemory.IsUsingVisualMemory)
        {
            return EnemyPerceptionDecision.None;
        }

        bool stillHasTarget = perceptionMemory.TickVisualMemory(
            deltaTime,
            out EnemyTarget rememberedTarget,
            out Vector3 rememberedPosition,
            out bool hasRememberedPosition
        );

        if (hasRememberedPosition)
        {
            blackboard.InvestigationMemory.RememberLastKnownTargetPosition(rememberedPosition);
        }

        if (stillHasTarget && rememberedTarget != null)
        {
            blackboard.SetCurrentStimulus(
                EnemyPerceptionStimulus.ForConfirmedTarget(
                    rememberedTarget,
                    rememberedPosition,
                    1f,
                    EnemyPerceptionSource.Vision
                ),
                Time.time
            );

            return EnemyPerceptionDecision.None;
        }

        blackboard.TargetMemory.ForgetCurrentTargetButKeepLastKnownPosition();
        blackboard.ClearCurrentStimulus();
        SyncTarget();

        if (currentState == EnemyState.Chase || currentState == EnemyState.Attack)
        {
            return EnemyPerceptionDecision.SuspiciousPosition();
        }

        return EnemyPerceptionDecision.None;
    }

    private EnemyPerceptionDecision TickTargetRefresh(
        float deltaTime,
        EnemyState currentState
    )
    {
        targetRefreshTimer -= deltaTime;

        if (targetRefreshTimer > 0f)
        {
            return EnemyPerceptionDecision.None;
        }

        targetRefreshTimer = Mathf.Max(0.05f, config.targetRefreshInterval);
        return RefreshTarget(currentState);
    }

    private EnemyPerceptionDecision RefreshTarget(EnemyState currentState)
    {
        if (!targetDetector.TryResolveBestStimulus(
                config,
                blackboard,
                currentState,
                out EnemyStimulusResolution resolution
            ))
        {
            return HandleNoStimulus(currentState);
        }

        return ApplyStimulusResolution(resolution, currentState);
    }

    private EnemyPerceptionDecision ApplyStimulusResolution(
        EnemyStimulusResolution resolution,
        EnemyState currentState
    )
    {
        if (!resolution.HasResolution)
        {
            return HandleNoStimulus(currentState);
        }

        blackboard.SetCurrentStimulus(resolution.PrimaryStimulus, Time.time);

        switch (resolution.Action)
        {
            case EnemyStimulusResolutionAction.ChaseConfirmedTarget:
                EnemyPerceptionDecision confirmedTargetDecision = ApplyConfirmedTargetStimulus(
                    resolution.PrimaryStimulus
                );

                if (resolution.HasSecondaryStimulus)
                {
                    RememberSecondarySuspiciousStimulus(resolution.SecondaryStimulus);
                }

                return confirmedTargetDecision;

            case EnemyStimulusResolutionAction.InvestigateSuspiciousPosition:
                return ApplySuspiciousStimulus(
                    resolution.PrimaryStimulus,
                    resolution.ShouldClearCurrentTarget,
                    currentState
                );

            case EnemyStimulusResolutionAction.RememberSecondarySuspicion:
                RememberSecondarySuspiciousStimulus(resolution.PrimaryStimulus);
                return EnemyPerceptionDecision.None;

            default:
                return HandleNoStimulus(currentState);
        }
    }

    private void RememberSecondarySuspiciousStimulus(EnemyPerceptionStimulus stimulus)
    {
        if (!stimulus.HasStimulus)
        {
            return;
        }

        blackboard.InvestigationMemory.RememberSuspiciousPosition(stimulus.Position);
    }

    private EnemyPerceptionDecision ApplyConfirmedTargetStimulus(
        EnemyPerceptionStimulus stimulus
    )
    {
        EnemyTargetMemory targetMemory = blackboard.TargetMemory;
        EnemyPerceptionMemory perceptionMemory = blackboard.PerceptionMemory;
        EnemyInvestigationMemory investigationMemory = blackboard.InvestigationMemory;

        if (targetMemory.HasTarget && targetMemory.CurrentTarget == stimulus.Target)
        {
            targetMemory.RefreshConfirmedTarget(stimulus.Target);
        }
        else
        {
            targetMemory.SetTarget(stimulus.Target);
        }

        investigationMemory.RememberLastKnownTargetPosition(stimulus.Position);
        investigationMemory.ClearSuspiciousPosition();

        perceptionMemory.CancelVisualMemory();

        SyncTarget();

        return EnemyPerceptionDecision.ConfirmedTarget();
    }

    private EnemyPerceptionDecision ApplySuspiciousStimulus(
        EnemyPerceptionStimulus stimulus,
        bool forceInvestigate,
        EnemyState currentState
    )
    {
        if (!stimulus.HasStimulus)
        {
            return EnemyPerceptionDecision.None;
        }

        EnemyTargetMemory targetMemory = blackboard.TargetMemory;
        EnemyPerceptionMemory perceptionMemory = blackboard.PerceptionMemory;
        EnemyInvestigationMemory investigationMemory = blackboard.InvestigationMemory;

        if (!forceInvestigate && IsPursuingConfirmedTarget(currentState))
        {
            investigationMemory.RememberSuspiciousPosition(stimulus.Position);
            return EnemyPerceptionDecision.None;
        }

        if (!forceInvestigate && targetMemory.HasTarget)
        {
            perceptionMemory.TryStartVisualMemoryGracePeriod(
                targetMemory.CurrentTarget,
                config.visualTargetMemoryDuration
            );

            return EnemyPerceptionDecision.None;
        }

        if (forceInvestigate && targetMemory.HasTarget)
        {
            targetMemory.ForgetCurrentTargetButKeepLastKnownPosition();
            perceptionMemory.CancelVisualMemory();
            SyncTarget();
        }

        investigationMemory.RememberLastKnownTargetPosition(stimulus.Position);

        return EnemyPerceptionDecision.SuspiciousPosition();
    }

    private bool IsPursuingConfirmedTarget(EnemyState currentState)
    {
        EnemyTargetMemory targetMemory = blackboard.TargetMemory;

        return targetMemory.HasTarget &&
               targetMemory.IsCurrentTargetValid &&
               (currentState == EnemyState.Chase || currentState == EnemyState.Attack);
    }

    private EnemyPerceptionDecision HandleNoStimulus(EnemyState currentState)
    {
        blackboard.ClearCurrentStimulus();

        EnemyTargetMemory targetMemory = blackboard.TargetMemory;
        EnemyPerceptionMemory perceptionMemory = blackboard.PerceptionMemory;

        if (currentState == EnemyState.Chase || currentState == EnemyState.Attack)
        {
            if (targetMemory.HasTarget)
            {
                perceptionMemory.TryStartVisualMemoryGracePeriod(
                    targetMemory.CurrentTarget,
                    config.visualTargetMemoryDuration
                );
            }

            return EnemyPerceptionDecision.None;
        }

        if (currentState == EnemyState.Investigate)
        {
            return EnemyPerceptionDecision.None;
        }

        if (targetMemory.HasTarget || targetMemory.CurrentTargetIdentity.HasTarget)
        {
            targetMemory.ClearAll();
            perceptionMemory.ClearAll();
            blackboard.InvestigationMemory.ClearAll();

            SyncTarget();
        }

        return EnemyPerceptionDecision.None;
    }

    private void SyncTarget()
    {
        setTargetIdentity?.Invoke(blackboard.TargetMemory.CurrentTargetIdentity);
    }
}