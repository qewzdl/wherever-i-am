using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;

[DisallowMultipleComponent]
public sealed class EnemyDoorInteractionZone : NetworkBehaviour
{
    private static readonly List<EnemyDoorInteractionZone> RegisteredDoorZones = new();

    [Header("Detection")]
    [SerializeField] private Collider interactionCollider;

    [Header("Door State")]
    [SerializeField] private DoorInteractableObject linkedDoor;
    [SerializeField] private bool driveLinkedDoor = true;

    [Header("Door Visual")]
    [SerializeField] private Transform animatedDoor;
    [SerializeField] private bool driveAnimatedDoorWhenLinkedDoor;
    [SerializeField] private bool startsOpen;
    [SerializeField] private Vector3 closedLocalEulerAngles;
    [SerializeField] private Vector3 openLocalEulerAngles = new(0f, 90f, 0f);

    [Header("Enemy Rules")]
    [SerializeField] private bool overrideEnemyAction;
    [SerializeField] private EnemyDoorActionType enemyActionOverride = EnemyDoorActionType.Open;
    [SerializeField] private bool enemyCanOpen = true;
    [SerializeField] private bool enemyCanBreak;

    private readonly NetworkVariable<bool> isOpenNetwork = new(
        false,
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Server
    );

    private bool localIsOpen;
    private bool isBroken;
    private bool isEnemyInteractionInProgress;

    private bool isPreviewingAction;
    private EnemyDoorActionType previewAction;
    private float previewStartedAt;
    private float previewDuration;

    private Quaternion closedLocalRotation;
    private Quaternion openLocalRotation;

    public static IReadOnlyList<EnemyDoorInteractionZone> RegisteredZones => RegisteredDoorZones;

    public bool IsOpen => ShouldUseLinkedDoor ? linkedDoor.IsOpen : IsSpawned ? isOpenNetwork.Value : localIsOpen;
    public bool IsBroken => isBroken;
    public bool IsEnemyInteractionInProgress => isEnemyInteractionInProgress;
    public bool IsBlockingNavigation => !IsOpen && !IsBroken;

    public Vector3 InteractionPosition
    {
        get
        {
            if (interactionCollider != null)
            {
                return interactionCollider.bounds.center;
            }

            return transform.position;
        }
    }

    private bool ShouldUseLinkedDoor => driveLinkedDoor && linkedDoor != null;
    private bool ShouldDriveAnimatedDoor => animatedDoor != null && (!ShouldUseLinkedDoor || driveAnimatedDoorWhenLinkedDoor);

    private void Awake()
    {
        CacheComponents();
        CacheRotations();

        localIsOpen = startsOpen;
        ApplyDoorVisualForState(localIsOpen);

        if (interactionCollider == null)
        {
            Debug.LogError(
                $"{nameof(EnemyDoorInteractionZone)} requires an interaction collider.",
                this
            );
        }
    }

    private void OnEnable()
    {
        RegisterZone(this);
    }

    private void OnDisable()
    {
        UnregisterZone(this);
    }

    public override void OnNetworkSpawn()
    {
        isOpenNetwork.OnValueChanged += HandleOpenChanged;

        if (IsServer)
        {
            if (ShouldUseLinkedDoor)
            {
                if (startsOpen)
                {
                    linkedDoor.TrySetOpen(true);
                }
            }
            else
            {
                isOpenNetwork.Value = startsOpen;
            }
        }

        localIsOpen = IsOpen;
        ApplyDoorVisualForState(localIsOpen);
    }

    public override void OnNetworkDespawn()
    {
        isOpenNetwork.OnValueChanged -= HandleOpenChanged;
    }

    private void Update()
    {
        if (!isPreviewingAction)
        {
            return;
        }

        float duration = Mathf.Max(0.01f, previewDuration);
        float progress = Mathf.Clamp01((Time.time - previewStartedAt) / duration);

        ApplyPreview(previewAction, progress);

        if (progress >= 1f)
        {
            isPreviewingAction = false;
        }
    }

    public Vector3 GetInteractionPointFor(Vector3 actorPosition)
    {
        if (interactionCollider == null)
        {
            return transform.position;
        }

        Vector3 closestPoint = interactionCollider.ClosestPoint(actorPosition);

        if (closestPoint == actorPosition && interactionCollider.bounds.Contains(actorPosition))
        {
            return actorPosition;
        }

        return closestPoint;
    }

    public EnemyDoorActionType ResolveEnemyAction(EnemyDoorActionType defaultAction)
    {
        return overrideEnemyAction ? enemyActionOverride : defaultAction;
    }

    public bool CanPerformEnemyAction(EnemyDoorActionType action)
    {
        switch (action)
        {
            case EnemyDoorActionType.Open:
                return enemyCanOpen && IsBlockingNavigation;

            case EnemyDoorActionType.Break:
                return enemyCanBreak && IsBlockingNavigation;

            case EnemyDoorActionType.CloseBehind:
                return enemyCanOpen && IsOpen && !IsBroken;

            default:
                Debug.LogError(
                    $"{nameof(EnemyDoorInteractionZone)} received unsupported enemy action {action}.",
                    this
                );

                return false;
        }
    }

    public bool CanStartEnemyAction(EnemyDoorActionType action)
    {
        if (isEnemyInteractionInProgress)
        {
            return false;
        }

        return CanPerformEnemyAction(action);
    }

    public bool TryBeginEnemyAction(EnemyDoorActionType action, float duration)
    {
        if (IsSpawned && !IsServer)
        {
            return false;
        }

        if (!CanStartEnemyAction(action))
        {
            return false;
        }

        isEnemyInteractionInProgress = true;
        TryBeginPreview(action, duration);

        if (IsSpawned && IsServer && ShouldDriveAnimatedDoor)
        {
            BeginEnemyActionClientRpc(action, duration);
        }

        return true;
    }

    public void CompleteEnemyAction(EnemyDoorActionType action)
    {
        isEnemyInteractionInProgress = false;
        isPreviewingAction = false;

        switch (action)
        {
            case EnemyDoorActionType.Open:
                SetOpenState(true);
                break;

            case EnemyDoorActionType.Break:
                isBroken = true;
                SetOpenState(true);
                break;

            case EnemyDoorActionType.CloseBehind:
                SetOpenState(false);
                break;

            default:
                Debug.LogError(
                    $"{nameof(EnemyDoorInteractionZone)} cannot complete unsupported action {action}.",
                    this
                );

                break;
        }
    }

    public void CancelEnemyAction()
    {
        isEnemyInteractionInProgress = false;
        isPreviewingAction = false;

        if (!IsOpen)
        {
            ApplyDoorVisualForState(false);
        }
    }

    [ClientRpc]
    private void BeginEnemyActionClientRpc(EnemyDoorActionType action, float duration)
    {
        TryBeginPreview(action, duration);
    }

    private void TryBeginPreview(EnemyDoorActionType action, float duration)
    {
        if (!ShouldDriveAnimatedDoor)
        {
            return;
        }

        BeginPreview(action, duration);
    }

    private void BeginPreview(EnemyDoorActionType action, float duration)
    {
        previewAction = action;
        previewStartedAt = Time.time;
        previewDuration = Mathf.Max(0.01f, duration);
        isPreviewingAction = true;
    }

    private void ApplyPreview(EnemyDoorActionType action, float progress)
    {
        switch (action)
        {
            case EnemyDoorActionType.Open:
            case EnemyDoorActionType.Break:
                ApplyDoorVisual(progress);
                break;

            case EnemyDoorActionType.CloseBehind:
                ApplyDoorVisual(1f - progress);
                break;
        }
    }

    private void SetOpenState(bool isOpen)
    {
        if (ShouldUseLinkedDoor)
        {
            linkedDoor.TrySetOpen(isOpen);
            localIsOpen = linkedDoor.IsOpen;
            ApplyDoorVisualForState(localIsOpen);
            return;
        }

        localIsOpen = isOpen;

        if (IsSpawned && IsServer)
        {
            isOpenNetwork.Value = isOpen;
        }

        ApplyDoorVisual(isOpen ? 1f : 0f);
    }

    private void HandleOpenChanged(bool previousValue, bool nextValue)
    {
        if (ShouldUseLinkedDoor)
        {
            return;
        }

        localIsOpen = nextValue;
        isPreviewingAction = false;
        ApplyDoorVisualForState(nextValue);
    }

    private void ApplyDoorVisualForState(bool isOpen)
    {
        ApplyDoorVisual(isOpen ? 1f : 0f);
    }

    private void ApplyDoorVisual(float normalizedOpenAmount)
    {
        if (!ShouldDriveAnimatedDoor)
        {
            return;
        }

        animatedDoor.localRotation = Quaternion.Lerp(
            closedLocalRotation,
            openLocalRotation,
            Mathf.Clamp01(normalizedOpenAmount)
        );
    }

    private void CacheComponents()
    {
        if (interactionCollider == null)
        {
            interactionCollider = GetComponent<Collider>();
        }

        if (linkedDoor == null)
        {
            linkedDoor = GetComponentInParent<DoorInteractableObject>();
        }
    }

    private void CacheRotations()
    {
        closedLocalRotation = Quaternion.Euler(closedLocalEulerAngles);
        openLocalRotation = Quaternion.Euler(openLocalEulerAngles);
    }

    private static void RegisterZone(EnemyDoorInteractionZone zone)
    {
        if (zone == null || RegisteredDoorZones.Contains(zone))
        {
            return;
        }

        RegisteredDoorZones.Add(zone);
    }

    private static void UnregisterZone(EnemyDoorInteractionZone zone)
    {
        RegisteredDoorZones.Remove(zone);
    }

#if UNITY_EDITOR
    private void Reset()
    {
        CacheComponents();

        if (interactionCollider != null)
        {
            interactionCollider.isTrigger = true;
        }

        if (animatedDoor != null)
        {
            closedLocalEulerAngles = animatedDoor.localEulerAngles;
        }

        CacheRotations();
    }

    private void OnValidate()
    {
        CacheComponents();
        CacheRotations();
    }
#endif
}
