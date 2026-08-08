using UnityEngine;
using UnityEngine.AI;

// Breaks off once it has been noticed. Backs away from the target until it is
// no longer in anyone's view, then hands back to stalking to come round from
// somewhere else.
public sealed class EnemyRetreatState : IEnemyStateHandler
{
    private const float BodyHeight = 1.8f;
    private const float RepathInterval = 0.4f;

    private readonly EnemyBrainContext context;

    private float repathTimer;
    private float unseenTimer;

    public EnemyState State => EnemyState.Retreat;

    public EnemyRetreatState(EnemyBrainContext context)
    {
        this.context = context;
    }

    public void Enter()
    {
        repathTimer = 0f;
        unseenTimer = 0f;
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

        if (PlayerGazeNetwork.IsBodySeenByAnyone(selfPosition, BodyHeight))
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

        if (TryFindRetreatPoint(
                selfPosition,
                player.transform.position,
                out Vector3 retreat))
        {
            context.TryMoveTo(retreat, context.Config.chaseSpeed);
        }
    }

    public void Exit()
    {
        repathTimer = 0f;
        unseenTimer = 0f;
    }

    // Straight away from the target, and if the level does not allow that, off
    // to either side of it. Backing into a corner and staying visible is the
    // failure this guards against.
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

        float distance = context.Config.retreatDistance;

        for (int i = 0; i < RetreatAngles.Length; i++)
        {
            Vector3 direction = Quaternion.Euler(0f, RetreatAngles[i], 0f) * away;

            if (NavMesh.SamplePosition(
                    selfPosition + direction * distance,
                    out NavMeshHit hit,
                    distance * 0.5f,
                    NavMesh.AllAreas))
            {
                retreatPoint = hit.position;
                return true;
            }
        }

        retreatPoint = default;
        return false;
    }

    private static readonly float[] RetreatAngles = { 0f, -40f, 40f, -75f, 75f };
}
