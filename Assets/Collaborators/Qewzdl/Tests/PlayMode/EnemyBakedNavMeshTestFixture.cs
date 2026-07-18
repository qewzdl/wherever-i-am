using UnityEngine;

public sealed class EnemyBakedNavMeshTestFixture : MonoBehaviour
{
    [SerializeField] private GameObject enemyPrefab;
    [SerializeField] private EnemyConfig enemyConfig;

    public GameObject EnemyPrefab => enemyPrefab;
    public EnemyConfig EnemyConfig => enemyConfig;
}
