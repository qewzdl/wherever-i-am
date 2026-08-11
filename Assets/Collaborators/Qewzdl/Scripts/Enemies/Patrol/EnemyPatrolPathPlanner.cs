using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;

internal sealed class EnemyPatrolPathPlanner
{
    private const float MinimumSampleRadius = 0.15f;
    private const float MaximumSampleRadius = 0.75f;
    private const float MaximumVerticalDeviation = 0.75f;
    private const float MinimumPointDistance = 0.1f;
    private const float PathLengthTolerance = 0.05f;
    private const float DestinationSampleRadius = 2f;
    private const int MaximumIntermediatePointCount = 8;

    private readonly System.Random random;
    private readonly NavMeshPath baselinePath = new();
    private readonly NavMeshPath segmentPath = new();
    private readonly NavMeshPath remainingPath = new();

    internal EnemyPatrolPathPlanner(int seed)
    {
        random = new System.Random(seed);
    }

    internal bool TryBuildPlan(
        Vector3 start,
        Vector3 destination,
        NavMeshQueryFilter filter,
        EnemyConfig config,
        List<Vector3> output
    )
    {
        int unlimitedTestBudget = int.MaxValue;
        return TryBuildPlan(
            start,
            destination,
            filter,
            config,
            output,
            ref unlimitedTestBudget
        );
    }

    internal bool TryBuildPlan(
        Vector3 start,
        Vector3 destination,
        NavMeshQueryFilter filter,
        EnemyConfig config,
        List<Vector3> output,
        ref int remainingPathQueries
    )
    {
        if (output == null)
        {
            throw new ArgumentNullException(nameof(output));
        }

        output.Clear();

        if (config == null ||
            !TrySamplePosition(start, MinimumSampleRadius, filter, out Vector3 sampledStart) ||
            !TrySamplePosition(
                destination,
                DestinationSampleRadius,
                filter,
                out Vector3 sampledDestination
            ) ||
            !TryCalculateCompletePath(
                sampledStart,
                sampledDestination,
                filter,
                baselinePath,
                ref remainingPathQueries,
                out float baselineLength
            ))
        {
            return false;
        }

        float variation = Mathf.Max(0f, config.patrolRouteVariation);
        float spacing = Mathf.Max(1f, config.patrolIntermediatePointSpacing);

        if (variation <= Mathf.Epsilon || baselineLength <= spacing)
        {
            output.Add(sampledDestination);
            return true;
        }

        float edgeClearance = Mathf.Max(0f, config.patrolEdgeClearance);
        float maxPlanLength = baselineLength * Mathf.Max(1f, config.patrolMaxDetourRatio);
        int attempts = Mathf.Max(1, config.patrolRouteSampleAttempts);
        float endpointReserve = Mathf.Max(1f, spacing * 0.5f);
        int intermediatePointCount = Mathf.Clamp(
            Mathf.FloorToInt((baselineLength - endpointReserve) / spacing),
            0,
            MaximumIntermediatePointCount
        );
        float anchorSpacing = baselineLength / (intermediatePointCount + 1);
        float minimumProgress = Mathf.Max(0.25f, anchorSpacing * 0.2f);

        Vector3 previousPoint = sampledStart;
        float acceptedPrefixLength = 0f;
        float previousRemainingLength = baselineLength;

        for (int pointIndex = 1; pointIndex <= intermediatePointCount; pointIndex++)
        {
            float distance = anchorSpacing * pointIndex;

            if (!TryGetPointAtDistance(baselinePath.corners, distance, out Vector3 anchor))
            {
                break;
            }

            if (!TrySelectIntermediatePoint(
                    anchor,
                    previousPoint,
                    sampledDestination,
                    filter,
                    variation,
                    edgeClearance,
                    attempts,
                    minimumProgress,
                    previousRemainingLength,
                    acceptedPrefixLength,
                    maxPlanLength,
                    ref remainingPathQueries,
                    out Vector3 candidate,
                    out float segmentLength,
                    out float remainingLength
                ))
            {
                continue;
            }

            output.Add(candidate);
            previousPoint = candidate;
            acceptedPrefixLength += segmentLength;
            previousRemainingLength = remainingLength;
        }

        if (!TryCalculateCompletePath(
                previousPoint,
                sampledDestination,
                filter,
                segmentPath,
                ref remainingPathQueries,
                out float finalSegmentLength
            ) ||
            acceptedPrefixLength + finalSegmentLength >
            maxPlanLength + PathLengthTolerance)
        {
            output.Clear();
            output.Add(sampledDestination);
            return true;
        }

        if ((sampledDestination - previousPoint).sqrMagnitude >
            MinimumPointDistance * MinimumPointDistance)
        {
            output.Add(sampledDestination);
        }

        return output.Count > 0;
    }

    private bool TrySelectIntermediatePoint(
        Vector3 anchor,
        Vector3 previousPoint,
        Vector3 destination,
        NavMeshQueryFilter filter,
        float variation,
        float edgeClearance,
        int attempts,
        float minimumProgress,
        float previousRemainingLength,
        float acceptedPrefixLength,
        float maxPlanLength,
        ref int remainingPathQueries,
        out Vector3 bestPoint,
        out float bestSegmentLength,
        out float bestRemainingLength
    )
    {
        bestPoint = default;
        bestSegmentLength = 0f;
        bestRemainingLength = 0f;

        bool found = false;
        float bestScore = float.MinValue;
        float minimumOffset = variation * 0.2f;
        float minimumOffsetSqr = minimumOffset * minimumOffset;
        float sampleRadius = Mathf.Clamp(
            variation * 0.35f,
            MinimumSampleRadius,
            MaximumSampleRadius
        );

        for (int attempt = 0; attempt < attempts; attempt++)
        {
            Vector2 offset = NextInsideUnitCircle() * variation;

            if (offset.sqrMagnitude < minimumOffsetSqr)
            {
                continue;
            }

            Vector3 rawPoint = new(
                anchor.x + offset.x,
                anchor.y,
                anchor.z + offset.y
            );

            if (!TrySamplePosition(rawPoint, sampleRadius, filter, out Vector3 candidate) ||
                Mathf.Abs(candidate.y - anchor.y) > MaximumVerticalDeviation)
            {
                continue;
            }

            Vector3 flatAnchorDelta = candidate - anchor;
            flatAnchorDelta.y = 0f;

            if (flatAnchorDelta.sqrMagnitude < minimumOffsetSqr ||
                flatAnchorDelta.magnitude > variation + sampleRadius)
            {
                continue;
            }

            if (!TryGetEdgeDistance(candidate, filter, out float edgeDistance) ||
                edgeDistance + PathLengthTolerance < edgeClearance)
            {
                continue;
            }

            if (!TryCalculateCompletePath(
                    previousPoint,
                    candidate,
                    filter,
                    segmentPath,
                    ref remainingPathQueries,
                    out float segmentLength
                ) ||
                !TryCalculateCompletePath(
                    candidate,
                    destination,
                    filter,
                    remainingPath,
                    ref remainingPathQueries,
                    out float remainingLength
                ))
            {
                continue;
            }

            if (remainingLength > previousRemainingLength - minimumProgress)
            {
                continue;
            }

            float estimatedPlanLength =
                acceptedPrefixLength +
                segmentLength +
                remainingLength;

            if (estimatedPlanLength > maxPlanLength + PathLengthTolerance)
            {
                continue;
            }

            float score = edgeDistance + flatAnchorDelta.magnitude * 0.25f;

            if (found && score <= bestScore)
            {
                continue;
            }

            found = true;
            bestScore = score;
            bestPoint = candidate;
            bestSegmentLength = segmentLength;
            bestRemainingLength = remainingLength;
        }

        return found;
    }

    private Vector2 NextInsideUnitCircle()
    {
        double angle = random.NextDouble() * Math.PI * 2d;
        double radius = Math.Sqrt(random.NextDouble());

        return new Vector2(
            (float)(Math.Cos(angle) * radius),
            (float)(Math.Sin(angle) * radius)
        );
    }

    private static bool TrySamplePosition(
        Vector3 source,
        float radius,
        NavMeshQueryFilter filter,
        out Vector3 sampledPosition
    )
    {
        if (NavMesh.SamplePosition(
                source,
                out NavMeshHit hit,
                Mathf.Max(MinimumSampleRadius, radius),
                filter
            ))
        {
            sampledPosition = hit.position;
            return true;
        }

        sampledPosition = default;
        return false;
    }

    private static bool TryGetEdgeDistance(
        Vector3 position,
        NavMeshQueryFilter filter,
        out float distance
    )
    {
        if (NavMesh.FindClosestEdge(position, out NavMeshHit hit, filter))
        {
            distance = hit.distance;
            return true;
        }

        distance = 0f;
        return false;
    }

    private static bool TryCalculateCompletePath(
        Vector3 source,
        Vector3 destination,
        NavMeshQueryFilter filter,
        NavMeshPath path,
        ref int remainingPathQueries,
        out float length
    )
    {
        length = 0f;

        if (path == null || remainingPathQueries <= 0)
        {
            return false;
        }

        remainingPathQueries--;

        if (!NavMesh.CalculatePath(source, destination, filter, path) ||
            path.status != NavMeshPathStatus.PathComplete)
        {
            return false;
        }

        Vector3[] corners = path.corners;

        for (int i = 1; i < corners.Length; i++)
        {
            length += Vector3.Distance(corners[i - 1], corners[i]);
        }

        return true;
    }

    private static bool TryGetPointAtDistance(
        Vector3[] corners,
        float targetDistance,
        out Vector3 point
    )
    {
        point = default;

        if (corners == null || corners.Length == 0)
        {
            return false;
        }

        float remainingDistance = Mathf.Max(0f, targetDistance);

        for (int i = 1; i < corners.Length; i++)
        {
            Vector3 segmentStart = corners[i - 1];
            Vector3 segmentEnd = corners[i];
            float segmentLength = Vector3.Distance(segmentStart, segmentEnd);

            if (remainingDistance <= segmentLength)
            {
                float t = segmentLength > Mathf.Epsilon
                    ? remainingDistance / segmentLength
                    : 0f;

                point = Vector3.Lerp(segmentStart, segmentEnd, t);
                return true;
            }

            remainingDistance -= segmentLength;
        }

        point = corners[corners.Length - 1];
        return true;
    }
}
