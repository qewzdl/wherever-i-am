using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;

// Puts enemies into a map at runtime instead of having them stand in the scene
// waiting for the match to start. Hand placed enemies cannot be varied by
// difficulty, cannot be added partway through a match, and cannot be the one
// that appears because somebody picked the doll up.
//
// It knows how to spawn and nothing about what: which enemy belongs where is
// on the spawn points, which live in the map. This lives in the Game scene,
// the shell every map is played in, and naming an enemy from there would make
// every map share one.
//
// It composes itself the way NetworkObjectiveFlow does - resolving the map
// service on spawn and waiting for the map if it is not ready yet.
[DisallowMultipleComponent]
public sealed class NetworkEnemySpawner : NetworkBehaviour
{
    [Header("Behaviour")]
    [Tooltip("Fill every spawn point the map declares as soon as the map is ready.")]
    [SerializeField] private bool spawnMapEnemiesWhenReady = true;

    private readonly List<NetworkEnemyController> spawnedEnemies = new();
    private IGameMapSessionService gameMapService;
    private bool subscribedToMapReady;

    public IReadOnlyList<NetworkEnemyController> SpawnedEnemies => spawnedEnemies;

    public override void OnNetworkSpawn()
    {
        if (!IsServer)
        {
            return;
        }

        NetworkObjectServiceContext.TryResolveSessionService(
            NetworkManager,
            out gameMapService);

        if (!spawnMapEnemiesWhenReady || gameMapService == null)
        {
            return;
        }

        if (gameMapService.IsReadyForMatch)
        {
            SpawnMapEnemiesServerOnly();
            return;
        }

        SubscribeToMapReady();
    }

    public override void OnNetworkDespawn()
    {
        UnsubscribeFromMapReady();
        spawnedEnemies.Clear();
    }

    // Server only. The enemy reads the difficulty the host chose on its own,
    // when it spawns.
    public bool TrySpawnServerOnly(
        EnemySpawnPoint spawnPoint,
        out NetworkEnemyController enemy)
    {
        enemy = null;

        if (spawnPoint == null)
        {
            Debug.LogError($"{nameof(NetworkEnemySpawner)} received no spawn point.", this);
            return false;
        }

        if (spawnPoint.EnemyPrefab == null)
        {
            Debug.LogError(
                $"Enemy spawn point '{spawnPoint.name}' names no enemy, so nothing " +
                "can be put there.",
                spawnPoint);

            return false;
        }

        return TrySpawnServerOnly(
            spawnPoint.EnemyPrefab,
            spawnPoint.Position,
            spawnPoint.Rotation,
            spawnPoint.PatrolRoute,
            out enemy);
    }

    public bool TrySpawnServerOnly(
        NetworkEnemyController enemyPrefab,
        Vector3 position,
        Quaternion rotation,
        EnemyPatrolRoute patrolRoute,
        out NetworkEnemyController enemy)
    {
        enemy = null;

        if (!IsServer)
        {
            Debug.LogError("Only the server can spawn enemies.", this);
            return false;
        }

        if (!ValidatePrefab(enemyPrefab, out string error))
        {
            Debug.LogError(error, this);
            return false;
        }

        NetworkEnemyController instance = Instantiate(enemyPrefab, position, rotation);
        instance.ConstructServerOnly(patrolRoute);

        NetworkObject spawnedObject = instance.GetComponent<NetworkObject>();

        // Destroyed with the scene, the way player objects are: an enemy
        // belongs to the match that spawned it and must not outlive its map.
        spawnedObject.Spawn(true);

        if (!spawnedObject.IsSpawned)
        {
            Debug.LogError($"{nameof(NetworkEnemySpawner)} could not spawn an enemy.", this);
            Destroy(instance.gameObject);
            return false;
        }

        spawnedEnemies.Add(instance);
        enemy = instance;
        return true;
    }

    // Server only. Fills every spawn point the active map declares.
    public int SpawnMapEnemiesServerOnly()
    {
        if (!IsServer)
        {
            Debug.LogError("Only the server can spawn enemies.", this);
            return 0;
        }

        GameMapRoot mapRoot = gameMapService?.ActiveMapRoot;

        if (mapRoot == null)
        {
            Debug.LogError(
                $"{nameof(NetworkEnemySpawner)} has no active map to spawn enemies into.",
                this);

            return 0;
        }

        IReadOnlyList<EnemySpawnPoint> points = mapRoot.EnemySpawnPoints;
        int spawned = 0;

        for (int i = 0; i < points.Count; i++)
        {
            if (TrySpawnServerOnly(points[i], out _))
            {
                spawned++;
            }
        }

        return spawned;
    }

    public void DespawnAllServerOnly()
    {
        if (!IsServer)
        {
            return;
        }

        for (int i = spawnedEnemies.Count - 1; i >= 0; i--)
        {
            NetworkEnemyController enemy = spawnedEnemies[i];

            if (enemy == null)
            {
                continue;
            }

            NetworkObject enemyObject = enemy.GetComponent<NetworkObject>();

            if (enemyObject != null && enemyObject.IsSpawned)
            {
                enemyObject.Despawn(true);
            }
            else
            {
                Destroy(enemy.gameObject);
            }
        }

        spawnedEnemies.Clear();
    }

    private void SubscribeToMapReady()
    {
        if (subscribedToMapReady || gameMapService == null)
        {
            return;
        }

        gameMapService.MapReady += HandleMapReady;
        subscribedToMapReady = true;
    }

    private void UnsubscribeFromMapReady()
    {
        if (!subscribedToMapReady || gameMapService == null)
        {
            return;
        }

        gameMapService.MapReady -= HandleMapReady;
        subscribedToMapReady = false;
    }

    private void HandleMapReady()
    {
        UnsubscribeFromMapReady();
        SpawnMapEnemiesServerOnly();
    }

    private bool ValidatePrefab(NetworkEnemyController prefab, out string error)
    {
        if (prefab == null)
        {
            error = $"{nameof(NetworkEnemySpawner)} was given no enemy prefab.";
            return false;
        }

        // GetComponent rather than the NetworkObject property: on a prefab
        // asset that property is null, because it only resolves for a live
        // instance.
        NetworkObject prefabObject = prefab.GetComponent<NetworkObject>();

        if (prefabObject == null)
        {
            error = $"Enemy prefab '{prefab.name}' has no {nameof(NetworkObject)}.";
            return false;
        }

        // Spawning an unregistered prefab fails inside NGO with a message about
        // hashes. Saying it here names the prefab and the fix.
        if (!IsRegisteredNetworkPrefab(prefabObject))
        {
            error =
                $"Enemy prefab '{prefab.name}' is not a registered network prefab, so it " +
                "cannot be spawned at runtime. Add it to the network prefabs list.";
            return false;
        }

        error = string.Empty;
        return true;
    }

    private bool IsRegisteredNetworkPrefab(NetworkObject prefabObject)
    {
        NetworkConfig config = NetworkManager != null ? NetworkManager.NetworkConfig : null;

        if (config?.Prefabs == null)
        {
            return true;
        }

        return config.Prefabs.Contains(prefabObject.gameObject);
    }
}
