using Unity.Netcode;
using UnityEngine;

public class DoorInteractableObject : InteractableObject
{
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
    [SerializeField] private float enemyOpenCloseLockGraceDuration = 0.5f;

    private readonly NetworkVariable<DoorState> currentState = new(
        DoorState.Closed,
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Server
    );

    private DoorState localState;
    private DoorState pendingTransitionTarget;
    private float transitionCompletesAt;
    private float forcedOpenLockEndsAt;

    public DoorState CurrentState => IsSpawned ? currentState.Value : localState;
    public bool IsOpen => IsOpenState(CurrentState);
    public bool CanClose => CanCloseState(CurrentState);
    public float EnemyOpenCloseLockGraceDuration => Mathf.Max(0f, enemyOpenCloseLockGraceDuration);

    private void Awake()
    {
        CacheComponents();
        localState = startsOpen ? DoorState.Open : DoorState.Closed;
        ApplyDoorState(localState);
    }

    public override void OnNetworkSpawn()
    {
        base.OnNetworkSpawn();

        if (IsServer)
        {
            SetStateServer(startsOpen ? DoorState.Open : DoorState.Closed);
        }

        currentState.OnValueChanged += Sync;
        ApplyDoorState(currentState.Value);
    }

    public override void OnNetworkDespawn()
    {
        currentState.OnValueChanged -= Sync;

        base.OnNetworkDespawn();
    }

    private void Update()
    {
        if (IsSpawned && !IsServer)
        {
            return;
        }

        TickStateTimeouts();
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
            if (!CanCloseState(CurrentState))
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
        if (IsSpawned && !IsServer)
        {
            return false;
        }

        if (CurrentState != DoorState.Closed && CurrentState != DoorState.Closing)
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
        float lockDuration = Mathf.Max(
            EnemyOpenCloseLockGraceDuration,
            Mathf.Max(0f, minimumCloseLockDuration)
        );

        return TryForceOpen(lockDuration);
    }

    public bool TryCancelEnemyOpen()
    {
        if (IsSpawned && !IsServer)
        {
            return false;
        }

        if (CurrentState != DoorState.Opening)
        {
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

        if (!CanCloseState(CurrentState))
        {
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
        SetStateServer(DoorState.Open);
        return true;
    }

    public bool TryForceOpen(float closeLockDuration)
    {
        if (IsSpawned && !IsServer)
        {
            return false;
        }

        ClearTimedTransition();

        float lockDuration = Mathf.Max(0f, closeLockDuration);

        if (lockDuration <= 0f)
        {
            forcedOpenLockEndsAt = 0f;
            SetStateServer(DoorState.Open);
            return true;
        }

        forcedOpenLockEndsAt = Time.time + lockDuration;
        SetStateServer(DoorState.ForcedOpen);
        return true;
    }

    private bool TryOpenServer()
    {
        if (!CanRequestOpen(CurrentState))
        {
            return false;
        }

        BeginTimedTransition(DoorState.Opening, DoorState.Open);
        return true;
    }

    private bool TryCloseServer()
    {
        if (!CanCloseState(CurrentState))
        {
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
            SetStateServer(targetState);
            return;
        }

        pendingTransitionTarget = targetState;
        transitionCompletesAt = Time.time + duration;
        SetStateServer(transitionState);
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
            SetStateServer(targetState);
            return;
        }

        if (state == DoorState.ForcedOpen &&
            forcedOpenLockEndsAt > 0f &&
            Time.time >= forcedOpenLockEndsAt)
        {
            forcedOpenLockEndsAt = 0f;
            SetStateServer(DoorState.Open);
        }
    }

    private void ClearTimedTransition()
    {
        pendingTransitionTarget = DoorState.Closed;
        transitionCompletesAt = 0f;
    }

    private void Sync(DoorState oldValue, DoorState newValue)
    {
        ApplyDoorState(newValue);
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
    }

    private static bool IsOpenState(DoorState state)
    {
        return state == DoorState.Open || state == DoorState.ForcedOpen;
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
    }
#endif
}
