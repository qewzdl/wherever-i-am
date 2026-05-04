using Unity.AI.Navigation;
using UnityEngine;
using UnityEngine.AI;

[DefaultExecutionOrder(-10000)]
[DisallowMultipleComponent]
public class RuntimeNavMeshBuilder : MonoBehaviour
{
    private const int DefaultLayer = 0;
    private const int EnvironmentLayer = 7;
    private const int WallsLayer = 8;
    private const int DoorsLayer = 9;

    [SerializeField]
    private LayerMask includedLayers =
        (1 << DefaultLayer) |
        (1 << EnvironmentLayer) |
        (1 << WallsLayer) |
        (1 << DoorsLayer);

    [SerializeField] private NavMeshCollectGeometry geometry = NavMeshCollectGeometry.PhysicsColliders;
    [SerializeField] private bool buildOnAwake = true;

    private void Awake()
    {
        if (!buildOnAwake)
        {
            return;
        }

        NavMeshSurface surface = GetComponent<NavMeshSurface>();

        if (surface == null)
        {
            surface = gameObject.AddComponent<NavMeshSurface>();
        }

        surface.collectObjects = CollectObjects.All;
        surface.layerMask = includedLayers;
        surface.useGeometry = geometry;
        surface.ignoreNavMeshAgent = true;
        surface.ignoreNavMeshObstacle = true;
        surface.BuildNavMesh();
    }
}
