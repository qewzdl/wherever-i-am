using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.AI;

[DisallowMultipleComponent]
[RequireComponent(typeof(NavMeshObstacle))]
public sealed class ItemNavigationObstacle : NetworkBehaviour
{
    private static readonly HashSet<ItemNavigationObstacle>
        activeServerBarriers = new();

    [SerializeField] private DraggableObject item;
    [SerializeField] private NavMeshObstacle obstacle;
    [SerializeField, Min(0f)] private float boundsPadding = 0.05f;
    [SerializeField, Min(0.01f)] private float moveThreshold = 0.1f;
    [SerializeField, Min(0f)] private float timeToStationary = 0.25f;

    private bool subscribed;

    public bool IsBlockingNavigation =>
        obstacle != null && obstacle.enabled && obstacle.carving;

    public bool CanBePushedByEnemyNow
    {
        get
        {
            ResolveReferences();
            return CanAcceptEnemyPush();
        }
    }

    public float EnemyPushResistance
    {
        get
        {
            ResolveReferences();
            return item != null ? item.EnemyPushResistance : 0f;
        }
    }

    internal bool TryBeginPhysicalEnemyPushServer()
    {
        ResolveReferences();
        return CanAcceptEnemyPush() && item.TryBeginEnemyPushServer();
    }

    public bool TryGetBarrierGeometry(
        out Vector3 center,
        out Vector3 axisX,
        out Vector3 axisZ,
        out Vector3 halfAxisX,
        out Vector3 halfAxisY,
        out Vector3 halfAxisZ)
    {
        ResolveReferences();

        if (!IsBlockingNavigation || obstacle.shape != NavMeshObstacleShape.Box)
        {
            center = default;
            axisX = default;
            axisZ = default;
            halfAxisX = default;
            halfAxisY = default;
            halfAxisZ = default;
            return false;
        }

        center = transform.TransformPoint(obstacle.center);
        Vector3 halfSize = obstacle.size * 0.5f;
        halfAxisX = transform.TransformVector(Vector3.right * halfSize.x);
        halfAxisY = transform.TransformVector(Vector3.up * halfSize.y);
        halfAxisZ = transform.TransformVector(Vector3.forward * halfSize.z);
        axisX = Vector3.ProjectOnPlane(halfAxisX, Vector3.up).normalized;
        axisZ = Vector3.ProjectOnPlane(halfAxisZ, Vector3.up).normalized;
        return true;
    }

    public static void CopyActiveServerBarriersTo(
        List<ItemNavigationObstacle> target)
    {
        target.Clear();

        foreach (ItemNavigationObstacle barrier in activeServerBarriers)
        {
            if (barrier != null &&
                barrier.IsBlockingNavigation &&
                barrier.CanBePushedByEnemyNow)
            {
                target.Add(barrier);
            }
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
        activeServerBarriers.Remove(this);
        Unsubscribe();
        SetObstacleEnabled(false);
        base.OnNetworkDespawn();
    }

    private void OnDisable()
    {
        activeServerBarriers.Remove(this);
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
            item.IsBeingDragged ||
            item is PickupItem { IsPickedUp: true })
        {
            if (obstacle != null)
            {
                obstacle.carving = false;
            }

            SetObstacleEnabled(false);
            return;
        }

        obstacle.carving = true;
        SetObstacleEnabled(true);
        RefreshBarrierRegistration();
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

        if (!value)
        {
            activeServerBarriers.Remove(this);
        }
    }

    private void RefreshBarrierRegistration()
    {
        if (IsSpawned &&
            IsServer &&
            IsBlockingNavigation &&
            CanAcceptEnemyPush())
        {
            activeServerBarriers.Add(this);
            return;
        }

        activeServerBarriers.Remove(this);
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetBarrierRegistry()
    {
        activeServerBarriers.Clear();
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
