using UnityEngine;

// Looking for somebody around the place they were last heard, rather than only
// at it.
//
// The largest thing the investigating state used to do: a ring of candidate
// points built around the stimulus, bound to the room it is in, leaned in the
// direction the target was last seen moving, and walked in order.
//
// Without it an investigation is short and literal - go to the spot, look
// around if she can, try whatever second lead there is, give up. That is the
// enemy you can hide from by not being exactly where you were heard, which is a
// very different game from the one where standing still anywhere in the room is
// eventually fatal.
[CreateAssetMenu(
    menuName = "Wherever I Am/Enemies/Behaviors/Search Route",
    fileName = "EnemySearchRouteBehavior"
)]
public sealed class EnemySearchRouteModule : EnemyBehaviorModule
{
    public override void Install(EnemyBehaviorInstaller installer)
    {
        installer.AddCapability(new EnemySearchRoute(installer.Context));
    }
}
