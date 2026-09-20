using UnityEngine;

// Hanging back and watching, instead of running straight at somebody.
//
// The way into the whole sneaking sequence: nothing else in it is reachable
// without this, because the brain only ever enters the manoeuvre here. The
// other three are what she does once she has decided not to charge.
//
// Was one quarter of a module called StealthManeuvers, which installed all four
// on the grounds that they share a victim, one sighting and one deadline. They
// do share those. What that argument missed is that the fallback chain already
// covers a phase being absent, so each of the four can be left out and still
// leave a coherent enemy - and an enemy who stalks but never circles round is a
// different threat from one who does both.
[CreateAssetMenu(
    menuName = "Wherever I Am/Enemies/Behaviors/Stalk",
    fileName = "EnemyStalkBehavior"
)]
public sealed class EnemyStalkBehaviorModule : EnemyBehaviorModule
{
    public override void Install(EnemyBehaviorInstaller installer)
    {
        installer.AddState(new EnemyStalkState(installer.Context));
    }
}
