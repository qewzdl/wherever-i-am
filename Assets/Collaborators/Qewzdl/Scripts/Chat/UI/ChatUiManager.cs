using UnityEngine;

public class ChatUiManager : MonoBehaviour
{
    [Header("UI Mode")]
    [SerializeField] private ChatUiMode mode = ChatUiMode.LobbyWindow;

    [Header("Profile")]
    [SerializeField] private ChatUiProfile profile;

    [Header("Spawn Root")]
    [SerializeField] private Transform uiRoot;

    [Header("Lobby Chat")]
    [SerializeField] private GameObject lobbyChatPrefab;

    [Header("Phone Chat")]
    [SerializeField] private PhoneChatView phoneChatPrefab;

    private GameObject spawnedUi;

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
        if (profile == null)
        {
            Debug.LogError($"{nameof(ChatUiManager)} requires an assigned {nameof(ChatUiProfile)}.", this);
            return;
        }

        if (uiRoot == null)
        {
            Debug.LogError($"{nameof(ChatUiManager)}: UI Root is not assigned.", this);
            return;
        }

        ChatEventChannel activeChatEvents = profile.ChatEvents;

        if (activeChatEvents == null)
        {
            Debug.LogError($"{nameof(ChatUiManager)} requires a {nameof(ChatUiProfile)} with an assigned {nameof(ChatEventChannel)}.", this);
            return;
        }

        switch (mode)
        {
            case ChatUiMode.LobbyWindow:
                SpawnLobbyChat(activeChatEvents);
                break;

            case ChatUiMode.PhoneWindow:
                SpawnPhoneChat(activeChatEvents);
                break;
        }
    }

    private void SpawnLobbyChat(ChatEventChannel activeChatEvents)
    {
        if (lobbyChatPrefab == null)
        {
            Debug.LogError($"{nameof(ChatUiManager)}: Lobby Chat Prefab is not assigned.", this);
            return;
        }

        spawnedUi = Instantiate(lobbyChatPrefab, uiRoot);
        ChatUiEventChannelBinder.Apply(spawnedUi, activeChatEvents);
        ApplyChatInputSfx(spawnedUi, profile.LobbyInputSfx);
        ChatTypographyApplier.Apply(spawnedUi, profile.LobbyTypography);
        ApplyLobbyMessageNotificationSfx(spawnedUi, activeChatEvents);
    }

    private void SpawnPhoneChat(ChatEventChannel activeChatEvents)
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
            activeChatEvents,
            profile.PhoneInputSfx,
            profile.IncomingWhenClosedSfx,
            profile.IncomingWhenOpenedSfx,
            profile.PhoneOpenSfx,
            profile.PhoneCloseSfx,
            profile.PlayPhoneIncomingSfxForOwnMessages,
            profile.PlayPhoneIncomingSfxForSystemMessages,
            profile.PhoneTypography
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

    private void ApplyLobbyMessageNotificationSfx(GameObject root, ChatEventChannel activeChatEvents)
    {
        if (root == null || activeChatEvents == null)
        {
            return;
        }

        SoundEffect messageWhileChatClosedSfx = profile.LobbyMessageWhileChatClosedSfx;
        SoundEffect messageWhileChatOpenSfx = profile.LobbyMessageWhileChatOpenSfx;

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
            profile.PlayLobbyMessageSfxForOwnMessages,
            profile.PlayLobbyMessageSfxForSystemMessages
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
