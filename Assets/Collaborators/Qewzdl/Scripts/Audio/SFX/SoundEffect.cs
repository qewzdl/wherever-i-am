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

    public AudioClip GetClip()
    {
        if (clips == null || clips.Length == 0)
        {
            return null;
        }

        if (clips.Length == 1)
        {
            return clips[0];
        }

        int randomIndex = Random.Range(0, clips.Length);
        return clips[randomIndex];
    }

    public float GetVolume()
    {
        if (!randomizeVolume)
        {
            return volume;
        }

        return volume * Random.Range(minVolume, maxVolume);
    }

    public float GetPitch()
    {
        if (!randomizePitch)
        {
            return 1f;
        }

        return Random.Range(minPitch, maxPitch);
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
