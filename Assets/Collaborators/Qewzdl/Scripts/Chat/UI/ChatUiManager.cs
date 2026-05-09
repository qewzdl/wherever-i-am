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

    private ChatEventChannel ActiveChatEvents =>
        ResolveProfileValue(profile != null ? profile.ChatEvents : null, chatEvents);
    private SoundEffect ActiveLobbyInputSfx =>
        ResolveProfileValue(profile != null ? profile.LobbyInputSfx : null, lobbyInputSfx);
    private SoundEffect ActiveLobbyMessageWhileChatClosedSfx =>
        ResolveProfileValue(
            profile != null ? profile.LobbyMessageWhileChatClosedSfx : null,
            lobbyMessageWhileChatClosedSfx
        );
    private SoundEffect ActiveLobbyMessageWhileChatOpenSfx =>
        ResolveProfileValue(
            profile != null ? profile.LobbyMessageWhileChatOpenSfx : null,
            lobbyMessageWhileChatOpenSfx
        );
    private SoundEffect ActivePhoneInputSfx =>
        ResolveProfileValue(profile != null ? profile.PhoneInputSfx : null, phoneInputSfx);
    private SoundEffect ActivePhoneOpenSfx =>
        ResolveProfileValue(profile != null ? profile.PhoneOpenSfx : null, phoneOpenSfx);
    private SoundEffect ActivePhoneCloseSfx =>
        ResolveProfileValue(profile != null ? profile.PhoneCloseSfx : null, phoneCloseSfx);
    private SoundEffect ActiveIncomingWhenClosedSfx =>
        ResolveProfileValue(profile != null ? profile.IncomingWhenClosedSfx : null, incomingWhenClosedSfx);
    private SoundEffect ActiveIncomingWhenOpenedSfx =>
        ResolveProfileValue(profile != null ? profile.IncomingWhenOpenedSfx : null, incomingWhenOpenedSfx);

    private void Awake()
    {
        SpawnChatUi();
    }

    private static T ResolveProfileValue<T>(T profileValue, T fallbackValue) where T : UnityEngine.Object
    {
        return profileValue != null ? profileValue : fallbackValue;
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

        ChatEventChannel activeChatEvents = ActiveChatEvents;

        if (activeChatEvents == null)
        {
            Debug.LogError($"{nameof(ChatUiManager)} requires an assigned {nameof(ChatEventChannel)} for Phone Chat.", this);
            return;
        }

        PhoneChatView phoneView = Instantiate(phoneChatPrefab, uiRoot);
        spawnedUi = phoneView.gameObject;

        StretchToParent(spawnedUi);

        phoneView.Initialize(
            activeChatEvents,
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
