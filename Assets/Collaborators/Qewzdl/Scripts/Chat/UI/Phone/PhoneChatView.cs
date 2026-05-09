using System.Collections;
using UnityEngine;
using UnityEngine.UI;

public class PhoneChatView : MonoBehaviour
{
    private const float HiddenPositionBottomPadding = 40f;
    private const string PhoneRootName = "PhoneRoot";
    private const string PhoneImageName = "PhoneImage";
    private const string ScreenName = "Screen";
    private const string ChatContainerName = "ChatContainer";

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

    [Header("Events")]
    [SerializeField] private ChatEventChannel chatEvents;

    [Header("Animation")]
    [SerializeField] private bool useCurrentPositionAsShownPosition = true;
    [SerializeField] private Vector2 shownAnchoredPosition = new Vector2(40f, 40f);
    [SerializeField, Min(0f)] private float slideDuration = 0.25f;
    [SerializeField] private AnimationCurve slideCurve = AnimationCurve.EaseInOut(0f, 0f, 1f, 1f);

    [Header("Audio")]
    [SerializeField] private AudioSource fallbackAudioSource;
    [SerializeField] private SoundEffect openSfx;
    [SerializeField] private SoundEffect closeSfx;
    [SerializeField] private SoundEffect inputSfx;
    [SerializeField] private SoundEffect incomingWhenClosedSfx;
    [SerializeField] private SoundEffect incomingWhenOpenedSfx;

    [Header("Settings")]
    [SerializeField] private bool playIncomingSfxForOwnMessages;
    [SerializeField] private bool playIncomingSfxForSystemMessages = true;

    private GameObject spawnedChatWindow;
    private ChatWindowUI chatWindow;
    private Coroutine slideCoroutine;
    private Vector2 hiddenAnchoredPosition;
    private bool isInitialized;
    private bool isOpen;
    private bool isSubscribedToChatWindow;
    private bool isSubscribedToChatEvents;

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
            SubscribeToChatEvents();
        }
    }

    private void OnDisable()
    {
        UnsubscribeFromChatWindow();
        UnsubscribeFromChatEvents();
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

        if (chatEvents == null)
        {
            Debug.LogError($"{nameof(PhoneChatView)} requires an assigned {nameof(ChatEventChannel)}.", this);
            return;
        }

        ApplyScreenLayout(true);

        if (useCurrentPositionAsShownPosition)
        {
            shownAnchoredPosition = phoneRoot.anchoredPosition;
        }

        hiddenAnchoredPosition = CalculateHiddenAnchoredPosition();

        SpawnChatWindow();
        SubscribeToChatWindow();
        SubscribeToChatEvents();
        ForceClosed();

        isInitialized = true;
    }

    public void Initialize(
        ChatEventChannel chatEvents,
        SoundEffect inputSfx,
        SoundEffect incomingWhenClosedSfx,
        SoundEffect incomingWhenOpenedSfx,
        SoundEffect openSfx,
        SoundEffect closeSfx)
    {
        Configure(
            chatEvents,
            inputSfx,
            incomingWhenClosedSfx,
            incomingWhenOpenedSfx,
            openSfx,
            closeSfx
        );

        Initialize();
    }

    public void Configure(
        ChatEventChannel chatEvents,
        SoundEffect inputSfx,
        SoundEffect incomingWhenClosedSfx,
        SoundEffect incomingWhenOpenedSfx,
        SoundEffect openSfx,
        SoundEffect closeSfx)
    {
        bool shouldResubscribe = isSubscribedToChatEvents && isActiveAndEnabled;
        UnsubscribeFromChatEvents();

        this.chatEvents = chatEvents;
        this.inputSfx = inputSfx;
        this.incomingWhenClosedSfx = incomingWhenClosedSfx;
        this.incomingWhenOpenedSfx = incomingWhenOpenedSfx;
        this.openSfx = openSfx;
        this.closeSfx = closeSfx;

        ApplyChatInputSfx();
        ApplyChatEventChannel();

        if (shouldResubscribe)
        {
            SubscribeToChatEvents();
        }
    }

    public void SetInputSfx(SoundEffect sound)
    {
        inputSfx = sound;
        ApplyChatInputSfx();
    }

    private void ResolveReferences()
    {
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
            phoneRoot = FindChildRectTransform(PhoneRootName);
        }

        if (phoneRoot == null)
        {
            phoneRoot = transform as RectTransform;
        }

        if (phoneImage == null && phoneRoot != null)
        {
            phoneImage = phoneRoot.GetComponent<Image>();
        }

        if (phoneImage == null)
        {
            phoneImage = FindChildImage(PhoneImageName);
        }

        if (screenRect == null)
        {
            screenRect = FindChildRectTransform(ScreenName);
        }

        if (screenRect == null && chatContainer != null)
        {
            screenRect = chatContainer.parent as RectTransform;
        }

        if (chatContainer == null)
        {
            chatContainer = FindChildRectTransform(ChatContainerName);
        }
    }

    private RectTransform FindChildRectTransform(string childName)
    {
        RectTransform[] rectTransforms = GetComponentsInChildren<RectTransform>(true);

        for (int i = 0; i < rectTransforms.Length; i++)
        {
            if (rectTransforms[i].name == childName)
            {
                return rectTransforms[i];
            }
        }

        return null;
    }

    private Image FindChildImage(string childName)
    {
        Image[] images = GetComponentsInChildren<Image>(true);

        for (int i = 0; i < images.Length; i++)
        {
            if (images[i].name == childName)
            {
                return images[i];
            }
        }

        return null;
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
        ApplyChatEventChannel();
        DisableSpawnedChatMessageNotificationAudio();
        ApplyChatInputSfx();
    }

    private void ApplyChatEventChannel()
    {
        ChatUiEventChannelBinder.Apply(spawnedChatWindow, chatEvents);
    }

    private void DisableSpawnedChatMessageNotificationAudio()
    {
        if (spawnedChatWindow == null)
        {
            return;
        }

        ChatMessageNotificationAudioController[] messageNotificationAudioControllers =
            spawnedChatWindow.GetComponentsInChildren<ChatMessageNotificationAudioController>(true);

        for (int i = 0; i < messageNotificationAudioControllers.Length; i++)
        {
            messageNotificationAudioControllers[i].enabled = false;
        }
    }

    private void ApplyChatInputSfx()
    {
        if (chatWindow == null)
        {
            return;
        }

        chatWindow.SetInputSoundOverride(inputSfx);
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

    private void SubscribeToChatEvents()
    {
        if (chatEvents == null || isSubscribedToChatEvents)
        {
            return;
        }

        chatEvents.MessageReceived += HandleMessageReceived;
        isSubscribedToChatEvents = true;
    }

    private void UnsubscribeFromChatEvents()
    {
        if (chatEvents == null || !isSubscribedToChatEvents)
        {
            return;
        }

        chatEvents.MessageReceived -= HandleMessageReceived;
        isSubscribedToChatEvents = false;
    }

    private void HandleChatWindowOpened()
    {
        OpenShell();
    }

    private void HandleChatWindowClosed()
    {
        CloseShell();
    }

    private void HandleMessageReceived(ChatMessageReceivedEvent messageEvent)
    {
        if (messageEvent.IsLocalSender && !playIncomingSfxForOwnMessages)
        {
            return;
        }

        if (messageEvent.IsSystemMessage && !playIncomingSfxForSystemMessages)
        {
            return;
        }

        SoundEffect incomingSfx = isOpen
            ? incomingWhenOpenedSfx
            : incomingWhenClosedSfx;

        PlayOneShot(incomingSfx);
    }

    private void ForceClosed()
    {
        isOpen = false;
        hiddenAnchoredPosition = CalculateHiddenAnchoredPosition();
        phoneRoot.anchoredPosition = hiddenAnchoredPosition;

        if (phoneCanvasGroup != null)
        {
            phoneCanvasGroup.alpha = 1f;
            phoneCanvasGroup.interactable = false;
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
        hiddenAnchoredPosition = CalculateHiddenAnchoredPosition();
        StartSlide(hiddenAnchoredPosition, false);
    }

    private Vector2 CalculateHiddenAnchoredPosition()
    {
        return shownAnchoredPosition + new Vector2(0f, -phoneRoot.rect.height - HiddenPositionBottomPadding);
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
