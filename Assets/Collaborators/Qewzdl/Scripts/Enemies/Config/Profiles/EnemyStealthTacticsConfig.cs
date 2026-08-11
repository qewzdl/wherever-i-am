using UnityEngine;

// How the enemy sneaks, as opposed to what it can see.
//
// All of this lived in two places it did not belong. The distances and
// timeouts sat in EnemyVisionConfig, which is a description of an eye and had
// no business holding a decision about when to give up going round behind
// somebody. The rest - repath intervals, the fans of angles and distances,
// how far apart two enemies must claim their spots, how many raycasts one
// search may spend - was written into the state handlers as constants, so
// tuning the behaviour meant editing code and rebuilding.
//
// Optional. An enemy with no tactics profile assigned sneaks by the defaults
// below, which are the values those two places held.
[CreateAssetMenu(
    menuName = "Wherever I Am/Enemies/Profiles/Enemy Stealth Tactics Config",
    fileName = "EnemyStealthTacticsConfig"
)]
public class EnemyStealthTacticsConfig : ScriptableObject
{
    [Header("Manoeuvre")]
    [Tooltip("How long the whole sequence - watch, break off, come round, " +
             "wait - may run before the enemy gives up sneaking and comes " +
             "straight at the target. Without one overall budget Retreat and " +
             "Flank hand back and forth for as long as the level offers no " +
             "way round, each resetting the other's timer.")]
    [Min(1f)] public float maneuverTimeout = 25f;

    [Tooltip("How old the observed pose of the target may be and still be " +
             "worth planning against. Flanking loses sight on purpose, so " +
             "this is generous - it exists to stop a pose from a minute ago " +
             "surviving several Retreat/Flank cycles.")]
    [Min(0.5f)] public float observationMaxAge = 10f;

    [Header("Stalking")]
    [Tooltip("Closer than this, a seen target is simply chased.")]
    [Min(0f)] public float chaseWithoutStalkingDistance = 7.5f;

    [Tooltip("Further than this, a seen target is stalked instead.")]
    [Min(0f)] public float stalkInsteadOfChasingDistance = 10.5f;

    [Tooltip("How long the target has to look at the enemy for it to count " +
             "as having been noticed.")]
    [Min(0f)] public float stalkNoticedDuration = 0.4f;

    [Header("Retreat")]
    [Tooltip("How long out of sight before a retreat is over.")]
    [Min(0f)] public float retreatBrokenSightDuration = 1.2f;

    [Tooltip("How long to spend trying to break sight before giving up.")]
    [Min(1f)] public float retreatTimeout = 8f;

    [Tooltip("How far the enemy tries to put between itself and its " +
             "watchers while breaking off.")]
    [Min(1f)] public float retreatDistance = 8f;

    [Min(0.05f)] public float retreatRepathInterval = 0.4f;

    [Tooltip("Sideways before backwards. Leaving a view cone is a matter of " +
             "getting out of its edge, and walking straight back does not do " +
             "that at any distance.")]
    public float[] retreatAngles = { -90f, 90f, -135f, 135f, -45f, 45f, 0f };

    public float[] retreatDistanceScales = { 1f, 0.6f, 0.35f };

    [Header("Flank")]
    [Tooltip("How far behind the target the enemy tries to come to rest.")]
    [Min(1f)] public float flankBehindDistance = 3.5f;

    [Tooltip("How long to spend going round before giving up.")]
    [Min(1f)] public float flankTimeout = 12f;

    [Min(0.05f)] public float flankRepathInterval = 0.5f;

    [Tooltip("Straight behind first, then wider round either side. A point " +
             "directly behind is often against a wall.")]
    public float[] flankBehindAngles = { 0f, -35f, 35f, -70f, 70f, -105f, 105f };

    [Tooltip("Distances as well as angles. Five points at one radius all " +
             "land in the same wall in a corridor.")]
    public float[] flankDistanceScales = { 1f, 0.65f, 0.4f };

    [Header("Ambush")]
    [Tooltip("How long to wait behind a target that never turns round.")]
    [Min(1f)] public float ambushPatience = 8f;

    [Header("Planning budgets")]
    [Tooltip("How close a claimed spot has to be to another enemy's before " +
             "the two are taken to be the same spot.")]
    [Min(0.1f)] public float claimSpacing = 2f;

    [Tooltip("How near the chosen point counts as arrived.")]
    [Min(0.1f)] public float arrivalDistance = 1.2f;

    [Tooltip("How far apart a route is sampled when asking whether the enemy " +
             "would be seen walking it.")]
    [Min(0.25f)] public float routeSampleSpacing = 1.5f;

    [Tooltip("Raycast allowance a planning tick reserves for route checks, " +
             "on top of one per candidate for the endpoints. Endpoints and " +
             "routes then draw on it together, and a route needing more than " +
             "one tick holds is resumed where it stopped rather than " +
             "restarted - so this sizes a tick, not a whole search. Clamped " +
             "to what the server hands out in a frame, because asking for " +
             "more only gets the request cut down.")]
    [Min(1)] public int routeSampleBudget = 48;

    [Tooltip("How many candidate points one search may test per tick. The " +
             "rest are resumed from where the last tick stopped rather than " +
             "starting the same exhausted fan again.")]
    [Min(1)] public int candidatesPerTick = 8;

    public void Validate()
    {
        maneuverTimeout = Mathf.Max(1f, maneuverTimeout);
        observationMaxAge = Mathf.Max(0.5f, observationMaxAge);

        chaseWithoutStalkingDistance =
            Mathf.Max(0f, chaseWithoutStalkingDistance);

        // The band has to have width. Equal values are a single threshold,
        // and a single threshold flips the enemy between chasing and stalking
        // every time the target drifts across it.
        stalkInsteadOfChasingDistance = Mathf.Max(
            stalkInsteadOfChasingDistance,
            chaseWithoutStalkingDistance + 1f
        );

        stalkNoticedDuration = Mathf.Max(0f, stalkNoticedDuration);
        retreatBrokenSightDuration =
            Mathf.Max(0f, retreatBrokenSightDuration);

        // Has to outlast the wait for sight to break, or the retreat gives up
        // before the outcome it exists to reach can happen at all.
        retreatTimeout = Mathf.Max(
            1f,
            retreatBrokenSightDuration + 1f,
            retreatTimeout
        );

        retreatDistance = Mathf.Max(1f, retreatDistance);
        retreatRepathInterval = Mathf.Max(0.05f, retreatRepathInterval);

        flankBehindDistance = Mathf.Max(1f, flankBehindDistance);
        flankTimeout = Mathf.Max(1f, flankTimeout);
        flankRepathInterval = Mathf.Max(0.05f, flankRepathInterval);

        ambushPatience = Mathf.Max(1f, ambushPatience);

        claimSpacing = Mathf.Max(0.1f, claimSpacing);
        arrivalDistance = Mathf.Max(0.1f, arrivalDistance);
        routeSampleSpacing = Mathf.Max(0.25f, routeSampleSpacing);
        candidatesPerTick = Mathf.Max(1, candidatesPerTick);

        // A planning tick reserves one raycast per candidate for the endpoints
        // plus this for the routes, and the server hands out a fixed number of
        // them per frame. Anything above that is not a larger allowance, it is
        // a request the scheduler cuts down - so the inspector would be
        // showing a number the search never gets.
        routeSampleBudget = Mathf.Clamp(
            routeSampleBudget,
            1,
            Mathf.Max(
                1,
                EnemyServerPerceptionScheduler.VisibilityQueriesPerFrame -
                candidatesPerTick
            )
        );

        // A phase that cannot outlast the whole manoeuvre never reaches its
        // own ending, and one that outlasts it by a lot makes the overall
        // budget the only thing that ever fires.
        maneuverTimeout = Mathf.Max(
            maneuverTimeout,
            retreatTimeout + flankTimeout
        );

        retreatAngles = NonEmpty(retreatAngles, new[] { -90f, 90f, 0f });
        retreatDistanceScales = NonEmpty(
            retreatDistanceScales,
            new[] { 1f, 0.6f, 0.35f }
        );
        flankBehindAngles = NonEmpty(
            flankBehindAngles,
            new[] { 0f, -35f, 35f }
        );
        flankDistanceScales = NonEmpty(
            flankDistanceScales,
            new[] { 1f, 0.65f, 0.4f }
        );
    }

    // An empty fan is a search with nothing to search, and the state reads
    // that as "nowhere to go" and charges. Authoring one by accident is a
    // single delete in the inspector.
    private static float[] NonEmpty(float[] values, float[] fallback)
    {
        return values != null && values.Length > 0 ? values : fallback;
    }

    private void OnValidate()
    {
        Validate();
    }
}
