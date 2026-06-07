using UnityEngine;

[DisallowMultipleComponent]
public sealed class GameplayNoiseRelay : MonoBehaviour
{
    [SerializeField] private GameplayNoiseEmitter noiseEmitter;
    [SerializeField] private GameplayNoisePreset preset;

    public bool IsConfigured =>
        noiseEmitter != null &&
        preset != null &&
        preset.IsValid;

    public void Emit()
    {
        TryEmit();
    }

    public bool TryEmit()
    {
        if (!IsConfigured)
        {
            Debug.LogError(
                $"{nameof(GameplayNoiseRelay)} requires configured " +
                $"{nameof(GameplayNoiseEmitter)} and {nameof(GameplayNoisePreset)}.",
                this
            );

            return false;
        }

        return noiseEmitter.IsServer
            ? noiseEmitter.TryEmitServer(preset)
            : noiseEmitter.RequestEmitFromOwner(preset);
    }

#if UNITY_EDITOR
    private void Reset()
    {
        if (noiseEmitter == null)
        {
            noiseEmitter = GetComponentInParent<GameplayNoiseEmitter>();
        }
    }
#endif
}
