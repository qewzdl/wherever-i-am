using UnityEngine;

// Lets an enemy know that people climb into boxes.
//
// A capability rather than a state: the investigation state drives it, and an
// investigation that cannot find one searches the room and walks past the box.
// So this switches off cleanly - the enemy without it is one you can hide from
// in plain sight, which is a design lever rather than a fault.
//
// Installs nothing else. The reference it works from is written by perception,
// which records a pursued target vanishing into a place while this enemy was
// watching, and that happens whether or not this module is installed. An enemy
// without it simply never does anything with the note.
[CreateAssetMenu(
    menuName = "Wherever I Am/Enemies/Behaviors/Hiding Place Check",
    fileName = "EnemyHidingPlaceCheckBehavior"
)]
public sealed class EnemyHidingPlaceCheckModule : EnemyBehaviorModule
{
    public override void Install(EnemyBehaviorInstaller installer)
    {
        installer.AddCapability(new EnemyHidingPlaceCheck(installer.Context));
    }
}
