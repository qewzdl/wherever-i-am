using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.AI;

[DisallowMultipleComponent]
[RequireComponent(typeof(NavMeshObstacle))]
public sealed class ItemNavigationObstacle : NetworkBehaviour
{
    // ponytail: physical-contact reservation is renewed every FixedUpdate
    // tick while an enemy is actually touching the item, so this only
    // needs to bridge single-frame gaps, not a multi-second approach walk.
    private const float PushReservationDuration = 0.5f;

    private static readonly HashSet<ItemNavigationObstacle> activeServerBarriers = new();

    // Used only as a last-resort search when the enemy's normal route
    // toward its destination doesn't lead anywhere near a barrier (e.g.
    // a sealed room reachable only by pushing a specific distant item).
    public static IReadOnlyCollection<ItemNavigationObstacle> ActiveServerBarriers =>
        activeServerBarriers;

    [SerializeField] private DraggableObject item;
    [SerializeField] private NavMeshObstacle obstacle;
    [SerializeField, Min(0f)] private float boundsPadding = 0.05f;
    [SerializeField, Min(0.01f)] private float moveThreshold = 0.1f;
    [SerializeField, Min(0f)] private float timeToStationary = 0.25f;

    private bool subscribed;
    private int enemyReservationOwnerId;
    private float enemyReservationExpiresAt;

    public bool IsBlockingNavigation =>
        obstacle != null && obstacle.enabled && obstacle.carving;

    // Planar half-diagonal of the carved footprint: how far from this
    // item's centre a NavMesh sample must reach to have any chance of
    // landing outside the hole the item itself carves.
    public float ApproachRadius
    {
        get
        {
            ResolveReferences();

            if (obstacle == null)
            {
                return 0.5f;
            }

            Vector3 halfSize = obstacle.size * 0.5f;
            return new Vector2(halfSize.x, halfSize.z).magnitude;
        }
    }

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

    // The closest point on the item's actual physical surface to the
    // given position — never behind the item's own pivot, which can sit
    // flush against whatever the item is backed up against (a wall, for
    // furniture placed that way, which is the common case). Aiming
    // physical push movement at the raw transform position has no such
    // guarantee and can steer an enemy straight into that wall.
    public Vector3 GetClosestSurfacePoint(Vector3 fromPosition)
    {
        ResolveReferences();

        if (item == null || item.Colliders == null)
        {
            return transform.position;
        }

        Vector3 closest = transform.position;
        float bestSqrDistance = float.PositiveInfinity;

        foreach (Collider itemCollider in item.Colliders)
        {
            if (itemCollider == null || !itemCollider.enabled)
            {
                continue;
            }

            Vector3 point = itemCollider.ClosestPoint(fromPosition);
            float sqrDistance = (point - fromPosition).sqrMagnitude;

            if (sqrDistance >= bestSqrDistance)
            {
                continue;
            }

            bestSqrDistance = sqrDistance;
            closest = point;
        }

        return closest;
    }

    internal bool TryBeginPhysicalEnemyPushServer(int reservationOwnerId)
    {
        ResolveReferences();

        if (!CanAcceptEnemyPush() ||
            IsReservedByOtherEnemy(reservationOwnerId) ||
            !item.TryBeginEnemyPushServer())
        {
            return false;
        }

        TryReserveForEnemy(reservationOwnerId, PushReservationDuration);
        return true;
    }

    public bool TryReserveForEnemy(int ownerId, float duration)
    {
        if (ownerId == 0 ||
            !CanAcceptEnemyPush() ||
            IsReservedByOtherEnemy(ownerId))
        {
            return false;
        }

        enemyReservationOwnerId = ownerId;
        enemyReservationExpiresAt =
            Time.time + Mathf.Max(0.05f, duration);
        return true;
    }

    public void ReleaseEnemyReservation(int ownerId)
    {
        if (ownerId != 0 && enemyReservationOwnerId == ownerId)
        {
            ClearEnemyReservation();
        }
    }

    public bool IsReservedByOtherEnemy(int ownerId)
    {
        ExpireEnemyReservationIfNeeded();
        return enemyReservationOwnerId != 0 &&
               enemyReservationOwnerId != ownerId;
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
        ClearEnemyReservation();
        activeServerBarriers.Remove(this);
        Unsubscribe();
        SetObstacleEnabled(false);
        base.OnNetworkDespawn();
    }

    private void OnDisable()
    {
        ClearEnemyReservation();
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

    private void RefreshBarrierRegistration()
    {
        if (IsSpawned && IsServer && IsBlockingNavigation && CanAcceptEnemyPush())
        {
            activeServerBarriers.Add(this);
            return;
        }

        activeServerBarriers.Remove(this);
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
            EnemyNavigationTopology.MarkChanged();
        }

        if (!value)
        {
            activeServerBarriers.Remove(this);
        }
    }

    private void ExpireEnemyReservationIfNeeded()
    {
        if (enemyReservationOwnerId != 0 &&
            Time.time >= enemyReservationExpiresAt)
        {
            ClearEnemyReservation();
        }
    }

    private void ClearEnemyReservation()
    {
        enemyReservationOwnerId = 0;
        enemyReservationExpiresAt = 0f;
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
