using UnityEngine;

// Getting past the furniture a player dragged into the doorway.
//
// Two things, on one switch, because the second only happens during the first:
// lifting the NavMesh carving on blocking items so that a route through them
// exists at all, and occasionally shoving one bodily out of the way. How often
// she shoves, how hard and from how far are tuning and stay on EnemyConfig;
// whether she can do it at all is this.
//
// Switching it off hands the player a real barricade - an enemy who stops at
// the wardrobe you pushed across the door instead of walking through it. That
// is a design lever rather than a degradation, but it is a big one, and worth
// knowing you are pulling: the states that ask for push-through are chasing and
// attacking, so this decides whether a barricade can end a pursuit.
[CreateAssetMenu(
    menuName = "Wherever I Am/Enemies/Behaviors/Item Pushing",
    fileName = "EnemyItemPushingBehavior"
)]
public sealed class EnemyItemPushingModule : EnemyBehaviorModule
{
    public override void Install(EnemyBehaviorInstaller installer)
    {
        installer.Context.Navigator?.InstallItemPushing();
    }
}
