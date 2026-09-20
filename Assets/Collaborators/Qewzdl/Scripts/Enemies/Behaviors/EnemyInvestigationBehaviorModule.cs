using UnityEngine;

// Goes to look when a noise, or somebody vanishing, gives it somewhere to look.
//
// What it does once it gets there is partly this state and partly whatever
// capabilities are installed alongside it - checking a hiding place is the
// first of those, and an investigation without it searches the room and walks
// past the box. That separation is the whole reason capabilities exist: looking
// for somebody and knowing how to open a crate are different behaviours that
// happen to run at the same moment.
[CreateAssetMenu(
    menuName = "Wherever I Am/Enemies/Behaviors/Investigation",
    fileName = "EnemyInvestigationBehavior"
)]
public sealed class EnemyInvestigationBehaviorModule : EnemyBehaviorModule
{
    public override void Install(EnemyBehaviorInstaller installer)
    {
        installer.AddState(new EnemyInvestigateState(installer.Context));
    }
}
