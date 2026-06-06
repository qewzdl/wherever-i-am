public interface IGameplayNoiseRequestValidator
{
    bool CanEmitNoiseServer(
        GameplayNoiseEmitter emitter,
        ulong senderClientId
    );
}
