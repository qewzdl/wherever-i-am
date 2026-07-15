using UnityEngine;

[DisallowMultipleComponent]
public sealed class PhoneChatNotificationAudioController : MonoBehaviour, IUiSoundServiceConsumer
{
    [SerializeField] private ChatEventChannel chatEvents;
    [SerializeField] private AudioSource fallbackAudioSource;
    [SerializeField] private SoundEffect openSfx;
    [SerializeField] private SoundEffect closeSfx;
    [SerializeField] private SoundEffect incomingWhenClosedSfx;
    [SerializeField] private SoundEffect incomingWhenOpenedSfx;
    [SerializeField] private bool playIncomingSfxForOwnMessages;
    [SerializeField] private bool playIncomingSfxForSystemMessages = true;
    [SerializeField] private PhoneAudioCueEventChannel phoneAudioCueEvents;

    private bool isOpen;
    private bool isSubscribed;
    private IUiSoundService uiSoundService;

    public AudioSource FallbackAudioSource => fallbackAudioSource;

    public void Construct(IUiSoundService service)
    {
        uiSoundService = service;
    }

    public void ReleaseUiSoundService()
    {
        uiSoundService = null;
    }

    public void Configure(
        ChatEventChannel chatEvents,
        SoundEffect incomingWhenClosedSfx,
        SoundEffect incomingWhenOpenedSfx,
        SoundEffect openSfx,
        SoundEffect closeSfx,
        bool playIncomingSfxForOwnMessages,
        bool playIncomingSfxForSystemMessages,
        PhoneAudioCueEventChannel phoneAudioCueEvents)
    {
        bool shouldResubscribe = isSubscribed;
        Unsubscribe();

        this.chatEvents = chatEvents;
        this.incomingWhenClosedSfx = incomingWhenClosedSfx;
        this.incomingWhenOpenedSfx = incomingWhenOpenedSfx;
        this.openSfx = openSfx;
        this.closeSfx = closeSfx;
        this.playIncomingSfxForOwnMessages = playIncomingSfxForOwnMessages;
        this.playIncomingSfxForSystemMessages = playIncomingSfxForSystemMessages;
        this.phoneAudioCueEvents = phoneAudioCueEvents;

        if (shouldResubscribe)
        {
            Subscribe();
        }
    }

    public AudioSource ResolveFallbackAudioSource(AudioSource currentFallbackAudioSource)
    {
        if (currentFallbackAudioSource != null)
        {
            return currentFallbackAudioSource;
        }

        AudioSource resolvedAudioSource = GetComponent<AudioSource>();

        if (resolvedAudioSource == null)
        {
            resolvedAudioSource = gameObject.AddComponent<AudioSource>();
            resolvedAudioSource.playOnAwake = false;
            resolvedAudioSource.spatialBlend = 0f;
        }

        return resolvedAudioSource;
    }

    public void SetOpenState(bool isOpen)
    {
        this.isOpen = isOpen;
    }

    public void PlayOpen()
    {
        if (TryPlayOneShot(openSfx))
        {
            phoneAudioCueEvents?.RaiseCuePlayed(
                PhoneAudioCueEvent.Open()
            );
        }
    }

    public void PlayClose()
    {
        if (TryPlayOneShot(closeSfx))
        {
            phoneAudioCueEvents?.RaiseCuePlayed(
                PhoneAudioCueEvent.Close()
            );
        }
    }

    public void Subscribe()
    {
        if (chatEvents == null || isSubscribed)
        {
            return;
        }

        chatEvents.MessageReceived += HandleMessageReceived;
        isSubscribed = true;
    }

    public void Unsubscribe()
    {
        if (chatEvents == null || !isSubscribed)
        {
            return;
        }

        chatEvents.MessageReceived -= HandleMessageReceived;
        isSubscribed = false;
    }

    private void OnDisable()
    {
        Unsubscribe();
    }

    public void PlayOneShot(SoundEffect sound)
    {
        TryPlayOneShot(sound);
    }

    private bool TryPlayOneShot(SoundEffect sound)
    {
        if (sound == null)
        {
            return false;
        }

        if (uiSoundService != null)
            return uiSoundService.TryPlay(sound);

        if (fallbackAudioSource == null)
        {
            fallbackAudioSource = ResolveFallbackAudioSource(null);
        }

        AudioClip clip = sound.GetClip();

        if (fallbackAudioSource == null || clip == null)
        {
            return false;
        }

        fallbackAudioSource.pitch = sound.GetPitch();
        fallbackAudioSource.PlayOneShot(clip, sound.GetVolume());
        return true;
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

        if (!TryPlayOneShot(incomingSfx) ||
            !uint.TryParse(messageEvent.MessageId, out uint messageId))
        {
            return;
        }

        phoneAudioCueEvents?.RaiseCuePlayed(
            PhoneAudioCueEvent.IncomingNotification(messageId)
        );
    }
}
