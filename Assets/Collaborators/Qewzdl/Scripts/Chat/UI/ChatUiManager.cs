using UnityEngine;

public class ChatUiManager : SceneRuntimeFeature
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
    private ISessionServiceRegistry serviceRegistry;
    private IPlayerScopeRegistry playerScopes;
    private IGameStateService stateMachine;

    protected override bool ValidateFeature(SceneFeatureContext context)
    {
        bool valid = true;
        valid &= RequireReference(profile, nameof(profile));
        valid &= RequireReference(uiRoot, nameof(uiRoot));
        valid &= RequireService<ISessionServiceRegistry>(context, out _);
        valid &= RequireService<IPlayerScopeRegistry>(context, out _);
        valid &= RequireService<IGameStateService>(context, out _);

        switch (mode)
        {
            case ChatUiMode.LobbyWindow:
                valid &= RequireReference(lobbyChatPrefab, nameof(lobbyChatPrefab));
                break;

            case ChatUiMode.PhoneWindow:
                valid &= RequireReference(phoneChatPrefab, nameof(phoneChatPrefab));
                break;

            default:
                Debug.LogError($"Unsupported chat UI mode '{mode}'.", this);
                valid = false;
                break;
        }

        if (profile != null && profile.ChatEvents == null)
        {
            Debug.LogError(
                $"{nameof(ChatUiManager)} requires a {nameof(ChatUiProfile)} with an assigned " +
                $"{nameof(ChatEventChannel)}.",
                this);

            valid = false;
        }

        return valid;
    }

    protected override bool InstallFeature(SceneFeatureContext context)
    {
        serviceRegistry = context.Services.Resolve<ISessionServiceRegistry>();
        playerScopes = context.Services.Resolve<IPlayerScopeRegistry>();
        stateMachine = context.Services.Resolve<IGameStateService>();

        return SpawnChatUi() && BindSpawnedUi();
    }

    protected override void UninstallFeature(SceneFeatureContext context)
    {
        DestroySpawnedUi();
        serviceRegistry = null;
        playerScopes = null;
        stateMachine = null;
    }

    private bool SpawnChatUi()
    {
        if (profile == null)
        {
            Debug.LogError($"{nameof(ChatUiManager)} requires an assigned {nameof(ChatUiProfile)}.", this);
            return false;
        }

        if (uiRoot == null)
        {
            Debug.LogError($"{nameof(ChatUiManager)}: UI Root is not assigned.", this);
            return false;
        }

        ChatEventChannel activeChatEvents = profile.ChatEvents;

        if (activeChatEvents == null)
        {
            Debug.LogError($"{nameof(ChatUiManager)} requires a {nameof(ChatUiProfile)} with an assigned {nameof(ChatEventChannel)}.", this);
            return false;
        }

        switch (mode)
        {
            case ChatUiMode.LobbyWindow:
                return SpawnLobbyChat(activeChatEvents);

            case ChatUiMode.PhoneWindow:
                return SpawnPhoneChat(activeChatEvents);

            default:
                Debug.LogError($"Unsupported chat UI mode '{mode}'.", this);
                return false;
        }
    }

    private bool SpawnLobbyChat(ChatEventChannel activeChatEvents)
    {
        if (lobbyChatPrefab == null)
        {
            Debug.LogError($"{nameof(ChatUiManager)}: Lobby Chat Prefab is not assigned.", this);
            return false;
        }

        spawnedUi = Instantiate(lobbyChatPrefab, uiRoot);
        ChatUiEventChannelBinder.Apply(spawnedUi, activeChatEvents);
        ApplyChatInputSfx(spawnedUi, profile.LobbyInputSfx);
        ChatTypographyApplier.Apply(spawnedUi, profile.LobbyTypography);
        ApplyLobbyMessageNotificationSfx(spawnedUi, activeChatEvents);
        return true;
    }

    private bool SpawnPhoneChat(ChatEventChannel activeChatEvents)
    {
        if (phoneChatPrefab == null)
        {
            Debug.LogError($"{nameof(ChatUiManager)}: Phone Chat Prefab is not assigned.", this);
            return false;
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
            profile.PhoneAudioCueEvents,
            profile.PhoneTypography,
            profile.PhoneAnimation
        );

        return true;
    }

    private bool BindSpawnedUi()
    {
        if (spawnedUi == null ||
            serviceRegistry == null ||
            playerScopes == null ||
            stateMachine == null)
        {
            return false;
        }

        ChatSessionBinder[] binders =
            spawnedUi.GetComponentsInChildren<ChatSessionBinder>(true);

        if (binders.Length == 0)
        {
            Debug.LogError(
                $"Spawned chat UI requires at least one {nameof(ChatSessionBinder)}.",
                this);

            return false;
        }

        for (int i = 0; i < binders.Length; i++)
            binders[i]?.Construct(serviceRegistry, playerScopes, stateMachine);

        return true;
    }

    private void DestroySpawnedUi()
    {
        if (spawnedUi == null)
            return;

        ChatSessionBinder[] binders =
            spawnedUi.GetComponentsInChildren<ChatSessionBinder>(true);

        for (int i = binders.Length - 1; i >= 0; i--)
            binders[i]?.Dispose();

        Destroy(spawnedUi);
        spawnedUi = null;
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
