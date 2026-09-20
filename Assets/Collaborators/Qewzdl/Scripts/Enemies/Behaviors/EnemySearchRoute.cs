using UnityEngine;
using UnityEngine.AI;

// Working out where to look when the place somebody was last seen turns out to
// be empty, and remembering how far through that she has got.
//
// The largest thing the investigating state used to do and the least like the
// rest of it. Walking to a stimulus is one behaviour; building a ring of
// candidate points around it, binding them to a room and working along them is
// another, and an enemy can perfectly well have the first without the second.
//
// Without it an investigation is short and literal: she goes to the spot, looks
// around if she can, tries whatever second lead she has, and gives up. That is
// a real enemy rather than a broken one - the one you can hide from by not
// being exactly where you were heard.
//
// Navigation stays with the state. This decides where is worth going and in
// what order; getting there is the state's job whichever behaviours it has.
public sealed class EnemySearchRoute
{
    private readonly EnemyBrainContext context;
    private readonly EnemyInvestigationSearchPlanner planner = new();

    private int nextPointIndex;

    public EnemySearchRoute(EnemyBrainContext context)
    {
        this.context = context;
    }

    public int PointCount => planner.PointCount;

    // Builds a route around a centre and publishes it to the debug overlay and
    // the blackboard, which is where the editor tooling reads it from.
    public void Plan(Vector3 centre, NavMeshQueryFilter filter)
    {
        nextPointIndex = 0;

        planner.BuildHierarchicalSearchPlan(
            centre,
            context.Navigator.Position,
            context.Config.investigationBranchRadius,
            context.Config.investigationBranchPointCount,
            context.Config.investigationLeafRadius,
            context.Config.investigationLeafPointCountPerBranch,
            filter
        );

        context.InvestigationDebugData?.SetSearchPoints(planner.Points);
        context.InvestigationDebugData?.SetBoundRoom(planner.OriginRoom);
        context.Blackboard.SetCurrentInvestigationRoute(planner.Points);
    }

    // The next point worth walking to. The caller may refuse one it cannot path
    // to and ask again, which is why this hands back points rather than
    // destinations.
    public bool TryTakeNextPoint(out Vector3 point, out int routeIndex)
    {
        while (nextPointIndex < planner.PointCount)
        {
            routeIndex = nextPointIndex;
            nextPointIndex++;

            if (planner.TryGetPoint(routeIndex, out point))
            {
                return true;
            }
        }

        point = default;
        routeIndex = -1;
        return false;
    }

    public void Reset()
    {
        nextPointIndex = 0;
    }

    // Where to centre the ring, which is not the same as where she walked to.
    //
    // She already knows which way the target was moving when she last saw them:
    // the observation carries a forward beside its position, and until this was
    // written only the flank planner ever read it. The search did not, so the
    // ring was built evenly around the spot where they vanished - and half of
    // it therefore covered ground behind her, which is the one place a running
    // target provably is not. She spent the pause at those points looking at an
    // empty floor while the seconds the search is allowed ran out.
    //
    // Leaning the ring forward searches the half worth searching. The origin
    // she walked to is left alone: that is the last honest sighting, it is what
    // the hiding-place check and the debug overlay are about, and the target may
    // well still be standing on it.
    //
    // The push has to stay on floor she can walk. NavMesh.Raycast walks from the
    // origin towards the lead and stops at the first edge, so a target last seen
    // facing a wall shifts the ring as far as the wall and no further. Without
    // it the lead could land in the next room, and the planner binds the whole
    // route to whichever room its origin is in - she would have gone off to
    // search next door.
    public Vector3 GetLedCentre(Vector3 origin, NavMeshQueryFilter filter)
    {
        if (!context.TargetMemory.TryGetLastObservation(
                out EnemyTargetObservation observation) ||
            !EnemyInvestigationSearchPlanner.TryGetLeadOrigin(
                origin,
                observation.Position,
                observation.Forward,
                context.Config.investigationLeadDistance,
                context.Config.investigationBranchRadius,
                out Vector3 leadOrigin))
        {
            return origin;
        }

        if (!NavMesh.Raycast(origin, leadOrigin, out NavMeshHit hit, filter))
        {
            return leadOrigin;
        }

        // A query that could not start - the sighting was off the mesh -
        // reports a hit at infinity rather than an error. Anything past the
        // lead we asked for is not an answer, and NaN fails this too.
        return Vector3.Distance(hit.position, origin) <=
               context.Config.investigationLeadDistance
            ? hit.position
            : origin;
    }
}
