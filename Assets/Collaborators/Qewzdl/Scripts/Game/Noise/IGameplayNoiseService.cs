using UnityEngine;

public interface IGameplayNoiseService
{
    bool IsInitialized { get; }
    bool IsConfigured { get; }

    bool TryRaiseNoiseServer(
        Vector3 position,
        float radius,
        float loudness,
        GameplayNoiseSourceType sourceType,
        ulong sourceNetworkObjectId = GameplayNoiseEvent.NoNetworkObjectId,
        ulong sourceClientId = GameplayNoiseEvent.NoClientId,
        Object sourceObject = null);

    bool TryRegisterNoiseServer(GameplayNoiseEvent noiseEvent);

    bool TryFindBestNoise(
        Vector3 listenerPosition,
        float hearingRadius,
        float memoryDuration,
        float minimumLoudness,
        out GameplayNoiseEvent bestNoise,
        out float bestScore);

    void Clear();
}
