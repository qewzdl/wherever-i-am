using System.Collections;
using System.Collections.Generic;
using NUnit.Framework;
using Unity.AI.Navigation;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

[Category("Gameplay")]
public sealed class ProductionPatrolRoutePlayModeTests
{
    private const string MapPath =
        "Assets/Collaborators/Qewzdl/Scenes/Maps/Map_Prototype.unity";
    private const float RoutePointSampleRadius = 2f;

    private readonly List<NavMeshSurface> surfaces = new();
    private Scene mapScene;

    [UnityTest]
    public IEnumerator ProductionMap_EveryPatrolLegBuildsVisibleSafeVariation()
    {
        AsyncOperation loadOperation = SceneManager.LoadSceneAsync(
            MapPath,
            LoadSceneMode.Additive);

        Assert.That(loadOperation, Is.Not.Null);
        yield return loadOperation;

        mapScene = SceneManager.GetSceneByPath(MapPath);
        Assert.That(mapScene.IsValid(), Is.True);
        Assert.That(mapScene.isLoaded, Is.True);

        EnemyPatrolRoute route = null;
        NetworkEnemyController enemy = null;
        List<RuntimeNavMeshBuilder> builders = new();

        foreach (GameObject root in mapScene.GetRootGameObjects())
        {
            route ??= root.GetComponentInChildren<EnemyPatrolRoute>(true);
            enemy ??= root.GetComponentInChildren<NetworkEnemyController>(true);
            builders.AddRange(
                root.GetComponentsInChildren<RuntimeNavMeshBuilder>(true));
        }

        Assert.That(route, Is.Not.Null);
        Assert.That(enemy, Is.Not.Null);
        Assert.That(builders, Is.Not.Empty);

        foreach (RuntimeNavMeshBuilder builder in builders)
        {
            PlayModeTestReflection.SetField(
                builder,
                "buildMode",
                RuntimeNavMeshBuildMode.Always);
            PlayModeTestReflection.SetField(builder, "waitForGameMap", false);
            PlayModeTestReflection.SetField(builder, "buildOverMultipleFrames", false);
            Assert.That(builder.BuildIfAllowed(), Is.True);

            surfaces.AddRange(builder.GetComponents<NavMeshSurface>());
        }

        EnemyConfig config = PlayModeTestReflection.GetField<EnemyConfig>(
            enemy,
            "config");
        NavMeshAgent agent = enemy.GetComponent<NavMeshAgent>();
        EnemyPostureController posture =
            enemy.GetComponent<EnemyPostureController>();

        Assert.That(config, Is.Not.Null);
        Assert.That(agent, Is.Not.Null);
        Assert.That(posture, Is.Not.Null);

        NavMeshQueryFilter standingFilter = CreateFilter(
            posture.GetAgentTypeIdForPosture(EnemyPosture.Standing),
            agent.areaMask);
        NavMeshQueryFilter crawlingFilter = CreateFilter(
            posture.GetAgentTypeIdForPosture(EnemyPosture.Crawling),
            agent.areaMask);

        for (int routeIndex = 0; routeIndex < route.Count; routeIndex++)
        {
            Transform from = route.GetPoint(routeIndex);
            Transform to = route.GetPoint(routeIndex + 1);

            // Unity's != is the only comparison that sees an unassigned
            // inspector slot; NUnit's Is.Not.Null would happily accept one and
            // let the leg below die on an UnassignedReferenceException instead
            // of naming the empty slot.
            Assert.That(
                from != null,
                Is.True,
                $"Production patrol point {routeIndex} is an empty slot.");
            Assert.That(
                to != null,
                Is.True,
                $"Production patrol point {routeIndex + 1} is an empty slot.");

            List<Vector3> plan = new();
            EnemyPatrolPathPlanner planner = new(1000 + routeIndex);

            bool built = TryBuildPlan(
                planner,
                from.position,
                to.position,
                standingFilter,
                config,
                plan);
            NavMeshQueryFilter usedFilter = standingFilter;

            if (!built)
            {
                built = TryBuildPlan(
                    planner,
                    from.position,
                    to.position,
                    crawlingFilter,
                    config,
                    plan);
                usedFilter = crawlingFilter;
            }

            Assert.That(
                built,
                Is.True,
                $"Production patrol leg {routeIndex} has no complete standing or crawling route.");
            Assert.That(
                plan.Count,
                Is.GreaterThan(1),
                $"Production patrol leg {routeIndex} fell back to the direct shortest route.");

            for (int pointIndex = 0; pointIndex < plan.Count - 1; pointIndex++)
            {
                Assert.That(
                    NavMesh.FindClosestEdge(
                        plan[pointIndex],
                        out NavMeshHit edge,
                        usedFilter),
                    Is.True);
                Assert.That(
                    edge.distance,
                    Is.GreaterThanOrEqualTo(
                        config.patrolEdgeClearance - 0.05f),
                    $"Production patrol leg {routeIndex}, point {pointIndex} " +
                    "violates the configured edge clearance.");
            }
        }
    }

    [UnityTearDown]
    public IEnumerator TearDown()
    {
        for (int i = surfaces.Count - 1; i >= 0; i--)
        {
            if (surfaces[i] != null)
            {
                surfaces[i].RemoveData();
            }
        }

        surfaces.Clear();

        if (mapScene.IsValid() && mapScene.isLoaded)
        {
            AsyncOperation unloadOperation =
                SceneManager.UnloadSceneAsync(mapScene);

            if (unloadOperation != null)
            {
                yield return unloadOperation;
            }
        }

        mapScene = default;
        yield return null;
    }

    private static bool TryBuildPlan(
        EnemyPatrolPathPlanner planner,
        Vector3 source,
        Vector3 destination,
        NavMeshQueryFilter filter,
        EnemyConfig config,
        List<Vector3> plan
    )
    {
        plan.Clear();

        return NavMesh.SamplePosition(
                   source,
                   out NavMeshHit sourceHit,
                   RoutePointSampleRadius,
                   filter) &&
               planner.TryBuildPlan(
                   sourceHit.position,
                   destination,
                   filter,
                   config,
                   plan);
    }

    private static NavMeshQueryFilter CreateFilter(
        int agentTypeId,
        int areaMask
    )
    {
        return new NavMeshQueryFilter
        {
            agentTypeID = agentTypeId,
            areaMask = areaMask
        };
    }
}
