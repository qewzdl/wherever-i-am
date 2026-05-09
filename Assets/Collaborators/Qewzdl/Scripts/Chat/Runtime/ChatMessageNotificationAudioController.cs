using UnityEngine;

public class ChatMessageNotificationAudioController : MonoBehaviour
{
    [Header("Events")]
    [SerializeField] private ChatEventChannel chatEvents;

    [Header("Message Sounds")]
    [SerializeField] private SoundEffect messageWhileChatClosedSound;
    [SerializeField] private SoundEffect messageWhileChatOpenSound;

    [Header("Settings")]
    [SerializeField] private bool playSoundForOwnMessages;
    [SerializeField] private bool playSoundForSystemMessages = true;

    private bool isChatOpen;
    private bool isSubscribed;

    public void Configure(
        ChatEventChannel chatEvents,
        SoundEffect messageWhileChatClosedSound,
        SoundEffect messageWhileChatOpenSound,
        bool playSoundForOwnMessages)
    {
        Configure(
            chatEvents,
            messageWhileChatClosedSound,
            messageWhileChatOpenSound,
            playSoundForOwnMessages,
            playSoundForSystemMessages
        );
    }

    public void Configure(
        ChatEventChannel chatEvents,
        SoundEffect messageWhileChatClosedSound,
        SoundEffect messageWhileChatOpenSound,
        bool playSoundForOwnMessages,
        bool playSoundForSystemMessages)
    {
        Unsubscribe();

        this.chatEvents = chatEvents;
        this.messageWhileChatClosedSound = messageWhileChatClosedSound;
        this.messageWhileChatOpenSound = messageWhileChatOpenSound;
        this.playSoundForOwnMessages = playSoundForOwnMessages;
        this.playSoundForSystemMessages = playSoundForSystemMessages;

        if (isActiveAndEnabled)
        {
            Subscribe();
        }
    }

    private void OnEnable()
    {
        Subscribe();
    }

    private void OnDisable()
    {
        Unsubscribe();
    }

    private void Subscribe()
    {
        if (isSubscribed || chatEvents == null)
        {
            return;
        }

        chatEvents.MessageReceived += OnMessageReceived;
        chatEvents.VisibilityChanged += OnVisibilityChanged;
        isSubscribed = true;
    }

    private void Unsubscribe()
    {
        if (!isSubscribed || chatEvents == null)
        {
            return;
        }

        chatEvents.MessageReceived -= OnMessageReceived;
        chatEvents.VisibilityChanged -= OnVisibilityChanged;
        isSubscribed = false;
    }

    private void OnMessageReceived(ChatMessageReceivedEvent messageEvent)
    {
        if (messageEvent.IsLocalSender && !playSoundForOwnMessages)
        {
            return;
        }

        if (messageEvent.IsSystemMessage && !playSoundForSystemMessages)
        {
            return;
        }

        SoundEffect sound = isChatOpen
            ? messageWhileChatOpenSound
            : messageWhileChatClosedSound;

        PlayUiSound(sound);
    }

    private void OnVisibilityChanged(ChatVisibilityChangedEvent visibilityEvent)
    {
        isChatOpen = visibilityEvent.IsOpen;
    }

    private void PlayUiSound(SoundEffect sound)
    {
        if (sound == null)
        {
            return;
        }

        if (AudioManager.Instance == null || AudioManager.Instance.UI == null)
        {
            Debug.LogWarning($"{nameof(ChatMessageNotificationAudioController)}: AudioManager or UiSoundManager is missing.");
            return;
        }

        AudioManager.Instance.UI.Play(sound);
    }
}
