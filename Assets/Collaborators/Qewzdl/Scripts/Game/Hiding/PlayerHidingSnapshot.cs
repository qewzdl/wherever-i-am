using System;
using Unity.Netcode;

internal struct PlayerHidingSnapshot :
    INetworkSerializable,
    IEquatable<PlayerHidingSnapshot>
{
    internal static readonly PlayerHidingSnapshot NotHidden = new(
        HidingTransitionState.Available,
        HidingPlaceInteractable.NoOccupantNetworkObjectId,
        HidingPoseType.Standing,
        false,
        false,
        false
    );

    private HidingTransitionState state;
    private ulong hidingPlaceNetworkObjectId;
    private HidingPoseType pose;
    private bool canPeek;
    private bool hidePlayerVisuals;
    private bool disablePlayerColliders;

    internal HidingTransitionState State => state;
    internal bool IsHidden => state == HidingTransitionState.Occupied;
    internal bool IsInHidingSequence =>
        state != HidingTransitionState.Available;
    internal ulong HidingPlaceNetworkObjectId => hidingPlaceNetworkObjectId;
    internal HidingPoseType Pose => pose;
    internal bool CanPeek => canPeek;
    internal bool HidePlayerVisuals => hidePlayerVisuals;
    internal bool DisablePlayerColliders => disablePlayerColliders;

    internal PlayerHidingSnapshot(
        HidingTransitionState state,
        ulong hidingPlaceNetworkObjectId,
        HidingPoseType pose,
        bool canPeek,
        bool hidePlayerVisuals,
        bool disablePlayerColliders
    )
    {
        this.state = state;
        this.hidingPlaceNetworkObjectId = hidingPlaceNetworkObjectId;
        this.pose = pose;
        this.canPeek = canPeek;
        this.hidePlayerVisuals = hidePlayerVisuals;
        this.disablePlayerColliders = disablePlayerColliders;
    }

    public void NetworkSerialize<T>(BufferSerializer<T> serializer)
        where T : IReaderWriter
    {
        serializer.SerializeValue(ref state);
        serializer.SerializeValue(ref hidingPlaceNetworkObjectId);
        serializer.SerializeValue(ref pose);
        serializer.SerializeValue(ref canPeek);
        serializer.SerializeValue(ref hidePlayerVisuals);
        serializer.SerializeValue(ref disablePlayerColliders);
    }

    public bool Equals(PlayerHidingSnapshot other)
    {
        return state == other.state &&
               hidingPlaceNetworkObjectId == other.hidingPlaceNetworkObjectId &&
               pose == other.pose &&
               canPeek == other.canPeek &&
               hidePlayerVisuals == other.hidePlayerVisuals &&
               disablePlayerColliders == other.disablePlayerColliders;
    }

    public override bool Equals(object obj)
    {
        return obj is PlayerHidingSnapshot other && Equals(other);
    }

    public override int GetHashCode()
    {
        unchecked
        {
            int hash = (int)state;
            hash = hash * 23 + hidingPlaceNetworkObjectId.GetHashCode();
            hash = hash * 23 + (int)pose;
            hash = hash * 23 + (canPeek ? 1 : 0);
            hash = hash * 23 + (hidePlayerVisuals ? 1 : 0);
            hash = hash * 23 + (disablePlayerColliders ? 1 : 0);
            return hash;
        }
    }
}
