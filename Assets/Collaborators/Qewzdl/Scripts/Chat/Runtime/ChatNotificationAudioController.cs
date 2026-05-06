using UnityEngine;

public class ChatNotificationAudioController : MonoBehaviour
{
    [Header("Events")]
    [SerializeField] private ChatEventChannel chatEvents;

    [Header("Message Sounds")]
    [SerializeField] private SoundEffect messageWhileChatClosedSound;
    [SerializeField] private SoundEffect messageWhileChatOpenSound;

    [Header("Error Sounds")]
    [SerializeField] private SoundEffect sendRejectedSound;

    [Header("Settings")]
    [SerializeField] private bool playSoundForOwnMessages;

    private bool isChatOpen;

    private void OnEnable()
    {
        ResolveEventChannel();

        chatEvents.MessageReceived += OnMessageReceived;
        chatEvents.VisibilityChanged += OnVisibilityChanged;
        chatEvents.SendRejected += OnSendRejected;
    }

    private void OnDisable()
    {
        if (chatEvents == null)
        {
            return;
        }

        chatEvents.MessageReceived -= OnMessageReceived;
        chatEvents.VisibilityChanged -= OnVisibilityChanged;
        chatEvents.SendRejected -= OnSendRejected;
    }

    private void OnMessageReceived(ChatMessageReceivedEvent messageEvent)
    {
        if (messageEvent.IsLocalSender && !playSoundForOwnMessages)
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

    private void OnSendRejected(ChatSendRejectedEvent rejectedEvent)
    {
        PlayUiSound(sendRejectedSound);
    }

    private void PlayUiSound(SoundEffect sound)
    {
        if (sound == null)
        {
            return;
        }

        if (AudioManager.Instance == null || AudioManager.Instance.UI == null)
        {
            Debug.LogWarning($"{nameof(ChatNotificationAudioController)}: AudioManager or UiSoundManager is missing.");
            return;
        }

        AudioManager.Instance.UI.Play(sound);
    }

    private void ResolveEventChannel()
    {
        chatEvents = ChatEventChannel.Resolve(chatEvents);
    }
}
