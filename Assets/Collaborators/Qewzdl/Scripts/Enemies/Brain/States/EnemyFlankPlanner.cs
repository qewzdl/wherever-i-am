using System.Collections.Generic;
using UnityEngine;

// Picks somewhere behind the target to come to rest.
//
// Split out of EnemyFlankState, which had grown to four jobs in one file:
// transitions, NavMesh searching, visibility sampling and reserving the spot
// against other enemies. The spot reservation now lives in
// EnemyTacticalSlotRegistry and the routes come from the enemy's own agent
// through EnemyTacticalNavigationPlanner, so a point this likes is one the
// navigator will accept.
//
// Straight behind first, then wider round either side, then closer in. A point
// directly behind is often against a wall; the fan gives the level a chance to
// offer something reachable that is still out of view.
internal sealed class EnemyFlankPlanner
{
    private readonly EnemyBrainContext context;

    private int cursor;
    private int searchedThisPass;
    private bool mirrorSides;
    private Vector3 fallbackPoint;
    private bool hasFallbackPoint;

    public EnemyFlankPlanner(EnemyBrainContext context)
    {
        this.context = context;
    }

    // Swap which way round the fan is tried on every attempt. The order used
    // to be fixed, so an approach that failed on the left was retried on the
    // left, and the enemy kept walking into the same blocked corner.
    public void Restart()
    {
        mirrorSides = !mirrorSides;
        ResetSearch();
    }

    public EnemyTacticalPlanResult TryFindPointBehind(
        EnemyTargetObservation targetObservation,
        Vector3 selfPosition,
        ref int visibilityBudget,
        out Vector3 point
    )
    {
        EnemyStealthTacticsConfig tactics = context.Config.StealthTactics;
        float[] angles = tactics.flankBehindAngles;
        float[] scales = tactics.flankDistanceScales;
        int total = angles.Length * scales.Length;
        int allowance = Mathf.Clamp(tactics.candidatesPerTick, 1, total);
        int routeBudget = Mathf.Min(tactics.routeSampleBudget, visibilityBudget);

        Vector3 behind = -targetObservation.Forward;
        behind.y = 0f;

        if (behind.sqrMagnitude < 0.001f)
        {
            behind = Vector3.forward;
        }

        behind.Normalize();

        float preferred = context.Config.flankBehindDistance;
        float bodyHeight = context.Navigator.BodyHeight;

        for (int n = 0; n < allowance; n++)
        {
            int index = (cursor + n) % total;
            float distance = preferred * scales[index / angles.Length];
            float angle = angles[index % angles.Length];

            if (mirrorSides)
            {
                angle = -angle;
            }

            Vector3 direction = Quaternion.Euler(0f, angle, 0f) * behind;

            if (!context.TacticalPlanner.TrySamplePoint(
                    targetObservation.Position + direction * distance,
                    distance * 0.5f,
                    out Vector3 candidate))
            {
                continue;
            }

            if (visibilityBudget <= 0)
            {
                break;
            }

            visibilityBudget--;

            // No use walking to a spot the player is already looking at.
            // Hidden from everyone, not just the one being crept up on.
            // Checking only the nearest meant the enemy planned a route
            // hidden from one player, walked it in full view of another, was
            // spotted, broke off and started over - the same circling as
            // before, brought on by a second person in the room.
            if (PlayerGazeNetwork.IsBodySeenByAnyone(candidate, bodyHeight))
            {
                continue;
            }

            // Nor to a hidden spot the enemy cannot get to. Asked with this
            // agent's own type, area mask and posture, so "reachable" means
            // reachable by this enemy rather than by an idealised one.
            if (!context.TacticalPlanner.TryPlanRoute(
                    selfPosition,
                    candidate,
                    out IReadOnlyList<Vector3> route))
            {
                continue;
            }

            // Nor by a route that crosses somebody's view on the way. The
            // shortest path to somewhere behind a person usually goes straight
            // past their face, which is how an enemy circling a player who
            // never moved kept walking back into sight and starting over.
            bool routeHidden = context.TacticalPlanner.IsRouteHidden(
                route,
                bodyHeight,
                tactics.routeSampleSpacing,
                ref routeBudget,
                out _
            );

            visibilityBudget = Mathf.Min(visibilityBudget, routeBudget);

            if (EnemyTacticalSlotRegistry.IsClaimedByAnother(
                    context.TacticalOwnerId,
                    candidate,
                    tactics.claimSpacing))
            {
                continue;
            }

            if (routeHidden)
            {
                Claim(candidate);
                ResetSearch();
                point = candidate;
                return EnemyTacticalPlanResult.Found;
            }

            // A hidden place to stand, reached by a route that is seen for
            // part of the way, still ends with the enemy behind the player.
            // Insisting on a wholly hidden route found nothing at all in a
            // real level - the log for that build is Flank falling straight
            // back to Stalk over and over - so it is preferred, not required.
            if (!hasFallbackPoint)
            {
                hasFallbackPoint = true;
                fallbackPoint = candidate;
            }
        }

        cursor = (cursor + allowance) % total;
        searchedThisPass += allowance;

        if (searchedThisPass < total)
        {
            point = fallbackPoint;
            return EnemyTacticalPlanResult.Deferred;
        }

        bool hadFallback = hasFallbackPoint;
        point = fallbackPoint;

        if (hadFallback)
        {
            Claim(fallbackPoint);
        }

        ResetSearch();

        return hadFallback
            ? EnemyTacticalPlanResult.Found
            : EnemyTacticalPlanResult.NotFound;
    }

    public void ReleaseClaim()
    {
        EnemyTacticalSlotRegistry.Release(context.TacticalOwnerId);
    }

    private void Claim(Vector3 point)
    {
        EnemyTacticalSlotRegistry.Claim(context.TacticalOwnerId, point);
    }

    private void ResetSearch()
    {
        cursor = 0;
        searchedThisPass = 0;
        hasFallbackPoint = false;
        fallbackPoint = default;
    }
}
