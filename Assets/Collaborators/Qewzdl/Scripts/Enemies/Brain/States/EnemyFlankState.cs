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
    private const float BodyHeight = 1.8f;
    private const float RepathInterval = 0.5f;
    private const float ArrivalDistance = 1.2f;

    private const float RouteSampleSpacing = 1.5f;
    private const int MaxRouteSamples = 48;

    private readonly EnemyBrainContext context;
    private readonly NavMeshPath routePath = new();

    private Vector3 flankPoint;
    private bool hasFlankPoint;
    private float repathTimer;
    private float giveUpTimer;

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
        // else rather than walking the rest of the way in full view.
        if (PlayerGazeNetwork.IsBodySeenByAnyone(selfPosition, BodyHeight))
        {
            context.ChangeState(EnemyState.Retreat);
            return;
        }

        giveUpTimer += deltaTime;

        if (giveUpTimer >= context.Config.flankTimeout)
        {
            context.ChangeState(EnemyState.Stalk);
            return;
        }

        repathTimer -= deltaTime;

        if (!hasFlankPoint || repathTimer <= 0f)
        {
            repathTimer = RepathInterval;
            hasFlankPoint =
                TryFindPointBehind(player, selfPosition, out flankPoint);
        }

        if (!hasFlankPoint)
        {
            context.ChangeState(EnemyState.Stalk);
            return;
        }

        if (Vector3.Distance(selfPosition, flankPoint) <= ArrivalDistance)
        {
            context.ChangeState(EnemyState.Ambush);
            return;
        }

        context.TryMoveTo(flankPoint, context.Config.chaseSpeed);
    }

    public void Exit()
    {
        hasFlankPoint = false;
    }

    // Walks the route the agent would actually take and asks whether the
    // player could see the enemy standing anywhere along it. Sampled rather
    // than continuous: a stride is about a metre, and a gap the enemy crosses
    // between two samples is a glimpse, not a sighting.
    private bool IsRouteHidden(
        PlayerGazeNetwork player,
        Vector3 from,
        Vector3 to
    )
    {
        if (!NavMesh.CalculatePath(from, to, NavMesh.AllAreas, routePath) ||
            routePath.status != NavMeshPathStatus.PathComplete)
        {
            return false;
        }

        Vector3[] corners = routePath.corners;
        int budget = MaxRouteSamples;

        for (int i = 1; i < corners.Length && budget > 0; i++)
        {
            Vector3 previous = corners[i - 1];
            Vector3 corner = corners[i];
            float length = Vector3.Distance(previous, corner);
            int steps = Mathf.Max(1, Mathf.CeilToInt(length / RouteSampleSpacing));

            for (int step = 1; step <= steps && budget > 0; step++)
            {
                budget--;

                Vector3 sample = Vector3.Lerp(previous, corner, step / (float)steps);

                if (player.CanSeeBody(sample, BodyHeight))
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
        Vector3 playerPosition = player.transform.position;
        Vector3 behind = -player.transform.forward;
        behind.y = 0f;
        behind.Normalize();

        float preferred = context.Config.flankBehindDistance;

        // Distances as well as angles. Five points at one radius all land in
        // the same wall in a corridor, and the enemy gave up and went back to
        // watching - which is what the first build did in a real level. Closer
        // rings are worse ambush spots but a great deal better than none.
        for (int r = 0; r < DistanceScales.Length; r++)
        {
            float distance = preferred * DistanceScales[r];

            for (int i = 0; i < BehindAngles.Length; i++)
            {
                Vector3 direction =
                    Quaternion.Euler(0f, BehindAngles[i], 0f) * behind;

                if (!NavMesh.SamplePosition(
                        playerPosition + direction * distance,
                        out NavMeshHit hit,
                        distance * 0.5f,
                        NavMesh.AllAreas))
                {
                    continue;
                }

                // No use walking to a spot the player is already looking at.
                if (player.CanSeeBody(hit.position, BodyHeight))
                {
                    continue;
                }

                // Nor to a hidden spot by a route that crosses the player's
                // view on the way. The shortest path to somewhere behind
                // someone usually goes straight past their face, which is how
                // an enemy circling a player who never moved kept walking
                // back into sight and starting over.
                if (!IsRouteHidden(player, selfPosition, hit.position))
                {
                    continue;
                }

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
