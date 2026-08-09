using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;

// Walks round to stand behind the target while it is not looking.
//
// Deliberately does not need a live target. The whole point of this state is
// to be somewhere the player cannot see, and an enemy the player cannot see
// usually cannot see the player either - the state machine log for the first
// build is full of the target being lost every few seconds. Losing it here is
// expected, so the destination is worked out from the player's position and
// facing at the moment it is needed, not from the enemy's own memory.
public sealed class EnemyFlankState : IEnemyStateHandler
{
    private const float RepathInterval = 0.5f;
    private const float ArrivalDistance = 1.2f;

    private const float RouteSampleSpacing = 1.5f;
    // Spent across the whole search rather than per candidate. Twenty one
    // candidates each walking a long route is thousands of raycasts twice a
    // second, and the worst case lands exactly when the enemy is stuck and
    // repathing hardest.
    private const int RouteSampleBudget = 120;

    // Two enemies both work out "behind the player, three and a half metres"
    // and walk into each other. Whoever gets there first holds the spot; the
    // other is pushed on to the next candidate in the fan.
    private const float ClaimSpacing = 2f;

    private static readonly Dictionary<EnemyFlankState, Vector3> ClaimedPoints =
        new();

    private readonly EnemyBrainContext context;
    private readonly NavMeshPath routePath = new();

    private Vector3 flankPoint;
    private bool hasFlankPoint;
    private float repathTimer;
    private float giveUpTimer;
    private float seenTimer;
    private bool mirrorSides;

    public EnemyState State => EnemyState.Flank;

    public EnemyFlankState(EnemyBrainContext context)
    {
        this.context = context;
    }

    public void Enter()
    {
        hasFlankPoint = false;
        repathTimer = 0f;
        giveUpTimer = 0f;
        seenTimer = 0f;

        // Swap which way round the fan is tried on every attempt. The
        // order used to be fixed, so an approach that failed on the left
        // was retried on the left, and the enemy kept walking into the
        // same blocked corner.
        mirrorSides = !mirrorSides;
    }

    public void Tick(float deltaTime)
    {
        if (!PlayerGazeNetwork.TryGetNearest(
                context.Navigator.Position,
                out PlayerGazeNetwork player))
        {
            context.ChangeState(EnemyState.Investigate);
            return;
        }

        Vector3 selfPosition = context.Navigator.Position;

        // Spotted on the way round. Break off and try again from somewhere
        // else rather than walking the rest of the way in full view - but not
        // on a single frame of it. Crossing a doorway puts the enemy in view
        // for an instant, and treating that as being caught threw away the
        // whole approach every few metres.
        bool isSeen = PlayerGazeNetwork.IsBodySeenByAnyone(
            selfPosition,
            context.Navigator.BodyHeight);

        if (isSeen)
        {
            seenTimer += deltaTime;

            if (seenTimer >= context.Config.stalkNoticedDuration)
            {
                context.ChangeState(EnemyState.Retreat);
                return;
            }
        }
        else
        {
            seenTimer = 0f;
        }

        giveUpTimer += deltaTime;

        // Twelve seconds of trying to get round is long enough. Going back to
        // watching restarted the whole sequence - watch, be seen, break off,
        // fail to get round, watch again - which is the loop the log showed
        // running until the player walked away.
        if (giveUpTimer >= context.Config.flankTimeout)
        {
            context.ChangeState(EnemyState.Chase);
            return;
        }

        repathTimer -= deltaTime;

        if (!hasFlankPoint || repathTimer <= 0f)
        {
            repathTimer = RepathInterval;
            hasFlankPoint =
                TryFindPointBehind(player, selfPosition, out flankPoint);
        }

        // Nowhere behind them to stand, by either standard. Sneaking is off,
        // so the honest answer is to come at them rather than go back and
        // start the same failing sequence again.
        if (!hasFlankPoint)
        {
            context.ChangeState(EnemyState.Chase);
            return;
        }

        if (Vector3.Distance(selfPosition, flankPoint) <= ArrivalDistance)
        {
            context.ChangeState(EnemyState.Ambush);
            return;
        }

        // Caught partway round. The route is allowed to be seen for stretches
        // of it - insisting otherwise found nothing in a real level - but
        // walking at someone while they are looking straight at you is not a
        // flank, it is a charge. Wait them out; either the sighting passes or
        // the timer above hands this to the retreat.
        if (isSeen &&
            EnemyStateRules.ClosesOnWatcher(
                selfPosition,
                flankPoint,
                player.transform.position))
        {
            context.StopNavigation();
            return;
        }

        context.TryMoveTo(flankPoint, context.Config.chaseSpeed);
    }

    public void Exit()
    {
        hasFlankPoint = false;
        ClaimedPoints.Remove(this);
    }

    private bool IsClaimedByAnotherEnemy(Vector3 candidate)
    {
        foreach (KeyValuePair<EnemyFlankState, Vector3> claim in ClaimedPoints)
        {
            if (claim.Key != this &&
                (claim.Value - candidate).sqrMagnitude <
                ClaimSpacing * ClaimSpacing)
            {
                return true;
            }
        }

        return false;
    }

    // Walks the route the agent would actually take and asks whether the
    // player could see the enemy standing anywhere along it. Sampled rather
    // than continuous: a stride is about a metre, and a gap the enemy crosses
    // between two samples is a glimpse, not a sighting.
    private bool IsRouteHidden(Vector3 from, Vector3 to, ref int budget)
    {
        if (!NavMesh.CalculatePath(from, to, NavMesh.AllAreas, routePath) ||
            routePath.status != NavMeshPathStatus.PathComplete)
        {
            return false;
        }

        Vector3[] corners = routePath.corners;

        for (int i = 1; i < corners.Length; i++)
        {
            Vector3 previous = corners[i - 1];
            Vector3 corner = corners[i];
            float length = Vector3.Distance(previous, corner);
            int steps = Mathf.Max(1, Mathf.CeilToInt(length / RouteSampleSpacing));

            for (int step = 1; step <= steps; step++)
            {
                // Out of budget with the route half walked. Refusing is the
                // only safe answer: calling an unchecked route hidden is how
                // the enemy would walk straight across the view it was trying
                // to avoid.
                if (budget <= 0)
                {
                    return false;
                }

                budget--;

                Vector3 sample = Vector3.Lerp(previous, corner, step / (float)steps);

                if (PlayerGazeNetwork.IsBodySeenByAnyone(
                        sample,
                        context.Navigator.BodyHeight))
                {
                    return false;
                }
            }
        }

        return true;
    }

    // Straight behind first, then wider round either side, then closer in. A
    // point directly behind is often against a wall; the fan gives the level a
    // chance to offer something reachable that is still out of view.
    private bool TryFindPointBehind(
        PlayerGazeNetwork player,
        Vector3 selfPosition,
        out Vector3 point
    )
    {
        // Two passes. A route that stays hidden the whole way is the good
        // answer, but insisting on it found nothing at all in a real level -
        // the log for that build is Flank falling straight back to Stalk over
        // and over. A hidden place to stand, reached by a route that is seen
        // for part of the way, still ends with the enemy behind the player;
        // refusing it just means never getting there.
        return TryFindPointBehind(player, selfPosition, true, out point) ||
               TryFindPointBehind(player, selfPosition, false, out point);
    }

    private bool TryFindPointBehind(
        PlayerGazeNetwork player,
        Vector3 selfPosition,
        bool requireHiddenRoute,
        out Vector3 point
    )
    {
        Vector3 playerPosition = player.transform.position;
        Vector3 behind = -player.transform.forward;
        behind.y = 0f;
        behind.Normalize();

        float preferred = context.Config.flankBehindDistance;
        int budget = RouteSampleBudget;

        // Distances as well as angles. Five points at one radius all land in
        // the same wall in a corridor, and the enemy gave up and went back to
        // watching - which is what the first build did in a real level. Closer
        // rings are worse ambush spots but a great deal better than none.
        for (int r = 0; r < DistanceScales.Length; r++)
        {
            float distance = preferred * DistanceScales[r];

            for (int i = 0; i < BehindAngles.Length; i++)
            {
                float angle = mirrorSides ? -BehindAngles[i] : BehindAngles[i];
                Vector3 direction = Quaternion.Euler(0f, angle, 0f) * behind;

                if (!NavMesh.SamplePosition(
                        playerPosition + direction * distance,
                        out NavMeshHit hit,
                        distance * 0.5f,
                        NavMesh.AllAreas))
                {
                    continue;
                }

                // No use walking to a spot the player is already looking at.
                // Hidden from everyone, not just the one being crept up on.
                // Checking only the nearest meant the enemy planned a route
                // hidden from one player, walked it in full view of another,
                // was spotted, broke off and started over - the same circling
                // as before, brought on by a second person in the room.
                if (PlayerGazeNetwork.IsBodySeenByAnyone(
                        hit.position,
                        context.Navigator.BodyHeight))
                {
                    continue;
                }

                // Nor to a hidden spot by a route that crosses the player's
                // view on the way. The shortest path to somewhere behind
                // someone usually goes straight past their face, which is how
                // an enemy circling a player who never moved kept walking
                // back into sight and starting over.
                if (requireHiddenRoute &&
                    !IsRouteHidden(selfPosition, hit.position, ref budget))
                {
                    continue;
                }

                if (IsClaimedByAnotherEnemy(hit.position))
                {
                    continue;
                }

                ClaimedPoints[this] = hit.position;
                point = hit.position;
                return true;
            }
        }

        point = default;
        return false;
    }

    private static readonly float[] BehindAngles =
        { 0f, -35f, 35f, -70f, 70f, -105f, 105f };

    private static readonly float[] DistanceScales = { 1f, 0.65f, 0.4f };
}
