using Unity.Netcode;
using UnityEngine;
using UnityEngine.AI;

[DisallowMultipleComponent]
[RequireComponent(typeof(NavMeshObstacle))]
public sealed class HidingPlaceNavigationObstacle : NetworkBehaviour
{
    [SerializeField] private NavMeshObstacle obstacle;
    [SerializeField, Min(0f)] private float boundsPadding = 0.05f;
    [SerializeField, Min(0.01f)] private float moveThreshold = 0.1f;
    [SerializeField, Min(0f)] private float timeToStationary = 0.1f;

    public bool IsBlockingNavigation =>
        obstacle != null && obstacle.enabled && obstacle.carving;

    private void Awake()
    {
        ResolveAndConfigure();
        SetObstacleEnabled(false);
    }

    private void OnEnable()
    {
        if (IsSpawned)
        {
            RefreshObstacleState();
        }
    }

    public override void OnNetworkSpawn()
    {
        base.OnNetworkSpawn();
        RefreshObstacleState();
    }

    public override void OnNetworkDespawn()
    {
        SetObstacleEnabled(false);
        base.OnNetworkDespawn();
    }

    private void OnDisable()
    {
        SetObstacleEnabled(false);
    }

    private void RefreshObstacleState()
    {
        ResolveAndConfigure();

        if (obstacle != null)
        {
            obstacle.carving = true;
        }

        SetObstacleEnabled(IsSpawned && IsServer);
    }

    private void ResolveAndConfigure()
    {
        if (obstacle == null)
        {
            obstacle = GetComponent<NavMeshObstacle>();
        }

        NavigationObstacleBoundsUtility.ConfigureBox(
            transform,
            obstacle,
            boundsPadding,
            moveThreshold,
            timeToStationary);
    }

    private void SetObstacleEnabled(bool value)
    {
        if (obstacle != null && obstacle.enabled != value)
        {
            obstacle.enabled = value;
        }
    }

#if UNITY_EDITOR
    private void Reset()
    {
        ResolveAndConfigure();

        if (obstacle != null)
        {
            obstacle.carving = true;
        }

        SetObstacleEnabled(false);
    }

    private void OnValidate()
    {
        boundsPadding = Mathf.Max(0f, boundsPadding);
        moveThreshold = Mathf.Max(0.01f, moveThreshold);
        timeToStationary = Mathf.Max(0f, timeToStationary);
        ResolveAndConfigure();
    }
#endif
}
