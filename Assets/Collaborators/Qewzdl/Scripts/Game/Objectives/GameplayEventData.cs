using Unity.Netcode;

public readonly struct GameplayEventData
{
    public readonly string EventId;
    public readonly ulong ActorClientId;
    public readonly NetworkObject SourceObject;

    public GameplayEventData(string eventId, ulong actorClientId, NetworkObject sourceObject)
    {
        EventId = eventId;
        ActorClientId = actorClientId;
        SourceObject = sourceObject;
    }
}