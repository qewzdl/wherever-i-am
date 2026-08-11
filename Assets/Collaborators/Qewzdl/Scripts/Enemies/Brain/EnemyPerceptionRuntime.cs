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

        TrackTargetHidingPlace();

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

        // The hold timer measures time spent on a target. Carrying it across a
        // teardown would have a reused enemy refusing its first target for a
        // couple of seconds on the strength of a pursuit that is over.
        targetDetector?.ResetTargetSelection();
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

        if (EnemyStateRules.IsEngagedWithTarget(currentState))
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

        Transform targetTransform = stimulus.Target.NetworkObject != null &&
                                    stimulus.Target.NetworkObject.IsSpawned
            ? stimulus.Target.NetworkObject.transform
            : stimulus.Target.transform;

        targetMemory.RememberObservation(
            stimulus.Target,
            targetTransform.position,
            targetTransform.forward,
            Time.time
        );

        investigationMemory.RememberLastKnownTargetPosition(stimulus.Position);
        investigationMemory.ClearSuspiciousPosition();

        // The target is visible again, so whichever box it used earlier is
        // history - keeping it would send the next investigation to a place
        // this pursuit has nothing to do with.
        investigationMemory.ClearObservedHidingPlace();

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
                config.visualTargetMemoryDuration,
                config.visualMemoryTracksLiveTarget
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

    // A pursued target that stopped being detectable while still spawned did
    // one of two things: broke line of sight, or climbed into something. Only
    // the second resolves to a hiding place, and it is the one an enemy could
    // honestly have watched happen.
    //
    // This runs every tick, at the very top, and that placement is the whole
    // point. Reading the Entering state is useless - an entry completes inside
    // a single call, so no polling sensor ever observes a player mid-climb. And
    // hanging it off the perception grace period is just as useless, because
    // EnemyChaseState drops a target the moment it stops being detectable,
    // which is the same tick. The brain runs perception before the state
    // handler, so this is the last point where the target is still held.
    private void TrackTargetHidingPlace()
    {
        EnemyTarget target = blackboard.TargetMemory.CurrentTarget;

        if (target == null || target.CanBeDetected)
        {
            return;
        }

        // Set OR clear: a target that vanished behind a wall rather than into a
        // box must wipe any earlier reference, otherwise it outlives the
        // pursuit that earned it and redirects the next investigation.
        HidingPlaceInteractable hidingPlace = null;
        target.TryGetCurrentHidingPlace(out hidingPlace);

        blackboard.InvestigationMemory.RememberObservedHidingPlace(
            hidingPlace
        );
    }

    private bool IsPursuingConfirmedTarget(EnemyState currentState)
    {
        EnemyTargetMemory targetMemory = blackboard.TargetMemory;

        return targetMemory.HasTarget &&
               targetMemory.IsCurrentTargetValid &&
               EnemyStateRules.IsEngagedWithTarget(currentState);
    }

    private EnemyPerceptionDecision HandleNoStimulus(EnemyState currentState)
    {
        blackboard.ClearCurrentStimulus();

        EnemyTargetMemory targetMemory = blackboard.TargetMemory;
        EnemyPerceptionMemory perceptionMemory = blackboard.PerceptionMemory;

        if (EnemyStateRules.IsEngagedWithTarget(currentState))
        {
            if (targetMemory.HasTarget)
            {
                perceptionMemory.TryStartVisualMemoryGracePeriod(
                    targetMemory.CurrentTarget,
                    config.visualTargetMemoryDuration,
                    config.visualMemoryTracksLiveTarget
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
