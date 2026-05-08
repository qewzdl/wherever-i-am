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
    [SerializeField] private bool playMessageNotifications = true;
    [SerializeField] private bool playSoundForOwnMessages;

    private bool isChatOpen;

    public void SetMessageNotificationsEnabled(bool value)
    {
        playMessageNotifications = value;
    }

    private void OnEnable()
    {
        if (chatEvents == null)
        {
            Debug.LogError($"{nameof(ChatNotificationAudioController)} requires an assigned {nameof(ChatEventChannel)}.", this);
            enabled = false;
            return;
        }

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
        if (!playMessageNotifications)
        {
            return;
        }

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
}
