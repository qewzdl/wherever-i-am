using UnityEngine;

// Opening doors and walking through them.
//
// A capability, and one that lives in the navigator rather than in the
// capability registry: the door handling is woven through pathing - it can stop
// a route mid-corner, override a destination, and hold the agent still while a
// door swings - and none of that is something a state could ask for after the
// fact. So the module switches it on where it runs.
//
// Without it a closed door is a wall. She will route around it if the house
// allows, and stand at it if it does not, which makes a door worth closing.
[CreateAssetMenu(
    menuName = "Wherever I Am/Enemies/Behaviors/Door Traversal",
    fileName = "EnemyDoorTraversalBehavior"
)]
public sealed class EnemyDoorTraversalModule : EnemyBehaviorModule
{
    public override void Install(EnemyBehaviorInstaller installer)
    {
        installer.Context.Navigator?.InstallDoorTraversal();
    }
}
