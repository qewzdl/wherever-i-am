using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;

public class DoorInteractableObject : InteractableObject
{
    private static readonly List<DoorInteractableObject> RegisteredLineOfSightBlockers = new();

    [SerializeField] private Transform doorPivot;
    [SerializeField] private bool startsOpen;
    [SerializeField] private Vector3 closedEulerAngles;
    [SerializeField] private Vector3 openEulerAngles = new(0f, -90f, 0f);
    [SerializeField] private bool useLocalRotation;

    [Header("State")]
    [Min(0f)]
    [SerializeField] private float transitionDuration;

    [Header("Enemy Interaction Lock")]
    [Min(0f)]
    [SerializeField] private float enemyOpenCloseLockGraceDuration = 1.5f;
    [Min(0.1f)]
    [SerializeField] private float enemyReservationTimeout = 3f;

    private readonly NetworkVariable<DoorState> currentState = new(
        DoorState.Closed,
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Server
    );

    private readonly NetworkVariable<DoorReservationState> reservationState = new(
        DoorReservationState.None,
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Server
    );

    private readonly List<Collider> occupyingActorColliders = new();
    private readonly List<Collider> lineOfSightColliders = new();

    private DoorState localState;
    private DoorState pendingTransitionTarget;
    private DoorReservationState localReservationState;

    private float transitionCompletesAt;
    private float closeLockedUntil;
    private float enemyReservationExpiresAt;

    public DoorState CurrentState => IsSpawned ? currentState.Value : localState;
    public DoorReservationState ReservationState =>
        IsSpawned ? reservationState.Value : localReservationState;
    public static IReadOnlyList<DoorInteractableObject> LineOfSightBlockers =>
        RegisteredLineOfSightBlockers;
    public bool IsOpen => IsOpenState(CurrentState);
    public bool BlocksLineOfSight => !IsVisuallyOpen(CurrentState);
    public bool IsReservedByEnemy => ReservationState == DoorReservationState.ReservedByEnemy;
    public bool CanClose => CanCloseState(CurrentState) && !IsReservedByEnemy;
    public float EnemyOpenCloseLockGraceDuration => Mathf.Max(0f, enemyOpenCloseLockGraceDuration);
    public bool CanEvaluateAuthoritatively =>
        !IsSpawned ||
        NetworkManager == null ||
        !NetworkManager.IsListening ||
        IsServer;

    private void Awake()
    {
        CacheComponents();

        localState = startsOpen ? DoorState.Open : DoorState.Closed;
        localReservationState = DoorReservationState.None;

        ApplyDoorState(localState);
    }

    private void OnEnable()
    {
        RegisterLineOfSightBlocker(this);
    }

    private void OnDisable()
    {
        UnregisterLineOfSightBlocker(this);
    }

    public override void OnNetworkSpawn()
    {
        base.OnNetworkSpawn();

        currentState.OnValueChanged += SyncState;
        reservationState.OnValueChanged += SyncReservation;

        if (IsServer)
        {
            closeLockedUntil = 0f;
            enemyReservationExpiresAt = 0f;
            occupyingActorColliders.Clear();

            SetReservationStateServer(DoorReservationState.None);
            SetStateServer(startsOpen ? DoorState.Open : DoorState.Closed);
        }

        ApplyDoorState(currentState.Value);
        ApplyReservationState(reservationState.Value);
    }

    public override void OnNetworkDespawn()
    {
        currentState.OnValueChanged -= SyncState;
        reservationState.OnValueChanged -= SyncReservation;

        base.OnNetworkDespawn();
    }

    private void Update()
    {
        if (IsSpawned && !IsServer)
        {
            return;
        }

        TickEnemyReservationTimeout();
        TickStateTimeouts();
        RefreshCloseBlockStateServer();
    }

    public override void OnInteract(InteractionContext context)
    {
        if (IsOpen)
        {
            TryClose();
            return;
        }

        TryOpen();
    }

    public bool TrySetOpen(bool value)
    {
        return value ? TryOpen() : TryClose();
    }

    public bool TryOpen()
    {
        if (IsSpawned && !IsServer)
        {
            if (!CanRequestOpen(CurrentState))
            {
                return false;
            }

            RequestOpenStateRpc(true);
            return true;
        }

        return TryOpenServer();
    }

    public bool TryClose()
    {
        if (IsSpawned && !IsServer)
        {
            if (!CanClose)
            {
                return false;
            }

            RequestOpenStateRpc(false);
            return true;
        }

        return TryCloseServer();
    }

    public bool TryBeginEnemyOpen()
    {
        return TryBeginEnemyOpen(0f);
    }

    public bool TryBeginEnemyOpen(float reservationDuration)
    {
        if (IsSpawned && !IsServer)
        {
            return false;
        }

        if (CurrentState != DoorState.Closed && CurrentState != DoorState.Closing)
        {
            return false;
        }

        if (!TryReserveByEnemyServer(reservationDuration))
        {
            return false;
        }

        ClearTimedTransition();
        SetStateServer(DoorState.Opening);
        return true;
    }

    public bool TryCompleteEnemyOpen()
    {
        return TryCompleteEnemyOpen(0f);
    }

    public bool TryCompleteEnemyOpen(float minimumCloseLockDuration)
    {
        if (IsSpawned && !IsServer)
        {
            return false;
        }

        if (CurrentState != DoorState.Opening)
        {
            return false;
        }

        float lockDuration = Mathf.Max(
            EnemyOpenCloseLockGraceDuration,
            Mathf.Max(0f, minimumCloseLockDuration)
        );

        ClearTimedTransition();
        SetCloseLockedOpenServer(lockDuration, keepEnemyReservation: true);
        return true;
    }

    public bool TryCancelEnemyOpen()
    {
        if (IsSpawned && !IsServer)
        {
            return false;
        }

        bool wasOpening = CurrentState == DoorState.Opening;

        ReleaseEnemyReservationServer();

        if (!wasOpening)
        {
            RefreshCloseBlockStateServer();
            return false;
        }

        ClearTimedTransition();
        SetStateServer(DoorState.Closed);
        return true;
    }

    public bool TryBeginEnemyClose()
    {
        if (IsSpawned && !IsServer)
        {
            return false;
        }

        if (!CanCloseState(CurrentState) || IsCloseBlockedServer())
        {
            RefreshCloseBlockStateServer();
            return false;
        }

        ClearTimedTransition();
        SetStateServer(DoorState.Closing);
        return true;
    }

    public bool TryCompleteEnemyClose()
    {
        if (IsSpawned && !IsServer)
        {
            return false;
        }

        if (CurrentState != DoorState.Closing)
        {
            return false;
        }

        ClearTimedTransition();
        SetStateServer(DoorState.Closed);
        return true;
    }

    public bool TryCancelEnemyClose()
    {
        if (IsSpawned && !IsServer)
        {
            return false;
        }

        if (CurrentState != DoorState.Closing)
        {
            return false;
        }

        ClearTimedTransition();
        SetStateServer(HasBlockingOccupantsServer() ? DoorState.Blocked : DoorState.Open);
        return true;
    }

    public bool TryForceOpen(float closeLockDuration)
    {
        if (IsSpawned && !IsServer)
        {
            return false;
        }

        ClearTimedTransition();
        SetCloseLockedOpenServer(closeLockDuration, keepEnemyReservation: false);
        return true;
    }

    public void RegisterOccupyingActor(Collider actorCollider)
    {
        if (actorCollider == null || IsSpawned && !IsServer)
        {
            return;
        }

        PruneOccupyingActors();

        if (occupyingActorColliders.Contains(actorCollider))
        {
            return;
        }

        occupyingActorColliders.Add(actorCollider);
        RefreshCloseBlockStateServer();
    }

    public void UnregisterOccupyingActor(Collider actorCollider)
    {
        if (actorCollider == null || IsSpawned && !IsServer)
        {
            return;
        }

        occupyingActorColliders.Remove(actorCollider);
        RefreshCloseBlockStateServer();
    }

    public bool TryBlockLineOfSight(Ray ray, float maxDistance)
    {
        if (!BlocksLineOfSight || maxDistance <= Mathf.Epsilon || !isActiveAndEnabled)
        {
            return false;
        }

        PruneLineOfSightColliders();

        if (lineOfSightColliders.Count == 0)
        {
            CacheLineOfSightColliders();
        }

        for (int i = 0; i < lineOfSightColliders.Count; i++)
        {
            Collider lineOfSightCollider = lineOfSightColliders[i];

            if (!CanUseLineOfSightCollider(lineOfSightCollider))
            {
                continue;
            }

            if (lineOfSightCollider.bounds.Contains(ray.origin) ||
                lineOfSightCollider.Raycast(ray, out _, maxDistance))
            {
                return true;
            }
        }

        return false;
    }

    private bool TryOpenServer()
    {
        if (IsReservedByEnemy || !CanRequestOpen(CurrentState))
        {
            return false;
        }

        BeginTimedTransition(DoorState.Opening, DoorState.Open);
        return true;
    }

    private bool TryCloseServer()
    {
        if (!CanCloseState(CurrentState) || IsCloseBlockedServer())
        {
            RefreshCloseBlockStateServer();
            return false;
        }

        BeginTimedTransition(DoorState.Closing, DoorState.Closed);
        return true;
    }

    private void BeginTimedTransition(DoorState transitionState, DoorState targetState)
    {
        float duration = Mathf.Max(0f, transitionDuration);

        ClearTimedTransition();

        if (duration <= 0f)
        {
            SetStateServer(ResolveTargetState(targetState));
            return;
        }

        pendingTransitionTarget = targetState;
        transitionCompletesAt = Time.time + duration;
        SetStateServer(transitionState);
    }

    private void TickEnemyReservationTimeout()
    {
        if (!IsReservedByEnemy ||
            enemyReservationExpiresAt <= 0f ||
            Time.time < enemyReservationExpiresAt)
        {
            return;
        }

        bool wasOpening = CurrentState == DoorState.Opening;

        ReleaseEnemyReservationServer();

        if (!wasOpening)
        {
            RefreshCloseBlockStateServer();
            return;
        }

        ClearTimedTransition();
        SetStateServer(DoorState.Closed);
    }

    private void TickStateTimeouts()
    {
        DoorState state = CurrentState;

        if ((state == DoorState.Opening || state == DoorState.Closing) &&
            transitionCompletesAt > 0f &&
            Time.time >= transitionCompletesAt)
        {
            DoorState targetState = pendingTransitionTarget;
            ClearTimedTransition();
            SetStateServer(ResolveTargetState(targetState));
            return;
        }

        if (state == DoorState.ForcedOpen &&
            closeLockedUntil > 0f &&
            Time.time >= closeLockedUntil)
        {
            closeLockedUntil = 0f;
            ReleaseEnemyReservationServer();
            SetStateServer(HasBlockingOccupantsServer() ? DoorState.Blocked : DoorState.Open);
        }
    }

    private void RefreshCloseBlockStateServer()
    {
        if (IsSpawned && !IsServer)
        {
            return;
        }

        bool hasBlockingOccupants = HasBlockingOccupantsServer();
        DoorState state = CurrentState;

        if (hasBlockingOccupants)
        {
            if (state == DoorState.Open ||
                state == DoorState.Closing ||
                state == DoorState.Blocked)
            {
                ClearTimedTransition();
                SetStateServer(DoorState.Blocked);
            }

            return;
        }

        if (state == DoorState.Blocked)
        {
            SetStateServer(IsCloseLockedServer() ? DoorState.ForcedOpen : DoorState.Open);
        }
    }

    private DoorState ResolveTargetState(DoorState targetState)
    {
        if (targetState == DoorState.Open && HasBlockingOccupantsServer())
        {
            return DoorState.Blocked;
        }

        return targetState;
    }

    private void SetCloseLockedOpenServer(
        float closeLockDuration,
        bool keepEnemyReservation
    )
    {
        float lockDuration = Mathf.Max(0f, closeLockDuration);

        if (lockDuration <= 0f)
        {
            closeLockedUntil = 0f;

            if (!keepEnemyReservation)
            {
                ReleaseEnemyReservationServer();
            }

            SetStateServer(HasBlockingOccupantsServer() ? DoorState.Blocked : DoorState.Open);
            return;
        }

        closeLockedUntil = Time.time + lockDuration;

        if (keepEnemyReservation)
        {
            ExtendEnemyReservationServer(lockDuration);
        }
        else
        {
            ReleaseEnemyReservationServer();
        }

        SetStateServer(DoorState.ForcedOpen);
    }

    private bool TryReserveByEnemyServer(float reservationDuration)
    {
        if (IsReservedByEnemy)
        {
            return false;
        }

        float leaseDuration = Mathf.Max(
            Mathf.Max(0f, reservationDuration),
            Mathf.Max(0.1f, enemyReservationTimeout)
        );

        enemyReservationExpiresAt = Time.time + leaseDuration;
        SetReservationStateServer(DoorReservationState.ReservedByEnemy);
        return true;
    }

    private void ExtendEnemyReservationServer(float reservationDuration)
    {
        if (!IsReservedByEnemy)
        {
            TryReserveByEnemyServer(reservationDuration);
            return;
        }

        float leaseDuration = Mathf.Max(0f, reservationDuration);

        if (leaseDuration <= 0f)
        {
            return;
        }

        float nextExpiresAt = Time.time + leaseDuration;
        enemyReservationExpiresAt = Mathf.Max(enemyReservationExpiresAt, nextExpiresAt);
    }

    private void ReleaseEnemyReservationServer()
    {
        enemyReservationExpiresAt = 0f;
        SetReservationStateServer(DoorReservationState.None);
    }

    private bool IsCloseBlockedServer()
    {
        return IsReservedByEnemy || IsCloseLockedServer() || HasBlockingOccupantsServer();
    }

    private bool IsCloseLockedServer()
    {
        return closeLockedUntil > 0f && Time.time < closeLockedUntil;
    }

    private bool HasBlockingOccupantsServer()
    {
        PruneOccupyingActors();
        return occupyingActorColliders.Count > 0;
    }

    private void PruneOccupyingActors()
    {
        for (int i = occupyingActorColliders.Count - 1; i >= 0; i--)
        {
            Collider actorCollider = occupyingActorColliders[i];

            if (actorCollider == null ||
                !actorCollider.enabled ||
                !actorCollider.gameObject.activeInHierarchy)
            {
                occupyingActorColliders.RemoveAt(i);
            }
        }
    }

    private void ClearTimedTransition()
    {
        pendingTransitionTarget = DoorState.Closed;
        transitionCompletesAt = 0f;
    }

    private void SyncState(DoorState oldValue, DoorState newValue)
    {
        ApplyDoorState(newValue);
    }

    private void SyncReservation(
        DoorReservationState oldValue,
        DoorReservationState newValue
    )
    {
        ApplyReservationState(newValue);
    }

    private void SetStateServer(DoorState state)
    {
        localState = state;

        if (IsSpawned && IsServer)
        {
            currentState.Value = state;
        }

        ApplyDoorState(state);
    }

    private void SetReservationStateServer(DoorReservationState state)
    {
        localReservationState = state;

        if (IsSpawned && IsServer)
        {
            reservationState.Value = state;
        }
    }

    [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Everyone)]
    private void RequestOpenStateRpc(bool shouldOpen)
    {
        if (shouldOpen)
        {
            TryOpenServer();
            return;
        }

        TryCloseServer();
    }

    private void ApplyDoorState(DoorState state)
    {
        localState = state;
        ApplyDoorVisual(IsVisuallyOpen(state));
    }

    private void ApplyReservationState(DoorReservationState state)
    {
        localReservationState = state;
    }

    private void ApplyDoorVisual(bool isVisuallyOpen)
    {
        Transform pivot = doorPivot != null ? doorPivot : transform;
        Quaternion rotation = Quaternion.Euler(isVisuallyOpen ? openEulerAngles : closedEulerAngles);

        if (useLocalRotation)
        {
            pivot.localRotation = rotation;
            return;
        }

        pivot.rotation = rotation;
    }

    private void CacheComponents()
    {
        if (doorPivot == null)
        {
            doorPivot = transform;
        }

        CacheLineOfSightColliders();
    }

    private void CacheLineOfSightColliders()
    {
        lineOfSightColliders.Clear();

        Collider[] colliders = GetComponentsInChildren<Collider>(true);

        for (int i = 0; i < colliders.Length; i++)
        {
            Collider candidate = colliders[i];

            if (CanUseLineOfSightCollider(candidate))
            {
                lineOfSightColliders.Add(candidate);
            }
        }
    }

    private void PruneLineOfSightColliders()
    {
        for (int i = lineOfSightColliders.Count - 1; i >= 0; i--)
        {
            if (!CanUseLineOfSightCollider(lineOfSightColliders[i]))
            {
                lineOfSightColliders.RemoveAt(i);
            }
        }
    }

    private static bool CanUseLineOfSightCollider(Collider candidate)
    {
        return candidate != null &&
               candidate.enabled &&
               !candidate.isTrigger &&
               candidate.gameObject.activeInHierarchy;
    }

    private static void RegisterLineOfSightBlocker(DoorInteractableObject door)
    {
        if (door == null || RegisteredLineOfSightBlockers.Contains(door))
        {
            return;
        }

        RegisteredLineOfSightBlockers.Add(door);
    }

    private static void UnregisterLineOfSightBlocker(DoorInteractableObject door)
    {
        RegisteredLineOfSightBlockers.Remove(door);
    }

    private static bool IsOpenState(DoorState state)
    {
        return state == DoorState.Open ||
               state == DoorState.Blocked ||
               state == DoorState.ForcedOpen;
    }

    private static bool CanRequestOpen(DoorState state)
    {
        return state == DoorState.Closed || state == DoorState.Closing;
    }

    private static bool CanCloseState(DoorState state)
    {
        return state == DoorState.Open;
    }

    private static bool IsVisuallyOpen(DoorState state)
    {
        return state == DoorState.Open ||
               state == DoorState.Blocked ||
               state == DoorState.ForcedOpen ||
               state == DoorState.Closing;
    }

#if UNITY_EDITOR
    private void Reset()
    {
        CacheComponents();
    }

    private void OnValidate()
    {
        CacheComponents();
        transitionDuration = Mathf.Max(0f, transitionDuration);
        enemyOpenCloseLockGraceDuration = Mathf.Max(0f, enemyOpenCloseLockGraceDuration);
        enemyReservationTimeout = Mathf.Max(0.1f, enemyReservationTimeout);
    }
#endif
}
