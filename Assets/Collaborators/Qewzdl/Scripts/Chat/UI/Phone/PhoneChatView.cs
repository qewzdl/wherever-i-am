using System.Collections;
using UnityEngine;
using UnityEngine.UI;

public class PhoneChatView : MonoBehaviour
{
    [Header("Phone Root")]
    [SerializeField] private RectTransform phoneRoot;
    [SerializeField] private CanvasGroup phoneCanvasGroup;

    [Header("Phone Screen Layout")]
    [SerializeField] private Image phoneImage;
    [SerializeField] private RectTransform screenRect;
    [SerializeField] private bool fitScreenRectToTexture = true;
    [SerializeField] private Rect screenPixelRectFromTopLeft = new Rect(310f, 1120f, 1278f, 920f);
    [SerializeField] private Vector4 chatContainerPadding = new Vector4(8f, 8f, 8f, 8f);
    [SerializeField] private bool addScreenRectMask = true;

    [Header("Chat Window")]
    [SerializeField] private RectTransform chatContainer;
    [SerializeField] private GameObject chatWindowPrefab;

    [Header("Animation")]
    [SerializeField] private bool useCurrentPositionAsShownPosition = true;
    [SerializeField] private Vector2 shownAnchoredPosition = new Vector2(40f, 40f);
    [SerializeField] private Vector2 hiddenOffset = new Vector2(0f, -900f);
    [SerializeField, Min(0f)] private float slideDuration = 0.25f;
    [SerializeField] private AnimationCurve slideCurve = AnimationCurve.EaseInOut(0f, 0f, 1f, 1f);

    [Header("Audio")]
    [SerializeField] private AudioSource fallbackAudioSource;
    [SerializeField] private SoundEffect openSfx;
    [SerializeField] private SoundEffect closeSfx;

    private GameObject spawnedChatWindow;
    private ChatWindowUI chatWindow;
    private Coroutine slideCoroutine;
    private Vector2 hiddenAnchoredPosition;
    private bool isInitialized;
    private bool isOpen;
    private bool isSubscribedToChatWindow;

    private void OnValidate()
    {
        ResolveScreenReferences();
        ApplyScreenLayout(false);
    }

    private void OnEnable()
    {
        if (isInitialized)
        {
            SubscribeToChatWindow();
        }
    }

    private void OnDisable()
    {
        UnsubscribeFromChatWindow();
    }

    public void Initialize()
    {
        if (isInitialized)
        {
            return;
        }

        ResolveReferences();

        if (phoneRoot == null)
        {
            Debug.LogError($"{nameof(PhoneChatView)}: Phone Root is not assigned.", this);
            return;
        }

        ApplyScreenLayout(true);

        if (useCurrentPositionAsShownPosition)
        {
            shownAnchoredPosition = phoneRoot.anchoredPosition;
        }

        hiddenAnchoredPosition = shownAnchoredPosition + hiddenOffset;

        SpawnChatWindow();
        SubscribeToChatWindow();
        ForceClosed();

        isInitialized = true;
    }

    private void ResolveReferences()
    {
        if (phoneRoot == null)
        {
            phoneRoot = transform as RectTransform;
        }

        ResolveScreenReferences();

        if (phoneCanvasGroup == null && phoneRoot != null)
        {
            phoneCanvasGroup = phoneRoot.GetComponent<CanvasGroup>();
        }

        if (phoneCanvasGroup == null && phoneRoot != null)
        {
            phoneCanvasGroup = phoneRoot.gameObject.AddComponent<CanvasGroup>();
        }

        if (fallbackAudioSource == null)
        {
            fallbackAudioSource = GetComponent<AudioSource>();
        }

        if (fallbackAudioSource == null)
        {
            fallbackAudioSource = gameObject.AddComponent<AudioSource>();
            fallbackAudioSource.playOnAwake = false;
            fallbackAudioSource.spatialBlend = 0f;
        }
    }

    private void ResolveScreenReferences()
    {
        if (phoneRoot == null)
        {
            phoneRoot = transform as RectTransform;
        }

        if (phoneImage == null && phoneRoot != null)
        {
            phoneImage = phoneRoot.GetComponent<Image>();
        }

        if (screenRect == null && chatContainer != null)
        {
            screenRect = chatContainer.parent as RectTransform;
        }
    }

    private void ApplyScreenLayout(bool ensureMask)
    {
        if (!fitScreenRectToTexture)
        {
            ApplyChatContainerPadding();
            return;
        }

        ResolveScreenReferences();

        if (phoneImage == null || screenRect == null)
        {
            ApplyChatContainerPadding();
            return;
        }

        Rect textureRect;

        if (phoneImage.sprite != null)
        {
            textureRect = phoneImage.sprite.rect;
        }
        else if (phoneImage.mainTexture != null)
        {
            textureRect = new Rect(0f, 0f, phoneImage.mainTexture.width, phoneImage.mainTexture.height);
        }
        else
        {
            ApplyChatContainerPadding();
            return;
        }

        float textureWidth = Mathf.Max(1f, textureRect.width);
        float textureHeight = Mathf.Max(1f, textureRect.height);
        float left = Mathf.Clamp(screenPixelRectFromTopLeft.xMin, 0f, textureWidth);
        float top = Mathf.Clamp(screenPixelRectFromTopLeft.yMin, 0f, textureHeight);
        float right = Mathf.Clamp(screenPixelRectFromTopLeft.xMax, left, textureWidth);
        float bottom = Mathf.Clamp(screenPixelRectFromTopLeft.yMax, top, textureHeight);

        if (right <= left || bottom <= top)
        {
            ApplyChatContainerPadding();
            return;
        }

        screenRect.anchorMin = new Vector2(left / textureWidth, 1f - bottom / textureHeight);
        screenRect.anchorMax = new Vector2(right / textureWidth, 1f - top / textureHeight);
        screenRect.anchoredPosition = Vector2.zero;
        screenRect.offsetMin = Vector2.zero;
        screenRect.offsetMax = Vector2.zero;
        screenRect.localScale = Vector3.one;

        ApplyChatContainerPadding();

        if (ensureMask && addScreenRectMask && screenRect.GetComponent<RectMask2D>() == null)
        {
            screenRect.gameObject.AddComponent<RectMask2D>();
        }
    }

    private void ApplyChatContainerPadding()
    {
        if (chatContainer == null)
        {
            return;
        }

        chatContainer.anchorMin = Vector2.zero;
        chatContainer.anchorMax = Vector2.one;
        chatContainer.offsetMin = new Vector2(chatContainerPadding.x, chatContainerPadding.w);
        chatContainer.offsetMax = new Vector2(-chatContainerPadding.z, -chatContainerPadding.y);
        chatContainer.localScale = Vector3.one;
    }

    private void SpawnChatWindow()
    {
        if (spawnedChatWindow != null)
        {
            return;
        }

        if (chatContainer == null)
        {
            Debug.LogError($"{nameof(PhoneChatView)}: Chat Container is not assigned.", this);
            return;
        }

        if (chatWindowPrefab == null)
        {
            Debug.LogError($"{nameof(PhoneChatView)}: Chat Window Prefab is not assigned.", this);
            return;
        }

        spawnedChatWindow = Instantiate(chatWindowPrefab, chatContainer);
        StretchToParent(spawnedChatWindow);

        chatWindow = spawnedChatWindow.GetComponentInChildren<ChatWindowUI>(true);
    }

    private void SubscribeToChatWindow()
    {
        if (chatWindow == null || isSubscribedToChatWindow)
        {
            return;
        }

        chatWindow.Opened += HandleChatWindowOpened;
        chatWindow.Closed += HandleChatWindowClosed;
        isSubscribedToChatWindow = true;
    }

    private void UnsubscribeFromChatWindow()
    {
        if (chatWindow == null || !isSubscribedToChatWindow)
        {
            return;
        }

        chatWindow.Opened -= HandleChatWindowOpened;
        chatWindow.Closed -= HandleChatWindowClosed;
        isSubscribedToChatWindow = false;
    }

    private void HandleChatWindowOpened()
    {
        OpenShell();
    }

    private void HandleChatWindowClosed()
    {
        CloseShell();
    }

    private void ForceClosed()
    {
        isOpen = false;
        phoneRoot.anchoredPosition = hiddenAnchoredPosition;

        if (phoneCanvasGroup != null)
        {
            phoneCanvasGroup.alpha = 1f;
            phoneCanvasGroup.interactable = true;
            phoneCanvasGroup.blocksRaycasts = false;
        }
    }

    private void OpenShell()
    {
        if (isOpen)
        {
            return;
        }

        isOpen = true;
        PlayOneShot(openSfx);
        StartSlide(shownAnchoredPosition, true);
    }

    private void CloseShell()
    {
        if (!isOpen)
        {
            return;
        }

        isOpen = false;
        PlayOneShot(closeSfx);
        StartSlide(hiddenAnchoredPosition, false);
    }

    private void StartSlide(Vector2 targetPosition, bool shouldInteractAfterSlide)
    {
        if (slideCoroutine != null)
        {
            StopCoroutine(slideCoroutine);
        }

        slideCoroutine = StartCoroutine(SlideRoutine(targetPosition, shouldInteractAfterSlide));
    }

    private IEnumerator SlideRoutine(Vector2 targetPosition, bool shouldInteractAfterSlide)
    {
        if (phoneCanvasGroup != null)
        {
            phoneCanvasGroup.interactable = true;
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
                float curveValue = slideCurve.Evaluate(normalizedTime);

                phoneRoot.anchoredPosition = Vector2.LerpUnclamped(startPosition, targetPosition, curveValue);

                yield return null;
            }
        }

        phoneRoot.anchoredPosition = targetPosition;

        if (phoneCanvasGroup != null)
        {
            phoneCanvasGroup.interactable = true;
            phoneCanvasGroup.blocksRaycasts = shouldInteractAfterSlide;
        }

        slideCoroutine = null;
    }

    private void PlayOneShot(SoundEffect sound)
    {
        if (sound == null)
        {
            return;
        }

        if (AudioManager.Instance != null && AudioManager.Instance.UI != null)
        {
            AudioManager.Instance.UI.Play(sound);
            return;
        }

        if (fallbackAudioSource == null)
        {
            ResolveReferences();
        }

        AudioClip clip = sound.GetClip();

        if (fallbackAudioSource == null || clip == null)
        {
            return;
        }

        fallbackAudioSource.pitch = sound.GetPitch();
        fallbackAudioSource.PlayOneShot(clip, sound.GetVolume());
    }

    private void StretchToParent(GameObject target)
    {
        RectTransform rectTransform = target.transform as RectTransform;

        if (rectTransform == null)
        {
            return;
        }

        rectTransform.anchorMin = Vector2.zero;
        rectTransform.anchorMax = Vector2.one;
        rectTransform.offsetMin = Vector2.zero;
        rectTransform.offsetMax = Vector2.zero;
        rectTransform.localScale = Vector3.one;
    }
}
