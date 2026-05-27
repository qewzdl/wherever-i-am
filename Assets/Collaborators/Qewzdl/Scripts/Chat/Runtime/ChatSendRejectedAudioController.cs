using UnityEngine;

public class ChatSendRejectedAudioController : MonoBehaviour
{
    [Header("Events")]
    [SerializeField] private ChatEventChannel chatEvents;

    [Header("Sounds")]
    [SerializeField] private SoundEffect sendRejectedSound;

    private bool isSubscribed;

    public void SetEventChannel(ChatEventChannel chatEvents)
    {
        bool shouldSubscribe = isActiveAndEnabled;
        Unsubscribe();

        this.chatEvents = chatEvents;

        if (shouldSubscribe)
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
        if (isSubscribed)
        {
            return;
        }

        if (chatEvents == null)
        {
            return;
        }

        chatEvents.SendRejected += OnSendRejected;
        isSubscribed = true;
    }

    private void Unsubscribe()
    {
        if (!isSubscribed || chatEvents == null)
        {
            isSubscribed = false;
            return;
        }

        chatEvents.SendRejected -= OnSendRejected;
        isSubscribed = false;
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
            Debug.LogWarning($"{nameof(ChatSendRejectedAudioController)}: AudioManager or UiSoundManager is missing.");
            return;
        }

        AudioManager.Instance.UI.Play(sound);
    }
}
