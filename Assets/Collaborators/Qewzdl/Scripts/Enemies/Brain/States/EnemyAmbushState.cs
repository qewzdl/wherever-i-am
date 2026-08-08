using UnityEngine;

// Stands behind the target and waits for it to turn round.
//
// The moment it is looked at, it goes. Handing over to chasing rather than
// attacking here on purpose: chasing already knows how to close the last
// couple of metres and hand over to the attack, and a second copy of that
// would be a second thing to keep correct.
public sealed class EnemyAmbushState : IEnemyStateHandler
{
    private const float BodyHeight = 1.8f;
    private const float FacingDegreesPerSecond = 260f;

    private readonly EnemyBrainContext context;

    private float waitTimer;

    public EnemyState State => EnemyState.Ambush;

    public EnemyAmbushState(EnemyBrainContext context)
    {
        this.context = context;
    }

    public void Enter()
    {
        waitTimer = 0f;
        context.StopNavigation();
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

        FacePlayer(player.transform.position, selfPosition, deltaTime);

        // Turned round and seen it. This is the beat the whole sequence was
        // built for.
        if (PlayerGazeNetwork.IsBodySeenByAnyone(selfPosition, BodyHeight))
        {
            context.ChangeState(EnemyState.Chase);
            return;
        }

        // Walked off without ever turning round. Standing here forever would
        // leave the enemy behind a player who is now in another room.
        waitTimer += deltaTime;

        if (waitTimer >= context.Config.ambushPatience ||
            Vector3.Distance(selfPosition, player.transform.position) >
            context.Config.stalkInsteadOfChasingDistance)
        {
            context.ChangeState(EnemyState.Stalk);
        }
    }

    public void Exit()
    {
        waitTimer = 0f;
    }

    private void FacePlayer(
        Vector3 playerPosition,
        Vector3 selfPosition,
        float deltaTime
    )
    {
        Vector3 toPlayer = playerPosition - selfPosition;
        toPlayer.y = 0f;

        context.Navigator.FaceDirection(
            toPlayer,
            FacingDegreesPerSecond,
            deltaTime
        );
    }
}
