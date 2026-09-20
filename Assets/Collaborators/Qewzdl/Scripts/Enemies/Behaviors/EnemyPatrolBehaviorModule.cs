using UnityEngine;

// Walks a route when there is nothing better to do.
//
// Without it the enemy stands where it was left until something happens, which
// is what you want for one posted in a room rather than roaming a house.
[CreateAssetMenu(
    menuName = "Wherever I Am/Enemies/Behaviors/Patrol",
    fileName = "EnemyPatrolBehavior"
)]
public sealed class EnemyPatrolBehaviorModule : EnemyBehaviorModule
{
    public override void Install(EnemyBehaviorInstaller installer)
    {
        installer.AddState(new EnemyPatrolState(installer.Context));
    }
}
