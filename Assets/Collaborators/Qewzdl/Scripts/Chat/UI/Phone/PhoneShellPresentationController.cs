using System;
using System.Collections;
using UnityEngine;
using UnityEngine.UI;

[DisallowMultipleComponent]
public sealed class PhoneShellPresentationController : MonoBehaviour
{
    private const float HiddenPositionBottomPadding = 40f;

    [Header("Phone Root")]
    [SerializeField] private RectTransform phoneRoot;
    [SerializeField] private Image phoneImage;
    [SerializeField] private CanvasGroup phoneCanvasGroup;

    [Header("Phone Sprite Animation")]
    [SerializeField] private PhoneSpriteAnimator spriteAnimator;
    [SerializeField] private PhoneSpriteAnimationProfile spriteAnimationProfile;

    [Header("Chat Content Gate")]
    [SerializeField] private RectTransform chatContainer;
    [SerializeField] private CanvasGroup chatContentCanvasGroup;
    [SerializeField] private bool hideChatContentUntilPhoneOpened = true;

    [Header("Slide")]
    [SerializeField] private bool useCurrentPositionAsShownPosition = true;
    [SerializeField] private Vector2 shownAnchoredPosition = new Vector2(40f, 40f);
    [SerializeField, Min(0f)] private float slideDuration = 0.25f;
    [SerializeField] private AnimationCurve slideCurve = AnimationCurve.EaseInOut(0f, 0f, 1f, 1f);

    private Action focusInput;
    private Action refreshScreenLayout;
    private Coroutine slideCoroutine;

    private Vector2 hiddenAnchoredPosition;
    private Vector2 hiddenAnchoredPositionSize;

    private bool hasHiddenAnchoredPosition;
    private bool isOpen;
    private bool waitingForOpeningSlide;
    private bool waitingForOpeningSprite;

    public bool IsOpen => isOpen;
    public bool HasPhoneRoot => phoneRoot != null;
    public Vector2 ShownAnchoredPosition => shownAnchoredPosition;
    public CanvasGroup ChatContentCanvasGroup => chatContentCanvasGroup;

    public void ConfigureCallbacks(
        Action focusInput,
        Action refreshScreenLayout)
    {
        this.focusInput = focusInput;
        this.refreshScreenLayout = refreshScreenLayout;

        ResolveChatContentCanvasGroup();
        ConfigureSpriteAnimator();
    }

    public void SetSpriteAnimationProfile(PhoneSpriteAnimationProfile spriteAnimationProfile)
    {
        this.spriteAnimationProfile = spriteAnimationProfile;
        ConfigureSpriteAnimator();
    }

    public void Dispose()
    {
        UnsubscribeFromSpriteAnimator();
    }

    private void OnDisable()
    {
        UnsubscribeFromSpriteAnimator();

        if (slideCoroutine != null)
        {
            StopCoroutine(slideCoroutine);
            slideCoroutine = null;
        }
    }

    public void SetShownPositionFromCurrent()
    {
        if (phoneRoot == null)
        {
            return;
        }

        shownAnchoredPosition = phoneRoot.anchoredPosition;
    }

    public void ForceRefreshHiddenAnchoredPosition()
    {
        if (phoneRoot == null)
        {
            return;
        }

        Canvas.ForceUpdateCanvases();
        LayoutRebuilder.ForceRebuildLayoutImmediate(phoneRoot);

        RefreshHiddenAnchoredPosition();
    }

    public void PrepareClosedPosition()
    {
        ResolveChatContentCanvasGroup();
        ConfigureSpriteAnimator();

        if (useCurrentPositionAsShownPosition)
        {
            SetShownPositionFromCurrent();
        }

        ForceRefreshHiddenAnchoredPosition();
    }

    public void ForceClosed()
    {
        if (phoneRoot == null)
        {
            return;
        }

        isOpen = false;
        ResetOpeningGate();
        RefreshHiddenAnchoredPositionIfSizeChanged();
        phoneRoot.anchoredPosition = hiddenAnchoredPosition;
        ForcePhoneClosedSprite();
        SetChatContentVisible(false);

        if (phoneCanvasGroup != null)
        {
            phoneCanvasGroup.alpha = 1f;
            phoneCanvasGroup.interactable = false;
            phoneCanvasGroup.blocksRaycasts = false;
        }
    }

    public bool Open()
    {
        if (isOpen || phoneRoot == null)
        {
            return false;
        }

        isOpen = true;
        SetChatContentVisible(false);

        if (phoneCanvasGroup != null)
        {
            phoneCanvasGroup.interactable = false;
            phoneCanvasGroup.blocksRaycasts = false;
        }

        waitingForOpeningSlide = true;
        waitingForOpeningSprite = PlayPhoneOpeningAnimation();

        StartSlide(shownAnchoredPosition, false, HandleOpeningSlideCompleted);
        TryCompletePhoneOpeningPresentation();

        return true;
    }

    public bool Close()
    {
        if (!isOpen || phoneRoot == null)
        {
            return false;
        }

        isOpen = false;
        ResetOpeningGate();
        SetChatContentVisible(false);
        PlayPhoneClosingAnimation();

        RefreshHiddenAnchoredPositionIfSizeChanged();
        StartSlide(hiddenAnchoredPosition, false);

        return true;
    }

    public void ForcePhoneClosedSprite()
    {
        if (spriteAnimator == null)
        {
            return;
        }

        spriteAnimator.ForceClosedSprite();
    }

    private void ResolveChatContentCanvasGroup()
    {
        if (!hideChatContentUntilPhoneOpened)
        {
            return;
        }

        if (chatContentCanvasGroup != null || chatContainer == null)
        {
            return;
        }

        chatContentCanvasGroup = chatContainer.GetComponent<CanvasGroup>();

        if (chatContentCanvasGroup == null)
        {
            chatContentCanvasGroup = chatContainer.gameObject.AddComponent<CanvasGroup>();
        }
    }

    private void ConfigureSpriteAnimator()
    {
        if (spriteAnimator == null)
        {
            return;
        }

        spriteAnimator.FrameChanged -= HandlePhoneSpriteFrameChanged;
        spriteAnimator.FrameChanged += HandlePhoneSpriteFrameChanged;

        spriteAnimator.PlaybackCompleted -= HandlePhoneSpritePlaybackCompleted;
        spriteAnimator.PlaybackCompleted += HandlePhoneSpritePlaybackCompleted;

        spriteAnimator.Configure(phoneImage, spriteAnimationProfile);
    }

    private void UnsubscribeFromSpriteAnimator()
    {
        if (spriteAnimator == null)
        {
            return;
        }

        spriteAnimator.FrameChanged -= HandlePhoneSpriteFrameChanged;
        spriteAnimator.PlaybackCompleted -= HandlePhoneSpritePlaybackCompleted;
    }

    private void HandlePhoneSpriteFrameChanged()
    {
        if (spriteAnimationProfile == null)
        {
            return;
        }

        if (!spriteAnimationProfile.RefreshScreenLayoutOnFrameChange)
        {
            return;
        }

        refreshScreenLayout?.Invoke();
    }

    private void HandlePhoneSpritePlaybackCompleted(PhoneSpriteAnimationDirection direction)
    {
        if (direction != PhoneSpriteAnimationDirection.Opening)
        {
            return;
        }

        waitingForOpeningSprite = false;
        TryCompletePhoneOpeningPresentation();
    }

    private void SetChatContentVisible(bool visible)
    {
        if (!hideChatContentUntilPhoneOpened)
        {
            return;
        }

        ResolveChatContentCanvasGroup();

        if (chatContentCanvasGroup == null)
        {
            return;
        }

        chatContentCanvasGroup.alpha = visible ? 1f : 0f;
        chatContentCanvasGroup.interactable = visible;
        chatContentCanvasGroup.blocksRaycasts = visible;
    }

    private void ResetOpeningGate()
    {
        waitingForOpeningSlide = false;
        waitingForOpeningSprite = false;
    }

    private void HandleOpeningSlideCompleted()
    {
        waitingForOpeningSlide = false;
        TryCompletePhoneOpeningPresentation();
    }

    private void TryCompletePhoneOpeningPresentation()
    {
        if (!isOpen)
        {
            return;
        }

        if (waitingForOpeningSlide || waitingForOpeningSprite)
        {
            return;
        }

        SetChatContentVisible(true);

        if (phoneCanvasGroup != null)
        {
            phoneCanvasGroup.interactable = true;
            phoneCanvasGroup.blocksRaycasts = true;
        }

        focusInput?.Invoke();
    }

    private bool PlayPhoneOpeningAnimation()
    {
        if (spriteAnimator == null)
        {
            return false;
        }

        return spriteAnimator.PlayOpening();
    }

    private bool PlayPhoneClosingAnimation()
    {
        if (spriteAnimator == null)
        {
            return false;
        }

        return spriteAnimator.PlayClosing();
    }

    private void RefreshHiddenAnchoredPositionIfSizeChanged()
    {
        if (!hasHiddenAnchoredPosition || HasPhoneRootSizeChanged())
        {
            RefreshHiddenAnchoredPosition();
        }
    }

    private bool HasPhoneRootSizeChanged()
    {
        Vector2 currentSize = phoneRoot.rect.size;
        return (currentSize - hiddenAnchoredPositionSize).sqrMagnitude > 0.01f;
    }

    private void RefreshHiddenAnchoredPosition()
    {
        hiddenAnchoredPositionSize = phoneRoot.rect.size;
        hasHiddenAnchoredPosition = true;

        float hiddenOffsetY = -hiddenAnchoredPositionSize.y - HiddenPositionBottomPadding;
        hiddenAnchoredPosition = shownAnchoredPosition + new Vector2(0f, hiddenOffsetY);
    }

    private void StartSlide(
        Vector2 targetPosition,
        bool shouldInteractAfterSlide,
        Action completed = null)
    {
        if (slideCoroutine != null)
        {
            StopCoroutine(slideCoroutine);
        }

        slideCoroutine = StartCoroutine(SlideRoutine(
            targetPosition,
            shouldInteractAfterSlide,
            completed));
    }

    private IEnumerator SlideRoutine(
        Vector2 targetPosition,
        bool shouldInteractAfterSlide,
        Action completed)
    {
        if (phoneCanvasGroup != null)
        {
            phoneCanvasGroup.interactable = false;
            phoneCanvasGroup.blocksRaycasts = false;
        }

        Vector2 startPosition = phoneRoot.anchoredPosition;
        float elapsed = 0f;

        if (slideDuration <= 0f)
        {
            phoneRoot.anchoredPosition = targetPosition;
        }
        else
        {
            while (elapsed < slideDuration)
            {
                elapsed += Time.unscaledDeltaTime;

                float normalizedTime = Mathf.Clamp01(elapsed / slideDuration);
                float curveValue = slideCurve != null
                    ? slideCurve.Evaluate(normalizedTime)
                    : normalizedTime;

                phoneRoot.anchoredPosition = Vector2.LerpUnclamped(
                    startPosition,
                    targetPosition,
                    curveValue);

                yield return null;
            }
        }

        phoneRoot.anchoredPosition = targetPosition;

        if (phoneCanvasGroup != null)
        {
            phoneCanvasGroup.interactable = shouldInteractAfterSlide;
            phoneCanvasGroup.blocksRaycasts = shouldInteractAfterSlide;
        }

        slideCoroutine = null;
        completed?.Invoke();
    }
}
