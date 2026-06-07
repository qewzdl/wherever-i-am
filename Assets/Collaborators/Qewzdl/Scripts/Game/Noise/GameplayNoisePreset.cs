using UnityEngine;

[CreateAssetMenu(
    fileName = "GameplayNoisePreset",
    menuName = "Wherever I Am/Game/Noise/Gameplay Noise Preset")]
public sealed class GameplayNoisePreset : ScriptableObject
{
    [Header("Noise")]
    [SerializeField] private GameplayNoiseSourceType sourceType =
        GameplayNoiseSourceType.Environment;
    [SerializeField, Min(0f)] private float radius = 8f;
    [SerializeField, Min(0f)] private float loudness = 1f;

    [Header("Server Rate Limit")]
    [SerializeField, Min(0f)] private float serverCooldown = 0.15f;

    public GameplayNoiseSourceType SourceType => sourceType;
    public float Radius => Mathf.Max(0f, radius);
    public float Loudness => Mathf.Max(0f, loudness);
    public float ServerCooldown => Mathf.Max(0f, serverCooldown);
    public bool IsValid =>
        sourceType != GameplayNoiseSourceType.Unknown &&
        Radius > 0f &&
        Loudness > 0f;

#if UNITY_EDITOR
    private void OnValidate()
    {
        radius = Mathf.Max(0f, radius);
        loudness = Mathf.Max(0f, loudness);
        serverCooldown = Mathf.Max(0f, serverCooldown);
    }
#endif
}
