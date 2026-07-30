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

    private static readonly HashSet<ItemNavigationObstacle> blockingInstances = new();

    private bool subscribed;
    private int pushThroughRequests;

    public bool IsBlockingNavigation =>
        obstacle != null && obstacle.enabled && obstacle.carving;

    // Every item currently carving the NavMesh. Lets a blocked enemy lift all
    // of them at once instead of guessing which one barricades the route.
    public static IReadOnlyCollection<ItemNavigationObstacle> BlockingInstances =>
        blockingInstances;

    // ponytail: a request counter, not a queue - lets more than one enemy
    // push through the same barricade without one's release re-blocking
    // it for the other.
    public void RequestPushThrough()
    {
        pushThroughRequests++;
        ReclaimServerOwnershipForPush();
        RefreshObstacleState();
    }

    public void ReleasePushThrough()
    {
        if (pushThroughRequests > 0)
        {
            pushThroughRequests--;
        }

        RefreshObstacleState();
    }

    // Server-only forceful shove: an occasional alternative to just walking
    // through the item on the way past it.
    public void ApplyForcefulPush(Vector3 origin, float force)
    {
        if (!IsServer || item == null || force <= 0f)
        {
            return;
        }

        Vector3 direction = transform.position - origin;
        direction.y = 0f;

        if (direction.sqrMagnitude < 0.0001f)
        {
            direction = transform.forward;
        }

        item.ApplyImpulse(direction.normalized * force);
    }

    // A dropped item keeps whatever client last dragged it as its network
    // owner (never reverts on its own), and NetworkRigidbody makes the
    // rigidbody kinematic on every non-owner instance - including the
    // server, where the enemy's physics run. Without reclaiming ownership
    // here, a previously-dragged item is an immovable kinematic wall to
    // the enemy no matter its mass; only untouched, still-server-owned
    // items were ever actually pushable.
    private void ReclaimServerOwnershipForPush()
    {
        if (!IsServer || item == null)
        {
            return;
        }

        NetworkObject itemNetworkObject = item.NetworkObject;

        if (itemNetworkObject != null &&
            itemNetworkObject.OwnerClientId != NetworkManager.ServerClientId)
        {
            itemNetworkObject.ChangeOwnership(NetworkManager.ServerClientId);
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
        Unsubscribe();
        SetObstacleEnabled(false);
        base.OnNetworkDespawn();
    }

    private void OnDisable()
    {
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
            item is PickupItem { IsPickedUp: true } ||
            pushThroughRequests > 0)
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
        if (value)
        {
            blockingInstances.Add(this);
        }
        else
        {
            blockingInstances.Remove(this);
        }

        if (obstacle != null && obstacle.enabled != value)
        {
            obstacle.enabled = value;
            EnemyNavigationTopology.MarkChanged();
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
