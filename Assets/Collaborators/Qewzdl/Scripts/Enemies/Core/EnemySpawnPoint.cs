using UnityEngine;

// Which enemy a map puts here, and what it walks once it is there. Both are
// the map's decision: the Game scene is the shell every map is played in and
// has no business naming the enemy, and a house may want different ones on
// different floors.
//
// A prefab cannot reference a route that lives in the scene, so the placement
// holds the route and hands it over at spawn time.
[DisallowMultipleComponent]
public sealed class EnemySpawnPoint : MonoBehaviour
{
    [Tooltip("Must also be registered as a network prefab, or it cannot be spawned at runtime.")]
    [SerializeField] private NetworkEnemyController enemyPrefab;

    [Tooltip("Optional. Without one the enemy stands its ground and reacts to what it notices.")]
    [SerializeField] private EnemyPatrolRoute patrolRoute;

    public NetworkEnemyController EnemyPrefab => enemyPrefab;
    public EnemyPatrolRoute PatrolRoute => patrolRoute;
    public Vector3 Position => transform.position;
    public Quaternion Rotation => transform.rotation;

#if UNITY_EDITOR
    public void ConfigureEditor(NetworkEnemyController prefab, EnemyPatrolRoute route)
    {
        enemyPrefab = prefab;
        patrolRoute = route;
    }

    private void OnDrawGizmos()
    {
        Vector3 position = transform.position;

        // Hollow when there is no enemy to put here, so an unfinished point is
        // visible from across the room rather than found by clicking it.
        Gizmos.color = enemyPrefab != null
            ? new Color(0.8f, 0.2f, 0.2f, 0.7f)
            : new Color(0.5f, 0.5f, 0.5f, 0.5f);
        Gizmos.DrawWireSphere(position, 0.4f);
        Gizmos.DrawRay(position, transform.forward);

        // Which route this one walks, drawn rather than read off the inspector
        // one point at a time. A map with several enemies and several routes is
        // otherwise a guessing game.
        if (patrolRoute == null || !patrolRoute.HasPoints)
        {
            return;
        }

        Transform firstPoint = patrolRoute.GetPoint(0);

        if (firstPoint == null)
        {
            return;
        }

        Gizmos.color = new Color(0.8f, 0.5f, 0.2f, 0.5f);
        Gizmos.DrawLine(position, firstPoint.position);
    }
#endif
}
