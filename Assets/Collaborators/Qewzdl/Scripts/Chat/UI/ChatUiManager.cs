using UnityEngine;

public class ChatUiManager : MonoBehaviour
{
    [Header("UI Mode")]
    [SerializeField] private ChatUiMode mode = ChatUiMode.LobbyWindow;

    [Header("Profile")]
    [SerializeField] private ChatUiProfile profile;

    [Header("Spawn Root")]
    [SerializeField] private Transform uiRoot;

    [Header("Events")]
    [SerializeField] private ChatEventChannel chatEvents;

    [Header("Lobby Chat")]
    [SerializeField] private GameObject lobbyChatPrefab;

    [Header("Phone Chat")]
    [SerializeField] private PhoneChatView phoneChatPrefab;

    [Header("Audio")]
    [SerializeField] private SoundEffect lobbyInputSfx;
    [SerializeField] private SoundEffect lobbyMessageWhileChatClosedSfx;
    [SerializeField] private SoundEffect lobbyMessageWhileChatOpenSfx;
    [SerializeField] private bool playLobbyMessageSfxForOwnMessages;
    [SerializeField] private bool playLobbyMessageSfxForSystemMessages = true;
    [SerializeField] private SoundEffect phoneInputSfx;
    [SerializeField] private SoundEffect phoneOpenSfx;
    [SerializeField] private SoundEffect phoneCloseSfx;
    [SerializeField] private SoundEffect incomingWhenClosedSfx;
    [SerializeField] private SoundEffect incomingWhenOpenedSfx;

    private GameObject spawnedUi;

    private ChatEventChannel ActiveChatEvents => profile != null ? profile.ChatEvents : chatEvents;
    private SoundEffect ActiveLobbyInputSfx => profile != null ? profile.LobbyInputSfx : lobbyInputSfx;
    private SoundEffect ActiveLobbyMessageWhileChatClosedSfx =>
        profile != null ? profile.LobbyMessageWhileChatClosedSfx : lobbyMessageWhileChatClosedSfx;
    private SoundEffect ActiveLobbyMessageWhileChatOpenSfx =>
        profile != null ? profile.LobbyMessageWhileChatOpenSfx : lobbyMessageWhileChatOpenSfx;
    private SoundEffect ActivePhoneInputSfx => profile != null ? profile.PhoneInputSfx : phoneInputSfx;
    private SoundEffect ActivePhoneOpenSfx => profile != null ? profile.PhoneOpenSfx : phoneOpenSfx;
    private SoundEffect ActivePhoneCloseSfx => profile != null ? profile.PhoneCloseSfx : phoneCloseSfx;
    private SoundEffect ActiveIncomingWhenClosedSfx =>
        profile != null ? profile.IncomingWhenClosedSfx : incomingWhenClosedSfx;
    private SoundEffect ActiveIncomingWhenOpenedSfx =>
        profile != null ? profile.IncomingWhenOpenedSfx : incomingWhenOpenedSfx;

    private void Awake()
    {
        SpawnChatUi();
    }

    private void OnDestroy()
    {
        if (spawnedUi == null)
        {
            return;
        }

        Destroy(spawnedUi);
        spawnedUi = null;
    }

    private void SpawnChatUi()
    {
        if (uiRoot == null)
        {
            Debug.LogError($"{nameof(ChatUiManager)}: UI Root is not assigned.", this);
            return;
        }

        switch (mode)
        {
            case ChatUiMode.LobbyWindow:
                SpawnLobbyChat();
                break;

            case ChatUiMode.PhoneWindow:
                SpawnPhoneChat();
                break;
        }
    }

    private void SpawnLobbyChat()
    {
        if (lobbyChatPrefab == null)
        {
            Debug.LogError($"{nameof(ChatUiManager)}: Lobby Chat Prefab is not assigned.", this);
            return;
        }

        spawnedUi = Instantiate(lobbyChatPrefab, uiRoot);
        ChatUiEventChannelBinder.Apply(spawnedUi, ActiveChatEvents);
        ApplyChatInputSfx(spawnedUi, ActiveLobbyInputSfx);
        ApplyLobbyMessageNotificationSfx(spawnedUi);
    }

    private void SpawnPhoneChat()
    {
        if (phoneChatPrefab == null)
        {
            Debug.LogError($"{nameof(ChatUiManager)}: Phone Chat Prefab is not assigned.", this);
            return;
        }

        PhoneChatView phoneView = Instantiate(phoneChatPrefab, uiRoot);
        spawnedUi = phoneView.gameObject;

        StretchToParent(spawnedUi);

        phoneView.Initialize(
            ActiveChatEvents,
            ActivePhoneInputSfx,
            ActiveIncomingWhenClosedSfx,
            ActiveIncomingWhenOpenedSfx,
            ActivePhoneOpenSfx,
            ActivePhoneCloseSfx
        );
    }

    private void ApplyChatInputSfx(GameObject root, SoundEffect inputSfx)
    {
        if (root == null || inputSfx == null)
        {
            return;
        }

        ChatWindowUI chatWindow = root.GetComponentInChildren<ChatWindowUI>(true);

        if (chatWindow == null)
        {
            return;
        }

        chatWindow.SetInputSoundOverride(inputSfx);
    }

    private void ApplyLobbyMessageNotificationSfx(GameObject root)
    {
        ChatEventChannel activeChatEvents = ActiveChatEvents;

        if (root == null || activeChatEvents == null)
        {
            return;
        }

        SoundEffect messageWhileChatClosedSfx = ActiveLobbyMessageWhileChatClosedSfx;
        SoundEffect messageWhileChatOpenSfx = ActiveLobbyMessageWhileChatOpenSfx;

        if (messageWhileChatClosedSfx == null && messageWhileChatOpenSfx == null)
        {
            return;
        }

        ChatMessageNotificationAudioController notificationAudio =
            root.GetComponent<ChatMessageNotificationAudioController>();

        if (notificationAudio == null)
        {
            notificationAudio = root.AddComponent<ChatMessageNotificationAudioController>();
        }

        notificationAudio.Configure(
            activeChatEvents,
            messageWhileChatClosedSfx,
            messageWhileChatOpenSfx,
            playLobbyMessageSfxForOwnMessages,
            playLobbyMessageSfxForSystemMessages
        );
    }

    private void StretchToParent(GameObject target)
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
