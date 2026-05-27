using System.Collections.Generic;
using UnityEngine;

[CreateAssetMenu(
    fileName = "PhoneSpriteAnimationProfile",
    menuName = "Wherever I Am/Chat/Phone Sprite Animation Profile")]
public class PhoneSpriteAnimationProfile : ScriptableObject
{
    [Header("Frames")]
    [SerializeField] private List<Sprite> frames = new List<Sprite>();

    [Header("Playback")]
    [SerializeField, Min(1f)] private float framesPerSecond = 12f;
    [SerializeField] private bool playClosingInReverse = true;
    [SerializeField] private bool useUnscaledTime = true;

    [Header("Layout")]
    [SerializeField] private bool refreshScreenLayoutOnFrameChange;

    public int FrameCount => frames != null ? frames.Count : 0;
    public bool HasFrames => FrameCount > 0;
    public float FrameDuration => 1f / Mathf.Max(1f, framesPerSecond);
    public bool UseUnscaledTime => useUnscaledTime;
    public bool RefreshScreenLayoutOnFrameChange => refreshScreenLayoutOnFrameChange;

    public Sprite ClosedSprite => HasFrames ? frames[0] : null;
    public Sprite OpenedSprite => HasFrames ? frames[FrameCount - 1] : null;

    public Sprite GetFrame(PhoneSpriteAnimationDirection direction, int frameIndex)
    {
        if (!HasFrames)
        {
            return null;
        }

        int clampedIndex = Mathf.Clamp(frameIndex, 0, FrameCount - 1);

        if (direction == PhoneSpriteAnimationDirection.Closing && playClosingInReverse)
        {
            clampedIndex = FrameCount - 1 - clampedIndex;
        }

        return frames[clampedIndex];
    }
}