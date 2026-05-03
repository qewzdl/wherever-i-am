using System;
using UnityEngine;

[CreateAssetMenu(fileName = "UiSoundTheme", menuName = "Game Audio/UI Sound Theme")]
public class UiSoundTheme : ScriptableObject
{
    [SerializeField] private UiSoundBinding[] sounds;

    public bool TryGetSound(UiSoundType type, out SoundEffect sound)
    {
        sound = null;

        if (sounds == null || sounds.Length == 0)
        {
            return false;
        }

        for (int i = 0; i < sounds.Length; i++)
        {
            UiSoundBinding binding = sounds[i];

            if (binding.Type != type) continue;

            sound = binding.Sound;
            return sound != null;
        }

        return false;
    }
}