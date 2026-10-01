using UnityEngine;

[CreateAssetMenu(fileName = "Sfx", menuName = "Wherever I Am/Audio/SFX/Sound Effect")]
public class SoundEffect : ScriptableObject
{
    [Header("Audio")]
    [SerializeField] private AudioClip[] clips;

    [Header("Volume")]
    [SerializeField, Range(0f, 1f)] private float volume = 1f;
    [SerializeField] private bool randomizeVolume;
    [SerializeField, Range(0f, 1f)] private float minVolume = 0.9f;
    [SerializeField, Range(0f, 1f)] private float maxVolume = 1f;

    [Header("Pitch")]
    [SerializeField] private bool randomizePitch = true;
    [SerializeField] private float minPitch = 0.95f;
    [SerializeField] private float maxPitch = 1.05f;

    [Header("3D Settings")]
    [SerializeField, Range(0f, 1f)] private float spatialBlend = 1f;
    [SerializeField, Min(0f)] private float minDistance = 1f;
    [SerializeField, Min(0f)] private float maxDistance = 25f;

    public float SpatialBlend => spatialBlend;
    public float MinDistance => minDistance;
    public float MaxDistance => maxDistance;

    public AudioClip GetClip() => GetClip(Random.value);
    public float GetVolume() => GetVolume(Random.value);
    public float GetPitch() => GetPitch(Random.value);

    // The same choices, from a roll somebody else made - see SoundRoll, for
    // a sound every machine has to hear alike.
    public AudioClip GetClip(SoundRoll roll) => GetClip(roll[SoundDraw.Clip]);
    public float GetVolume(SoundRoll roll) => GetVolume(roll[SoundDraw.Volume]);
    public float GetPitch(SoundRoll roll) => GetPitch(roll[SoundDraw.Pitch]);

    private AudioClip GetClip(float roll)
    {
        if (clips == null || clips.Length == 0)
        {
            return null;
        }

        int index = Mathf.Min(clips.Length - 1, (int)(roll * clips.Length));
        return clips[index];
    }

    private float GetVolume(float roll)
    {
        if (!randomizeVolume)
        {
            return volume;
        }

        return volume * Mathf.Lerp(minVolume, maxVolume, roll);
    }

    private float GetPitch(float roll)
    {
        if (!randomizePitch)
        {
            return 1f;
        }

        return Mathf.Lerp(minPitch, maxPitch, roll);
    }

    private void OnValidate()
    {
        if (minVolume > maxVolume)
        {
            minVolume = maxVolume;
        }

        if (minPitch > maxPitch)
        {
            minPitch = maxPitch;
        }

        if (minDistance > maxDistance)
        {
            minDistance = maxDistance;
        }
    }
}
