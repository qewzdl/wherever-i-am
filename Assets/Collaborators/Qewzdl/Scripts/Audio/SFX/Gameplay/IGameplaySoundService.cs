using UnityEngine;

public interface IGameplaySoundService
{
    void Play2D(SoundEffect sound);
    void PlayAtPosition(SoundEffect sound, Vector3 position);

    // Played as the roll says rather than as this machine pleases, so every
    // machine that plays it plays the same take.
    void Play2D(SoundEffect sound, SoundRoll roll);
    void PlayAtPosition(SoundEffect sound, Vector3 position, SoundRoll roll);
    void SetMasterVolume(float volume);
}
