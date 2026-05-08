using UnityEngine;

public class ChatSendRejectedAudioController : MonoBehaviour
{
    [Header("Events")]
    [SerializeField] private ChatEventChannel chatEvents;

    [Header("Sounds")]
    [SerializeField] private SoundEffect sendRejectedSound;

    private bool isSubscribed;

    private void OnEnable()
    {
        if (isSubscribed)
        {
            return;
        }

        if (chatEvents == null)
        {
            Debug.LogError($"{nameof(ChatSendRejectedAudioController)} requires an assigned {nameof(ChatEventChannel)}.", this);
            enabled = false;
            return;
        }

        chatEvents.SendRejected += OnSendRejected;
        isSubscribed = true;
    }

    private void OnDisable()
    {
        if (!isSubscribed || chatEvents == null)
        {
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
