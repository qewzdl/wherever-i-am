using UnityEngine;

// Stopping in place behind somebody and waiting for them to turn round.
//
// The leaf of the sneaking sequence - only flanking reaches it, and it only
// ever goes back to flanking - which makes it the easiest of the four to leave
// out. Without it a completed flank becomes a charge: she comes round behind
// you and takes you immediately instead of standing in the dark waiting to be
// noticed.
//
// Which of those is more frightening is a real question rather than a
// rhetorical one, and the point of it being a module is that it can be answered
// by trying both.
[CreateAssetMenu(
    menuName = "Wherever I Am/Enemies/Behaviors/Ambush",
    fileName = "EnemyAmbushBehavior"
)]
public sealed class EnemyAmbushBehaviorModule : EnemyBehaviorModule
{
    public override void Install(EnemyBehaviorInstaller installer)
    {
        installer.AddState(new EnemyAmbushState(installer.Context));
    }
}
