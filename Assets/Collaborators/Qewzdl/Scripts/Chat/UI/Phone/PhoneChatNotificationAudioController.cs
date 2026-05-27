using UnityEngine;

[DisallowMultipleComponent]
public sealed class PhoneChatNotificationAudioController : MonoBehaviour
{
    [SerializeField] private ChatEventChannel chatEvents;
    [SerializeField] private AudioSource fallbackAudioSource;
    [SerializeField] private SoundEffect openSfx;
    [SerializeField] private SoundEffect closeSfx;
    [SerializeField] private SoundEffect incomingWhenClosedSfx;
    [SerializeField] private SoundEffect incomingWhenOpenedSfx;
    [SerializeField] private bool playIncomingSfxForOwnMessages;
    [SerializeField] private bool playIncomingSfxForSystemMessages = true;

    private bool isOpen;
    private bool isSubscribed;

    public AudioSource FallbackAudioSource => fallbackAudioSource;

    public void Configure(
        ChatEventChannel chatEvents,
        SoundEffect incomingWhenClosedSfx,
        SoundEffect incomingWhenOpenedSfx,
        SoundEffect openSfx,
        SoundEffect closeSfx,
        bool playIncomingSfxForOwnMessages,
        bool playIncomingSfxForSystemMessages)
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
        PlayOneShot(openSfx);
    }

    public void PlayClose()
    {
        PlayOneShot(closeSfx);
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
            fallbackAudioSource = ResolveFallbackAudioSource(null);
        }

        AudioClip clip = sound.GetClip();

        if (fallbackAudioSource == null || clip == null)
        {
            return;
        }

        fallbackAudioSource.pitch = sound.GetPitch();
        fallbackAudioSource.PlayOneShot(clip, sound.GetVolume());
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
}
