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

    // One queue per resource, not one queue for both.
    //
    // A single queue meant an enemy waiting on raycasts held up an enemy that
    // only wanted a path, for work that has nothing to do with it - and the
    // tactical planners ask for both while EnemyNavigator asks for paths
    // alone, so the two ends of the same enemy queued behind each other.
    private static readonly ResourceQueue PathQueue = new(PathQueriesPerFrame);
    private static readonly ResourceQueue VisibilityQueue =
        new(VisibilityQueriesPerFrame);

    private static int budgetFrame = -1;

    private static readonly Dictionary<int, GazeSample> GazeSamples = new();
    private static readonly List<PlayerWatcher> WatcherBuffer = new();
    private static readonly List<PlayerWatcher> EmptyWatchers = new();

    public static int PathQueriesRemainingThisFrame
    {
        get
        {
            RefreshFrameBudget();
            return PathQueue.Remaining;
        }
    }

    public static int VisibilityQueriesRemainingThisFrame
    {
        get
        {
            RefreshFrameBudget();
            return VisibilityQueue.Remaining;
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
        return GetBodyWatchers(
            enemyId,
            footPosition,
            bodyHeight,
            refreshInterval
        ).Count > 0;
    }

    // Who can see this enemy, from the same cached sample as the bool above.
    //
    // The returned list belongs to the cache and is refilled on the next
    // sample for this enemy, so it is read within the tick that asked for it
    // and not stored.
    public static IReadOnlyList<PlayerWatcher> GetBodyWatchers(
        int enemyId,
        Vector3 footPosition,
        float bodyHeight,
        float refreshInterval = DefaultGazeRefreshInterval
    )
    {
        float now = Time.time;
        refreshInterval = Mathf.Max(0.05f, refreshInterval);

        if (!GazeSamples.TryGetValue(enemyId, out GazeSample sample))
        {
            sample = new GazeSample();
            GazeSamples[enemyId] = sample;
        }
        else if (now < sample.NextSampleAt &&
                 (sample.FootPosition - footPosition).sqrMagnitude <
                 ResampleDistance * ResampleDistance)
        {
            return sample.Watchers;
        }

        PlayerGazeNetwork.CollectBodyWatchers(
            footPosition,
            bodyHeight,
            WatcherBuffer
        );

        sample.Watchers.Clear();
        sample.Watchers.AddRange(WatcherBuffer);
        sample.FootPosition = footPosition;

        float phase = GetSamplePhase(enemyId, refreshInterval);
        sample.NextSampleAt = GetNextSampleTime(now, refreshInterval, phase);

        return sample.Watchers;
    }

    public static IReadOnlyList<PlayerWatcher> NoWatchers => EmptyWatchers;

    public static void Forget(int enemyId)
    {
        GazeSamples.Remove(enemyId);
        PathQueue.Forget(enemyId);
        VisibilityQueue.Forget(enemyId);
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
        WatcherBuffer.Clear();
        PathQueue.Reset();
        VisibilityQueue.Reset();
        budgetFrame = -1;
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
        PathQueue.BeginFrame();
        VisibilityQueue.BeginFrame();
    }

    private static bool TryReserveWork(
        int enemyId,
        int requestedPathQueries,
        int requestedVisibilityQueries,
        out int grantedPathQueries,
        out int grantedVisibilityQueries
    )
    {
        RefreshFrameBudget();

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

        // Both or neither: a half-reserved plan spends budget on a search that
        // cannot finish.
        if (!PathQueue.CanServe(enemyId, requestedPathQueries) ||
            !VisibilityQueue.CanServe(enemyId, requestedVisibilityQueries))
        {
            PathQueue.Defer(enemyId, requestedPathQueries);
            VisibilityQueue.Defer(enemyId, requestedVisibilityQueries);
            return false;
        }

        PathQueue.Consume(enemyId, requestedPathQueries);
        VisibilityQueue.Consume(enemyId, requestedVisibilityQueries);

        grantedPathQueries = requestedPathQueries;
        grantedVisibilityQueries = requestedVisibilityQueries;
        return true;
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

    private sealed class GazeSample
    {
        public float NextSampleAt;
        public Vector3 FootPosition;
        public readonly List<PlayerWatcher> Watchers = new();
    }

    private sealed class ResourceQueue
    {
        private readonly Queue<int> pending = new();
        private readonly HashSet<int> pendingSet = new();
        private readonly HashSet<int> grantedThisFrame = new();
        private readonly Dictionary<int, int> lastRequestedFrame = new();
        private readonly int perFrame;

        public int Remaining { get; private set; }

        public ResourceQueue(int perFrame)
        {
            this.perFrame = perFrame;
            Remaining = perFrame;
        }

        public void BeginFrame()
        {
            Remaining = perFrame;
            grantedThisFrame.Clear();
        }

        public bool CanServe(int enemyId, int requestedQueries)
        {
            if (requestedQueries <= 0)
            {
                return true;
            }

            CleanupForgottenRequesters();

            // An enemy that has already been served this frame yields only to
            // someone actually waiting. Refusing outright meant a state's
            // tactical plan and the same enemy's navigator repath took turns
            // over alternate frames even on an empty server, which is a
            // visible stutter for no gain.
            if (grantedThisFrame.Contains(enemyId) && pending.Count > 0)
            {
                return false;
            }

            if (pending.Count > 0 && pending.Peek() != enemyId)
            {
                return false;
            }

            return Remaining >= requestedQueries;
        }

        public void Consume(int enemyId, int requestedQueries)
        {
            if (requestedQueries <= 0)
            {
                return;
            }

            Remaining -= requestedQueries;
            grantedThisFrame.Add(enemyId);

            if (pending.Count > 0 && pending.Peek() == enemyId)
            {
                pending.Dequeue();
                pendingSet.Remove(enemyId);
            }

            lastRequestedFrame.Remove(enemyId);
        }

        public void Defer(int enemyId, int requestedQueries)
        {
            if (requestedQueries <= 0)
            {
                return;
            }

            lastRequestedFrame[enemyId] = Time.frameCount;

            if (pendingSet.Add(enemyId))
            {
                pending.Enqueue(enemyId);
            }
        }

        public void Forget(int enemyId)
        {
            pendingSet.Remove(enemyId);
            grantedThisFrame.Remove(enemyId);
            lastRequestedFrame.Remove(enemyId);
        }

        public void Reset()
        {
            pending.Clear();
            pendingSet.Clear();
            grantedThisFrame.Clear();
            lastRequestedFrame.Clear();
            Remaining = perFrame;
        }

        private void CleanupForgottenRequesters()
        {
            while (pending.Count > 0 && IsForgottenOrStale(pending.Peek()))
            {
                int staleRequester = pending.Dequeue();
                pendingSet.Remove(staleRequester);
                lastRequestedFrame.Remove(staleRequester);
            }
        }

        // A requester can change state after being queued. Do not let a slot
        // it no longer asks for block every living enemy forever. One grace
        // frame preserves fairness for normal Update ordering.
        private bool IsForgottenOrStale(int enemyId)
        {
            return !pendingSet.Contains(enemyId) ||
                   !lastRequestedFrame.TryGetValue(
                       enemyId,
                       out int requestedFrame) ||
                   requestedFrame < Time.frameCount - 1;
        }
    }
}
