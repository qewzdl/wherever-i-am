using UnityEngine;
using UnityEngine.AI;

// Breaks off once it has been noticed. Backs away from the target until it is
// no longer in anyone's view, then hands back to stalking to come round from
// somewhere else.
public sealed class EnemyRetreatState : IEnemyStateHandler
{
    private const float RepathInterval = 0.4f;
    private const float ArrivalDistance = 1.2f;

    private readonly EnemyBrainContext context;

    private float repathTimer;
    private float unseenTimer;
    private Vector3 retreatPoint;
    private bool hasRetreatPoint;

    public EnemyState State => EnemyState.Retreat;

    public EnemyRetreatState(EnemyBrainContext context)
    {
        this.context = context;
    }

    public void Enter()
    {
        repathTimer = 0f;
        unseenTimer = 0f;
        hasRetreatPoint = false;
    }

    public void Tick(float deltaTime)
    {
        Vector3 selfPosition = context.Navigator.Position;

        // Deliberately does not need a live target. Breaking sight is this
        // state's entire job, so the target is lost partway through almost
        // every time - requiring it meant the retreat abandoned itself and
        // went off to search instead of finishing and going round.
        if (!PlayerGazeNetwork.TryGetNearest(
                selfPosition,
                out PlayerGazeNetwork player))
        {
            context.ChangeState(EnemyState.Investigate);
            return;
        }

        if (PlayerGazeNetwork.IsBodySeenByAnyone(selfPosition, context.Navigator.BodyHeight))
        {
            unseenTimer = 0f;
        }
        else
        {
            unseenTimer += deltaTime;

            // Out of sight long enough to move. Going round rather than
            // back to watching: returning to stalking made a closed loop -
            // watch, be seen, back off, watch again - which is what the first
            // build did, seven times over, and it reads as aimless wandering.
            if (unseenTimer >= context.Config.retreatBrokenSightDuration)
            {
                context.ChangeState(EnemyState.Flank);
                return;
            }
        }

        repathTimer -= deltaTime;

        if (repathTimer > 0f)
        {
            return;
        }

        repathTimer = RepathInterval;

        // Keep the spot once it is chosen. Picking again every repath meant a
        // different corner of the fan each time, and the enemy walked a
        // circuit around the player instead of leaving.
        Vector3 playerPosition = player.transform.position;

        if (hasRetreatPoint &&
            Vector3.Distance(selfPosition, retreatPoint) > ArrivalDistance &&
            // The point was chosen against where they stood then. They move,
            // and a spot that led away a moment ago can lead straight back
            // past them.
            !EnemyStateRules.ClosesOnWatcher(
                selfPosition,
                retreatPoint,
                playerPosition) &&
            !PlayerGazeNetwork.IsBodySeenByAnyone(
                retreatPoint,
                context.Navigator.BodyHeight))
        {
            context.TryMoveTo(retreatPoint, context.Config.chaseSpeed);
            return;
        }

        hasRetreatPoint = TryFindRetreatPoint(
            selfPosition,
            playerPosition,
            out retreatPoint);

        if (hasRetreatPoint)
        {
            context.TryMoveTo(retreatPoint, context.Config.chaseSpeed);
            return;
        }

        // Nothing to move to that leads away. Standing still beats walking in
        // the one direction this state exists to avoid.
        context.StopNavigation();
    }

    public void Exit()
    {
        repathTimer = 0f;
        unseenTimer = 0f;
        hasRetreatPoint = false;
    }

    // Straight away from someone looking at you keeps you dead centre in
    // their view the whole way - which is why the first build appeared to
    // hide in plain sight. Sideways is what actually leaves a cone, so the
    // lateral options are tried first and a candidate the player can still
    // see is not a retreat at all.
    private bool TryFindRetreatPoint(
        Vector3 selfPosition,
        Vector3 targetPosition,
        out Vector3 retreatPoint
    )
    {
        Vector3 away = selfPosition - targetPosition;
        away.y = 0f;

        // Standing exactly on the target is not a direction; anywhere will do.
        if (away.sqrMagnitude < 0.01f)
        {
            away = Vector3.forward;
        }

        away.Normalize();

        float preferred = context.Config.retreatDistance;
        float bodyHeight = context.Navigator.BodyHeight;
        bool hasFallback = false;
        Vector3 fallback = default;

        for (int r = 0; r < DistanceScales.Length; r++)
        {
            float distance = preferred * DistanceScales[r];

            for (int i = 0; i < RetreatAngles.Length; i++)
            {
                Vector3 direction =
                    Quaternion.Euler(0f, RetreatAngles[i], 0f) * away;

                if (!NavMesh.SamplePosition(
                        selfPosition + direction * distance,
                        out NavMeshHit hit,
                        distance * 0.5f,
                        NavMesh.AllAreas))
                {
                    continue;
                }

                // The wide angles swing round the target rather than away
                // from it, and at this state's distances they can finish on
                // the far side - further off than they started, having walked
                // right through the target on the way. A retreat that passes
                // closer than the gap it started with is not a retreat.
                if (EnemyStateRules.ClosesOnWatcher(
                        selfPosition,
                        hit.position,
                        targetPosition))
                {
                    continue;
                }

                if (!PlayerGazeNetwork.IsBodySeenByAnyone(hit.position, bodyHeight))
                {
                    retreatPoint = hit.position;
                    return true;
                }

                // Nowhere out of sight yet. Remember the first reachable spot
                // so the enemy keeps moving instead of standing in the open
                // waiting for a perfect answer that this room may not have.
                if (!hasFallback)
                {
                    hasFallback = true;
                    fallback = hit.position;
                }
            }
        }

        retreatPoint = fallback;
        return hasFallback;
    }

    // Sideways before backwards. Leaving a hundred and twenty degree cone is
    // a matter of getting out of its edge, and walking straight back does not
    // do that at any distance.
    private static readonly float[] RetreatAngles =
        { -90f, 90f, -135f, 135f, -45f, 45f, 0f };

    private static readonly float[] DistanceScales = { 1f, 0.6f, 0.35f };
}
