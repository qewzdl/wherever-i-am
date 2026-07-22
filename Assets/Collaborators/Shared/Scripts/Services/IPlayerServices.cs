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
