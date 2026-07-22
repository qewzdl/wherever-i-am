public interface IPlayerNetworkService
{
    ulong NetworkObjectId { get; }
    ulong OwnerClientId { get; }
    bool IsServer { get; }
    bool IsLocalPlayer { get; }
}

public interface IReplicatedPlayerStateService
{
    bool IsCrouching { get; }
}

public interface IReplicatedPlayerHidingStateService
{
    bool IsHidden { get; }
    bool IsInHidingSequence { get; }
    HidingTransitionState HidingState { get; }
    HidingPoseType HidingPose { get; }
    bool CanPeek { get; }
    ulong HidingPlaceNetworkObjectId { get; }
}

public enum PlayerActionKind
{
    None = 0,
    Pickup = 1,
    Drag = 2,
    Hiding = 3
}

/// <summary>
/// Owns the one mutually-exclusive gameplay action currently performed by a
/// player. The owner token prevents one mechanic from releasing another
/// mechanic's action.
/// </summary>
public interface IPlayerActionGate
{
    bool IsBusy { get; }
    PlayerActionKind ActiveAction { get; }

    bool IsActive(PlayerActionKind action);
    bool CanBegin(PlayerActionKind action, object owner);
    bool TryBegin(PlayerActionKind action, object owner);
    void Confirm(PlayerActionKind action, object owner);
    bool End(PlayerActionKind action, object owner);
}

/// <summary>
/// Owner-side commands used by interaction composition without exposing the
/// concrete hiding implementation.
/// </summary>
public interface IPlayerHidingCommandService
{
    ulong NetworkObjectId { get; }
    ulong OwnerClientId { get; }
    bool IsSpawned { get; }
    bool IsOwner { get; }

    bool TryBeginHidingRequest();
    void CancelHidingRequest();
    void RequestExitHiding();
}

public interface ILocalPlayerInputService
{
    void SetInputActive(bool value);
    void SetInputActive(object source, bool value);
}

public interface ILocalPlayerCameraService
{
    void SetLookActive(bool value);
    void SetLookActive(object source, bool value);
    void SetCursorLocked(bool locked);
}

public interface ILocalPlayerPresentationService
{
    bool IsPresentationActive { get; }
}
