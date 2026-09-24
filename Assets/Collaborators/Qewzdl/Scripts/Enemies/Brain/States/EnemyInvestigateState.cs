using UnityEngine;
using UnityEngine.AI;

public sealed class EnemyInvestigateState : IEnemyStateHandler
{
    private enum InvestigationPhase
    {
        MovingToLastKnownPosition,
        CheckingHidingPlace,
        FollowingSearchRoute
    }

    private readonly EnemyBrainContext context;

    private InvestigationPhase phase;

    private Vector3 investigationOrigin;
    private bool hasInvestigationOrigin;
    private Vector3 currentDestination;

    private float repathTimer;

    // How fast she goes to the place she is investigating, chosen once when
    // the investigation starts from whatever started it. See
    // ChooseApproachSpeed.
    private float approachSpeed;

    private bool hasDestination;

    // Resolved on every Enter rather than in the constructor: the states are
    // built while the modules are being installed, and the module that provides
    // this one may not have had its turn yet. Null is the ordinary answer for
    // an enemy that was not given it.
    private EnemyHidingPlaceCheck hidingPlaceCheck;
    private EnemyLookAround lookAround;
    private EnemySearchRoute searchRoute;

    public EnemyState State => EnemyState.Investigate;

    public EnemyInvestigateState(EnemyBrainContext context)
    {
        this.context = context;
    }

    public void Enter()
    {
        ResetRuntimeState();

        context.Capabilities.TryGet(out hidingPlaceCheck);
        context.Capabilities.TryGet(out lookAround);
        context.Capabilities.TryGet(out searchRoute);

        // Read here and nowhere later: perception rewrites the current
        // stimulus every tick, and by the time she is halfway there it
        // describes whatever she is hearing now rather than what sent her.
        // A newer noise worth restarting for comes back through Enter and is
        // weighed afresh.
        approachSpeed = ChooseApproachSpeed(
            context.PerceptionMemory.CurrentStimulus,
            context.Config.urgentNoiseScore,
            context.Config.chaseSpeed,
            context.Config.investigationSearchSpeed);

        if (!TryResolveInvestigationOrigin(out investigationOrigin))
        {
            FinishInvestigation();
            return;
        }

        hasInvestigationOrigin = true;

        context.InvestigationDebugData?.Begin(investigationOrigin);

        phase = InvestigationPhase.MovingToLastKnownPosition;

        if (!TrySetDestination(investigationOrigin, approachSpeed))
        {
            TryMoveToSecondaryOrFinish();
        }
    }

    public void Tick(float deltaTime)
    {
        if (context.TargetMemory.HasTarget)
        {
            context.ChangeState(EnemyState.Chase);
            return;
        }

        if (TryRestartOnNewerStimulus())
        {
            return;
        }

        repathTimer -= deltaTime;

        if (phase == InvestigationPhase.MovingToLastKnownPosition)
        {
            TickMovingToLastKnownPosition(deltaTime);
            return;
        }

        if (phase == InvestigationPhase.CheckingHidingPlace)
        {
            TickCheckingHidingPlace(deltaTime);
            return;
        }

        TickFollowingSearchRoute(deltaTime);
    }

    public void Exit()
    {
        context.InvestigationDebugData?.Finish();
        ResetRuntimeState();
    }

    private void TickMovingToLastKnownPosition(float deltaTime)
    {
        if (lookAround != null && lookAround.IsLookingAround)
        {
            if (lookAround.Tick(deltaTime))
            {
                return;
            }

            StartHierarchicalSearch();
            return;
        }

        if (!hasDestination)
        {
            if (!TrySetDestination(investigationOrigin, approachSpeed))
            {
                TryMoveToSecondaryOrFinish();
            }

            return;
        }

        RepathToCurrentDestination(approachSpeed);

        if (!context.Navigator.HasReached(context.Config.investigationReachDistance))
        {
            return;
        }

        // A box she watched somebody climb into is not a place to look around
        // at, and this used to be the other way round.
        //
        // The dwell exists for a place where the target MIGHT be: she stops,
        // turns her head either way, and covers the corners the walk would
        // have dragged her cone past. When the origin is a hiding place she
        // saw the target get into, there is nothing to cover - she knows. So
        // the old order stood her in front of the box for the whole dwell,
        // a second and a half of scanning an empty room, before opening it.
        //
        // From a player's side that is the entire bug: watched from two metres
        // away, she walks over, stops, looks slowly left, looks slowly right,
        // and only then lifts the lid. It reads as her having lost you and
        // searching the room, because that is exactly the behaviour she is
        // performing.
        if (TryStartHidingPlaceCheck())
        {
            return;
        }

        if (lookAround != null && lookAround.TryBegin())
        {
            return;
        }

        StartHierarchicalSearch();
    }

    // Run at a noise worth running at; walk at one that is not.
    //
    // Every investigation used to go to its origin at chase speed, whatever
    // started it, so a creaking floorboard at the edge of her hearing and a
    // box dropped beside her got the same sprint. That made quiet play
    // pointless in exactly the way the footstep, stamina and breathing work
    // was meant to reward: the noise that gave you away was answered at full
    // speed however little of it she heard.
    //
    // Heard noises are weighed by their score, which is already the right
    // quantity - the noise's loudness scaled down by how far it had to travel
    // and how old it is - so "loud or close" rather than just "loud". Anything
    // that is not a heard noise keeps the sprint: an investigation with no
    // stimulus behind it is one that started because she lost sight of
    // somebody she was chasing, and slowing down there would hand them the
    // escape for nothing.
    public static float ChooseApproachSpeed(
        EnemyPerceptionStimulus stimulus,
        float urgentNoiseScore,
        float chaseSpeed,
        float cautiousSpeed)
    {
        if (!stimulus.HasStimulus ||
            stimulus.Source != EnemyPerceptionSource.Hearing)
        {
            return chaseSpeed;
        }

        return stimulus.Score >= urgentNoiseScore
            ? chaseSpeed
            : cautiousSpeed;
    }

    // A stimulus that lands while the enemy is already investigating updates
    // the memory but cannot restart the state: ApplyPerceptionDecision only
    // calls ChangeState when the enemy is not already investigating, and
    // ChangeState returns early on a matching state, so Enter never runs again
    // and the plan built around the old origin stands. Notice the move here.
    private bool TryRestartOnNewerStimulus()
    {
        // Committed to opening a box. The check is short and bounded by its own
        // timer, and abandoning it would let any thrown object rescue a player
        // this enemy actually watched climb in.
        if (phase == InvestigationPhase.CheckingHidingPlace)
        {
            return false;
        }

        // A known hiding place is a stronger lead than a noise, and it is what
        // TryResolveInvestigationOrigin would pick anyway - restarting on it
        // would rebuild the same plan every tick.
        //
        // Asked of the capability rather than of the memory, because an enemy
        // that cannot check boxes has no business treating one as a lead. The
        // note is still written for her - perception does not know what she can
        // do - and without this she would refuse to follow a noise on the
        // strength of a box she will never open.
        if (hidingPlaceCheck != null && hidingPlaceCheck.HasRememberedPlace)
        {
            return false;
        }

        if (!context.InvestigationMemory.TryGetLastKnownTargetPosition(
                out Vector3 lastKnown))
        {
            return false;
        }

        // Inside the ring already being walked, the current plan covers it.
        // Restarting on every repeat of the same noise would thrash the route.
        float relevanceRadius = context.Config.investigationBranchRadius;

        if ((lastKnown - investigationOrigin).sqrMagnitude <=
            relevanceRadius * relevanceRadius)
        {
            return false;
        }

        Enter();
        return true;
    }

    // Walk up to the hiding place instead of stopping where the stimulus was:
    // the server only opens it from within EnemyInvestigationDistance, and the
    // investigation origin can sit a metre or two off the anchor.
    //
    // Everything about which box, how long to wait and how close to stand is
    // the capability's. What is left here is navigation, which is the state's
    // whichever way the enemy is configured.
    private bool TryStartHidingPlaceCheck()
    {
        if (hidingPlaceCheck == null ||
            !hidingPlaceCheck.TryBegin(out Vector3 approachPosition))
        {
            return false;
        }

        phase = InvestigationPhase.CheckingHidingPlace;

        if (!TrySetDestination(approachPosition, context.Config.chaseSpeed))
        {
            context.StopNavigation();
        }

        return true;
    }

    private void TickCheckingHidingPlace(float deltaTime)
    {
        // The null case cannot happen - the phase is only entered through
        // TryStartHidingPlaceCheck, which needs the capability - but a state
        // machine that would spin forever if it ever did is worth one branch.
        if (hidingPlaceCheck == null ||
            hidingPlaceCheck.Tick(deltaTime) ==
            EnemyHidingPlaceCheckStatus.Finished)
        {
            StartHierarchicalSearch();
            return;
        }

        RepathToCurrentDestination(context.Config.chaseSpeed);
    }

    // Nothing to search with means the investigation is over once the walk to
    // the stimulus is: she tries whatever second lead she has and gives up.
    // That is the enemy you can hide from by not being exactly where you were
    // heard, and it is a configuration rather than a failure.
    private void StartHierarchicalSearch()
    {
        if (searchRoute == null)
        {
            TryMoveToSecondaryOrFinish();
            return;
        }

        phase = InvestigationPhase.FollowingSearchRoute;

        context.InvestigationMemory.ClearLastKnownTargetPosition();

        NavMeshQueryFilter filter = GetNavigationQueryFilter();
        searchRoute.Plan(
            searchRoute.GetLedCentre(investigationOrigin, filter),
            filter);

        if (searchRoute.PointCount == 0)
        {
            TryMoveToSecondaryOrFinish();
            return;
        }

        MoveToNextSearchPointOrFinish();
    }

    private void TickFollowingSearchRoute(float deltaTime)
    {
        if (lookAround != null && lookAround.IsLookingAround)
        {
            if (lookAround.Tick(deltaTime))
            {
                return;
            }

            MoveToNextSearchPointOrFinish();
            return;
        }

        if (!hasDestination)
        {
            MoveToNextSearchPointOrFinish();
            return;
        }

        RepathToCurrentDestination(context.Config.investigationSearchSpeed);

        if (!context.Navigator.HasReached(context.Config.investigationReachDistance))
        {
            return;
        }

        if (lookAround != null && lookAround.TryBegin())
        {
            return;
        }

        MoveToNextSearchPointOrFinish();
    }

    private void MoveToNextSearchPointOrFinish()
    {
        while (searchRoute != null &&
               searchRoute.TryTakeNextPoint(
                   out Vector3 nextPoint,
                   out int routeIndex))
        {
            if (!TrySetDestination(
                    nextPoint,
                    context.Config.investigationSearchSpeed))
            {
                continue;
            }

            context.InvestigationDebugData?.SetActiveRouteIndex(routeIndex);
            context.Blackboard.SetActiveInvestigationRouteIndex(routeIndex);

            return;
        }

        TryMoveToSecondaryOrFinish();
    }

    private void TryMoveToSecondaryOrFinish()
    {
        if (context.InvestigationMemory.PromoteSuspiciousPositionToLastKnown())
        {
            if (TryResolveInvestigationOrigin(out investigationOrigin))
            {
                hasInvestigationOrigin = true;

                context.InvestigationDebugData?.Begin(investigationOrigin);

                phase = InvestigationPhase.MovingToLastKnownPosition;

                // Planned around the origin itself rather than the led centre.
                // The lead is a guess about where somebody was going; a second
                // stimulus is a fresh fact, and a fact does not want leading.
                searchRoute?.Plan(investigationOrigin, GetNavigationQueryFilter());

                if (!TrySetDestination(investigationOrigin, approachSpeed))
                {
                    FinishInvestigation();
                }

                return;
            }
        }

        FinishInvestigation();
    }

    // A remembered hiding place wins over the last known position, and it has
    // to. Visual memory hands out the target's LIVE position while it runs, so
    // a target that climbed into a box leaves its last known position inside
    // that box - inside the hole the box carves out of the NavMesh. Pathing
    // there fails, Enter gives up, and the enemy forgets a player it just
    // watched hide. The interaction anchor is reachable by construction.
    //
    // Safe to prefer because the reference now has exactly one writer: a
    // pursued target that vanished into a box while the enemy was watching.
    private bool TryResolveInvestigationOrigin(out Vector3 position)
    {
        if (hidingPlaceCheck != null &&
            hidingPlaceCheck.TryGetInvestigationOrigin(out position))
        {
            return true;
        }

        if (context.InvestigationMemory.TryGetLastKnownTargetPosition(out position))
        {
            return true;
        }

        if (context.InvestigationMemory.PromoteSuspiciousPositionToLastKnown())
        {
            return context.InvestigationMemory.TryGetLastKnownTargetPosition(out position);
        }

        position = default;
        return false;
    }

    // Investigation pushes through barricades like Chase does. Losing sight of
    // the target for a moment (the barricade itself blocks line of sight up
    // close) drops the enemy out of Chase, and without this the navigator
    // released its push-through holds, the items carved the route shut again,
    // and the enemy froze outside a sealed room until the target reappeared.
    private bool TrySetDestination(Vector3 destination, float speed)
    {
        currentDestination = destination;
        hasDestination = context.TryMoveTo(
            destination,
            speed,
            allowPushThrough: true);
        repathTimer = Mathf.Max(0.05f, context.Config.investigationRepathInterval);

        if (hasDestination)
        {
            context.InvestigationDebugData?.SetCurrentDestination(destination);
        }
        else
        {
            context.InvestigationDebugData?.ClearCurrentDestination();
        }

        return hasDestination;
    }

    private NavMeshQueryFilter GetNavigationQueryFilter()
    {
        EnemyPosture posture = context.PostureController != null
            ? context.PostureController.CurrentPosture
            : EnemyPosture.Standing;

        if (context.Navigator.TryGetNavigationQueryFilter(
                posture,
                out NavMeshQueryFilter filter))
        {
            return filter;
        }

        NavMeshBuildSettings settings = NavMesh.GetSettingsByIndex(0);
        return new NavMeshQueryFilter
        {
            agentTypeID = settings.agentTypeID,
            areaMask = NavMesh.AllAreas
        };
    }

    private void RepathToCurrentDestination(float speed)
    {
        if (!hasDestination || repathTimer > 0f)
        {
            return;
        }

        hasDestination = context.TryMoveTo(
            currentDestination,
            speed,
            allowPushThrough: true);
        repathTimer = Mathf.Max(0.05f, context.Config.investigationRepathInterval);

        if (hasDestination)
        {
            context.InvestigationDebugData?.SetCurrentDestination(currentDestination);
        }
        else
        {
            context.InvestigationDebugData?.ClearCurrentDestination();
        }
    }

    private void FinishInvestigation()
    {
        context.InvestigationDebugData?.Finish();
        context.Blackboard.ClearCurrentInvestigationRoute();
        context.Blackboard.ClearCurrentDestination();

        // She goes back to patrolling, but not back to where patrolling had
        // got to.
        //
        // Losing somebody used to cost her nothing at all: the search ended,
        // the route carried on from whichever point it had been interrupted
        // at, and within a few seconds she was somewhere else in the level
        // behaving exactly as she had before she ever saw anyone. A player who
        // was nearly caught could wait fifteen seconds and walk back into the
        // same room, because nothing about that room was different.
        //
        // Rejoining the loop at the point nearest where the search ran out
        // keeps her in that part of the level for a while, without a raised
        // alert state, a suspicion timer, or anything else that has to be
        // tuned and decay and be explained to the player. She is not angrier.
        // She is just still nearby, which is the thing that makes coming back
        // out feel like a decision.
        //
        // Only when the search had somewhere to be. An investigation that
        // ended because it could not work out where to go in the first place
        // knows nothing about the level, and its origin is the world origin.
        if (hasInvestigationOrigin)
        {
            context.PatrolController?.ResumeNearest(investigationOrigin);
        }

        context.ClearAllTargetMemory();
        context.ReturnToDefaultBehaviour();
    }

    private void ResetRuntimeState()
    {
        phase = InvestigationPhase.MovingToLastKnownPosition;

        investigationOrigin = default;
        hasInvestigationOrigin = false;
        currentDestination = default;

        repathTimer = 0f;

        lookAround?.Reset();
        searchRoute?.Reset();

        hasDestination = false;
        hidingPlaceCheck?.Reset();

        context.Blackboard.ClearCurrentDestination();
        context.Blackboard.ClearCurrentInvestigationRoute();
    }
}
