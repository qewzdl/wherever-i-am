using UnityEngine;

[DisallowMultipleComponent]
public sealed class GameMapRoot : MonoBehaviour
{
    [Header("Gameplay")]
    [SerializeField] private Transform[] playerSpawnPoints;
    [SerializeField] private ObjectiveSceneBindingRegistry objectiveBindingRegistry;

    public ObjectiveSceneBindingRegistry ObjectiveBindingRegistry => objectiveBindingRegistry;
    public int PlayerSpawnPointCount => playerSpawnPoints == null ? 0 : playerSpawnPoints.Length;

    public bool TryGetPlayerSpawn(ulong clientId, out Vector3 position, out Quaternion rotation)
    {
        int spawnCount = PlayerSpawnPointCount;

        if (spawnCount == 0)
        {
            position = transform.position;
            rotation = transform.rotation;
            return false;
        }

        int spawnIndex = (int)(clientId % (ulong)spawnCount);
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

#if UNITY_EDITOR
    public void ConfigureEditor(Transform[] spawnPoints)
    {
        playerSpawnPoints = spawnPoints;
    }
#endif
}
