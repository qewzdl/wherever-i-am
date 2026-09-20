using UnityEngine;

// Stopping when she arrives somewhere and turning her head both ways.
//
// Without it she never pauses: she walks her search route straight through,
// which is faster and worse, because a moving vision cone drags past the
// corners a stationary one covers. A lever on how thorough an enemy is rather
// than a switch between working and broken.
[CreateAssetMenu(
    menuName = "Wherever I Am/Enemies/Behaviors/Look Around",
    fileName = "EnemyLookAroundBehavior"
)]
public sealed class EnemyLookAroundModule : EnemyBehaviorModule
{
    public override void Install(EnemyBehaviorInstaller installer)
    {
        installer.AddCapability(new EnemyLookAround(installer.Context));
    }
}
