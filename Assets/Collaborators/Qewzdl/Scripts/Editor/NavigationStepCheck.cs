using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.SceneManagement;

// Finds steps in a map's geometry that the enemy cannot walk up: a floor that
// is higher than the floor beside it by more than the enemy's climb. Such a
// step is a wall to her - the navmesh does not join the two floors, and she
// is never sent up. A step taller than a wall is not reported: that is a wall.
//
// Works on the map's own colliders, in the layers its navmesh is built from,
// by sampling the top of the floor on a grid. Only steps to the four sides of a
// cell are looked at, so a stair is reported once per edge it has.
public static class NavigationStepCheck
{
    private const string EnemyAgentName = "EnemyStanding";
    private const float CellSize = 0.2f;
    private const float ReportedAboveClimb = 0.6f;
    private const float UpwardsNormal = 0.95f;
    private const int MaximumWarnings = 8;

    public static void Find(Scene scene, LayerMask layers, List<string> warnings)
    {
        float climb = ClimbOf(EnemyAgentName);

        if (climb <= 0f || !scene.IsValid())
            return;

        Bounds bounds = new Bounds();
        bool any = false;

        foreach (GameObject root in scene.GetRootGameObjects())
        {
            foreach (Collider collider in root.GetComponentsInChildren<Collider>(false))
            {
                if (collider.isTrigger || ((1 << collider.gameObject.layer) & layers.value) == 0)
                    continue;

                if (!any)
                {
                    bounds = collider.bounds;
                    any = true;
                }
                else
                {
                    bounds.Encapsulate(collider.bounds);
                }
            }
        }

        if (!any)
            return;

        PhysicsScene physics = scene.GetPhysicsScene();
        int columns = Mathf.CeilToInt(bounds.size.x / CellSize);
        int rows = Mathf.CeilToInt(bounds.size.z / CellSize);
        float[,] floors = new float[columns, rows];

        for (int i = 0; i < columns; i++)
        {
            for (int j = 0; j < rows; j++)
            {
                float x = bounds.min.x + (i + 0.5f) * CellSize;
                float z = bounds.min.z + (j + 0.5f) * CellSize;
                floors[i, j] = FloorAt(physics, new Vector3(x, bounds.max.y + 0.5f, z), bounds.min.y - 0.5f, layers);
            }
        }

        HashSet<string> reported = new HashSet<string>();

        for (int i = 0; i < columns; i++)
        {
            for (int j = 0; j < rows; j++)
            {
                if (float.IsNaN(floors[i, j]))
                    continue;

                TryReport(floors, i, j, i + 1, j, climb, bounds, reported, warnings);
                TryReport(floors, i, j, i, j + 1, climb, bounds, reported, warnings);

                if (warnings.Count >= MaximumWarnings)
                    return;
            }
        }
    }

    private static void TryReport(
        float[,] floors,
        int i,
        int j,
        int otherI,
        int otherJ,
        float climb,
        Bounds bounds,
        HashSet<string> reported,
        List<string> warnings)
    {
        if (otherI >= floors.GetLength(0) || otherJ >= floors.GetLength(1))
            return;

        float here = floors[i, j];
        float there = floors[otherI, otherJ];

        if (float.IsNaN(there))
            return;

        float step = Mathf.Abs(there - here);

        if (step <= climb + 0.02f || step >= climb + ReportedAboveClimb)
            return;

        Vector3 at = new Vector3(
            bounds.min.x + (i + otherI + 1) * 0.5f * CellSize,
            Mathf.Max(here, there),
            bounds.min.z + (j + otherJ + 1) * 0.5f * CellSize);

        // One warning per place, not per sample along its edge.
        string place = $"{Mathf.Round(at.x * 2f) / 2f:F1},{Mathf.Round(at.z * 2f) / 2f:F1}";

        if (!reported.Add(place))
            return;

        warnings.Add(
            $"A {step:F2} m step at ({at.x:F1}, {at.y:F2}, {at.z:F1}) is higher than the enemy " +
            $"can climb ({climb:F2} m). She cannot walk up it: make it lower, or she will not go there.");
    }

    // The top of the walkable floor under a point, from above. Surfaces facing
    // down - a ceiling - are passed through to whatever is under them.
    private static float FloorAt(PhysicsScene physics, Vector3 origin, float lowest, LayerMask layers)
    {
        for (int pass = 0; pass < 6; pass++)
        {
            float distance = origin.y - lowest;

            if (distance <= 0f)
                return float.NaN;

            if (!physics.Raycast(origin, Vector3.down, out RaycastHit hit, distance, layers.value, QueryTriggerInteraction.Ignore))
                return float.NaN;

            if (hit.normal.y >= UpwardsNormal)
                return hit.point.y;

            origin = hit.point + Vector3.down * 0.01f;
        }

        return float.NaN;
    }

    private static float ClimbOf(string agentName)
    {
        for (int i = 0; i < NavMesh.GetSettingsCount(); i++)
        {
            NavMeshBuildSettings settings = NavMesh.GetSettingsByIndex(i);

            if (NavMesh.GetSettingsNameFromID(settings.agentTypeID) == agentName)
                return settings.agentClimb;
        }

        return 0f;
    }
}
