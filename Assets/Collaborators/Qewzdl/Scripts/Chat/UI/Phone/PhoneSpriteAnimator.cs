using System;
using System.Collections;
using UnityEngine;
using UnityEngine.UI;

public class PhoneSpriteAnimator : MonoBehaviour
{
    [SerializeField] private Image targetImage;
    [SerializeField] private PhoneSpriteAnimationProfile profile;

    private Coroutine animationCoroutine;

    public event Action FrameChanged;

    public PhoneSpriteAnimationProfile Profile => profile;

    public void Configure(Image targetImage, PhoneSpriteAnimationProfile profile)
    {
        Stop();

        this.targetImage = targetImage;
        this.profile = profile;
    }

    public void SetTargetImage(Image targetImage)
    {
        this.targetImage = targetImage;
    }

    public void SetProfile(PhoneSpriteAnimationProfile profile)
    {
        Stop();
        this.profile = profile;
    }

    public void ForceClosedSprite()
    {
        if (profile == null)
        {
            return;
        }

        SetSprite(profile.ClosedSprite);
    }

    public void ForceOpenedSprite()
    {
        if (profile == null)
        {
            return;
        }

        SetSprite(profile.OpenedSprite);
    }

    public void PlayOpening()
    {
        Play(PhoneSpriteAnimationDirection.Opening);
    }

    public void PlayClosing()
    {
        Play(PhoneSpriteAnimationDirection.Closing);
    }

    public void Stop()
    {
        if (animationCoroutine == null)
        {
            return;
        }

        StopCoroutine(animationCoroutine);
        animationCoroutine = null;
    }

    private void OnDisable()
    {
        Stop();
    }

    private void Play(PhoneSpriteAnimationDirection direction)
    {
        Stop();

        if (targetImage == null || profile == null || !profile.HasFrames)
        {
            return;
        }

        animationCoroutine = StartCoroutine(PlayRoutine(direction, profile));
    }

    private IEnumerator PlayRoutine(
        PhoneSpriteAnimationDirection direction,
        PhoneSpriteAnimationProfile activeProfile)
    {
        for (int i = 0; i < activeProfile.FrameCount; i++)
        {
            SetSprite(activeProfile.GetFrame(direction, i));
            yield return WaitFrameDuration(activeProfile);
        }

        SetSprite(direction == PhoneSpriteAnimationDirection.Opening
            ? activeProfile.OpenedSprite
            : activeProfile.ClosedSprite);

        animationCoroutine = null;
    }

    private IEnumerator WaitFrameDuration(PhoneSpriteAnimationProfile activeProfile)
    {
        float duration = activeProfile.FrameDuration;

        if (duration <= 0f)
        {
            yield break;
        }

        if (!activeProfile.UseUnscaledTime)
        {
            yield return new WaitForSeconds(duration);
            yield break;
        }

        float elapsed = 0f;

        while (elapsed < duration)
        {
            elapsed += Time.unscaledDeltaTime;
            yield return null;
        }
    }

    private void SetSprite(Sprite sprite)
    {
        if (targetImage == null || sprite == null)
        {
            return;
        }

        if (targetImage.sprite == sprite)
        {
            return;
        }

        targetImage.sprite = sprite;
        FrameChanged?.Invoke();
    }
}