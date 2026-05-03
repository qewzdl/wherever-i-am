using System;
using Unity.Collections;
using Unity.Netcode;

public struct ChatMessageData : INetworkSerializable, IEquatable<ChatMessageData>
{
    public ulong SenderClientId;
    public FixedString32Bytes SenderName;
    public FixedString512Bytes Text;
    public ChatChannel Channel;
    public double ServerTime;

    public ChatMessageData(
        ulong senderClientId,
        string senderName,
        string text,
        ChatChannel channel,
        double serverTime)
    {
        SenderClientId = senderClientId;
        SenderName = senderName;
        Text = text;
        Channel = channel;
        ServerTime = serverTime;
    }

    public void NetworkSerialize<T>(BufferSerializer<T> serializer) where T : IReaderWriter
    {
        serializer.SerializeValue(ref SenderClientId);
        serializer.SerializeValue(ref SenderName);
        serializer.SerializeValue(ref Text);

        byte channelValue = (byte)Channel;
        serializer.SerializeValue(ref channelValue);

        if (serializer.IsReader)
            Channel = (ChatChannel)channelValue;

        serializer.SerializeValue(ref ServerTime);
    }

    public bool Equals(ChatMessageData other)
    {
        return SenderClientId == other.SenderClientId &&
               SenderName.Equals(other.SenderName) &&
               Text.Equals(other.Text) &&
               Channel == other.Channel &&
               ServerTime.Equals(other.ServerTime);
    }

    public override bool Equals(object obj)
    {
        return obj is ChatMessageData other && Equals(other);
    }

    public override int GetHashCode()
    {
        unchecked
        {
            int hashCode = SenderClientId.GetHashCode();
            hashCode = (hashCode * 397) ^ SenderName.GetHashCode();
            hashCode = (hashCode * 397) ^ Text.GetHashCode();
            hashCode = (hashCode * 397) ^ Channel.GetHashCode();
            hashCode = (hashCode * 397) ^ ServerTime.GetHashCode();
            return hashCode;
        }
    }
}