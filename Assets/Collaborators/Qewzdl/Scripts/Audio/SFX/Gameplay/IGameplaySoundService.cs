using UnityEngine;

public interface IGameplaySoundService
{
    void Play2D(SoundEffect sound);
    void PlayAtPosition(SoundEffect sound, Vector3 position);
    void SetMasterVolume(float volume);
}
