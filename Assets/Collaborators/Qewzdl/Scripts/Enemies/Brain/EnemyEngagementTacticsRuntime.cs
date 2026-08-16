using System.Collections.Generic;
using UnityEngine;

// How this enemy has decided to go about its current target.
public enum EnemyPursuitIntent
{
    // Watch, break off, come round behind. The default.
    Stealth = 0,

    // Sneaking has been tried and did not work. Walk at them.
    Assault = 1,
}

// Why the last stealth attempt ended. Kept so the next decision is made on
// what went wrong rather than on a timer alone.
public enum EnemyStealthFailureReason
{
    None = 0,
    NoHiddenRoute = 1,
    AllRoutesObserved = 2,
    Detected = 3,
    Timeout = 4,
    TargetMoved = 5,
    NoEscape = 6,
    AttemptsSpent = 7,
    SlotOccupied = 8,
    PreviouslyFailedRoute = 9,

    // Not a failure at all: the ambush worked and the target turned round onto
    // it. It ends the stealth attempt and commits the same way the failures
    // do, because the chase that follows must not be talked back into
    // stalking - but recording it as Detected made the field read as "what
    // went wrong" when nothing had.
    AmbushSprung = 10,
}

// What this enemy has learned during the engagement it is in, as opposed to
// during the phase it is in.
//
// EnemyStealthManeuver holds one attempt: this victim, this pose, this
// deadline. It is thrown away and rebuilt every time the enemy drops out of
// the four stealth states, which is exactly what let the enemy fail the same
// way forever - flank, get seen, retreat, chase, drift past the stalk
// distance, flank again, get seen in the same doorway. Nothing outlived the
// attempt, so nothing could count it.
//
// This does. It lives as long as the pursuit of one person, and it holds the
// three things the state machine had no memory of: whether sneaking is still
// on the table, how many times it has been tried, and which way round the
// enemy was seen coming.
//
// Server only. Nothing here is replicated - clients already get EnemyState,
// the target identity and the attack phase, and none of the decisions below
// change what any of those look like.
public sealed class EnemyEngagementTacticsRuntime
{
    // A handful of routes is enough. The fan offers twenty-one candidates and
    // the reason a flank fails twice in a row is nearly always one corridor,
    // not four.
    private const int MaxRememberedRoutes = 4;

    // How far the target has to move, or how far it has to turn, before what
    // was learned about the routes behind it is about somewhere else. Loose on
    // purpose: this is "the situation is different now", not the flank
    // planner's "the pose I measured against has drifted".
    private const float ForgetMoveDistance = 3f;
    private const float ForgetTurnDot = 0.7f;

    // Quantisation for the gaze topology signature. Two metres and thirty
    // degrees, so a player fidgeting does not read as the room having changed.
    // Direction is quantised as an angle rather than component by component:
    // a normalised component divided by two is always between -0.5 and 0.5,
    // which rounded to zero and made every horizontal gaze look identical.
    private const float GazePositionQuantum = 2f;
    private const float GazeYawQuantumDegrees = 30f;

    private readonly List<int> failedRouteFingerprints = new();

    private EnemyTargetIdentity engagedTarget;
    private bool hasEngagedTarget;

    private int committedGazeSignature;

    private Vector3 firstDetectionPoint;
    private bool hasFirstDetection;
    private Vector3 failureTargetPosition;
    private Vector3 failureTargetForward;
    private float failedSide;
    private bool hasFailedSide;

    public EnemyPursuitIntent Intent { get; private set; } = EnemyPursuitIntent.Stealth;

    // How many times sneaking has been started against this person, and how
    // many times it has been caught on the way round.
    public int StealthAttempts { get; private set; }
    public int FlankExposures { get; private set; }

    public EnemyStealthFailureReason LastFailure { get; private set; }

    // The earliest a change in the room may talk the enemy out of an assault
    // it has committed to. Without it, one player turning their head releases
    // the commitment on the next frame and the whole loop starts again.
    public float RetryNotBefore { get; private set; }

    // Which way round the last attempt was caught, expressed as the fan the
    // next one should start with. The flank fan's positive angles run to the
    // target's left, so a failure on the left is answered by the mirrored fan.
    public bool PrefersMirroredFan => hasFailedSide && failedSide < 0f;

    public bool HasSidePreference => hasFailedSide;

    // A pursuit is of one person. Perception can hand the enemy somebody else
    // mid-chase, and everything above was learned about the first one.
    public void BeginEngagement(EnemyTargetIdentity target)
    {
        if (hasEngagedTarget && engagedTarget.Equals(target))
        {
            return;
        }

        Clear();

        engagedTarget = target;

        // Set even for an identity that names nobody. A target whose network
        // object is not spawned has no identity to compare, and taking that as
        // "no engagement yet" made every perception refresh start a new one -
        // which throws away the commitment four times a second.
        hasEngagedTarget = true;
    }

    public void NoteStealthAttempt()
    {
        StealthAttempts++;
    }

    public void NoteFlankExposure()
    {
        FlankExposures++;
    }

    public bool HasSpentStealthAttempts(int maxAttempts)
    {
        return StealthAttempts > Mathf.Max(1, maxAttempts);
    }

    // Asked before starting one rather than after. The attempt in progress is
    // already counted, so "have I spent them" answers a different question and
    // lets a phase start an attempt the next tick refuses.
    public bool CanStartAnotherStealthAttempt(int maxAttempts)
    {
        return StealthAttempts < Mathf.Max(1, maxAttempts);
    }

    public bool HasSpentFlankExposures(int maxRetries)
    {
        return FlankExposures > Mathf.Max(0, maxRetries);
    }

    public void CommitToAssault(
        EnemyStealthFailureReason reason,
        float serverTime,
        float minimumCommitDuration,
        int gazeSignature
    )
    {
        Intent = EnemyPursuitIntent.Assault;
        LastFailure = reason;

        // This is a minimum, not an expiry. Expiring on the clock alone made
        // an unchanged room start the same failed flank every ten seconds.
        // Past the minimum the relevant players still have to change their
        // coarse positions or gaze before stealth comes back on the table.
        RetryNotBefore = serverTime + Mathf.Max(1f, minimumCommitDuration);
        committedGazeSignature = gazeSignature;
    }

    // Whether the enemy is still committed to coming straight at the target.
    //
    // A clock can make the commitment eligible for reconsideration; it cannot
    // release it by itself. The relevant gaze topology must also differ from
    // the one the failed attempt was planned against. This is what makes an
    // unchanged room stay in Assault rather than repeat the same route on a
    // slower timer.
    public bool IsAssaultCommitted(
        float serverTime,
        int currentGazeSignature
    )
    {
        if (Intent != EnemyPursuitIntent.Assault)
        {
            return false;
        }

        if (serverTime < RetryNotBefore ||
            currentGazeSignature == committedGazeSignature)
        {
            return true;
        }

        ReleaseAssault();
        return false;
    }

    // The route that got the enemy noticed, and the spot it was standing on
    // when that happened. Both, because the fingerprint only recognises the
    // very same route back: a route one metre to the side of it walks through
    // the same doorway and is seen from the same place.
    public void RememberFailedRoute(
        int routeFingerprint,
        Vector3 detectedAt,
        Vector3 targetPosition,
        Vector3 targetForward
    )
    {
        if (routeFingerprint != 0 &&
            !failedRouteFingerprints.Contains(routeFingerprint))
        {
            if (failedRouteFingerprints.Count >= MaxRememberedRoutes)
            {
                failedRouteFingerprints.RemoveAt(0);
            }

            failedRouteFingerprints.Add(routeFingerprint);
        }

        firstDetectionPoint = detectedAt;
        hasFirstDetection = true;
        failureTargetPosition = targetPosition;
        failureTargetForward = targetForward;

        Vector3 toDetection = detectedAt - targetPosition;
        toDetection.y = 0f;

        float side = Vector3.Dot(Vector3.Cross(Vector3.up, targetForward), toDetection);

        // Caught directly behind or directly in front says nothing about a
        // side, and picking one on floating point noise sends the next attempt
        // the same way half the time.
        hasFailedSide = Mathf.Abs(side) > 0.5f;
        failedSide = side;
    }

    // Whether this route is one the last attempt already failed on: the same
    // route, or one that walks back through the place the enemy was first
    // seen from.
    public bool IsRouteRejected(
        IReadOnlyList<Vector3> route,
        int routeFingerprint,
        float avoidRadius
    )
    {
        if (routeFingerprint != 0 &&
            failedRouteFingerprints.Contains(routeFingerprint))
        {
            return true;
        }

        if (!hasFirstDetection || avoidRadius <= 0f)
        {
            return false;
        }

        return EnemyStateRules.RouteComesWithin(
            route,
            firstDetectionPoint,
            avoidRadius
        );
    }

    // The history is about the routes behind one pose. It survives a phase
    // change and a failed attempt on purpose - that is the point of it - and
    // is dropped when the target has moved or turned far enough that the
    // corridor it was caught in is no longer the corridor behind them.
    public void ForgetFailedRoutesIfTargetMoved(
        Vector3 targetPosition,
        Vector3 targetForward
    )
    {
        if (!hasFirstDetection)
        {
            return;
        }

        if ((targetPosition - failureTargetPosition).sqrMagnitude <
            ForgetMoveDistance * ForgetMoveDistance &&
            Vector3.Dot(targetForward, failureTargetForward) > ForgetTurnDot)
        {
            return;
        }

        ClearFailedRoutes();
    }

    public void Clear()
    {
        Intent = EnemyPursuitIntent.Stealth;
        LastFailure = EnemyStealthFailureReason.None;
        StealthAttempts = 0;
        FlankExposures = 0;
        RetryNotBefore = 0f;
        committedGazeSignature = 0;
        hasEngagedTarget = false;
        engagedTarget = default;

        ClearFailedRoutes();
    }

    // Who is looking where, coarsely, as one number.
    //
    // Every relevant player rather than only the ones who can see this enemy:
    // an enemy holding still in cover is seen by nobody, so a signature built
    // from its watchers would be the same empty answer every tick and could
    // never notice the player turning away.
    public static int GazeTopologySignature(
        Vector3 focusPosition,
        float relevanceRadius
    )
    {
        IReadOnlyList<PlayerGazeNetwork> gazes = PlayerGazeNetwork.All;
        float radius = Mathf.Max(1f, relevanceRadius);
        float sqrRadius = radius * radius;

        unchecked
        {
            int hash = 17;
            int includedCount = 0;

            for (int i = 0; i < gazes.Count; i++)
            {
                PlayerGazeNetwork gaze = gazes[i];

                if (gaze == null || !gaze.IsWatching)
                {
                    continue;
                }

                Vector3 eye = gaze.EyePosition;
                Vector3 fromFocus = eye - focusPosition;
                fromFocus.y = 0f;

                // A player on the other side of the level is not a tactical
                // change for this engagement. Hashing every network player
                // made one unrelated client walking release every enemy's
                // Assault at once.
                if (fromFocus.sqrMagnitude > sqrRadius)
                {
                    continue;
                }

                Vector3 direction = gaze.GazeDirection;
                ulong ownerClientId = gaze.OwnerClientId;

                includedCount++;
                hash = hash * 31 + (int)ownerClientId;
                hash = hash * 31 + (int)(ownerClientId >> 32);
                hash = hash * 31 + Quantise(eye.x, GazePositionQuantum);
                hash = hash * 31 + Quantise(eye.z, GazePositionQuantum);
                hash = hash * 31 + GazeYawBucket(direction);
            }

            return hash * 31 + includedCount;
        }
    }

    internal static int GazeYawBucket(Vector3 direction)
    {
        Vector3 flat = Vector3.ProjectOnPlane(direction, Vector3.up);

        if (flat.sqrMagnitude <= 0.001f)
        {
            return 0;
        }

        float yaw = Mathf.Atan2(flat.x, flat.z) * Mathf.Rad2Deg;
        int bucketCount = Mathf.RoundToInt(360f / GazeYawQuantumDegrees);

        // Yaw is circular. Atan2 returns the same backwards direction as
        // either +180 or -180 depending on which side of the seam a tiny turn
        // lands. Leaving those as +6 and -6 made a one-degree fidget look like
        // a changed room and released Assault once its minimum elapsed.
        int bucket = Mathf.RoundToInt(
            Mathf.Repeat(yaw, 360f) / GazeYawQuantumDegrees
        );

        return bucket % bucketCount;
    }

    private static int Quantise(float value, float quantum)
    {
        return Mathf.RoundToInt(value / quantum);
    }

    private void ReleaseAssault()
    {
        Intent = EnemyPursuitIntent.Stealth;
        RetryNotBefore = 0f;
        committedGazeSignature = 0;

        // A released commitment is permission to sneak again, so everything
        // that spent it goes with it - including the failed routes.
        //
        // They used to survive a release, and that made the permission
        // worthless: the release says the relevant gaze topology changed, and
        // "this corridor gets seen" is a fact about that same topology, so the
        // first pass of the fresh attempt answered OnlyPreviouslyFailedRoutes
        // and committed to an assault again. The history is dropped for a
        // moved target for the same reason.
        StealthAttempts = 0;
        FlankExposures = 0;
        ClearFailedRoutes();
    }

    private void ClearFailedRoutes()
    {
        failedRouteFingerprints.Clear();
        hasFirstDetection = false;
        firstDetectionPoint = default;
        failureTargetPosition = default;
        failureTargetForward = default;
        failedSide = 0f;
        hasFailedSide = false;
    }
}
