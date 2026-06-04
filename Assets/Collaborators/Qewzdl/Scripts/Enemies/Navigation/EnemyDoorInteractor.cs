using System.Collections.Generic;
using UnityEngine;

public readonly struct EnemyDoorNavigationResult
{
    private EnemyDoorNavigationResult(
        bool isHandled,
        bool shouldStop,
        bool hasOverrideDestination,
        Vector3 overrideDestination
    )
    {
        IsHandled = isHandled;
        ShouldStop = shouldStop;
        HasOverrideDestination = hasOverrideDestination;
        OverrideDestination = overrideDestination;
    }

    public bool IsHandled { get; }
    public bool ShouldStop { get; }
    public bool HasOverrideDestination { get; }
    public Vector3 OverrideDestination { get; }

    public static EnemyDoorNavigationResult None => default;

    public static EnemyDoorNavigationResult Stop()
    {
        return new EnemyDoorNavigationResult(true, true, false, default);
    }

    public static EnemyDoorNavigationResult MoveTo(Vector3 destination)
    {
        return new EnemyDoorNavigationResult(true, false, true, destination);
    }
}

[DisallowMultipleComponent]
public sealed class EnemyDoorInteractor : MonoBehaviour
{
    private const int MaxDoorHits = 32;

    private enum DoorInteractionPhase
    {
        None,
        WaitingForOtherInteraction,
        Interacting,
        WaitingAfterInteraction
    }

    [SerializeField] private EnemyDoorInteractionConfig config;

    private readonly Collider[] doorHitBuffer = new Collider[MaxDoorHits];
    private readonly List<EnemyDoorInteractionZone> seenDoorBuffer = new(MaxDoorHits);

    private EnemyDoorInteractionZone activeDoor;
    private EnemyDoorActionType activeAction;
    private DoorInteractionPhase phase;

    private bool ownsActiveDoorAction;
    private float phaseStartedAt;
    private int lastActiveTickFrame = -1;

    public bool HasActiveInteraction => activeDoor != null;

    public EnemyDoorNavigationResult EvaluateNavigation(
        Vector3 currentPosition,
        Vector3 requestedDestination
    )
    {
        if (config == null)
        {
            return EnemyDoorNavigationResult.None;
        }

        if (activeDoor != null)
        {
            return TickActiveInteraction(currentPosition);
        }

        if (!TryFindBlockingDoor(
                currentPosition,
                requestedDestination,
                out EnemyDoorInteractionZone door
            ))
        {
            return EnemyDoorNavigationResult.None;
        }

        if (!IsInsideInteractionDistance(currentPosition, door))
        {
            return EnemyDoorNavigationResult.MoveTo(door.GetInteractionPointFor(currentPosition));
        }

        if (door.IsEnemyInteractionInProgress)
        {
            BeginWaitingForDoor(door);
            return EnemyDoorNavigationResult.Stop();
        }

        if (TryBeginInteraction(door))
        {
            return EnemyDoorNavigationResult.Stop();
        }

        if (door.IsEnemyInteractionInProgress)
        {
            BeginWaitingForDoor(door);
            return EnemyDoorNavigationResult.Stop();
        }

        return EnemyDoorNavigationResult.None;
    }

    public bool TryBlockNavigation(Vector3 currentPosition, Vector3 requestedDestination)
    {
        return EvaluateNavigation(currentPosition, requestedDestination).ShouldStop;
    }

    public void CancelActiveInteraction()
    {
        if (activeDoor != null && ownsActiveDoorAction)
        {
            activeDoor.CancelEnemyAction();
        }

        ClearActiveInteraction();
    }

    private void BeginWaitingForDoor(EnemyDoorInteractionZone door)
    {
        activeDoor = door;
        phase = DoorInteractionPhase.WaitingForOtherInteraction;
        ownsActiveDoorAction = false;
        phaseStartedAt = Time.time;
    }

    private bool TryBeginInteraction(EnemyDoorInteractionZone door)
    {
        if (door == null || !TrySelectAction(door, true, out EnemyDoorActionType action))
        {
            return false;
        }

        if (!door.TryBeginEnemyAction(action, config.InteractionDuration))
        {
            return false;
        }

        activeDoor = door;
        activeAction = action;
        phase = DoorInteractionPhase.Interacting;
        ownsActiveDoorAction = true;
        phaseStartedAt = Time.time;
        lastActiveTickFrame = Time.frameCount;

        return true;
    }

    private EnemyDoorNavigationResult TickActiveInteraction(Vector3 currentPosition)
    {
        if (activeDoor == null)
        {
            ClearActiveInteraction();
            return EnemyDoorNavigationResult.None;
        }

        if (lastActiveTickFrame == Time.frameCount)
        {
            return EnemyDoorNavigationResult.Stop();
        }

        lastActiveTickFrame = Time.frameCount;

        if (!activeDoor.IsBlockingNavigation && phase != DoorInteractionPhase.WaitingAfterInteraction)
        {
            if (ownsActiveDoorAction)
            {
                activeDoor.CancelEnemyAction();
                ownsActiveDoorAction = false;
            }

            phase = DoorInteractionPhase.WaitingAfterInteraction;
            phaseStartedAt = Time.time;
            return EnemyDoorNavigationResult.Stop();
        }

        switch (phase)
        {
            case DoorInteractionPhase.WaitingForOtherInteraction:
                return TickWaitingForOtherInteraction(currentPosition);

            case DoorInteractionPhase.Interacting:
                return TickInteractionDuration();

            case DoorInteractionPhase.WaitingAfterInteraction:
                return TickWaitAfterInteraction();

            case DoorInteractionPhase.None:
                ClearActiveInteraction();
                return EnemyDoorNavigationResult.None;

            default:
                Debug.LogError(
                    $"{nameof(EnemyDoorInteractor)} received unsupported phase {phase}.",
                    this
                );

                ClearActiveInteraction();
                return EnemyDoorNavigationResult.None;
        }
    }

    private EnemyDoorNavigationResult TickWaitingForOtherInteraction(Vector3 currentPosition)
    {
        if (activeDoor == null || !activeDoor.IsBlockingNavigation)
        {
            ClearActiveInteraction();
            return EnemyDoorNavigationResult.None;
        }

        if (activeDoor.IsEnemyInteractionInProgress)
        {
            return EnemyDoorNavigationResult.Stop();
        }

        if (!IsInsideInteractionDistance(currentPosition, activeDoor))
        {
            Vector3 approachDestination = activeDoor.GetInteractionPointFor(currentPosition);
            ClearActiveInteraction();
            return EnemyDoorNavigationResult.MoveTo(approachDestination);
        }

        if (TryBeginInteraction(activeDoor))
        {
            return EnemyDoorNavigationResult.Stop();
        }

        ClearActiveInteraction();
        return EnemyDoorNavigationResult.None;
    }

    private EnemyDoorNavigationResult TickInteractionDuration()
    {
        if (activeDoor == null)
        {
            ClearActiveInteraction();
            return EnemyDoorNavigationResult.None;
        }

        if (!activeDoor.IsBlockingNavigation)
        {
            ownsActiveDoorAction = false;
            phase = DoorInteractionPhase.WaitingAfterInteraction;
            phaseStartedAt = Time.time;
            return EnemyDoorNavigationResult.Stop();
        }

        float duration = config.InteractionDuration;

        if (Time.time - phaseStartedAt < duration)
        {
            return EnemyDoorNavigationResult.Stop();
        }

        activeDoor.CompleteEnemyAction(activeAction);
        ownsActiveDoorAction = false;

        phase = DoorInteractionPhase.WaitingAfterInteraction;
        phaseStartedAt = Time.time;

        return EnemyDoorNavigationResult.Stop();
    }

    private EnemyDoorNavigationResult TickWaitAfterInteraction()
    {
        float waitDuration = config.WaitAfterInteractionDuration;

        if (Time.time - phaseStartedAt < waitDuration)
        {
            return EnemyDoorNavigationResult.Stop();
        }

        ClearActiveInteraction();
        return EnemyDoorNavigationResult.None;
    }

    private bool TryFindBlockingDoor(
        Vector3 currentPosition,
        Vector3 requestedDestination,
        out EnemyDoorInteractionZone bestDoor
    )
    {
        bestDoor = null;
        seenDoorBuffer.Clear();

        Vector3 flatDestinationDelta = requestedDestination - currentPosition;
        flatDestinationDelta.y = 0f;

        float destinationDistance = flatDestinationDelta.magnitude;

        if (destinationDistance <= 0.05f)
        {
            return false;
        }

        Vector3 routeDirection = flatDestinationDelta / destinationDistance;
        float bestSqrDistance = float.MaxValue;

        if (config.UseRegisteredDoorZones)
        {
            IReadOnlyList<EnemyDoorInteractionZone> registeredZones =
                EnemyDoorInteractionZone.RegisteredZones;

            for (int i = 0; i < registeredZones.Count; i++)
            {
                TryConsiderDoor(
                    registeredZones[i],
                    currentPosition,
                    routeDirection,
                    destinationDistance,
                    ref bestDoor,
                    ref bestSqrDistance
                );
            }
        }

        if (config.UsePhysicsOverlapFallback)
        {
            LayerMask layerMask = config.UseDoorLayerMask ? config.DoorLayerMask : ~0;

            int hitCount = Physics.OverlapSphereNonAlloc(
                currentPosition,
                config.DetectionRadius,
                doorHitBuffer,
                layerMask,
                config.TriggerInteraction
            );

            for (int i = 0; i < hitCount; i++)
            {
                Collider hit = doorHitBuffer[i];

                if (hit == null)
                {
                    continue;
                }

                TryConsiderDoor(
                    hit.GetComponentInParent<EnemyDoorInteractionZone>(),
                    currentPosition,
                    routeDirection,
                    destinationDistance,
                    ref bestDoor,
                    ref bestSqrDistance
                );
            }
        }

        seenDoorBuffer.Clear();
        return bestDoor != null;
    }

    private void TryConsiderDoor(
        EnemyDoorInteractionZone door,
        Vector3 currentPosition,
        Vector3 routeDirection,
        float destinationDistance,
        ref EnemyDoorInteractionZone bestDoor,
        ref float bestSqrDistance
    )
    {
        if (door == null || !door.isActiveAndEnabled || seenDoorBuffer.Contains(door))
        {
            return;
        }

        seenDoorBuffer.Add(door);

        if (!door.IsBlockingNavigation && !door.IsEnemyInteractionInProgress)
        {
            return;
        }

        if (config.UseDoorLayerMask && !IsLayerInMask(door.gameObject.layer, config.DoorLayerMask))
        {
            return;
        }

        if (!door.IsEnemyInteractionInProgress && !TrySelectAction(door, false, out _))
        {
            return;
        }

        if (!IsDoorOnRequestedRoute(
                currentPosition,
                routeDirection,
                destinationDistance,
                door
            ))
        {
            return;
        }

        Vector3 flatDoorDelta = door.GetInteractionPointFor(currentPosition) - currentPosition;
        flatDoorDelta.y = 0f;

        float sqrDistance = flatDoorDelta.sqrMagnitude;

        if (sqrDistance >= bestSqrDistance)
        {
            return;
        }

        bestSqrDistance = sqrDistance;
        bestDoor = door;
    }

    private bool TrySelectAction(
        EnemyDoorInteractionZone door,
        bool requireStart,
        out EnemyDoorActionType action
    )
    {
        action = EnemyDoorActionType.Open;

        if (door == null)
        {
            return false;
        }

        EnemyDoorActionType preferredAction = door.ResolveEnemyAction(config.DefaultAction);

        if (CanUseAction(door, preferredAction, requireStart))
        {
            action = preferredAction;
            return true;
        }

        if (config.AllowBreakFallback &&
            preferredAction != EnemyDoorActionType.Break &&
            CanUseAction(door, EnemyDoorActionType.Break, requireStart))
        {
            action = EnemyDoorActionType.Break;
            return true;
        }

        return false;
    }

    private static bool CanUseAction(
        EnemyDoorInteractionZone door,
        EnemyDoorActionType action,
        bool requireStart
    )
    {
        return requireStart
            ? door.CanStartEnemyAction(action)
            : door.CanPerformEnemyAction(action);
    }

    private bool IsDoorOnRequestedRoute(
        Vector3 currentPosition,
        Vector3 routeDirection,
        float destinationDistance,
        EnemyDoorInteractionZone door
    )
    {
        Vector3 flatDoorDelta = door.GetInteractionPointFor(currentPosition) - currentPosition;
        flatDoorDelta.y = 0f;

        float doorSqrDistance = flatDoorDelta.sqrMagnitude;
        float interactionDistance = config.InteractionDistance;

        if (doorSqrDistance <= interactionDistance * interactionDistance)
        {
            return true;
        }

        float projection = Vector3.Dot(flatDoorDelta, routeDirection);

        if (projection < 0f)
        {
            return false;
        }

        float maxProjection = Mathf.Min(
            config.DetectionRadius,
            destinationDistance + interactionDistance
        );

        if (projection > maxProjection)
        {
            return false;
        }

        float perpendicularSqrDistance = Mathf.Max(
            0f,
            doorSqrDistance - projection * projection
        );

        float pathHalfWidth = config.PathHalfWidth;

        return perpendicularSqrDistance <= pathHalfWidth * pathHalfWidth;
    }

    private bool IsInsideInteractionDistance(Vector3 currentPosition, EnemyDoorInteractionZone door)
    {
        Vector3 flatDoorDelta = door.GetInteractionPointFor(currentPosition) - currentPosition;
        flatDoorDelta.y = 0f;

        float interactionDistance = config.InteractionDistance;

        return flatDoorDelta.sqrMagnitude <= interactionDistance * interactionDistance;
    }

    private static bool IsLayerInMask(int layer, LayerMask layerMask)
    {
        return (layerMask.value & (1 << layer)) != 0;
    }

    private void ClearActiveInteraction()
    {
        activeDoor = null;
        phase = DoorInteractionPhase.None;
        ownsActiveDoorAction = false;
        phaseStartedAt = 0f;
        lastActiveTickFrame = -1;
    }
}
