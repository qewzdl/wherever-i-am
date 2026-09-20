using UnityEngine;

// What an enemy is, when nobody has said otherwise.
//
// A config with an empty module list gets this. That leniency is deliberate -
// the list is newer than every config in the project, and reading empty as "no
// behaviours" would have stopped every enemy in the game dead - but it puts a
// duty on this file: the default set has to stay equal to what an enemy could
// do before the list existed. Anything short of that takes a behaviour away
// from every config nobody has got round to filling in, and takes it away
// silently, because an enemy that no longer checks boxes still looks like an
// enemy searching a room.
//
// That is not a hypothetical either. The hiding place check was left out of
// this set on the first attempt, for exactly one reason: it had been extracted
// out of the investigating state that day, so it did not look like one of the
// four things the brain used to install. It used to come free with every enemy
// in the game.
//
// Its own file rather than a method on the brain so that a test can ask what a
// default enemy is without building one.
public static class EnemyDefaultBehaviors
{
    public static void Install(EnemyBehaviorInstaller installer)
    {
        Install<EnemyCoreBehaviorModule>(installer);
        Install<EnemyPatrolBehaviorModule>(installer);
        Install<EnemyInvestigationBehaviorModule>(installer);
        Install<EnemyStealthManeuverBehaviorModule>(installer);
        Install<EnemyHidingPlaceCheckModule>(installer);
    }

    // Written as module instances rather than as another copy of what each one
    // installs, so that there is exactly one description of every behaviour and
    // this cannot drift away from the assets.
    private static void Install<T>(EnemyBehaviorInstaller installer)
        where T : EnemyBehaviorModule
    {
        T module = ScriptableObject.CreateInstance<T>();

        try
        {
            module.Install(installer);
        }
        finally
        {
            // The runtime objects it made outlive it; the module itself was
            // only ever a factory, and a ScriptableObject nobody destroys is a
            // leak per enemy per match.
            //
            // Both spellings, because this runs in edit mode too - the tests
            // build brains - and Destroy throws there while DestroyImmediate is
            // refused during play.
            if (Application.isPlaying)
            {
                Object.Destroy(module);
            }
            else
            {
                Object.DestroyImmediate(module);
            }
        }
    }
}
