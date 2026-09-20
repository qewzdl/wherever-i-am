using UnityEngine;

// Stalking, breaking off, circling round, and lying in wait.
//
// Four states and one behaviour. They share a victim, one sighting of them and
// one deadline through EnemyStealthManeuver, and they run as a sequence, so
// installing some of them would leave a manoeuvre that starts and cannot
// finish. That is why this is one module with four AddState calls rather than
// four modules somebody could pick from.
//
// Without it the enemy walks straight at whoever it has seen, which is the
// behaviour this game shipped with before any of these four existed.
[CreateAssetMenu(
    menuName = "Wherever I Am/Enemies/Behaviors/Stealth Maneuvers",
    fileName = "EnemyStealthManeuverBehavior"
)]
public sealed class EnemyStealthManeuverBehaviorModule : EnemyBehaviorModule
{
    public override void Install(EnemyBehaviorInstaller installer)
    {
        installer.AddState(new EnemyStalkState(installer.Context));
        installer.AddState(new EnemyRetreatState(installer.Context));
        installer.AddState(new EnemyFlankState(installer.Context));
        installer.AddState(new EnemyAmbushState(installer.Context));
    }
}
