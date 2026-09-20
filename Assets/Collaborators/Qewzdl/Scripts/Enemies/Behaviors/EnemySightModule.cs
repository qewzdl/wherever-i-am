using UnityEngine;

// Eyes.
//
// Without it she is blind, and blind is a very different enemy rather than a
// worse one: everything she knows about you arrives through sound, which turns
// the whole noise system - footsteps, doors, dropped things, and a winded
// player's own breathing - from a supplement to sight into the only thing that
// gives you away. A player who has learned to walk quietly stops being careful
// and starts being invisible.
//
// Safe to leave out because "saw nothing this tick" is what the stimulus
// resolver is handed most frames anyway. Switching sight off makes that answer
// permanent rather than introducing a new one.
[CreateAssetMenu(
    menuName = "Wherever I Am/Enemies/Behaviors/Sight",
    fileName = "EnemySightBehavior"
)]
public sealed class EnemySightModule : EnemyBehaviorModule
{
    public override void Install(EnemyBehaviorInstaller installer)
    {
        installer.Context.TargetDetector?.InstallSight();
    }
}
