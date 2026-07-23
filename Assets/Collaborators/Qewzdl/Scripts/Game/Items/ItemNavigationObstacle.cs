using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.AI;

[DisallowMultipleComponent]
[RequireComponent(typeof(NavMeshObstacle))]
public sealed class ItemNavigationObstacle : NetworkBehaviour
{
    [SerializeField] private DraggableObject item;
    [SerializeField] private NavMeshObstacle obstacle;
    [SerializeField, Min(0f)] private float boundsPadding = 0.05f;
    [SerializeField, Min(0.01f)] private float moveThreshold = 0.1f;
    [SerializeField, Min(0f)] private float timeToStationary = 0.25f;

    private bool subscribed;
    private readonly HashSet<int> enemyPushSources = new();

    public bool IsBlockingNavigation =>
        obstacle != null && obstacle.enabled;

    public bool IsBeingPushedByEnemy => enemyPushSources.Count > 0;

    public bool CanBePushedByEnemyNow
    {
        get
        {
            ResolveReferences();
            return CanAcceptEnemyPush();
        }
    }

    internal bool TryBeginEnemyPushServer(int sourceId)
    {
        ResolveReferences();

        if (!CanAcceptEnemyPush())
        {
            ReleaseEnemyPushServer(sourceId);
            return false;
        }

        enemyPushSources.Add(sourceId);
        RefreshObstacleState();

        if (item.TryBeginEnemyPushServer())
        {
            return true;
        }

        enemyPushSources.Remove(sourceId);
        RefreshObstacleState();
        return false;
    }

    internal void ReleaseEnemyPushServer(int sourceId)
    {
        if (enemyPushSources.Remove(sourceId))
        {
            RefreshObstacleState();
        }
    }

    private void Awake()
    {
        ResolveReferences();
        ConfigureObstacle();
        SetObstacleEnabled(false);
    }

    private void OnEnable()
    {
        if (!IsSpawned)
        {
            return;
        }

        Subscribe();
        RefreshObstacleState();
    }

    public override void OnNetworkSpawn()
    {
        base.OnNetworkSpawn();
        Subscribe();
        RefreshObstacleState();
    }

    public override void OnNetworkDespawn()
    {
        enemyPushSources.Clear();
        Unsubscribe();
        SetObstacleEnabled(false);
        base.OnNetworkDespawn();
    }

    private void OnDisable()
    {
        enemyPushSources.Clear();
        Unsubscribe();
        SetObstacleEnabled(false);
    }

    private void HandleDraggingChanged(bool _)
    {
        RefreshObstacleState();
    }

    private void HandlePickedUpChanged(bool _)
    {
        RefreshObstacleState();
    }

    private void RefreshObstacleState()
    {
        ResolveReferences();

        if (!IsSpawned ||
            !IsServer ||
            item == null ||
            !item.BlocksEnemyNavigation ||
            item is PickupItem { IsPickedUp: true })
        {
            if (obstacle != null)
            {
                obstacle.carving = false;
            }

            SetObstacleEnabled(false);
            return;
        }

        bool isReservedForPhysicalPush = enemyPushSources.Count > 0;
        SetObstacleEnabled(!isReservedForPhysicalPush);
        obstacle.carving =
            !isReservedForPhysicalPush &&
            !item.IsBeingDragged;
    }

    private bool CanAcceptEnemyPush()
    {
        if (!IsSpawned ||
            !IsServer ||
            item == null ||
            !item.CanBePushedByEnemies ||
            item.IsBeingDragged)
        {
            return false;
        }

        if (item is PickupItem pickup)
        {
            return !pickup.IsPickedUp;
        }

        return true;
    }

    private void Subscribe()
    {
        if (subscribed || item == null)
        {
            return;
        }

        item.DraggingChanged += HandleDraggingChanged;

        if (item is PickupItem pickup)
        {
            pickup.PickedUpChanged += HandlePickedUpChanged;
        }

        subscribed = true;
    }

    private void Unsubscribe()
    {
        if (!subscribed || item == null)
        {
            return;
        }

        item.DraggingChanged -= HandleDraggingChanged;

        if (item is PickupItem pickup)
        {
            pickup.PickedUpChanged -= HandlePickedUpChanged;
        }

        subscribed = false;
    }

    private void ResolveReferences()
    {
        if (item == null)
        {
            item = GetComponent<DraggableObject>();
        }

        if (obstacle == null)
        {
            obstacle = GetComponent<NavMeshObstacle>();
        }
    }

    private void ConfigureObstacle()
    {
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
        ResolveReferences();
        ConfigureObstacle();
        SetObstacleEnabled(false);
    }

    private void OnValidate()
    {
        boundsPadding = Mathf.Max(0f, boundsPadding);
        moveThreshold = Mathf.Max(0.01f, moveThreshold);
        timeToStationary = Mathf.Max(0f, timeToStationary);
        ResolveReferences();
    }
#endif
}
