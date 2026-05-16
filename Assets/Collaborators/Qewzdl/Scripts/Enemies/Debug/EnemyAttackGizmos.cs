using UnityEngine;

[DisallowMultipleComponent]
[RequireComponent(typeof(NetworkEnemyController))]
public class EnemyAttackGizmos : MonoBehaviour
{
    [Header("References")]
    [SerializeField] private NetworkEnemyController enemyController;

    [Header("Drawing")]
    [SerializeField] private bool drawOnlyWhenSelected = true;

    [Header("Colors")]
    [SerializeField] private Color attackDistanceColor = Color.magenta;

    private void Awake()
    {
        CacheComponents();
    }

    private void OnDrawGizmos()
    {
        if (drawOnlyWhenSelected)
        {
            return;
        }

        DrawGizmos();
    }

    private void OnDrawGizmosSelected()
    {
        if (!drawOnlyWhenSelected)
        {
            return;
        }

        DrawGizmos();
    }

    private void DrawGizmos()
    {
        CacheComponents();

        if (enemyController == null || enemyController.Config == null)
        {
            return;
        }

        Gizmos.color = attackDistanceColor;
        Gizmos.DrawWireSphere(transform.position, enemyController.Config.attackDistance);
    }

    private void CacheComponents()
    {
        if (enemyController == null)
        {
            enemyController = GetComponent<NetworkEnemyController>();
        }
    }

#if UNITY_EDITOR
    private void Reset()
    {
        CacheComponents();
    }

    private void OnValidate()
    {
        CacheComponents();
    }
#endif
}