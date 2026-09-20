using UnityEngine;

// Actually laying hands on somebody once she has caught them.
//
// The one state nothing else falls back to, and the easiest of all of them to
// leave out: only chasing ever asks for it, and only when the target is already
// in reach. Without it she runs you down, arrives, and keeps running you down -
// which is the enemy you want when the threat is being cornered rather than
// being caught.
//
// Falls back to chasing rather than nowhere, so that leaving it out is a
// configuration rather than a stuck transition somebody has to read a log to
// understand.
[CreateAssetMenu(
    menuName = "Wherever I Am/Enemies/Behaviors/Attack",
    fileName = "EnemyAttackBehavior"
)]
public sealed class EnemyAttackBehaviorModule : EnemyBehaviorModule
{
    public override void Install(EnemyBehaviorInstaller installer)
    {
        installer.AddState(new EnemyAttackState(installer.Context));
    }
}
