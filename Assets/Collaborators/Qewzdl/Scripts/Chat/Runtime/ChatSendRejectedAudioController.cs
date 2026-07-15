using UnityEngine;

public class ChatSendRejectedAudioController : MonoBehaviour, IUiSoundServiceConsumer
{
    [Header("Events")]
    [SerializeField] private ChatEventChannel chatEvents;

    [Header("Sounds")]
    [SerializeField] private SoundEffect sendRejectedSound;

    private bool isSubscribed;
    private IUiSoundService uiSoundService;

    public void Construct(IUiSoundService service)
    {
        uiSoundService = service;
    }

    public void ReleaseUiSoundService()
    {
        uiSoundService = null;
    }

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

        if (uiSoundService == null)
        {
            Debug.LogWarning($"{nameof(ChatSendRejectedAudioController)}: UI sound service was not constructed.");
            return;
        }

        uiSoundService.Play(sound);
    }
}
