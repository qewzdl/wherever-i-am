using Unity.Netcode;
using UnityEngine;
using UnityEngine.AI;

[DisallowMultipleComponent]
[RequireComponent(typeof(NavMeshObstacle))]
public sealed class ItemNavigationObstacle : NetworkBehaviour
{
    private const float NavigationCarvingPropagationPadding = 0.1f;
    private const float RotationMovementThreshold = 1f;

    [SerializeField] private DraggableObject item;
    [SerializeField] private NavMeshObstacle obstacle;
    [SerializeField, Min(0f)] private float boundsPadding = 0.05f;
    [SerializeField, Min(0.01f)] private float moveThreshold = 0.1f;
    [SerializeField, Min(0f)] private float timeToStationary = 0.25f;

    private bool subscribed;
    private bool tracksNavigationSettling;
    private Vector3 navigationSettlingPosition;
    private Quaternion navigationSettlingRotation;
    private float navigationReadyTime = float.PositiveInfinity;

    public bool IsBlockingNavigation =>
        obstacle != null && obstacle.enabled;

    public bool IsReadyForNavigationPlanning
    {
        get
        {
            ResolveReferences();
            UpdateNavigationReadiness();

            return IsCarvingNavigation &&
                   Time.time >= navigationReadyTime;
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

    internal bool TryBeginPhysicalEnemyPushServer()
    {
        ResolveReferences();
        return CanAcceptEnemyPush() && item.TryBeginEnemyPushServer();
    }

    private void Awake()
    {
        ResolveReferences();
        ConfigureObstacle();
        SetObstacleEnabled(false);
        ClearNavigationReadiness();
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
        ClearNavigationReadiness();
        base.OnNetworkDespawn();
    }

    private void OnDisable()
    {
        Unsubscribe();
        SetObstacleEnabled(false);
        ClearNavigationReadiness();
    }

    private void Update()
    {
        UpdateNavigationReadiness();
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
        bool wasCarvingNavigation = IsCarvingNavigation;

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
            ClearNavigationReadiness();
            return;
        }

        SetObstacleEnabled(true);
        obstacle.carving = !item.IsBeingDragged;

        if (IsCarvingNavigation)
        {
            if (!wasCarvingNavigation)
            {
                BeginNavigationSettling();
            }
        }
        else
        {
            ClearNavigationReadiness();
        }
    }

    private bool IsCarvingNavigation =>
        obstacle != null &&
        obstacle.enabled &&
        obstacle.carving;

    private void UpdateNavigationReadiness()
    {
        if (!IsCarvingNavigation)
        {
            ClearNavigationReadiness();
            return;
        }

        if (!tracksNavigationSettling)
        {
            BeginNavigationSettling();
            return;
        }

        float movementThreshold = Mathf.Max(0.01f, moveThreshold);
        bool moved =
            (transform.position - navigationSettlingPosition).sqrMagnitude >
            movementThreshold * movementThreshold;
        bool rotated = Quaternion.Angle(
            transform.rotation,
            navigationSettlingRotation) > RotationMovementThreshold;

        if (moved || rotated)
        {
            BeginNavigationSettling();
        }
    }

    private void BeginNavigationSettling()
    {
        tracksNavigationSettling = true;
        navigationSettlingPosition = transform.position;
        navigationSettlingRotation = transform.rotation;
        navigationReadyTime =
            Time.time +
            Mathf.Max(0f, timeToStationary) +
            NavigationCarvingPropagationPadding;
    }

    private void ClearNavigationReadiness()
    {
        tracksNavigationSettling = false;
        navigationSettlingPosition = default;
        navigationSettlingRotation = default;
        navigationReadyTime = float.PositiveInfinity;
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
