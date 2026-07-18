using System;
using Unity.Netcode;

internal struct PlayerHidingSnapshot :
    INetworkSerializable,
    IEquatable<PlayerHidingSnapshot>
{
    internal static readonly PlayerHidingSnapshot NotHidden = new(
        false,
        HidingPlaceInteractable.NoOccupantNetworkObjectId,
        false,
        false
    );

    private bool isHidden;
    private ulong hidingPlaceNetworkObjectId;
    private bool hidePlayerVisuals;
    private bool disablePlayerColliders;

    internal bool IsHidden => isHidden;
    internal ulong HidingPlaceNetworkObjectId => hidingPlaceNetworkObjectId;
    internal bool HidePlayerVisuals => hidePlayerVisuals;
    internal bool DisablePlayerColliders => disablePlayerColliders;

    internal PlayerHidingSnapshot(
        bool isHidden,
        ulong hidingPlaceNetworkObjectId,
        bool hidePlayerVisuals,
        bool disablePlayerColliders
    )
    {
        this.isHidden = isHidden;
        this.hidingPlaceNetworkObjectId = hidingPlaceNetworkObjectId;
        this.hidePlayerVisuals = hidePlayerVisuals;
        this.disablePlayerColliders = disablePlayerColliders;
    }

    public void NetworkSerialize<T>(BufferSerializer<T> serializer)
        where T : IReaderWriter
    {
        serializer.SerializeValue(ref isHidden);
        serializer.SerializeValue(ref hidingPlaceNetworkObjectId);
        serializer.SerializeValue(ref hidePlayerVisuals);
        serializer.SerializeValue(ref disablePlayerColliders);
    }

    public bool Equals(PlayerHidingSnapshot other)
    {
        return isHidden == other.isHidden &&
               hidingPlaceNetworkObjectId == other.hidingPlaceNetworkObjectId &&
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
            int hash = isHidden ? 17 : 31;
            hash = hash * 23 + hidingPlaceNetworkObjectId.GetHashCode();
            hash = hash * 23 + (hidePlayerVisuals ? 1 : 0);
            hash = hash * 23 + (disablePlayerColliders ? 1 : 0);
            return hash;
        }
    }
}
