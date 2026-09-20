using UnityEngine;

// Every behaviour there is, in one call.
//
// Used by tests, and by nothing that ships. It was EnemyDefaultBehaviors, and
// the brain installed it for any config with an empty list - which made it the
// answer to "what is an enemy when nobody said". There is no such enemy any
// more: an empty list now means an enemy that does nothing, and a config nobody
// filled in is caught by ProjectAssetValidationTests rather than quietly
// substituted for at runtime.
//
// What is left is the other job it was doing, which is worth keeping. Several
// tests build a navigator or a state by hand, with no config and no modules,
// and they are about pathing or about searching rather than about which
// behaviours an enemy was given. Those need the full set, and they need it to
// keep up on its own when a behaviour is added - a list copied out of here
// would not.
//
// Standing still is missing from this on purpose. It is not a behaviour and
// there is no module for it; the brain installs EnemyIdleState itself, because
// a fallback chain has to end somewhere.
public static class EnemyEveryBehavior
{
    public static void Install(EnemyBehaviorInstaller installer)
    {
        Install<EnemySightModule>(installer);
        Install<EnemyHearingModule>(installer);
        Install<EnemyChaseBehaviorModule>(installer);
        Install<EnemyAttackBehaviorModule>(installer);
        Install<EnemyPatrolBehaviorModule>(installer);
        Install<EnemyInvestigationBehaviorModule>(installer);
        Install<EnemyStalkBehaviorModule>(installer);
        Install<EnemyRetreatBehaviorModule>(installer);
        Install<EnemyFlankBehaviorModule>(installer);
        Install<EnemyAmbushBehaviorModule>(installer);
        Install<EnemyHidingPlaceCheckModule>(installer);
        Install<EnemyLookAroundModule>(installer);
        Install<EnemySearchRouteModule>(installer);
        Install<EnemyDoorTraversalModule>(installer);
        Install<EnemyItemPushingModule>(installer);
        Install<EnemyCrawlingModule>(installer);
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
