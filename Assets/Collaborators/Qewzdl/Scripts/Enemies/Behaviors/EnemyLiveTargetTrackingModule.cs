using UnityEngine;

// Knowing where you actually are for a few seconds after losing sight of you.
//
// The grace period exists either way: what this decides is what it is worth.
// With it, she follows the living target through walls for
// visualTargetMemoryDuration, which is the window in which you cannot shake her
// off - round a corner, behind a door, and she is still coming straight at you
// because she is pathing to where you are rather than to where you were.
//
// Without it she goes to the spot where sight broke and looks there. Turning a
// corner starts working. The chase becomes something you can win by breaking
// line of sight, instead of something you win by reaching a hiding place.
//
// Was visualMemoryTracksLiveTarget on the vision profile. The validation test's
// own list of fields that must not vary by difficulty already said why it does
// not belong there: turning it off "would be a different game, not an easier
// one", which is the definition of a behaviour rather than a lever.
[CreateAssetMenu(
    menuName = "Wherever I Am/Enemies/Behaviors/Live Target Tracking",
    fileName = "EnemyLiveTargetTrackingBehavior"
)]
public sealed class EnemyLiveTargetTrackingModule : EnemyBehaviorModule
{
    public override void Install(EnemyBehaviorInstaller installer)
    {
        installer.Context.PerceptionMemory?.InstallLiveTargetTracking();
    }
}
