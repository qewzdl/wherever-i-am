using UnityEngine;

// Standing still, walking at somebody, and hitting them.
//
// Its own module rather than something the brain always installs, because the
// point of the list is that the list is the whole answer: a reader who wants to
// know what an enemy can do should not have to know that three of the nine
// states are installed somewhere else for reasons of history.
//
// That said, leaving it out is almost certainly a mistake, and one that is
// silent in play rather than loud at build time - the enemy simply never does
// anything. EnemyStateRules' fallbacks all end at these three, so an enemy
// without them has nowhere to land when a behaviour it does not have is asked
// for. ProjectAssetValidationTests refuses a config that omits it.
[CreateAssetMenu(
    menuName = "Wherever I Am/Enemies/Behaviors/Core",
    fileName = "EnemyCoreBehavior"
)]
public sealed class EnemyCoreBehaviorModule : EnemyBehaviorModule
{
    public override void Install(EnemyBehaviorInstaller installer)
    {
        installer.AddState(new EnemyIdleState(installer.Context));
        installer.AddState(new EnemyChaseState(installer.Context));
        installer.AddState(new EnemyAttackState(installer.Context));
    }
}
