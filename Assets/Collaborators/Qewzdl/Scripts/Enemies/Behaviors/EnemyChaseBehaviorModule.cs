using UnityEngine;

// Going straight at somebody she can see.
//
// Was half of a module called Core, which bundled this with attacking and with
// standing still on the grounds that every fallback chain ended in one of the
// three. That is true of standing still and was never true of the other two:
// chasing and striking are things an enemy does, and an enemy that does one
// without the other is a perfectly good enemy. A watcher that follows you
// around the house and never lays a hand on you is a design, not a bug.
//
// Without it a confirmed target falls through to investigating - she knows
// where you are, comes to look, and never breaks into a run. Without
// investigating either, she goes back to her round. That chain is why chasing
// can be left out at all.
[CreateAssetMenu(
    menuName = "Wherever I Am/Enemies/Behaviors/Chase",
    fileName = "EnemyChaseBehavior"
)]
public sealed class EnemyChaseBehaviorModule : EnemyBehaviorModule
{
    public override void Install(EnemyBehaviorInstaller installer)
    {
        installer.AddState(new EnemyChaseState(installer.Context));
    }
}
