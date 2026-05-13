using UnityEngine;

[DisallowMultipleComponent]
[RequireComponent(typeof(PhoneScreenLayoutController))]
[RequireComponent(typeof(PhoneChatWindowHost))]
[RequireComponent(typeof(PhoneShellPresentationController))]
[RequireComponent(typeof(PhoneChatNotificationAudioController))]
public class PhoneChatView : MonoBehaviour
{
    [Header("Components")]
    [SerializeField] private PhoneScreenLayoutController screenLayoutController;
    [SerializeField] private PhoneChatWindowHost chatWindowHost;
    [SerializeField] private PhoneShellPresentationController shellPresentationController;
    [SerializeField] private PhoneChatNotificationAudioController notificationAudioController;

    [Header("Typography")]
    [SerializeField] private ChatTypographyProfile typographyProfile;

    private bool isInitialized;

    private void OnValidate()
    {
        EnsureControllers(false);

        if (screenLayoutController == null)
        {
            return;
        }

        screenLayoutController.ResolveReferences();
        screenLayoutController.Apply(false);
    }

    private void OnEnable()
    {
        if (!isInitialized)
        {
            return;
        }

        EnsureControllers();
        Subscribe();
    }

    private void OnDisable()
    {
        Unsubscribe();
    }

    public void Initialize()
    {
        if (isInitialized)
        {
            return;
        }

        EnsureControllers();
        ConfigurePresentationCallbacks();

        if (screenLayoutController != null)
        {
            screenLayoutController.ResolveReferences();
            screenLayoutController.Apply(true);
        }

        if (shellPresentationController == null || !shellPresentationController.HasPhoneRoot)
        {
            Debug.LogError($"{nameof(PhoneChatView)}: Phone Root is not assigned.", this);
            return;
        }

        shellPresentationController.PrepareClosedPosition();

        if (chatWindowHost == null || !chatWindowHost.Spawn())
        {
            return;
        }

        ApplyTypography();
        Subscribe();

        shellPresentationController.ForceClosed();
        notificationAudioController?.SetOpenState(false);

        isInitialized = true;
    }

    public void Initialize(
        ChatEventChannel chatEvents,
        SoundEffect inputSfx,
        SoundEffect incomingWhenClosedSfx,
        SoundEffect incomingWhenOpenedSfx,
        SoundEffect openSfx,
        SoundEffect closeSfx,
        bool playIncomingSfxForOwnMessages,
        bool playIncomingSfxForSystemMessages,
        ChatTypographyProfile typographyProfile,
        PhoneSpriteAnimationProfile spriteAnimationProfile)
    {
        Configure(
            chatEvents,
            inputSfx,
            incomingWhenClosedSfx,
            incomingWhenOpenedSfx,
            openSfx,
            closeSfx,
            playIncomingSfxForOwnMessages,
            playIncomingSfxForSystemMessages,
            typographyProfile,
            spriteAnimationProfile);

        Initialize();
    }

    public void Configure(
        ChatEventChannel chatEvents,
        SoundEffect inputSfx,
        SoundEffect incomingWhenClosedSfx,
        SoundEffect incomingWhenOpenedSfx,
        SoundEffect openSfx,
        SoundEffect closeSfx,
        bool playIncomingSfxForOwnMessages,
        bool playIncomingSfxForSystemMessages,
        ChatTypographyProfile typographyProfile,
        PhoneSpriteAnimationProfile spriteAnimationProfile)
    {
        EnsureControllers();

        chatWindowHost?.Configure(chatEvents, inputSfx);
        notificationAudioController?.Configure(
            chatEvents,
            incomingWhenClosedSfx,
            incomingWhenOpenedSfx,
            openSfx,
            closeSfx,
            playIncomingSfxForOwnMessages,
            playIncomingSfxForSystemMessages);
        shellPresentationController?.SetSpriteAnimationProfile(spriteAnimationProfile);

        this.typographyProfile = typographyProfile;
        ApplyTypography();
    }

    public void SetInputSfx(SoundEffect sound)
    {
        EnsureControllers();
        chatWindowHost?.SetInputSfx(sound);
    }

    public void SetTypographyProfile(ChatTypographyProfile typographyProfile)
    {
        this.typographyProfile = typographyProfile;
        ApplyTypography();
    }

    public void SetSpriteAnimationProfile(PhoneSpriteAnimationProfile spriteAnimationProfile)
    {
        EnsureControllers();
        shellPresentationController?.SetSpriteAnimationProfile(spriteAnimationProfile);

        if (shellPresentationController == null || !shellPresentationController.IsOpen)
        {
            shellPresentationController?.ForcePhoneClosedSprite();
        }
    }

    private void EnsureControllers(bool createMissing = true)
    {
        screenLayoutController = ResolveComponent(screenLayoutController, createMissing);
        shellPresentationController = ResolveComponent(shellPresentationController, createMissing);
        notificationAudioController = ResolveComponent(notificationAudioController, createMissing);

        PhoneChatWindowHost resolvedChatWindowHost = ResolveComponent(chatWindowHost, createMissing);

        if (chatWindowHost == resolvedChatWindowHost)
        {
            return;
        }

        if (chatWindowHost != null)
        {
            chatWindowHost.Opened -= HandleChatWindowOpened;
            chatWindowHost.Closed -= HandleChatWindowClosed;
        }

        chatWindowHost = resolvedChatWindowHost;
        SubscribeToChatWindow();
    }

    private T ResolveComponent<T>(T current, bool createMissing)
        where T : Component
    {
        if (current != null)
        {
            return current;
        }

        T component = GetComponent<T>();

        if (component != null || !createMissing)
        {
            return component;
        }

        return gameObject.AddComponent<T>();
    }

    private void ConfigurePresentationCallbacks()
    {
        shellPresentationController?.ConfigureCallbacks(
            FocusChatInput,
            RefreshScreenLayoutFromSpriteFrame);
    }

    private void ApplyTypography()
    {
        ChatTypographyApplier.Apply(gameObject, typographyProfile);
    }

    private void Subscribe()
    {
        SubscribeToChatWindow();
        notificationAudioController?.Subscribe();
    }

    private void Unsubscribe()
    {
        if (chatWindowHost != null)
        {
            chatWindowHost.Opened -= HandleChatWindowOpened;
            chatWindowHost.Closed -= HandleChatWindowClosed;
            chatWindowHost.Unsubscribe();
        }

        notificationAudioController?.Unsubscribe();
        shellPresentationController?.Dispose();
    }

    private void SubscribeToChatWindow()
    {
        if (chatWindowHost == null)
        {
            return;
        }

        chatWindowHost.Opened -= HandleChatWindowOpened;
        chatWindowHost.Closed -= HandleChatWindowClosed;
        chatWindowHost.Opened += HandleChatWindowOpened;
        chatWindowHost.Closed += HandleChatWindowClosed;
        chatWindowHost.Subscribe();
    }

    private void HandleChatWindowOpened()
    {
        if (shellPresentationController == null || !shellPresentationController.Open())
        {
            return;
        }

        notificationAudioController?.SetOpenState(true);
        notificationAudioController?.PlayOpen();
    }

    private void HandleChatWindowClosed()
    {
        if (shellPresentationController == null || !shellPresentationController.Close())
        {
            return;
        }

        notificationAudioController?.SetOpenState(false);
        notificationAudioController?.PlayClose();
    }

    private void FocusChatInput()
    {
        chatWindowHost?.ChatWindow?.FocusInput();
    }

    private void RefreshScreenLayoutFromSpriteFrame()
    {
        screenLayoutController?.Apply(false);
    }
}
