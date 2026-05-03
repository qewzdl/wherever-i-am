using System;
using UnityEngine;

[Serializable]
public class UiSoundBinding
{
    [SerializeField] private UiSoundType type;
    [SerializeField] private SoundEffect sound;

    public UiSoundType Type => type;
    public SoundEffect Sound => sound;
}