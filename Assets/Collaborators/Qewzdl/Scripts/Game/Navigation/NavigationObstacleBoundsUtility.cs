using UnityEngine;
using UnityEngine.AI;

internal static class NavigationObstacleBoundsUtility
{
    private const float MinimumObstacleSize = 0.05f;

    internal static void ConfigureBox(
        Transform root,
        NavMeshObstacle obstacle,
        float boundsPadding,
        float moveThreshold,
        float timeToStationary)
    {
        if (root == null || obstacle == null)
        {
            return;
        }

        obstacle.shape = NavMeshObstacleShape.Box;
        obstacle.carveOnlyStationary = true;
        obstacle.carvingMoveThreshold = Mathf.Max(0.01f, moveThreshold);
        obstacle.carvingTimeToStationary = Mathf.Max(
            0f,
            timeToStationary);

        if (!TryCalculateLocalBounds(root, out Bounds bounds))
        {
            return;
        }

        float padding = Mathf.Max(0f, boundsPadding) * 2f;
        Vector3 size = bounds.size + Vector3.one * padding;
        size.x = Mathf.Max(MinimumObstacleSize, size.x);
        size.y = Mathf.Max(MinimumObstacleSize, size.y);
        size.z = Mathf.Max(MinimumObstacleSize, size.z);

        obstacle.center = bounds.center;
        obstacle.size = size;
    }

    private static bool TryCalculateLocalBounds(
        Transform root,
        out Bounds bounds)
    {
        bounds = default;
        Collider[] colliders = root.GetComponentsInChildren<Collider>(true);
        bool hasBounds = false;

        for (int i = 0; i < colliders.Length; i++)
        {
            Collider candidate = colliders[i];

            if (candidate == null || candidate.isTrigger)
            {
                continue;
            }

            EncapsulateWorldBounds(
                root,
                candidate.bounds,
                ref bounds,
                ref hasBounds);
        }

        return hasBounds;
    }

    private static void EncapsulateWorldBounds(
        Transform root,
        Bounds worldBounds,
        ref Bounds localBounds,
        ref bool hasBounds)
    {
        Vector3 min = worldBounds.min;
        Vector3 max = worldBounds.max;

        for (int x = 0; x < 2; x++)
        {
            for (int y = 0; y < 2; y++)
            {
                for (int z = 0; z < 2; z++)
                {
                    Vector3 worldPoint = new(
                        x == 0 ? min.x : max.x,
                        y == 0 ? min.y : max.y,
                        z == 0 ? min.z : max.z);
                    Vector3 localPoint = root.InverseTransformPoint(
                        worldPoint);

                    if (!hasBounds)
                    {
                        localBounds = new Bounds(
                            localPoint,
                            Vector3.zero);
                        hasBounds = true;
                    }
                    else
                    {
                        localBounds.Encapsulate(localPoint);
                    }
                }
            }
        }
    }
}
