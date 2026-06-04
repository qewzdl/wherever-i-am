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

    [Header("Enemy Rules")]
    [SerializeField] private bool overrideEnemyAction;
    [SerializeField] private EnemyDoorActionType enemyActionOverride = EnemyDoorActionType.Open;
    [SerializeField] private bool enemyCanOpen = true;
    [SerializeField] private bool enemyCanBreak;

    private bool isBroken;
    private bool isEnemyInteractionInProgress;

    public static IReadOnlyList<EnemyDoorInteractionZone> RegisteredZones => RegisteredDoorZones;

    public bool IsOpen => linkedDoor != null && linkedDoor.IsOpen;
    public bool IsBroken => isBroken;
    public bool IsEnemyInteractionInProgress => isEnemyInteractionInProgress;
    public bool IsBlockingNavigation => linkedDoor != null && !IsOpen && !IsBroken;

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

    private void Awake()
    {
        CacheComponents();

        if (interactionCollider == null)
        {
            Debug.LogError(
                $"{nameof(EnemyDoorInteractionZone)} requires an interaction collider.",
                this
            );
        }

        if (linkedDoor == null)
        {
            Debug.LogError(
                $"{nameof(EnemyDoorInteractionZone)} requires a linked {nameof(DoorInteractableObject)}.",
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
        if (linkedDoor == null)
        {
            return false;
        }

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

    public bool TryBeginEnemyAction(EnemyDoorActionType action)
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

        return true;
    }

    public void CompleteEnemyAction(EnemyDoorActionType action)
    {
        isEnemyInteractionInProgress = false;

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
    }

    private void SetOpenState(bool isOpen)
    {
        if (linkedDoor == null)
        {
            Debug.LogError(
                $"{nameof(EnemyDoorInteractionZone)} cannot change door state without a linked {nameof(DoorInteractableObject)}.",
                this
            );

            return;
        }

        linkedDoor.TrySetOpen(isOpen);
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
    }

    private void OnValidate()
    {
        CacheComponents();
    }
#endif
}
