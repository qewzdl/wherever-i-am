using UnityEngine;

// Going round to come at somebody from behind.
//
// The hub of the sneaking sequence: stalking leads here, retreating leads here,
// and lying in wait is only ever reached from here. Leaving it out therefore
// takes the back half of the manoeuvre with it - she will hang back and she
// will break off, and she will never appear behind you.
[CreateAssetMenu(
    menuName = "Wherever I Am/Enemies/Behaviors/Flank",
    fileName = "EnemyFlankBehavior"
)]
public sealed class EnemyFlankBehaviorModule : EnemyBehaviorModule
{
    public override void Install(EnemyBehaviorInstaller installer)
    {
        installer.AddState(new EnemyFlankState(installer.Context));
    }
}
