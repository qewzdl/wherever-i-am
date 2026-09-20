using UnityEngine;

// Breaking off when she realises she has been seen.
//
// Reached from stalking and from a flank that got spotted on the way round.
// Without it she is a watcher who keeps watching after you have looked straight
// at her, which is a slower, more brazen enemy: you always know where she is,
// and she never gives you the moment of losing her.
[CreateAssetMenu(
    menuName = "Wherever I Am/Enemies/Behaviors/Retreat",
    fileName = "EnemyRetreatBehavior"
)]
public sealed class EnemyRetreatBehaviorModule : EnemyBehaviorModule
{
    public override void Install(EnemyBehaviorInstaller installer)
    {
        installer.AddState(new EnemyRetreatState(installer.Context));
    }
}
