using System.Collections.Generic;
using UnityEngine;

// What the whole server pays for perception and path planning, decided in one
// place instead of once per enemy.
//
// Two costs used to scale with the enemy count and nothing else. Every enemy
// asked "can anyone see me" from its own Update, three raycasts per player per
// call, every frame it was in a stealth state. And every enemy carried its own
// NavMesh path-query budget, so the ceiling was a per-enemy number multiplied
// by however many enemies happened to repath in the same frame - which is
// exactly when the frame was already in trouble.
//
// Static rather than a component: this is a property of the server process,
// there is nothing to place in a scene, and an enemy spawned mid-match must
// not have to find it. Frame boundaries come from Time.frameCount, so nothing
// has to be ticked for the budget to reset.
public static class EnemyServerPerceptionScheduler
{
    // Eight is enough for the stealth states, which act on being seen for a
    // fraction of a second rather than a single frame. Under it the enemy
    // starts reacting visibly late.
    public const float DefaultGazeRefreshInterval = 0.125f;

    // Shared by everyone in a frame. A single enemy fanning out over a route
    // can still spend the lot, which is correct - one enemy planning properly
    // beats four planning badly - it just cannot do it every frame.
    public const int PathQueriesPerFrame = 32;
    public const int VisibilityQueriesPerFrame = 64;

    private static int budgetFrame = -1;
    private static int remainingPathQueries;
    private static int remainingVisibilityQueries;

    private static readonly Dictionary<int, GazeSample> GazeSamples = new();
    private static readonly Queue<int> PendingWorkRequesters = new();
    private static readonly HashSet<int> PendingWorkRequesterSet = new();
    private static readonly HashSet<int> GrantedWorkRequestersThisFrame = new();
    private static readonly Dictionary<int, int> WorkLastRequestedFrame = new();

    public static int PathQueriesRemainingThisFrame
    {
        get
        {
            RefreshFrameBudget();
            return remainingPathQueries;
        }
    }

    public static int VisibilityQueriesRemainingThisFrame
    {
        get
        {
            RefreshFrameBudget();
            return remainingVisibilityQueries;
        }
    }

    // Reserves an entire bounded repath before it begins. Denial means
    // "deferred", not "there is no path": the caller keeps its current route
    // and retries without recording a failed navigation attempt. Queue order
    // prevents the same early-updating enemy from taking the budget every
    // frame.
    public static bool TryReservePathQueries(
        int enemyId,
        int requestedQueries,
        out int grantedQueries
    )
    {
        RefreshFrameBudget();
        return TryReserveWork(
            enemyId,
            requestedQueries,
            0,
            out grantedQueries,
            out _
        );
    }

    public static bool TryReserveVisibilityQueries(
        int enemyId,
        int requestedQueries,
        out int grantedQueries
    )
    {
        RefreshFrameBudget();
        return TryReserveWork(
            enemyId,
            0,
            requestedQueries,
            out _,
            out grantedQueries
        );
    }

    public static bool TryReservePlanningQueries(
        int enemyId,
        int requestedPathQueries,
        int requestedVisibilityQueries,
        out int grantedPathQueries,
        out int grantedVisibilityQueries
    )
    {
        RefreshFrameBudget();
        return TryReserveWork(
            enemyId,
            requestedPathQueries,
            requestedVisibilityQueries,
            out grantedPathQueries,
            out grantedVisibilityQueries
        );
    }

    // Cached per enemy, not globally: the answer depends on where the asker is
    // standing. Re-asked when the enemy has moved far enough that the old
    // answer is about a different place.
    public static bool IsBodySeenByAnyone(
        int enemyId,
        Vector3 footPosition,
        float bodyHeight,
        float refreshInterval = DefaultGazeRefreshInterval
    )
    {
        float now = Time.time;
        refreshInterval = Mathf.Max(0.05f, refreshInterval);

        if (GazeSamples.TryGetValue(enemyId, out GazeSample sample) &&
            now < sample.NextSampleAt &&
            (sample.FootPosition - footPosition).sqrMagnitude <
            ResampleDistance * ResampleDistance)
        {
            return sample.IsSeen;
        }

        bool isSeen = PlayerGazeNetwork.IsBodySeenByAnyone(
            footPosition,
            bodyHeight
        );

        float phase = GetSamplePhase(enemyId, refreshInterval);
        float nextSampleAt = GetNextSampleTime(now, refreshInterval, phase);
        GazeSamples[enemyId] = new GazeSample(
            nextSampleAt,
            footPosition,
            isSeen
        );
        return isSeen;
    }

    public static void Forget(int enemyId)
    {
        GazeSamples.Remove(enemyId);
        PendingWorkRequesterSet.Remove(enemyId);
        GrantedWorkRequestersThisFrame.Remove(enemyId);
        WorkLastRequestedFrame.Remove(enemyId);
    }

    // Entering and leaving play mode keeps statics alive in the editor, and a
    // second session would start with the first one's samples.
    public static void ResetForTests()
    {
        ResetRuntimeState();
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetRuntimeState()
    {
        GazeSamples.Clear();
        PendingWorkRequesters.Clear();
        PendingWorkRequesterSet.Clear();
        GrantedWorkRequestersThisFrame.Clear();
        WorkLastRequestedFrame.Clear();
        budgetFrame = -1;
        remainingPathQueries = 0;
        remainingVisibilityQueries = 0;
    }

    // Roughly a stride. Closer than this and the sight lines are the same
    // ones; further and the cached answer is about where the enemy was.
    private const float ResampleDistance = 0.75f;

    private static void RefreshFrameBudget()
    {
        int frame = Time.frameCount;

        if (budgetFrame == frame)
        {
            return;
        }

        budgetFrame = frame;
        remainingPathQueries = PathQueriesPerFrame;
        remainingVisibilityQueries = VisibilityQueriesPerFrame;
        GrantedWorkRequestersThisFrame.Clear();
    }

    private static bool TryReserveWork(
        int enemyId,
        int requestedPathQueries,
        int requestedVisibilityQueries,
        out int grantedPathQueries,
        out int grantedVisibilityQueries
    )
    {
        grantedPathQueries = 0;
        grantedVisibilityQueries = 0;
        requestedPathQueries = Mathf.Clamp(
            requestedPathQueries,
            0,
            PathQueriesPerFrame
        );
        requestedVisibilityQueries = Mathf.Clamp(
            requestedVisibilityQueries,
            0,
            VisibilityQueriesPerFrame
        );

        if (requestedPathQueries == 0 && requestedVisibilityQueries == 0)
        {
            return false;
        }

        WorkLastRequestedFrame[enemyId] = Time.frameCount;

        CleanupForgottenRequesters(
            PendingWorkRequesters,
            PendingWorkRequesterSet
        );

        if (GrantedWorkRequestersThisFrame.Contains(enemyId))
        {
            QueueRequester(
                enemyId,
                PendingWorkRequesters,
                PendingWorkRequesterSet
            );
            return false;
        }

        if (PendingWorkRequesters.Count > 0)
        {
            if (!PendingWorkRequesterSet.Contains(enemyId))
            {
                QueueRequester(
                    enemyId,
                    PendingWorkRequesters,
                    PendingWorkRequesterSet
                );
            }

            CleanupForgottenRequesters(
                PendingWorkRequesters,
                PendingWorkRequesterSet
            );

            if (PendingWorkRequesters.Count == 0 ||
                PendingWorkRequesters.Peek() != enemyId)
            {
                return false;
            }
        }

        if (remainingPathQueries < requestedPathQueries ||
            remainingVisibilityQueries < requestedVisibilityQueries)
        {
            QueueRequester(
                enemyId,
                PendingWorkRequesters,
                PendingWorkRequesterSet
            );
            return false;
        }

        if (PendingWorkRequesters.Count > 0 &&
            PendingWorkRequesters.Peek() == enemyId)
        {
            PendingWorkRequesters.Dequeue();
            PendingWorkRequesterSet.Remove(enemyId);
            WorkLastRequestedFrame.Remove(enemyId);
        }

        remainingPathQueries -= requestedPathQueries;
        remainingVisibilityQueries -= requestedVisibilityQueries;
        grantedPathQueries = requestedPathQueries;
        grantedVisibilityQueries = requestedVisibilityQueries;
        GrantedWorkRequestersThisFrame.Add(enemyId);
        WorkLastRequestedFrame.Remove(enemyId);
        return true;
    }

    private static void QueueRequester(
        int enemyId,
        Queue<int> pendingRequesters,
        HashSet<int> pendingRequesterSet
    )
    {
        WorkLastRequestedFrame[enemyId] = Time.frameCount;

        if (pendingRequesterSet.Add(enemyId))
        {
            pendingRequesters.Enqueue(enemyId);
        }
    }

    private static void CleanupForgottenRequesters(
        Queue<int> pendingRequesters,
        HashSet<int> pendingRequesterSet
    )
    {
        while (pendingRequesters.Count > 0 &&
               IsForgottenOrStale(pendingRequesters.Peek(), pendingRequesterSet))
        {
            int staleRequester = pendingRequesters.Dequeue();
            pendingRequesterSet.Remove(staleRequester);
            WorkLastRequestedFrame.Remove(staleRequester);
        }
    }

    private static bool IsForgottenOrStale(
        int enemyId,
        HashSet<int> pendingRequesterSet
    )
    {
        if (!pendingRequesterSet.Contains(enemyId) ||
            !WorkLastRequestedFrame.TryGetValue(enemyId, out int requestedFrame))
        {
            return true;
        }

        // A requester can change state after being queued. Do not let a slot
        // it no longer asks for block every living enemy forever. One grace
        // frame preserves fairness for normal Update ordering.
        return requestedFrame < Time.frameCount - 1;
    }

    private static float GetSamplePhase(int enemyId, float interval)
    {
        uint hash = unchecked((uint)enemyId * 2654435761u);
        return (hash & 1023u) / 1024f * interval;
    }

    private static float GetNextSampleTime(
        float now,
        float interval,
        float phase
    )
    {
        float slot = Mathf.Floor((now - phase) / interval) + 1f;
        return slot * interval + phase;
    }

    private readonly struct GazeSample
    {
        public GazeSample(float nextSampleAt, Vector3 footPosition, bool isSeen)
        {
            NextSampleAt = nextSampleAt;
            FootPosition = footPosition;
            IsSeen = isSeen;
        }

        public float NextSampleAt { get; }
        public Vector3 FootPosition { get; }
        public bool IsSeen { get; }
    }
}
