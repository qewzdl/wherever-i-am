using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;

internal readonly struct EnemyBlockedRoutePlan
{
    public ItemNavigationObstacle Barrier { get; }
    public Vector3 ApproachEndpoint { get; }
    public Vector3 PushDestination { get; }

    public EnemyBlockedRoutePlan(
        ItemNavigationObstacle barrier,
        Vector3 approachEndpoint,
        Vector3 pushDestination)
    {
        Barrier = barrier;
        ApproachEndpoint = approachEndpoint;
        PushDestination = pushDestination;
    }
}

internal sealed class EnemyBlockedRoutePlanner
{
    private const float ApproachPadding = 0.15f;
    private const float MinimumSampleRadius = 0.25f;
    private const float MinimumPushThroughDistance = 0.5f;
    private const float DirectionPenalty = 4f;
    private const float TargetDistanceWeight = 0.25f;

    private readonly List<ItemNavigationObstacle> barrierBuffer = new();
    private readonly NavMeshPath candidatePath = new();

    public bool TryBuildPlan(
        Vector3 source,
        Vector3 destination,
        NavMeshQueryFilter filter,
        float agentRadius,
        EnemyItemPusher itemPusher,
        NavMeshPath resultPath,
        out EnemyBlockedRoutePlan plan)
    {
        plan = default;

        if (itemPusher == null || resultPath == null)
        {
            return false;
        }

        ItemNavigationObstacle.CopyActiveServerBarriersTo(barrierBuffer);

        float bestScore = float.PositiveInfinity;
        ItemNavigationObstacle bestBarrier = null;
        Vector3 bestApproach = default;
        Vector3 bestPushDestination = default;

        for (int barrierIndex = 0;
             barrierIndex < barrierBuffer.Count;
             barrierIndex++)
        {
            ItemNavigationObstacle barrier = barrierBuffer[barrierIndex];

            if (barrier == null ||
                !barrier.TryGetBarrierGeometry(
                    out Vector3 center,
                    out Vector3 axisX,
                    out Vector3 axisZ,
                    out Vector3 halfAxisX,
                    out Vector3 halfAxisY,
                    out Vector3 halfAxisZ))
            {
                continue;
            }

            Vector3 preferredDirection = destination - center;
            preferredDirection.y = 0f;

            if (preferredDirection.sqrMagnitude > 0.0001f)
            {
                preferredDirection.Normalize();
            }

            EvaluateAxisDirections(axisX);
            EvaluateAxisDirections(axisZ);

            void EvaluateAxisDirections(Vector3 axis)
            {
                axis.y = 0f;

                if (axis.sqrMagnitude < 0.0001f)
                {
                    return;
                }

                axis.Normalize();
                EvaluateDirection(axis);
                EvaluateDirection(-axis);
            }

            void EvaluateDirection(Vector3 pushDirection)
            {
                float projectedRadius =
                    Mathf.Abs(Vector3.Dot(halfAxisX, pushDirection)) +
                    Mathf.Abs(Vector3.Dot(halfAxisY, pushDirection)) +
                    Mathf.Abs(Vector3.Dot(halfAxisZ, pushDirection));
                float safeAgentRadius = Mathf.Max(0.05f, agentRadius);
                Vector3 desiredApproach =
                    center -
                    pushDirection *
                    (projectedRadius + safeAgentRadius + ApproachPadding);
                desiredApproach.y = source.y;
                float sampleRadius = Mathf.Max(
                    MinimumSampleRadius,
                    safeAgentRadius * 0.75f);

                if (!NavMesh.SamplePosition(
                        desiredApproach,
                        out NavMeshHit approachHit,
                        sampleRadius,
                        filter))
                {
                    return;
                }

                Vector3 approachSide = center - approachHit.position;
                approachSide.y = 0f;

                if (Vector3.Dot(approachSide, pushDirection) <=
                    projectedRadius + safeAgentRadius * 0.1f)
                {
                    return;
                }

                candidatePath.ClearCorners();

                if (!NavMesh.CalculatePath(
                        source,
                        approachHit.position,
                        filter,
                        candidatePath) ||
                    candidatePath.status != NavMeshPathStatus.PathComplete ||
                    itemPusher.HasNavigationBlockerOnRoute(
                        candidatePath.corners))
                {
                    return;
                }

                float directionAlignment = preferredDirection.sqrMagnitude > 0f
                    ? Vector3.Dot(preferredDirection, pushDirection)
                    : 0f;
                float score = CalculatePathLength(candidatePath) +
                              Vector3.Distance(center, destination) *
                              TargetDistanceWeight +
                              (1f - directionAlignment) * DirectionPenalty;

                if (score >= bestScore)
                {
                    return;
                }

                bestScore = score;
                bestBarrier = barrier;
                bestApproach = approachHit.position;
                bestPushDestination =
                    center +
                    pushDirection *
                    (projectedRadius +
                     safeAgentRadius +
                     Mathf.Max(MinimumPushThroughDistance, safeAgentRadius));
            }
        }

        if (bestBarrier == null)
        {
            return false;
        }

        resultPath.ClearCorners();

        if (!NavMesh.CalculatePath(
                source,
                bestApproach,
                filter,
                resultPath) ||
            resultPath.status != NavMeshPathStatus.PathComplete ||
            itemPusher.HasNavigationBlockerOnRoute(resultPath.corners))
        {
            return false;
        }

        plan = new EnemyBlockedRoutePlan(
            bestBarrier,
            bestApproach,
            bestPushDestination);
        return true;
    }

    private static float CalculatePathLength(NavMeshPath path)
    {
        Vector3[] corners = path.corners;
        float length = 0f;

        for (int i = 1; i < corners.Length; i++)
        {
            length += Vector3.Distance(corners[i - 1], corners[i]);
        }

        return length;
    }
}
