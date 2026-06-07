using System;
using UnityEngine;

[DisallowMultipleComponent]
public sealed class PhoneChatWindowHost : MonoBehaviour
{
    [Header("Chat Window")]
    [SerializeField] private RectTransform chatContainer;
    [SerializeField] private GameObject chatWindowPrefab;

    [Header("Events")]
    [SerializeField] private ChatEventChannel chatEvents;

    [Header("Audio")]
    [SerializeField] private SoundEffect inputSfx;
    [SerializeField] private PhoneAudioCueEventChannel phoneAudioCueEvents;

    private GameObject spawnedChatWindow;
    private ChatWindowUI chatWindow;
    private UiInputSound inputSound;
    private bool isSubscribed;

    public event Action Opened;
    public event Action Closed;

    public GameObject SpawnedChatWindow => spawnedChatWindow;
    public ChatWindowUI ChatWindow => chatWindow;

    public void Configure(
        ChatEventChannel chatEvents,
        SoundEffect inputSfx,
        PhoneAudioCueEventChannel phoneAudioCueEvents)
    {
        this.chatEvents = chatEvents;
        this.inputSfx = inputSfx;
        this.phoneAudioCueEvents = phoneAudioCueEvents;

        ApplyEventChannel();
        ApplyInputSfx();
    }

    public void SetInputSfx(SoundEffect inputSfx)
    {
        this.inputSfx = inputSfx;
        ApplyInputSfx();
    }

    public bool Spawn()
    {
        if (spawnedChatWindow != null)
        {
            return chatWindow != null;
        }

        if (chatContainer == null)
        {
            Debug.LogError($"{nameof(PhoneChatView)}: Chat Container is not assigned.", this);
            return false;
        }

        if (chatWindowPrefab == null)
        {
            Debug.LogError($"{nameof(PhoneChatView)}: Chat Window Prefab is not assigned.", this);
            return false;
        }

        spawnedChatWindow = Instantiate(chatWindowPrefab, chatContainer);
        StretchToParent(spawnedChatWindow);

        chatWindow = spawnedChatWindow.GetComponentInChildren<ChatWindowUI>(true);

        if (chatWindow == null)
        {
            Debug.LogError($"{nameof(PhoneChatView)}: Spawned Chat Window has no {nameof(ChatWindowUI)}.", this);
            Destroy(spawnedChatWindow);
            spawnedChatWindow = null;
            return false;
        }

        ApplyEventChannel(chatEvents);
        DisableSpawnedChatMessageNotificationAudio();
        ApplyInputSfx();
        ResolveInputSound();

        return true;
    }

    public void ApplyEventChannel()
    {
        ApplyEventChannel(chatEvents);
    }

    public void ApplyEventChannel(ChatEventChannel chatEvents)
    {
        if (spawnedChatWindow == null)
        {
            return;
        }

        ChatUiEventChannelBinder.Apply(spawnedChatWindow, chatEvents);
    }

    private void ApplyInputSfx()
    {
        if (chatWindow == null)
        {
            return;
        }

        chatWindow.SetInputSoundOverride(inputSfx);
    }

    public void Subscribe()
    {
        if (chatWindow == null || isSubscribed)
        {
            return;
        }

        chatWindow.Opened += HandleChatWindowOpened;
        chatWindow.Closed += HandleChatWindowClosed;
        SubscribeToInputSound();
        isSubscribed = true;
    }

    public void Unsubscribe()
    {
        if (chatWindow == null || !isSubscribed)
        {
            return;
        }

        chatWindow.Opened -= HandleChatWindowOpened;
        chatWindow.Closed -= HandleChatWindowClosed;
        UnsubscribeFromInputSound();
        isSubscribed = false;
    }

    private void OnDisable()
    {
        Unsubscribe();
    }

    private void HandleChatWindowOpened()
    {
        Opened?.Invoke();
    }

    private void HandleChatWindowClosed()
    {
        Closed?.Invoke();
    }

    private void ResolveInputSound()
    {
        if (inputSound != null || spawnedChatWindow == null)
        {
            return;
        }

        inputSound = spawnedChatWindow.GetComponentInChildren<UiInputSound>(true);
    }

    private void SubscribeToInputSound()
    {
        ResolveInputSound();

        if (inputSound == null)
        {
            return;
        }

        inputSound.InputSoundPlayed -= HandleInputSoundPlayed;
        inputSound.InputSoundPlayed += HandleInputSoundPlayed;
    }

    private void UnsubscribeFromInputSound()
    {
        if (inputSound != null)
        {
            inputSound.InputSoundPlayed -= HandleInputSoundPlayed;
        }
    }

    private void HandleInputSoundPlayed()
    {
        phoneAudioCueEvents?.RaiseCuePlayed(
            PhoneAudioCueEvent.Input()
        );
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

    private static void StretchToParent(GameObject target)
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
