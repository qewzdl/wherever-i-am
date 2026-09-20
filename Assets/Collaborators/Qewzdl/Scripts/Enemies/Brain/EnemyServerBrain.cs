using System;
using System.Collections.Generic;
using UnityEngine;

public sealed class EnemyServerBrain
{
    private readonly EnemyConfig config;
    private readonly EnemyNavigator navigator;
    private readonly EnemyAttackController attackController;
    private readonly EnemyPostureController postureController;
    private readonly EnemyBlackboard blackboard;
    private readonly EnemyPerceptionRuntime perceptionRuntime;
    private readonly Action<EnemyState> setState;
    private readonly Action<EnemyTargetIdentity> setTargetIdentity;

    private readonly Dictionary<EnemyState, IEnemyStateHandler> stateHandlers = new();

    private EnemyBrainContext context;
    private IEnemyStateHandler currentHandler;
    private EnemyState currentState = EnemyState.Idle;

    public bool HasStarted { get; private set; }

    public EnemyServerBrain(
        EnemyConfig config,
        EnemyNavigator navigator,
        EnemyTargetDetector targetDetector,
        bool usesTargetDetection,
        EnemyPatrolController patrolController,
        EnemyAttackController attackController,
        EnemyPostureController postureController,
        EnemyBlackboard blackboard,
        Action<EnemyState> setState,
        Action<EnemyTargetIdentity> setTargetIdentity,
        Action<float, GameplayNoiseSourceType> reportHeardNoise
    )
    {
        if (usesTargetDetection && targetDetector == null)
        {
            throw new ArgumentNullException(
                nameof(targetDetector),
                $"{nameof(EnemyServerBrain)} requires {nameof(EnemyTargetDetector)} when target detection is enabled."
            );
        }

        this.blackboard = blackboard ?? throw new ArgumentNullException(
            nameof(blackboard),
            $"{nameof(EnemyServerBrain)} requires non-null {nameof(EnemyBlackboard)}."
        );

        this.config = config;
        this.navigator = navigator;
        this.attackController = attackController;
        this.postureController = postureController;
        this.setState = setState;
        this.setTargetIdentity = setTargetIdentity;

        perceptionRuntime = new EnemyPerceptionRuntime(
            config,
            targetDetector,
            usesTargetDetection,
            this.blackboard,
            setTargetIdentity,
            reportHeardNoise
        );

        context = new EnemyBrainContext(
            config,
            navigator,
            targetDetector,
            patrolController,
            attackController,
            postureController,
            this.blackboard,
            ChangeState,
            SyncTarget
        );

        RegisterStateHandlers();

        if (attackController != null)
        {
            attackController.AttackResolved += HandleAttackResolved;
        }
    }

    public void Start()
    {
        if (HasStarted)
        {
            return;
        }

        if (config == null || navigator == null || !navigator.TryEnsureOnNavMesh())
        {
            ChangeState(EnemyState.Idle);
            return;
        }

        HasStarted = true;
        context.RefreshPosture();

        if (context.HasPatrolRoute)
        {
            ChangeState(EnemyState.Patrol);
        }
        else
        {
            ChangeState(EnemyState.Idle);
        }
    }

    public void Tick(float deltaTime)
    {
        if (!HasStarted || config == null || navigator == null)
        {
            return;
        }

        if (!navigator.TryEnsureOnNavMesh())
        {
            return;
        }

        navigator.TickNavigationGate();

        context.RefreshPosture();

        attackController?.Tick(deltaTime, navigator.Position);

        EnemyPerceptionDecision perceptionDecision = perceptionRuntime.Tick(
            deltaTime,
            currentState
        );

        ApplyPerceptionDecision(perceptionDecision);

        currentHandler?.Tick(deltaTime);
    }

    public void Dispose()
    {
        if (attackController != null)
        {
            attackController.AttackResolved -= HandleAttackResolved;
        }

        attackController?.Interrupt();

        currentHandler?.Exit();
        currentHandler = null;

        perceptionRuntime.ResetRuntimeState();

        // The scheduler's cache is keyed per enemy and it has no way to notice
        // one going away, so a match's worth of dead enemies would sit in it.
        context?.ForgetPerceptionCache();

        blackboard.ClearAll();
        SyncTarget();

        HasStarted = false;
    }

    // A landed attack is the pursuit having got what it came for. Everything
    // the engagement remembers - the failed approaches, the count of them, a
    // commitment to charging because sneaking did not work - was in service of
    // reaching this, so it goes, and the next approach on the same person
    // starts from sneaking again rather than from a grudge.
    private void HandleAttackResolved(EnemyAttackResult result)
    {
        if (result.Type != EnemyAttackResultType.Hit)
        {
            return;
        }

        blackboard.EngagementTactics.Clear();
    }

    // Which behaviours this enemy actually gets.
    //
    // The list on the config is the whole answer. A state that no module
    // installed is not a state this enemy can be in, and nothing else has to
    // know: every request for one goes through ChangeState, which walks
    // EnemyStateRules' fallback chain and lands on something that is installed.
    // That is why the switch is here rather than scattered over the two dozen
    // places that ask for a transition.
    //
    // An empty list means the full set rather than an enemy that does nothing.
    // The field is newer than every config in the project, so empty is what
    // ships until somebody fills it in, and reading it as "no behaviours" would
    // have taken patrolling, searching and sneaking off every enemy in the game
    // at once - with a standing-still enemy as the only symptom.
    private void RegisterStateHandlers()
    {
        EnemyBehaviorInstaller installer = new(
            context,
            stateHandlers,
            context.Capabilities);

        IReadOnlyList<EnemyBehaviorModule> modules =
            config != null ? config.BehaviorModules : null;

        if (modules == null || modules.Count == 0)
        {
            EnemyDefaultBehaviors.Install(installer);
            return;
        }

        bool installedAnything = false;

        foreach (EnemyBehaviorModule module in modules)
        {
            // A hole in the list is somebody midway through filling it in, not
            // a reason to refuse to build the enemy.
            if (module == null)
            {
                continue;
            }

            module.Install(installer);
            installedAnything = true;
        }

        if (!installedAnything)
        {
            EnemyDefaultBehaviors.Install(installer);
        }
    }

    // The nearest thing to what was asked for that this enemy actually has.
    //
    // Walks the fallback chain rather than taking one step, because the chains
    // compose: an enemy with neither investigating nor patrolling asks for
    // Investigate, is offered Patrol, has not got that either, and ends at
    // Idle. One step would have stopped at a state as missing as the first.
    private bool TryResolveInstalledState(
        EnemyState requestedState,
        out EnemyState installedState)
    {
        installedState = requestedState;

        for (int step = 0; step < EnemyStateRules.FallbackChainLimit; step++)
        {
            if (stateHandlers.ContainsKey(installedState))
            {
                return true;
            }

            if (!EnemyStateRules.TryGetFallback(
                    installedState,
                    out EnemyState fallbackState))
            {
                return false;
            }

            installedState = fallbackState;
        }

        return false;
    }

    private void ChangeState(EnemyState requestedState)
    {
        // Resolved BEFORE the already-there check rather than after.
        //
        // An enemy with no investigating gets a suspicious position, asks for
        // Investigate, and lands on Patrol. If the comparison ran on the
        // request instead, Investigate would never equal Patrol, and she would
        // leave and re-enter Patrol on every frame that perception found
        // something - restarting the route, every frame, forever.
        if (!TryResolveInstalledState(requestedState, out EnemyState nextState))
        {
            Debug.LogError(
                $"{nameof(EnemyServerBrain)} has no handler for state " +
                $"{requestedState} and no installed state to fall back to.");

            return;
        }

        if (currentHandler != null && currentState == nextState)
        {
            return;
        }

        IEnemyStateHandler nextHandler = stateHandlers[nextState];

#if UNITY_EDITOR
        // Kept rather than deleted. Three separate misbehaviours in this state
        // machine were diagnosed from this line and none of them reproduced in
        // a fixture; removing it just means adding it back next time.
        Debug.Log(
            $"[enemy-state] {currentState} -> {nextState} " +
            $"hasTarget={blackboard.TargetMemory.HasTarget} " +
            $"valid={blackboard.TargetMemory.IsCurrentTargetValid}");
#endif

        UpdateStealthManeuver(currentState, nextState);

        currentHandler?.Exit();

        currentState = nextState;
        setState?.Invoke(nextState);

        currentHandler = nextHandler;
        currentHandler.Enter();
    }

    // The manoeuvre and its target lock live exactly as long as the manoeuvre.
    //
    // Starting one takes a snapshot of who it is against and when they were
    // last seen; moving between its phases keeps both. Leaving it is the
    // explicit abort - and the only thing that lets perception hand this enemy
    // somebody else. Nothing about a perception refresh releases it, which is
    // the whole point: the target vanishing used to null the held target and
    // fall straight through into adopting whoever was visible instead.
    private void UpdateStealthManeuver(EnemyState fromState, EnemyState toState)
    {
        bool wasManeuvering = EnemyStateRules.IsStealthManeuver(fromState);
        bool willManeuver = EnemyStateRules.IsStealthManeuver(toState);

        if (willManeuver && wasManeuvering && blackboard.StealthManeuver.IsActive)
        {
            blackboard.StealthManeuver.EnterPhase(toState);
        }
        else if (willManeuver)
        {
            blackboard.StealthManeuver.Begin(
                blackboard.TargetMemory.LastObservation,
                toState,
                Time.time
            );
        }
        else if (wasManeuvering)
        {
            blackboard.StealthManeuver.End();
            EnemyTacticalSlotRegistry.Release(context.TacticalOwnerId);
        }

        // Chase and Attack carry on against the same person, so the
        // commitment survives between them. Anything else has stopped dealing
        // with anybody at all, and that is what releases it.
        if (EnemyStateRules.IsEngagedWithTarget(fromState) &&
            !EnemyStateRules.IsEngagedWithTarget(toState))
        {
            context.TargetDetector?.ResetTargetSelection();

            // What was learned about how to approach this person goes with the
            // pursuit of them. Investigating is still that pursuit - the enemy
            // is looking for the same person and may well find them - so the
            // memory outlives a lost sighting and dies when the search does.
            if (toState != EnemyState.Investigate)
            {
                blackboard.EngagementTactics.Clear();
            }
        }
    }

    private void ApplyPerceptionDecision(EnemyPerceptionDecision decision)
    {
        if (!decision.HasDecision)
        {
            return;
        }

        switch (decision.Type)
        {
            case EnemyPerceptionDecisionType.ConfirmedTarget:
                ApplyConfirmedTarget();
                break;

            case EnemyPerceptionDecisionType.SuspiciousPosition:
                ApplySuspiciousPosition();
                break;

            case EnemyPerceptionDecisionType.None:
                break;

            default:
                Debug.LogError(
                    $"{nameof(EnemyServerBrain)} received unsupported perception decision {decision.Type}."
                );

                break;
        }
    }

    // Losing sight of the target normally means going to look for it. Not
    // while circling round or lying in wait: those states are out of view on
    // purpose, so they lose the target constantly by design, and pulling them
    // into a search every time it happens meant the enemy never once reached
    // the ambush.
    //
    // Both have their own timeouts, so nothing is left standing forever.
    private void ApplySuspiciousPosition()
    {
        if (EnemyStateRules.HandlesOwnSightLoss(currentState) ||
            currentState == EnemyState.Investigate)
        {
            return;
        }

        ChangeState(EnemyState.Investigate);
    }

    // Near targets are run at, far ones are stalked. Between the two
    // thresholds nothing changes, so a target drifting across a single line
    // cannot flip the enemy back and forth.
    private void ApplyConfirmedTarget()
    {
        // Everything the enemy has learned is about one person. A perception
        // refresh that hands it somebody else starts again from nothing.
        EnemyEngagementTacticsRuntime engagement = blackboard.EngagementTactics;
        engagement.BeginEngagement(blackboard.TargetMemory.CurrentTargetIdentity);

        // Chase is the only engaged state the fork may still change - the
        // rest run their own sequence and say when they are done.
        if (EnemyStateRules.IsEngagedWithTarget(currentState) &&
            currentState != EnemyState.Chase)
        {
            return;
        }

        // Sneaking was tried against this person and did not work. Distance is
        // what used to decide this, and distance is exactly what a failed
        // flank produces - the enemy backs off, crosses the stalk threshold,
        // and is sent round again on the strength of it. While the commitment
        // holds, a confirmed target means walk at them, whatever the range.
        if (engagement.IsAssaultCommitted(
                Time.time,
                context.GetRelevantGazeTopologySignature()))
        {
            if (currentState != EnemyState.Chase)
            {
                ChangeState(EnemyState.Chase);
            }

            return;
        }

        // No usable target position to measure against yet. Chasing is what
        // this did before stalking existed, and refusing to act at all would
        // mean a confirmed target that the enemy notices and then ignores.
        if (!blackboard.TargetMemory.HasTarget ||
            !blackboard.TargetMemory.IsCurrentTargetValid)
        {
            if (currentState != EnemyState.Chase)
            {
                ChangeState(EnemyState.Chase);
            }

            return;
        }

        float distance = Vector3.Distance(
            context.Navigator.Position,
            context.GetTargetNavigationPosition(blackboard.TargetMemory.CurrentTarget)
        );

        float chaseDistance = context.Config.chaseWithoutStalkingDistance;
        float stalkDistance = context.Config.stalkInsteadOfChasingDistance;

        // Not committed to either yet - coming off patrol, idle or a search.
        // There is nothing to flip between, so the band must not stop it
        // deciding: a target first seen inside the band would otherwise be
        // noticed and then ignored.
        if (currentState != EnemyState.Chase)
        {
            ChangeState(distance >= (chaseDistance + stalkDistance) * 0.5f
                ? EnemyState.Stalk
                : EnemyState.Chase);

            return;
        }

        // Already chasing. Only the far edge of the band turns that into
        // stalking, so a target drifting around the threshold does not keep
        // changing the enemy's mind. The way back is Stalk's own business.
        if (distance >= stalkDistance)
        {
            ChangeState(EnemyState.Stalk);
        }
    }

    private void SyncTarget()
    {
        setTargetIdentity?.Invoke(blackboard.TargetMemory.CurrentTargetIdentity);
    }
}
