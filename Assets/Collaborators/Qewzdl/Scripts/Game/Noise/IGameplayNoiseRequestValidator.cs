public interface IGameplayNoiseRequestValidator
{
    bool CanEmitNoiseServer(
        GameplayNoiseEmitter emitter,
        GameplayNoisePreset preset,
        ulong senderClientId
    );
}
