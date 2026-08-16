using System.Collections.Generic;
using UnityEngine;

[DisallowMultipleComponent]
public sealed class GameMapRoot : MonoBehaviour
{
    [Header("Gameplay")]
    [SerializeField] private Transform[] playerSpawnPoints;
    [SerializeField] private ObjectiveSceneBindingRegistry objectiveBindingRegistry;

    [Tooltip(
        "Leave empty to collect every spawn point under this map. Fill it in " +
        "only to use a hand-picked set.")]
    [SerializeField] private EnemySpawnPoint[] enemySpawnPoints;

    private readonly Dictionary<ulong, int> assignedSpawnIndices = new();

    // Collected rather than written back into the serialized field, so a point
    // added to the map later is still found. Same reason as the objective
    // binding registry.
    private EnemySpawnPoint[] resolvedEnemySpawnPoints;

    public ObjectiveSceneBindingRegistry ObjectiveBindingRegistry => objectiveBindingRegistry;
    public int PlayerSpawnPointCount => playerSpawnPoints == null ? 0 : playerSpawnPoints.Length;

    public IReadOnlyList<EnemySpawnPoint> EnemySpawnPoints
    {
        get
        {
            if (enemySpawnPoints != null && enemySpawnPoints.Length > 0)
            {
                return enemySpawnPoints;
            }

            resolvedEnemySpawnPoints ??= GetComponentsInChildren<EnemySpawnPoint>(true);
            return resolvedEnemySpawnPoints;
        }
    }

    public bool TryGetPlayerSpawn(ulong clientId, out Vector3 position, out Quaternion rotation)
    {
        int spawnCount = PlayerSpawnPointCount;

        if (spawnCount == 0)
        {
            position = transform.position;
            rotation = transform.rotation;
            return false;
        }

        int spawnIndex = ResolveSpawnIndex(clientId, spawnCount);
        Transform spawnPoint = playerSpawnPoints[spawnIndex];

        if (spawnPoint == null)
        {
            position = transform.position;
            rotation = transform.rotation;
            return false;
        }

        position = spawnPoint.position;
        rotation = spawnPoint.rotation;
        return true;
    }

    // Client ids are not contiguous - taking clientId % spawnCount put two
    // connected players on the same point as soon as somebody reconnected.
    // Every client keeps the point it was first given.
    // ponytail: linear scan over spawn points, they are counted in single digits
    private int ResolveSpawnIndex(ulong clientId, int spawnCount)
    {
        if (assignedSpawnIndices.TryGetValue(clientId, out int assignedIndex) &&
            assignedIndex < spawnCount)
        {
            return assignedIndex;
        }

        for (int index = 0; index < spawnCount; index++)
        {
            if (IsSpawnIndexTaken(index))
            {
                continue;
            }

            assignedSpawnIndices[clientId] = index;
            return index;
        }

        // More players than spawn points: somebody has to share one.
        int fallbackIndex = (int)(clientId % (ulong)spawnCount);
        assignedSpawnIndices[clientId] = fallbackIndex;
        return fallbackIndex;
    }

    private bool IsSpawnIndexTaken(int spawnIndex)
    {
        foreach (KeyValuePair<ulong, int> assignment in assignedSpawnIndices)
        {
            if (assignment.Value == spawnIndex)
            {
                return true;
            }
        }

        return false;
    }

#if UNITY_EDITOR
    public void ConfigureEditor(
        Transform[] spawnPoints,
        ObjectiveSceneBindingRegistry bindingRegistry)
    {
        playerSpawnPoints = spawnPoints;
        objectiveBindingRegistry = bindingRegistry;
    }
#endif
}
